namespace HomeAiAddon.Api.HomeAssistant;

/// <summary>
/// Переменные окружения для доступа к Home Assistant.
/// В аддоне Supervisor подставляет <see cref="SupervisorTokenVariable"/> автоматически
/// (см. https://developers.home-assistant.io/docs/add-ons/communication).
/// </summary>
public static class HomeAssistantEnvironment
{
    /// <summary>Токен Supervisor — Bearer для прокси Core API / WebSocket.</summary>
    public const string SupervisorTokenVariable = "SUPERVISOR_TOKEN";

    /// <summary>Опционально: long-lived token для локальной отладки вне Supervisor.</summary>
    public const string AccessTokenVariable = "HOME_ASSISTANT_ACCESS_TOKEN";
}
