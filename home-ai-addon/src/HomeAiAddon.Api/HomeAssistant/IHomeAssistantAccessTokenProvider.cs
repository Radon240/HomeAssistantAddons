namespace HomeAiAddon.Api.HomeAssistant;

public interface IHomeAssistantAccessTokenProvider
{
    string? GetAccessToken();

    /// <summary>supervisor | manual | none</summary>
    string GetAuthSource();
}
