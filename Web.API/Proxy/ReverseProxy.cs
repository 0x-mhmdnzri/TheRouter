using System.Net.Http.Headers;
using TheRouter.Core.Http;

namespace Web.API.Proxy;

/// <summary>
/// Streaming reverse-proxy. Forwards the current request to the upstream
/// configured on the matched MatchedRoute. Never buffers the full body.
/// </summary>
public sealed class ReverseProxy
{
    private static readonly HashSet<string> HopByHopHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Connection", "Keep-Alive", "Proxy-Authenticate", "Proxy-Authorization",
        "TE", "Trailers", "Transfer-Encoding", "Upgrade", "Proxy-Connection"
    };

    private readonly HttpMessageInvoker _invoker;
    private readonly TimeSpan _defaultTimeout;

    public ReverseProxy(HttpMessageInvoker invoker, TimeSpan? defaultTimeout = null)
    {
        _invoker = invoker ?? throw new ArgumentNullException(nameof(invoker));
        _defaultTimeout = defaultTimeout ?? TimeSpan.FromSeconds(30);
    }

    public async Task ForwardAsync(HttpContext context, MatchedRoute endpoint, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(endpoint.UpstreamBaseAddress))
        {
            context.Response.StatusCode = 502;
            await context.Response.WriteAsync("No upstream configured for this route.", cancellationToken).ConfigureAwait(false);
            return;
        }

        var timeout = endpoint.UpstreamTimeout ?? _defaultTimeout;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, context.RequestAborted);
        cts.CancelAfter(timeout);

        var targetPath = endpoint.UpstreamPathTemplate ?? context.Request.Path.Value ?? "/";
        var baseUri = new Uri(endpoint.UpstreamBaseAddress.TrimEnd('/') + "/");
        var targetUri = new Uri(baseUri, targetPath.TrimStart('/'));

        if (context.Request.QueryString.HasValue)
            targetUri = new Uri(targetUri.ToString() + context.Request.QueryString.Value);

        using var request = new HttpRequestMessage(new HttpMethod(context.Request.Method), targetUri);

        if (HttpMethods.IsGet(context.Request.Method) == false &&
            HttpMethods.IsHead(context.Request.Method) == false &&
            HttpMethods.IsDelete(context.Request.Method) == false)
        {
            request.Content = new StreamContent(context.Request.Body);
            if (context.Request.ContentType is not null)
                request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(context.Request.ContentType);
            if (context.Request.ContentLength is long len)
                request.Content.Headers.ContentLength = len;
        }

        foreach (var header in context.Request.Headers)
        {
            if (HopByHopHeaders.Contains(header.Key)) continue;
            if (header.Key.Equals("Host", StringComparison.OrdinalIgnoreCase)) continue;

            if (!request.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray()))
                request.Content?.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
        }

        using var response = await _invoker.SendAsync(request, cts.Token).ConfigureAwait(false);

        context.Response.StatusCode = (int)response.StatusCode;

        foreach (var header in response.Headers)
        {
            if (HopByHopHeaders.Contains(header.Key)) continue;
            context.Response.Headers[header.Key] = header.Value.ToArray();
        }

        if (response.Content is not null)
        {
            foreach (var header in response.Content.Headers)
            {
                if (HopByHopHeaders.Contains(header.Key)) continue;
                if (header.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)) continue;
                context.Response.Headers[header.Key] = header.Value.ToArray();
            }

            await response.Content.CopyToAsync(context.Response.Body, cts.Token).ConfigureAwait(false);
        }
    }
}
