using HomeAiAddon.Api.Data;
using HomeAiAddon.Api.Health;
using HomeAiAddon.Api.HomeAssistant;
using HomeAiAddon.Api.Options;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile(
    "/data/options.json",
    optional: true,
    reloadOnChange: true);

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddOptions<AddonOptions>()
    .Bind(builder.Configuration)
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<HomeAssistantIntegrationOptions>()
    .Bind(builder.Configuration.GetSection(HomeAssistantIntegrationOptions.SectionName))
    .PostConfigure<IConfiguration>((opts, cfg) =>
    {
        var flat = cfg["home_assistant_base_url"];
        if (!string.IsNullOrWhiteSpace(flat))
        {
            opts.BaseUrl = flat;
        }
    });

builder.Services.AddSingleton<HomeAssistantConnectionState>();
builder.Services.AddSingleton<IHomeAssistantAccessTokenProvider, HomeAssistantAccessTokenProvider>();
builder.Services.AddSingleton<HomeAssistantConnectionResolver>();
builder.Services.AddTransient<HomeAssistantBearerAuthHandler>();
builder.Services.AddHttpClient("HomeAssistant")
    .ConfigureHttpClient(client =>
    {
        client.Timeout = TimeSpan.FromSeconds(30);
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
    })
    .AddHttpMessageHandler<HomeAssistantBearerAuthHandler>();

builder.Services.AddHostedService<HomeAssistantWebSocketHostedService>();

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? "Data Source=/data/app.db";
EnsureDatabaseDirectoryExists(connectionString);

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(connectionString));

builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database")
    .AddCheck<HomeAssistantIntegrationHealthCheck>("home_assistant");

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    });

var app = builder.Build();

app.UseForwardedHeaders();

var pathBase = app.Configuration["ASPNETCORE_PATHBASE"];
if (!string.IsNullOrWhiteSpace(pathBase))
{
    app.UsePathBase(pathBase);
}

app.Use(async (context, next) =>
{
    if (context.Request.Headers.TryGetValue("X-Ingress-Path", out var ingressPath))
    {
        var raw = ingressPath.ToString();
        if (!string.IsNullOrWhiteSpace(raw))
        {
            var trimmed = raw.TrimEnd('/');
            if (trimmed.Length > 0 && trimmed.StartsWith('/'))
            {
                context.Request.PathBase = new PathString(trimmed);
                if (context.Request.Path.StartsWithSegments(context.Request.PathBase, out var remaining))
                {
                    context.Request.Path = remaining;
                }
            }
        }
    }

    await next();
});

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = WriteHealthResponseAsync
});

app.MapControllers();
app.MapFallbackToFile("index.html");

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await db.Database.MigrateAsync();
}

await app.RunAsync();

static void EnsureDatabaseDirectoryExists(string sqliteConnectionString)
{
    const string prefix = "Data Source=";
    if (!sqliteConnectionString.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
    {
        return;
    }

    var relativeOrAbsolute = sqliteConnectionString[prefix.Length..].Trim();
    var fullPath = Path.GetFullPath(relativeOrAbsolute);
    var directory = Path.GetDirectoryName(fullPath);
    if (!string.IsNullOrEmpty(directory))
    {
        Directory.CreateDirectory(directory);
    }
}

static async Task WriteHealthResponseAsync(HttpContext context, HealthReport report)
{
    context.Response.ContentType = "application/json; charset=utf-8";
    var payload = new
    {
        status = report.Status.ToString(),
        totalDuration = report.TotalDuration.TotalMilliseconds,
        checks = report.Entries.Select(e => new
        {
            name = e.Key,
            status = e.Value.Status.ToString(),
            durationMs = e.Value.Duration.TotalMilliseconds,
            description = e.Value.Description,
            data = e.Value.Data
        })
    };

    await context.Response.WriteAsync(JsonSerializer.Serialize(payload));
}
