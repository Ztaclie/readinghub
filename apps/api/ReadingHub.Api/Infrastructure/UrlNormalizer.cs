namespace ReadingHub.Api.Infrastructure;

public static class UrlNormalizer
{
    private static readonly HashSet<string> TrackingParams = new(StringComparer.OrdinalIgnoreCase)
    {
        "fbclid", "gclid", "mc_cid", "mc_eid", "ref", "ref_src", "igshid"
    };

    public static string Normalize(string url)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
        {
            throw AppException.BadRequest("URL must be an absolute http or https address.");
        }

        return Normalize(uri);
    }

    public static string Normalize(Uri uri)
    {
        if (uri.Scheme is not ("http" or "https"))
        {
            throw AppException.BadRequest("Only http and https URLs are supported.");
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            throw AppException.BadRequest("URLs must not include credentials.");
        }

        var builder = new UriBuilder(uri)
        {
            Scheme = uri.Scheme.ToLowerInvariant(),
            Host = uri.Host.ToLowerInvariant(),
            Port = uri.IsDefaultPort ? -1 : uri.Port,
            Fragment = ""
        };

        var query = ParseQuery(uri.Query)
            .Where(pair => !IsTracking(pair.Key))
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ThenBy(pair => pair.Value, StringComparer.Ordinal)
            .ToList();
        builder.Query = query.Count == 0
            ? ""
            : string.Join("&", query.Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));

        var path = builder.Path;
        if (path.Length > 1 && path.EndsWith('/'))
        {
            builder.Path = path.TrimEnd('/');
        }

        return builder.Uri.AbsoluteUri;
    }

    public static string DomainOf(string url)
    {
        var uri = new Uri(url);
        return uri.Host.ToLowerInvariant();
    }

    private static bool IsTracking(string key) =>
        key.StartsWith("utm_", StringComparison.OrdinalIgnoreCase) || TrackingParams.Contains(key);

    private static IEnumerable<KeyValuePair<string, string>> ParseQuery(string query)
    {
        if (string.IsNullOrEmpty(query))
        {
            yield break;
        }

        var trimmed = query.StartsWith('?') ? query[1..] : query;
        if (trimmed.Length == 0)
        {
            yield break;
        }

        foreach (var part in trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var split = part.Split('=', 2);
            var key = Uri.UnescapeDataString(split[0].Replace('+', ' '));
            var value = split.Length > 1 ? Uri.UnescapeDataString(split[1].Replace('+', ' ')) : "";
            if (key.Length > 0)
            {
                yield return new KeyValuePair<string, string>(key, value);
            }
        }
    }
}
