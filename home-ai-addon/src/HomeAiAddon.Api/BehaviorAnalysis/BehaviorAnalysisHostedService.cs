using System.Diagnostics;
using HomeAiAddon.Api.Options;
using Microsoft.Extensions.Options;

namespace HomeAiAddon.Api.BehaviorAnalysis;

/// <summary>
/// Starts the Python ML service locally when AutoStart is enabled (Development).
/// In Docker/Production the entrypoint starts uvicorn instead.
/// </summary>
public sealed class BehaviorAnalysisHostedService(
    IOptions<BehaviorAnalysisOptions> options,
    IBehaviorAnalysisClient analysisClient,
    IWebHostEnvironment environment,
    ILogger<BehaviorAnalysisHostedService> logger) : IHostedService
{
    private Process? _process;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var opts = options.Value;
        if (!opts.AutoStart)
        {
            return;
        }

        if (await analysisClient.IsHealthyAsync(cancellationToken))
        {
            logger.LogInformation("Behavior analysis ML service already running at {BaseUrl}", opts.BaseUrl);
            return;
        }

        var mlDirectory = ResolveMlServiceDirectory(opts);
        if (!Directory.Exists(mlDirectory))
        {
            logger.LogWarning(
                "ML service directory not found at {MlDirectory}. Start uvicorn manually or set BehaviorAnalysis:MlServiceDirectory.",
                mlDirectory);
            return;
        }

        var python = ResolvePythonExecutable(opts);
        if (python is null)
        {
            logger.LogWarning(
                "Python executable not found. Install Python 3.12+ and dependencies from ml-service/requirements.txt.");
            return;
        }

        var port = new Uri(opts.BaseUrl).Port;
        var arguments =
            $"-m uvicorn app.main:app --host 127.0.0.1 --port {port} --workers 1";

        var startInfo = new ProcessStartInfo
        {
            FileName = python,
            Arguments = arguments,
            WorkingDirectory = mlDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        startInfo.Environment["PYTHONUNBUFFERED"] = "1";

        try
        {
            _process = Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to start ML service process");
            return;
        }

        if (_process is null)
        {
            logger.LogError("ML service process did not start");
            return;
        }

        _ = Task.Run(() => PumpLogsAsync(_process.StandardOutput, LogLevel.Information), cancellationToken);
        _ = Task.Run(() => PumpLogsAsync(_process.StandardError, LogLevel.Warning), cancellationToken);

        logger.LogInformation(
            "Starting behavior analysis ML service (pid {Pid}) at {BaseUrl}",
            _process.Id,
            opts.BaseUrl);

        var ready = await WaitForHealthyAsync(opts.StartupWaitSeconds, cancellationToken);
        if (!ready)
        {
            logger.LogError(
                "ML service did not become healthy within {Seconds}s. Check Python dependencies in ml-service.",
                opts.StartupWaitSeconds);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        if (_process is { HasExited: false })
        {
            try
            {
                _process.Kill(entireProcessTree: true);
            }
            catch (Exception ex)
            {
                logger.LogDebug(ex, "Failed to stop ML service process");
            }
        }

        _process?.Dispose();
        _process = null;
        return Task.CompletedTask;
    }

    private async Task<bool> WaitForHealthyAsync(int timeoutSeconds, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddSeconds(Math.Max(timeoutSeconds, 5));
        while (DateTime.UtcNow < deadline)
        {
            if (_process is { HasExited: true })
            {
                logger.LogError("ML service process exited with code {ExitCode}", _process.ExitCode);
                return false;
            }

            if (await analysisClient.IsHealthyAsync(cancellationToken))
            {
                logger.LogInformation("Behavior analysis ML service is healthy");
                return true;
            }

            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }

        return false;
    }

    private string ResolveMlServiceDirectory(BehaviorAnalysisOptions opts)
    {
        if (!string.IsNullOrWhiteSpace(opts.MlServiceDirectory))
        {
            return Path.GetFullPath(opts.MlServiceDirectory);
        }

        return Path.GetFullPath(Path.Combine(environment.ContentRootPath, "..", "..", "ml-service"));
    }

    private static string? ResolvePythonExecutable(BehaviorAnalysisOptions opts)
    {
        if (!string.IsNullOrWhiteSpace(opts.PythonExecutable))
        {
            return opts.PythonExecutable;
        }

        foreach (var candidate in new[] { "python", "python3", "py" })
        {
            try
            {
                var probe = new ProcessStartInfo
                {
                    FileName = candidate,
                    Arguments = candidate == "py" ? "-3 -c \"import sys; print(sys.executable)\"" : "-c \"import sys; print(sys.executable)\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };

                using var process = Process.Start(probe);
                if (process is null)
                {
                    continue;
                }

                var output = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit(2000);
                if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
                {
                    return output;
                }

                if (candidate != "py")
                {
                    return candidate;
                }
            }
            catch
            {
                // try next candidate
            }
        }

        return null;
    }

    private async Task PumpLogsAsync(StreamReader reader, LogLevel level)
    {
        try
        {
            while (await reader.ReadLineAsync() is { } line)
            {
                logger.Log(level, "[ml-service] {Line}", line);
            }
        }
        catch
        {
            // process exited
        }
    }
}
