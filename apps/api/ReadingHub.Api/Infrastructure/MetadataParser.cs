using System.Net;
using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace ReadingHub.Api.Infrastructure;

public sealed record PageMetadata(
    string Title,
    string? Description,
    string? SiteName,
    string? FaviconUrl,
    string? ImageUrl,
    string? CanonicalUrl);

public sealed class MetadataParser
{
    private readonly IBrowsingContext _context = BrowsingContext.New(Configuration.Default);

    public async Task<PageMetadata> ParseAsync(string html, Uri pageUri)
    {
        var document = await _context.OpenAsync(req => req.Content(html).Address(pageUri.AbsoluteUri));
        var title = First(
            Meta(document, "og:title"),
            Meta(document, "twitter:title"),
            document.QuerySelector("title")?.TextContent) ?? pageUri.Host;
        var description = First(Meta(document, "og:description"), Meta(document, "description"), Meta(document, "twitter:description"));
        var siteName = First(Meta(document, "og:site_name")) ?? pageUri.Host;
        var image = Absolute(pageUri, First(Meta(document, "og:image"), Meta(document, "twitter:image")));
        var icon = Absolute(pageUri, document.QuerySelector("link[rel~='icon']")?.GetAttribute("href")
            ?? document.QuerySelector("link[rel='shortcut icon']")?.GetAttribute("href"));
        var canonical = Absolute(pageUri, document.QuerySelector("link[rel='canonical']")?.GetAttribute("href"));

        return new PageMetadata(
            Clean(title) ?? pageUri.Host,
            Clean(description),
            Clean(siteName),
            SafeRemote(icon),
            SafeRemote(image),
            SafeRemote(canonical));
    }

    private static string? Meta(IDocument document, string name)
    {
        return document.QuerySelector($"meta[property='{name}']")?.GetAttribute("content")
            ?? document.QuerySelector($"meta[name='{name}']")?.GetAttribute("content");
    }

    private static string? First(params string?[] values) =>
        values.Select(Clean).FirstOrDefault(value => value is not null);

    private static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return trimmed.Length == 0 ? null : trimmed[..Math.Min(trimmed.Length, 2000)];
    }

    private static string? Absolute(Uri page, string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return null;
        }

        if (!Uri.TryCreate(page, candidate.Trim(), out var absolute))
        {
            return null;
        }

        return absolute.AbsoluteUri;
    }

    public static string? SafeRemote(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return null;
        }

        if (uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo))
        {
            return null;
        }

        if (IPAddress.TryParse(uri.IdnHost, out var ip) && AddressPolicy.IsBlocked(ip))
        {
            return null;
        }

        return uri.AbsoluteUri;
    }
}
