using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using HomeAiAddon.Api.Data;

namespace HomeAiAddon.Api.HomeAssistant;

/// <summary>
/// Фоновый сервис: WebSocket API Home Assistant (аутентификация, subscribe_events на state_changed),
/// повторные подключения с экспоненциальной задержкой, корректное завершение по <see cref="CancellationToken"/>.
/// </summary>
public sealed class HomeAssistantWebSocketHostedService(
    ILogger<HomeAssistantWebSocketHostedService> logger,
    HomeAssistantConnectionResolver connectionResolver,
    IHomeAssistantAccessTokenProvider accessTokenProvider,
    HomeAssistantConnectionState connectionState,
    HomeAssistantEntityFilter entityFilter,
    RuntimeMetrics runtimeMetrics,
    IServiceScopeFactory scopeFactory,
    IHttpClientFactory httpClientFactory) : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private int _reconnectAttempt;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Home Assistant WebSocket worker started.");

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                if (!connectionResolver.TryResolve(out var endpoints))
                {
                    connectionState.SetWebSocketConnected(false);
                    connectionState.RecordError(
                        "Нет доступа к Home Assistant: в аддоне ожидается SUPERVISOR_TOKEN от Supervisor "
                        + "(включите homeassistant_api: true и перезапустите аддон). "
                        + "Для локальной отладки можно задать HOME_ASSISTANT_ACCESS_TOKEN.");

                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }

                    continue;
                }

                connectionState.ClearLastError();

                logger.LogInformation(
                    "Home Assistant: подключение REST {Rest}, WebSocket {Ws} (supervisor={Supervisor})",
                    endpoints.RestApiBase,
                    endpoints.WebSocketUri,
                    endpoints.UsesSupervisorProxy);

                try
                {
                    var accessToken = accessTokenProvider.GetAccessToken()!;
                    await using var session = await HomeAssistantWebSocketSession.ConnectAndRunAsync(
                        logger,
                        connectionState,
                        entityFilter,
                        runtimeMetrics,
                        scopeFactory,
                        httpClientFactory,
                        endpoints,
                        accessToken,
                        stoppingToken);

                    _reconnectAttempt = 0;
                    await session.RunReceiveLoopAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    connectionState.SetWebSocketConnected(false);
                    var message = SanitizeError(ex.Message);
                    connectionState.RecordError(message);
                    runtimeMetrics.IncrementReconnect();
                    logger.LogWarning(ex, "Home Assistant WebSocket session завершилась с ошибкой.");

                    var delay = ComputeBackoffDelay(_reconnectAttempt);
                    _reconnectAttempt = Math.Min(_reconnectAttempt + 1, 30);

                    try
                    {
                        await Task.Delay(delay, stoppingToken);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }
        }
        finally
        {
            connectionState.SetWebSocketConnected(false);
            logger.LogInformation("Home Assistant WebSocket worker stopped.");
        }
    }

    private static TimeSpan ComputeBackoffDelay(int attempt)
    {
        var seconds = Math.Min(60, Math.Pow(2, Math.Min(attempt, 10)));
        return TimeSpan.FromSeconds(seconds);
    }

    private static string SanitizeError(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return "Неизвестная ошибка.";
        }

        var line = message.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? message;
        return line.Length > 500 ? line[..500] : line;
    }

    private sealed class HomeAssistantWebSocketSession : IAsyncDisposable
    {
        private readonly ClientWebSocket _webSocket = new();
        private readonly ILogger _logger;
        private readonly HomeAssistantConnectionState _connectionState;
        private readonly HomeAssistantEndpoints _endpoints;
        private readonly string _accessToken;
        private readonly HomeAssistantEntityFilter _entityFilter;
        private readonly RuntimeMetrics _runtimeMetrics;
        private readonly IServiceScopeFactory _scopeFactory;
        private int _nextMessageId;

        private HomeAssistantWebSocketSession(
            ILogger logger,
            HomeAssistantConnectionState connectionState,
            HomeAssistantEntityFilter entityFilter,
            RuntimeMetrics runtimeMetrics,
            IServiceScopeFactory scopeFactory,
            HomeAssistantEndpoints endpoints,
            string accessToken)
        {
            _logger = logger;
            _connectionState = connectionState;
            _entityFilter = entityFilter;
            _runtimeMetrics = runtimeMetrics;
            _scopeFactory = scopeFactory;
            _endpoints = endpoints;
            _accessToken = accessToken;
        }

        public static async Task<HomeAssistantWebSocketSession> ConnectAndRunAsync(
            ILogger logger,
            HomeAssistantConnectionState connectionState,
            HomeAssistantEntityFilter entityFilter,
            RuntimeMetrics runtimeMetrics,
            IServiceScopeFactory scopeFactory,
            IHttpClientFactory httpClientFactory,
            HomeAssistantEndpoints endpoints,
            string accessToken,
            CancellationToken cancellationToken)
        {
            var session = new HomeAssistantWebSocketSession(
                logger,
                connectionState,
                entityFilter,
                runtimeMetrics,
                scopeFactory,
                endpoints,
                accessToken);

            await session.PingRestApiAsync(httpClientFactory, cancellationToken).ConfigureAwait(false);

            await session._webSocket.ConnectAsync(endpoints.WebSocketUri, cancellationToken).ConfigureAwait(false);

            await session.PerformAuthenticationAsync(cancellationToken).ConfigureAwait(false);
            await session.SubscribeStateChangedAsync(cancellationToken).ConfigureAwait(false);

            connectionState.SetWebSocketConnected(true);
            return session;
        }

        private async Task PingRestApiAsync(
            IHttpClientFactory httpClientFactory,
            CancellationToken cancellationToken)
        {
            var pingUri = HomeAssistantUriHelper.CombineRestPath(
                _endpoints.RestApiBase,
                _endpoints.RestHealthCheckRelativePath);
            var client = httpClientFactory.CreateClient("HomeAssistant");
            using var response = await client.GetAsync(pingUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"Home Assistant REST {pingUri} вернул {(int)response.StatusCode} {response.ReasonPhrase}.");
            }
        }

        private async Task PerformAuthenticationAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var text = await ReceiveTextMessageAsync(_webSocket, cancellationToken).ConfigureAwait(false);
                if (text is null)
                {
                    throw new InvalidOperationException("WebSocket закрыт до завершения аутентификации.");
                }

                using var doc = JsonDocument.Parse(text);
                var root = doc.RootElement;
                if (!root.TryGetProperty("type", out var typeProp) || typeProp.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var type = typeProp.GetString();
                switch (type)
                {
                    case "auth_required":
                        await SendJsonAsync(
                                _webSocket,
                                new { type = "auth", access_token = _accessToken },
                                cancellationToken)
                            .ConfigureAwait(false);
                        break;
                    case "auth_ok":
                        _logger.LogInformation("Home Assistant WebSocket: аутентификация успешна.");
                        return;
                    case "auth_invalid":
                        throw new InvalidOperationException(
                            "Home Assistant отклонил токен (auth_invalid). Выпустите новый long-lived access token.");
                    default:
                        _logger.LogDebug("Home Assistant WebSocket (до auth_ok): пропуск сообщения типа {Type}.", type);
                        break;
                }
            }

            throw new OperationCanceledException();
        }

        private async Task SubscribeStateChangedAsync(CancellationToken cancellationToken)
        {
            var id = Interlocked.Increment(ref _nextMessageId);
            await SendJsonAsync(
                    _webSocket,
                    new
                    {
                        id,
                        type = "subscribe_events",
                        event_type = "state_changed"
                    },
                    cancellationToken)
                .ConfigureAwait(false);

            while (!cancellationToken.IsCancellationRequested)
            {
                var text = await ReceiveTextMessageAsync(_webSocket, cancellationToken).ConfigureAwait(false);
                if (text is null)
                {
                    throw new InvalidOperationException("WebSocket закрыт до подтверждения подписки.");
                }

                using var doc = JsonDocument.Parse(text);
                var root = doc.RootElement;
                if (!root.TryGetProperty("type", out var typeProp) || typeProp.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var type = typeProp.GetString();
                if (type == "result")
                {
                    if (root.TryGetProperty("id", out var msgId) && msgId.ValueKind == JsonValueKind.Number &&
                        msgId.TryGetInt32(out var rid) && rid != id)
                    {
                        continue;
                    }

                    if (!TryReadResultSuccess(root, out var success, out var error))
                    {
                        continue;
                    }

                    if (!success)
                    {
                        throw new InvalidOperationException(
                            $"Подписка на state_changed отклонена: {error ?? "неизвестная ошибка"}.");
                    }

                    _logger.LogInformation("Home Assistant WebSocket: подписка на state_changed активна (id={Id}).", id);
                    return;
                }

                if (type == "event")
                {
                    HandleIncomingEvent(root);
                    continue;
                }

                if (type == "ping")
                {
                    await SendJsonAsync(_webSocket, new { type = "pong" }, cancellationToken).ConfigureAwait(false);
                }
            }

            throw new OperationCanceledException();
        }

        public async Task RunReceiveLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested && _webSocket.State == WebSocketState.Open)
            {
                string? text;
                try
                {
                    text = await ReceiveTextMessageAsync(_webSocket, cancellationToken).ConfigureAwait(false);
                }
                catch (ConnectionClosedException)
                {
                    _connectionState.SetWebSocketConnected(false);
                    return;
                }

                if (text is null)
                {
                    _connectionState.SetWebSocketConnected(false);
                    return;
                }

                using var doc = JsonDocument.Parse(text);
                var root = doc.RootElement;
                if (!root.TryGetProperty("type", out var typeProp) || typeProp.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var type = typeProp.GetString();
                switch (type)
                {
                    case "ping":
                        await SendJsonAsync(_webSocket, new { type = "pong" }, cancellationToken).ConfigureAwait(false);
                        break;
                    case "event":
                        HandleIncomingEvent(root);
                        break;
                    case "result":
                        if (!TryReadResultSuccess(root, out var success, out var error))
                        {
                            break;
                        }

                        if (!success)
                        {
                            _logger.LogWarning("Home Assistant WebSocket: result с ошибкой: {Error}.", error);
                        }

                        break;
                    default:
                        _logger.LogDebug("Home Assistant WebSocket: неизвестный тип сообщения {Type}.", type);
                        break;
                }
            }
        }

        private void HandleIncomingEvent(JsonElement root)
        {
            var receivedAt = DateTimeOffset.UtcNow;
            if (!HomeAssistantEventNormalizer.TryNormalizeStateChanged(root, receivedAt, out var normalized) ||
                normalized is null)
            {
                return;
            }

            if (!_entityFilter.ShouldTrack(normalized.EntityId))
            {
                _runtimeMetrics.IncrementFiltered();
                return;
            }

            _connectionState.AppendStateChange(normalized);
            _ = PersistEventAsync(normalized);
            _logger.LogDebug(
                "HA state_changed: {EntityId} -> {NewState} (было {OldState})",
                normalized.EntityId,
                normalized.NewState,
                normalized.OldState);
        }

        private async Task PersistEventAsync(NormalizedStateChangedEvent normalized)
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var store = scope.ServiceProvider.GetRequiredService<IStateChangeEventStore>();
                await store.AddAsync(normalized).ConfigureAwait(false);
                _runtimeMetrics.IncrementPersisted();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Не удалось сохранить событие {EntityId} в SQLite.", normalized.EntityId);
            }
        }

        private static bool TryReadResultSuccess(JsonElement root, out bool success, out string? error)
        {
            success = false;
            error = null;
            if (!root.TryGetProperty("success", out var successProp))
            {
                return false;
            }

            if (successProp.ValueKind != JsonValueKind.True && successProp.ValueKind != JsonValueKind.False)
            {
                return false;
            }

            success = successProp.GetBoolean();
            if (!success && root.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.Object)
            {
                if (err.TryGetProperty("message", out var msg) && msg.ValueKind == JsonValueKind.String)
                {
                    error = msg.GetString();
                }
            }

            return true;
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (_webSocket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "shutdown", cts.Token)
                        .ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Игнорируемая ошибка при закрытии WebSocket.");
            }
            finally
            {
                _webSocket.Dispose();
            }
        }

        private static async Task SendJsonAsync(ClientWebSocket ws, object payload, CancellationToken cancellationToken)
        {
            var json = JsonSerializer.Serialize(payload, JsonOptions);
            var bytes = Encoding.UTF8.GetBytes(json);
            await ws.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, cancellationToken)
                .ConfigureAwait(false);
        }

        private static async Task<string?> ReceiveTextMessageAsync(ClientWebSocket ws, CancellationToken cancellationToken)
        {
            using var buffer = new MemoryStream(capacity: 4096);
            var segment = new byte[16384];
            while (true)
            {
                var result = await ws.ReceiveAsync(segment, cancellationToken).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    throw new ConnectionClosedException();
                }

                if (result.MessageType == WebSocketMessageType.Binary)
                {
                    throw new InvalidOperationException("Неожиданное бинарное сообщение WebSocket от Home Assistant.");
                }

                buffer.Write(segment.AsSpan(0, result.Count));
                if (result.EndOfMessage)
                {
                    break;
                }
            }

            return Encoding.UTF8.GetString(buffer.ToArray());
        }

        private sealed class ConnectionClosedException : Exception;
    }
}
