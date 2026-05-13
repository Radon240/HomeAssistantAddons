using System.Text.Json;

namespace HomeAiAddon.Api.HomeAssistant;

public static class HomeAssistantEventNormalizer
{
    /// <summary>
    /// Нормализует payload события <c>state_changed</c> из WebSocket API (см. developers.home-assistant.io — subscribe_events).
    /// </summary>
    public static bool TryNormalizeStateChanged(JsonElement root, DateTimeOffset receivedAtUtc, out NormalizedStateChangedEvent? normalized)
    {
        normalized = null;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (!root.TryGetProperty("event", out var eventElement) || eventElement.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (!eventElement.TryGetProperty("event_type", out var eventType) ||
            eventType.GetString() != "state_changed")
        {
            return false;
        }

        if (!eventElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (!data.TryGetProperty("entity_id", out var entityIdProp) || entityIdProp.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var entityId = entityIdProp.GetString() ?? string.Empty;
        if (entityId.Length == 0)
        {
            return false;
        }

        var newState = ReadStateString(data, "new_state");
        var oldState = ReadStateString(data, "old_state");
        var friendlyName = ReadFriendlyName(data, "new_state");

        var timeFiredUtc = ReadTimeFired(eventElement, receivedAtUtc);

        normalized = new NormalizedStateChangedEvent(
            entityId,
            newState,
            oldState,
            friendlyName,
            timeFiredUtc,
            receivedAtUtc);

        return true;
    }

    private static DateTimeOffset ReadTimeFired(JsonElement eventElement, DateTimeOffset fallback)
    {
        if (!eventElement.TryGetProperty("time_fired", out var tf) || tf.ValueKind != JsonValueKind.String)
        {
            return fallback;
        }

        return DateTimeOffset.TryParse(tf.GetString(), out var parsed) ? parsed : fallback;
    }

    private static string? ReadStateString(JsonElement data, string stateProperty)
    {
        if (!data.TryGetProperty(stateProperty, out var stateObj) || stateObj.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!stateObj.TryGetProperty("state", out var state) || state.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return state.GetString();
    }

    private static string? ReadFriendlyName(JsonElement data, string stateProperty)
    {
        if (!data.TryGetProperty(stateProperty, out var stateObj) || stateObj.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!stateObj.TryGetProperty("attributes", out var attrs) || attrs.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!attrs.TryGetProperty("friendly_name", out var fn) || fn.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return fn.GetString();
    }
}
