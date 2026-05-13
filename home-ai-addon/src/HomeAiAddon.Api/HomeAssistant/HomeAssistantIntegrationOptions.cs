namespace HomeAiAddon.Api.HomeAssistant;

public sealed class HomeAssistantIntegrationOptions
{
    public const string SectionName = "HomeAssistant";

    /// <summary>Базовый URL инстанса Home Assistant (например https://home.example.com:8123). Без токена.</summary>
    public string? BaseUrl { get; set; }
}
