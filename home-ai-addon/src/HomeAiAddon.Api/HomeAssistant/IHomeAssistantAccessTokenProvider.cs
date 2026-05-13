namespace HomeAiAddon.Api.HomeAssistant;

public interface IHomeAssistantAccessTokenProvider
{
    /// <summary>Возвращает токен из окружения или null, если не задан.</summary>
    string? GetAccessToken();
}
