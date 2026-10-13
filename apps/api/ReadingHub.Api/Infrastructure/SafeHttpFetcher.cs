using System.Net;
using System.Net.Sockets;

namespace ReadingHub.Api.Infrastructure;

public sealed record FetchedDocument(Uri FinalUri, string Html, string? ContentType);

public sealed class SafeHttpFetcher(IDnsResolver dns, ILogger<SafeHttpFetcher> logger, HttpMessageHandler? handler = null)
{
    public const int MaxRedirects = 5;
    public const int MaxBytes = 1_048_576;

    public async Task<FetchedDocument> FetchAsync(string url, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var current))
        {
            throw AppException.BadRequest("URL must be an absolute http or https address.");
        }

        for (var hop = 0; hop <= MaxRedirects; hop++)
        {
            ValidateUrl(current);
            var addresses = await dns.ResolveAsync(current.Host, cancellationToken);
            if (addresses.Count == 0)
            {
                throw AppException.BadRequest("The host did not resolve.");
            }

            if (addresses.Any(AddressPolicy.IsBlocked))
            {
                logger.LogWarning("Blocked metadata fetch to {Host} because a resolved address is restricted", current.Host);
                throw AppException.BadRequest("That address cannot be fetched.");
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            using var client = CreateClient(addresses[0]);
            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            request.Headers.UserAgent.ParseAdd("ReadingHub/1.0 (+https://github.com/Ztaclie/readinghub)");
            request.Headers.Accept.ParseAdd("text/html,application/xhtml+xml");

            HttpResponseMessage response;
            try
            {
                response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw AppException.BadRequest("The site took too long to respond.");
            }
            catch (HttpRequestException ex)
            {
                logger.LogInformation(ex, "Metadata fetch failed for {Url}", current);
                throw AppException.BadRequest("The site could not be reached.");
            }

            using (response)
            {
                if ((int)response.StatusCode is >= 300 and < 400)
                {
                    var location = response.Headers.Location;
                    if (location is null)
                    {
                        throw AppException.BadRequest("The site returned a redirect without a location.");
                    }

                    if (hop == MaxRedirects)
                    {
                        throw AppException.BadRequest("Too many redirects.");
                    }

                    current = location.IsAbsoluteUri ? location : new Uri(current, location);
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    throw AppException.BadRequest($"The site returned {(int)response.StatusCode}.");
                }

                var mediaType = response.Content.Headers.ContentType?.MediaType;
                if (mediaType is not ("text/html" or "application/xhtml+xml"))
                {
                    throw AppException.BadRequest("Only HTML pages can be read for metadata.");
                }

                await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
                var html = await ReadLimitedAsync(stream, timeout.Token);
                return new FetchedDocument(current, html, mediaType);
            }
        }

        throw AppException.BadRequest("Too many redirects.");
    }

    private HttpClient CreateClient(IPAddress approved)
    {
        if (handler is not null)
        {
            return new HttpClient(handler, disposeHandler: false) { Timeout = TimeSpan.FromSeconds(10) };
        }

        var sockets = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            AutomaticDecompression = DecompressionMethods.None,
            ConnectTimeout = TimeSpan.FromSeconds(5),
            ConnectCallback = async (context, cancellationToken) =>
            {
                var socket = new Socket(approved.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                try
                {
                    await socket.ConnectAsync(new IPEndPoint(approved, context.DnsEndPoint.Port), cancellationToken);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            }
        };
        return new HttpClient(sockets) { Timeout = TimeSpan.FromSeconds(10) };
    }

    private static void ValidateUrl(Uri uri)
    {
        if (uri.Scheme is not ("http" or "https"))
        {
            throw AppException.BadRequest("Only http and https URLs are supported.");
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            throw AppException.BadRequest("URLs must not include credentials.");
        }
    }

    private static async Task<string> ReadLimitedAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        while (true)
        {
            var read = await stream.ReadAsync(chunk, cancellationToken);
            if (read == 0)
            {
                break;
            }

            if (buffer.Length + read > MaxBytes)
            {
                throw AppException.BadRequest("The page is larger than 1 MB.");
            }

            buffer.Write(chunk, 0, read);
        }

        return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
    }
}
