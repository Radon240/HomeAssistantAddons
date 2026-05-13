using System.Net.Http.Headers;

namespace HomeAiAddon.Api.HomeAssistant;

public sealed class HomeAssistantBearerAuthHandler(IHomeAssistantAccessTokenProvider tokens) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = tokens.GetAccessToken();
        if (!string.IsNullOrEmpty(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
