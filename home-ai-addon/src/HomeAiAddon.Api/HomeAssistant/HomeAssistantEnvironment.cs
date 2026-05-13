namespace HomeAiAddon.Api.HomeAssistant;

/// <summary>
/// Имена переменных окружения для интеграции. Токен читается только отсюда (см. официальные PAT в документации HA).
/// </summary>
public static class HomeAssistantEnvironment
{
    /// <summary>Long-lived access token (Personal Access Token). Не помещать в appsettings или options.json.</summary>
    public const string AccessTokenVariable = "HOME_ASSISTANT_ACCESS_TOKEN";
}
