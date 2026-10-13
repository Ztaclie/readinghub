using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using ReadingHub.Api.Infrastructure;

namespace ReadingHub.Api.Tests;

public sealed class AddressPolicyTests
{
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.1.2.3")]
    [InlineData("172.16.0.5")]
    [InlineData("192.168.1.20")]
    [InlineData("169.254.169.254")]
    [InlineData("100.64.0.1")]
    [InlineData("0.0.0.0")]
    [InlineData("224.0.0.1")]
    [InlineData("::1")]
    [InlineData("fd00::1")]
    [InlineData("fe80::1")]
    public void Blocks_restricted_addresses(string value)
    {
        Assert.True(AddressPolicy.IsBlocked(IPAddress.Parse(value)));
    }

    [Theory]
    [InlineData("1.1.1.1")]
    [InlineData("8.8.8.8")]
    [InlineData("93.184.216.34")]
    public void Allows_public_addresses(string value)
    {
        Assert.False(AddressPolicy.IsBlocked(IPAddress.Parse(value)));
    }
}

public sealed class UrlNormalizerTests
{
    [Fact]
    public void Strips_tracking_params_and_lowercases_the_host()
    {
        var normalized = UrlNormalizer.Normalize("HTTPS://Example.COM/path/?b=2&utm_source=x&a=1#frag");
        Assert.Equal("https://example.com/path?a=1&b=2", normalized);
    }
}

public sealed class MetadataSecurityTests
{
    [Fact]
    public async Task Parser_reads_open_graph_and_ignores_scripts()
    {
        var parser = new MetadataParser();
        var html = """
            <html><head>
            <title>Ignored</title>
            <meta property="og:title" content="Shadow Slave" />
            <meta property="og:description" content="A long story" />
            <meta property="og:site_name" content="Example" />
            <meta property="og:image" content="/cover.jpg" />
            <link rel="canonical" href="https://example.com/story" />
            <link rel="icon" href="/favicon.ico" />
            <script>window.steal = true</script>
            </head><body><p>Chapter</p></body></html>
            """;
        var metadata = await parser.ParseAsync(html, new Uri("https://example.com/chapter"));
        Assert.Equal("Shadow Slave", metadata.Title);
        Assert.Equal("A long story", metadata.Description);
        Assert.Equal("https://example.com/story", metadata.CanonicalUrl);
        Assert.Equal("https://example.com/cover.jpg", metadata.ImageUrl);
        Assert.DoesNotContain("steal", metadata.Title, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Fetch_rejects_loopback_and_redirects_to_metadata_addresses()
    {
        var handler = new StubHandler
        {
            ResponseFactory = request =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.Redirect);
                response.Headers.Location = new Uri("http://169.254.169.254/latest/meta-data");
                return response;
            }
        };
        var fetcher = new SafeHttpFetcher(new HostDns(), NullLogger<SafeHttpFetcher>.Instance, handler);
        var error = await Assert.ThrowsAsync<AppException>(() => fetcher.FetchAsync("http://example.com/page", CancellationToken.None));
        Assert.Equal(StatusCodes.Status400BadRequest, error.StatusCode);
    }

    [Theory]
    [InlineData("http://127.0.0.1/secret")]
    [InlineData("http://10.0.0.8/admin")]
    [InlineData("http://192.168.0.2/router")]
    [InlineData("http://169.254.169.254/latest/meta-data")]
    public async Task Fetch_rejects_restricted_literals(string url)
    {
        var fetcher = new SafeHttpFetcher(new HostDns(), NullLogger<SafeHttpFetcher>.Instance, new StubHandler());
        await Assert.ThrowsAsync<AppException>(() => fetcher.FetchAsync(url, CancellationToken.None));
    }

    private sealed class HostDns : IDnsResolver
    {
        public Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken)
        {
            if (IPAddress.TryParse(host, out var ip))
            {
                return Task.FromResult<IReadOnlyList<IPAddress>>([ip]);
            }

            return Task.FromResult<IReadOnlyList<IPAddress>>([IPAddress.Parse("93.184.216.34")]);
        }
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, HttpResponseMessage>? ResponseFactory { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(ResponseFactory?.Invoke(request) ?? new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<html><title>Ok</title></html>", System.Text.Encoding.UTF8, "text/html")
            });
        }
    }
}
