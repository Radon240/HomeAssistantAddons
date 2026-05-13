namespace HomeAiAddon.Api.HomeAssistant;

public sealed class EnvironmentHomeAssistantAccessTokenProvider : IHomeAssistantAccessTokenProvider
{
    public string? GetAccessToken()
    {
        var value = Environment.GetEnvironmentVariable(HomeAssistantEnvironment.AccessTokenVariable);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
