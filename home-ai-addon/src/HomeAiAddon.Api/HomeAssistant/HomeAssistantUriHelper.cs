namespace HomeAiAddon.Api.HomeAssistant;

public static class HomeAssistantUriHelper
{
    public static bool TryGetHttpOrigin(string? baseUrl, out Uri origin)
    {
        origin = default!;
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return false;
        }

        if (!Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out var parsed))
        {
            return false;
        }

        if (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        var left = parsed.GetLeftPart(UriPartial.Authority);
        if (!Uri.TryCreate(left, UriKind.Absolute, out var authorityUri))
        {
            return false;
        }

        origin = authorityUri;
        return true;
    }

    public static Uri BuildWebSocketUri(Uri httpOrigin)
    {
        var builder = new UriBuilder(httpOrigin)
        {
            Path = "/api/websocket"
        };

        if (builder.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase))
        {
            builder.Scheme = "ws";
        }
        else if (builder.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
        {
            builder.Scheme = "wss";
        }

        return builder.Uri;
    }
}
