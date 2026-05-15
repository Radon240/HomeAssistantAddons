namespace HomeAiAddon.Api.HomeAssistant;

/// <summary>
/// В аддоне: <see cref="HomeAssistantEnvironment.SupervisorTokenVariable"/> от Supervisor (автоматически).
/// Локально: опционально <see cref="HomeAssistantEnvironment.AccessTokenVariable"/>.
/// </summary>
public sealed class HomeAssistantAccessTokenProvider : IHomeAssistantAccessTokenProvider
{
    public string? GetAccessToken()
    {
        var supervisor = Environment.GetEnvironmentVariable(HomeAssistantEnvironment.SupervisorTokenVariable);
        if (!string.IsNullOrWhiteSpace(supervisor))
        {
            return supervisor.Trim();
        }

        var manual = Environment.GetEnvironmentVariable(HomeAssistantEnvironment.AccessTokenVariable);
        return string.IsNullOrWhiteSpace(manual) ? null : manual.Trim();
    }

    public string GetAuthSource()
    {
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(HomeAssistantEnvironment.SupervisorTokenVariable)))
        {
            return "supervisor";
        }

        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(HomeAssistantEnvironment.AccessTokenVariable)))
        {
            return "manual";
        }

        return "none";
    }
}
