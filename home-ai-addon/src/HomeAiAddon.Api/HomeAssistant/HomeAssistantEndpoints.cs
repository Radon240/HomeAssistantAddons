namespace HomeAiAddon.Api.HomeAssistant;

/// <summary>Параметры подключения к Core API (REST + WebSocket).</summary>
public sealed record HomeAssistantEndpoints(
    Uri RestApiBase,
    Uri WebSocketUri,
    string RestHealthCheckRelativePath,
    string AuthSource,
    bool UsesSupervisorProxy);
