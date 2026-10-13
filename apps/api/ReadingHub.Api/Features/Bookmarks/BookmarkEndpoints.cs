using System.Text;
using System.Text.Json;
using AngleSharp.Html.Parser;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using ReadingHub.Api.Data;
using ReadingHub.Api.Domain;
using ReadingHub.Api.Features.Auth;
using ReadingHub.Api.Infrastructure;
using ReadingHub.Api.Security;

namespace ReadingHub.Api.Features.Bookmarks;

public sealed class BookmarkWriteRequest
{
    public string Url { get; set; } = "";
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? Notes { get; set; }
    public bool FetchMetadata { get; set; } = true;
    public List<Guid>? CollectionIds { get; set; }
    public List<string>? Tags { get; set; }
}

public sealed record TagResponse(Guid Id, string Name);
public sealed record BookmarkResponse(
    Guid Id,
    string Url,
    string NormalizedUrl,
    string? CanonicalUrl,
    string Title,
    string? Description,
    string? SiteName,
    string? FaviconUrl,
    string? ImageUrl,
    string? Notes,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<Guid> CollectionIds,
    IReadOnlyList<TagResponse> Tags);

public sealed record PageResponse<T>(IReadOnlyList<T> Items, string? NextCursor);

public sealed record CollectionWriteRequest(string Name, string? Description, string? Icon, string? Color, Guid? ParentId, bool IsPinned, int SortOrder);
public sealed record CollectionResponse(Guid Id, Guid? ParentId, string Name, string? Description, string? Icon, string? Color, bool IsPinned, int SortOrder, int BookmarkCount);
public sealed record TagWriteRequest(string Name);
public sealed record ExtractRequest(string Url);
public sealed record ImportRequest(string Content);
public sealed record ImportResponse(int Created, int Skipped);

public sealed class BookmarkWriteValidator : AbstractValidator<BookmarkWriteRequest>
{
    public BookmarkWriteValidator()
    {
        RuleFor(request => request.Url).NotEmpty().MaximumLength(2000);
        RuleFor(request => request.Title).MaximumLength(500);
        RuleFor(request => request.Notes).MaximumLength(8000);
        RuleFor(request => request.Description).MaximumLength(4000);
    }
}

public sealed class CollectionWriteValidator : AbstractValidator<CollectionWriteRequest>
{
    public CollectionWriteValidator()
    {
        RuleFor(request => request.Name).NotEmpty().MaximumLength(120);
        RuleFor(request => request.Description).MaximumLength(2000);
        RuleFor(request => request.Icon).MaximumLength(32);
        RuleFor(request => request.Color).Matches("^#([0-9a-fA-F]{6})$").When(request => !string.IsNullOrWhiteSpace(request.Color));
    }
}

public static class BookmarkEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        var bookmarks = group.MapGroup("/bookmarks").WithTags("Bookmarks").RequireAuthorization();
        bookmarks.MapGet("/", List);
        bookmarks.MapPost("/", Create);
        bookmarks.MapGet("/export", Export);
        bookmarks.MapPost("/import", Import);
        bookmarks.MapGet("/{id:guid}", Get);
        bookmarks.MapPut("/{id:guid}", Update);
        bookmarks.MapDelete("/{id:guid}", Delete);

        var collections = group.MapGroup("/collections").WithTags("Collections").RequireAuthorization();
        collections.MapGet("/", ListCollections);
        collections.MapPost("/", CreateCollection);
        collections.MapPut("/{id:guid}", UpdateCollection);
        collections.MapDelete("/{id:guid}", DeleteCollection);

        var tags = group.MapGroup("/tags").WithTags("Tags").RequireAuthorization();
        tags.MapGet("/", ListTags);
        tags.MapPost("/", CreateTag);
        tags.MapDelete("/{id:guid}", DeleteTag);

        group.MapPost("/metadata/extract", Extract).WithTags("Metadata").RequireAuthorization();
    }

    private static async Task<IResult> List(
        HttpContext httpContext,
        AppDbContext db,
        string? q,
        Guid? collectionId,
        string? tag,
        string? sort,
        string? cursor,
        int limit = 24,
        CancellationToken cancellationToken = default)
    {
        limit = Math.Clamp(limit, 1, 100);
        var userId = CurrentUser.Id(httpContext);
        var query = db.Bookmarks.AsNoTracking().Where(bookmark => bookmark.UserId == userId);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var pattern = $"%{q.Trim()}%";
            query = query.Where(bookmark =>
                EF.Functions.ILike(bookmark.Title, pattern) ||
                EF.Functions.ILike(bookmark.Url, pattern) ||
                (bookmark.Description != null && EF.Functions.ILike(bookmark.Description, pattern)));
        }

        if (collectionId is not null)
        {
            query = query.Where(bookmark => bookmark.Collections.Any(link => link.CollectionId == collectionId));
        }

        if (!string.IsNullOrWhiteSpace(tag))
        {
            var normalized = tag.Trim().ToLowerInvariant();
            query = query.Where(bookmark => bookmark.Tags.Any(link => link.Tag.NormalizedName == normalized));
        }

        sort = sort?.ToLowerInvariant() ?? "created";
        if (TryReadCursor(cursor, out var cursorTime, out var cursorTitle, out var cursorId))
        {
            query = sort switch
            {
                "title" => query.Where(bookmark => bookmark.Title.CompareTo(cursorTitle) > 0 || (bookmark.Title == cursorTitle && bookmark.Id.CompareTo(cursorId) > 0)),
                "updated" => query.Where(bookmark => bookmark.UpdatedAt < cursorTime || (bookmark.UpdatedAt == cursorTime && bookmark.Id.CompareTo(cursorId) < 0)),
                _ => query.Where(bookmark => bookmark.CreatedAt < cursorTime || (bookmark.CreatedAt == cursorTime && bookmark.Id.CompareTo(cursorId) < 0))
            };
        }

        query = sort switch
        {
            "title" => query.OrderBy(bookmark => bookmark.Title).ThenBy(bookmark => bookmark.Id),
            "updated" => query.OrderByDescending(bookmark => bookmark.UpdatedAt).ThenByDescending(bookmark => bookmark.Id),
            _ => query.OrderByDescending(bookmark => bookmark.CreatedAt).ThenByDescending(bookmark => bookmark.Id)
        };

        var rows = await query.AsSplitQuery().Include(bookmark => bookmark.Collections).Include(bookmark => bookmark.Tags).ThenInclude(link => link.Tag)
            .Take(limit + 1)
            .ToListAsync(cancellationToken);
        var page = rows.Take(limit).Select(ToBookmark).ToList();
        string? next = null;
        if (rows.Count > limit)
        {
            var last = rows[limit - 1];
            next = EncodeCursor(sort == "title" ? null : sort == "updated" ? last.UpdatedAt : last.CreatedAt, sort == "title" ? last.Title : null, last.Id);
        }

        return Results.Ok(new PageResponse<BookmarkResponse>(page, next));
    }

    private static async Task<IResult> Create(
        BookmarkWriteRequest? request,
        HttpContext httpContext,
        AppDbContext db,
        SafeHttpFetcher fetcher,
        MetadataParser parser,
        CancellationToken cancellationToken)
    {
        if (await AuthEndpoints.RejectCookieCsrf(httpContext) is { } csrf)
        {
            return csrf;
        }

        var problem = await Validate(request, new BookmarkWriteValidator());
        if (problem is not null || request is null)
        {
            return problem!;
        }

        var userId = CurrentUser.Id(httpContext);
        var normalized = UrlNormalizer.Normalize(request.Url);
        var existing = await db.Bookmarks.AsNoTracking().FirstOrDefaultAsync(bookmark => bookmark.UserId == userId && bookmark.NormalizedUrl == normalized, cancellationToken);
        if (existing is not null)
        {
            throw AppException.Conflict("A bookmark for this URL already exists.", new Dictionary<string, object?> { ["existingId"] = existing.Id });
        }

        var metadata = await TryMetadata(request, normalized, fetcher, parser, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var bookmark = new Bookmark
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Url = request.Url.Trim(),
            NormalizedUrl = normalized,
            CanonicalUrl = metadata?.CanonicalUrl,
            Title = FirstText(request.Title, metadata?.Title, new Uri(normalized).Host),
            Description = request.Description ?? metadata?.Description,
            SiteName = metadata?.SiteName,
            FaviconUrl = metadata?.FaviconUrl,
            ImageUrl = metadata?.ImageUrl,
            Notes = request.Notes,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.Bookmarks.Add(bookmark);
        await AssignLinks(db, bookmark, userId, request.CollectionIds, request.Tags, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return Results.Created($"/api/v1/bookmarks/{bookmark.Id}", await Load(db, userId, bookmark.Id, cancellationToken));
    }

    private static async Task<IResult> Get(Guid id, HttpContext httpContext, AppDbContext db, CancellationToken cancellationToken)
    {
        return Results.Ok(await Load(db, CurrentUser.Id(httpContext), id, cancellationToken));
    }

    private static async Task<IResult> Update(
        Guid id,
        BookmarkWriteRequest? request,
        HttpContext httpContext,
        AppDbContext db,
        SafeHttpFetcher fetcher,
        MetadataParser parser,
        CancellationToken cancellationToken)
    {
        if (await AuthEndpoints.RejectCookieCsrf(httpContext) is { } csrf)
        {
            return csrf;
        }

        var problem = await Validate(request, new BookmarkWriteValidator());
        if (problem is not null || request is null)
        {
            return problem!;
        }

        var userId = CurrentUser.Id(httpContext);
        var bookmark = await db.Bookmarks.Include(item => item.Collections).Include(item => item.Tags)
            .FirstOrDefaultAsync(item => item.Id == id && item.UserId == userId, cancellationToken)
            ?? throw AppException.NotFound("Bookmark not found.");
        var normalized = UrlNormalizer.Normalize(request.Url);
        var duplicate = await db.Bookmarks.AnyAsync(item => item.UserId == userId && item.NormalizedUrl == normalized && item.Id != id, cancellationToken);
        if (duplicate)
        {
            throw AppException.Conflict("A bookmark for this URL already exists.");
        }

        var metadata = request.FetchMetadata ? await TryMetadata(request, normalized, fetcher, parser, cancellationToken) : null;
        bookmark.Url = request.Url.Trim();
        bookmark.NormalizedUrl = normalized;
        bookmark.Title = FirstText(request.Title, metadata?.Title, bookmark.Title);
        bookmark.Description = request.Description ?? metadata?.Description ?? bookmark.Description;
        bookmark.Notes = request.Notes;
        bookmark.CanonicalUrl = metadata?.CanonicalUrl ?? bookmark.CanonicalUrl;
        bookmark.SiteName = metadata?.SiteName ?? bookmark.SiteName;
        bookmark.FaviconUrl = metadata?.FaviconUrl ?? bookmark.FaviconUrl;
        bookmark.ImageUrl = metadata?.ImageUrl ?? bookmark.ImageUrl;
        bookmark.UpdatedAt = DateTimeOffset.UtcNow;
        bookmark.Collections.Clear();
        bookmark.Tags.Clear();
        await AssignLinks(db, bookmark, userId, request.CollectionIds, request.Tags, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(await Load(db, userId, bookmark.Id, cancellationToken));
    }

    private static async Task<IResult> Delete(Guid id, HttpContext httpContext, AppDbContext db, CancellationToken cancellationToken)
    {
        if (await AuthEndpoints.RejectCookieCsrf(httpContext) is { } csrf)
        {
            return csrf;
        }

        var bookmark = await db.Bookmarks.FirstOrDefaultAsync(item => item.Id == id && item.UserId == CurrentUser.Id(httpContext), cancellationToken)
            ?? throw AppException.NotFound("Bookmark not found.");
        db.Bookmarks.Remove(bookmark);
        await db.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> Export(HttpContext httpContext, AppDbContext db, CancellationToken cancellationToken)
    {
        var userId = CurrentUser.Id(httpContext);
        var bookmarks = await db.Bookmarks.AsNoTracking().Where(item => item.UserId == userId)
            .Include(item => item.Collections).Include(item => item.Tags).ThenInclude(link => link.Tag)
            .OrderByDescending(item => item.CreatedAt)
            .ToListAsync(cancellationToken);
        var collections = await db.Collections.AsNoTracking().Where(item => item.UserId == userId).OrderBy(item => item.Name).ToListAsync(cancellationToken);
        var payload = new
        {
            exportedAt = DateTimeOffset.UtcNow,
            collections = collections.Select(item => new { item.Id, item.ParentId, item.Name, item.Description, item.Icon, item.Color, item.IsPinned, item.SortOrder }),
            bookmarks = bookmarks.Select(ToBookmark)
        };
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
        return Results.File(Encoding.UTF8.GetBytes(json), "application/json", "readinghub-bookmarks.json");
    }

    private static async Task<IResult> Import(ImportRequest? request, HttpContext httpContext, AppDbContext db, CancellationToken cancellationToken)
    {
        if (await AuthEndpoints.RejectCookieCsrf(httpContext) is { } csrf)
        {
            return csrf;
        }

        if (request is null || string.IsNullOrWhiteSpace(request.Content))
        {
            throw AppException.BadRequest("Bookmark HTML is required.");
        }

        if (request.Content.Length > 2_000_000)
        {
            throw AppException.BadRequest("Import file is too large.");
        }

        var userId = CurrentUser.Id(httpContext);
        var parser = new HtmlParser();
        var document = parser.ParseDocument(request.Content);
        var created = 0;
        var skipped = 0;
        var folderIds = new Dictionary<string, Guid>(StringComparer.Ordinal);
        var root = document.QuerySelector("dl");
        if (root is null)
        {
            throw AppException.BadRequest("The file is not a Netscape bookmark export.");
        }

        await WalkFolder(root, null, "root");
        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(new ImportResponse(created, skipped));

        async Task WalkFolder(AngleSharp.Dom.IElement dl, Guid? parentId, string path)
        {
            foreach (var node in dl.Children)
            {
                if (!node.NodeName.Equals("DT", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var heading = node.Children.FirstOrDefault(child => child.NodeName.Equals("H3", StringComparison.OrdinalIgnoreCase));
                var link = node.Children.FirstOrDefault(child => child.NodeName.Equals("A", StringComparison.OrdinalIgnoreCase));
                if (heading is not null)
                {
                    var name = heading.TextContent.Trim();
                    if (name.Length == 0)
                    {
                        continue;
                    }

                    var key = path + "/" + name.ToLowerInvariant();
                    if (!folderIds.TryGetValue(key, out var collectionId))
                    {
                        var existing = await db.Collections.FirstOrDefaultAsync(
                            item => item.UserId == userId && item.ParentKey == (parentId ?? Guid.Empty) && item.Name == name,
                            cancellationToken);
                        if (existing is null)
                        {
                            existing = new Collection
                            {
                                Id = Guid.NewGuid(),
                                UserId = userId,
                                ParentId = parentId,
                                ParentKey = parentId ?? Guid.Empty,
                                Name = name[..Math.Min(name.Length, 120)],
                                CreatedAt = DateTimeOffset.UtcNow,
                                UpdatedAt = DateTimeOffset.UtcNow
                            };
                            db.Collections.Add(existing);
                        }

                        collectionId = existing.Id;
                        folderIds[key] = collectionId;
                    }

                    var child = node.NextElementSibling;
                    if (child is not null && child.NodeName.Equals("DL", StringComparison.OrdinalIgnoreCase))
                    {
                        await WalkFolder(child, collectionId, key);
                    }
                }
                else if (link?.GetAttribute("href") is { } href)
                {
                    string normalized;
                    try
                    {
                        normalized = UrlNormalizer.Normalize(href);
                    }
                    catch (AppException)
                    {
                        skipped++;
                        continue;
                    }

                    if (await db.Bookmarks.AnyAsync(item => item.UserId == userId && item.NormalizedUrl == normalized, cancellationToken))
                    {
                        skipped++;
                        continue;
                    }

                    var notes = node.NextElementSibling is { NodeName: "DD" } dd ? dd.TextContent.Trim() : null;
                    var bookmark = new Bookmark
                    {
                        Id = Guid.NewGuid(),
                        UserId = userId,
                        Url = href.Trim(),
                        NormalizedUrl = normalized,
                        Title = string.IsNullOrWhiteSpace(link.TextContent) ? new Uri(normalized).Host : link.TextContent.Trim(),
                        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes,
                        CreatedAt = DateTimeOffset.UtcNow,
                        UpdatedAt = DateTimeOffset.UtcNow
                    };
                    db.Bookmarks.Add(bookmark);
                    if (parentId is not null)
                    {
                        db.BookmarkCollections.Add(new BookmarkCollection { BookmarkId = bookmark.Id, CollectionId = parentId.Value });
                    }

                    created++;
                    if (created > 5000)
                    {
                        throw AppException.BadRequest("Imports are limited to 5000 new bookmarks.");
                    }
                }
            }
        }
    }

    private static async Task<IResult> ListCollections(HttpContext httpContext, AppDbContext db, CancellationToken cancellationToken)
    {
        var userId = CurrentUser.Id(httpContext);
        var rows = await db.Collections.AsNoTracking()
            .Where(item => item.UserId == userId)
            .OrderBy(item => item.SortOrder).ThenBy(item => item.Name)
            .Select(item => new CollectionResponse(
                item.Id,
                item.ParentId,
                item.Name,
                item.Description,
                item.Icon,
                item.Color,
                item.IsPinned,
                item.SortOrder,
                item.Bookmarks.Count))
            .ToListAsync(cancellationToken);
        return Results.Ok(rows);
    }

    private static async Task<IResult> CreateCollection(CollectionWriteRequest? request, HttpContext httpContext, AppDbContext db, CancellationToken cancellationToken)
    {
        if (await AuthEndpoints.RejectCookieCsrf(httpContext) is { } csrf)
        {
            return csrf;
        }

        var problem = await Validate(request, new CollectionWriteValidator());
        if (problem is not null || request is null)
        {
            return problem!;
        }

        var userId = CurrentUser.Id(httpContext);
        await EnsureDepth(db, userId, request.ParentId, null, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var collection = new Collection
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ParentId = request.ParentId,
            ParentKey = request.ParentId ?? Guid.Empty,
            Name = request.Name.Trim(),
            Description = request.Description,
            Icon = request.Icon,
            Color = request.Color,
            IsPinned = request.IsPinned,
            SortOrder = request.SortOrder,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.Collections.Add(collection);
        await db.SaveChangesAsync(cancellationToken);
        return Results.Created($"/api/v1/collections/{collection.Id}", ToCollection(collection, 0));
    }

    private static async Task<IResult> UpdateCollection(Guid id, CollectionWriteRequest? request, HttpContext httpContext, AppDbContext db, CancellationToken cancellationToken)
    {
        if (await AuthEndpoints.RejectCookieCsrf(httpContext) is { } csrf)
        {
            return csrf;
        }

        var problem = await Validate(request, new CollectionWriteValidator());
        if (problem is not null || request is null)
        {
            return problem!;
        }

        var userId = CurrentUser.Id(httpContext);
        var collection = await db.Collections.FirstOrDefaultAsync(item => item.Id == id && item.UserId == userId, cancellationToken)
            ?? throw AppException.NotFound("Collection not found.");
        await EnsureDepth(db, userId, request.ParentId, collection.Id, cancellationToken);
        collection.ParentId = request.ParentId;
        collection.ParentKey = request.ParentId ?? Guid.Empty;
        collection.Name = request.Name.Trim();
        collection.Description = request.Description;
        collection.Icon = request.Icon;
        collection.Color = request.Color;
        collection.IsPinned = request.IsPinned;
        collection.SortOrder = request.SortOrder;
        collection.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        var count = await db.BookmarkCollections.CountAsync(link => link.CollectionId == id, cancellationToken);
        return Results.Ok(ToCollection(collection, count));
    }

    private static async Task<IResult> DeleteCollection(Guid id, HttpContext httpContext, AppDbContext db, CancellationToken cancellationToken)
    {
        if (await AuthEndpoints.RejectCookieCsrf(httpContext) is { } csrf)
        {
            return csrf;
        }

        var userId = CurrentUser.Id(httpContext);
        var collection = await db.Collections.FirstOrDefaultAsync(item => item.Id == id && item.UserId == userId, cancellationToken)
            ?? throw AppException.NotFound("Collection not found.");
        if (await db.Collections.AnyAsync(item => item.ParentId == id, cancellationToken))
        {
            throw AppException.Conflict("Move or delete child collections first.");
        }

        db.Collections.Remove(collection);
        await db.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> ListTags(HttpContext httpContext, AppDbContext db, CancellationToken cancellationToken)
    {
        var tags = await db.Tags.AsNoTracking()
            .Where(item => item.UserId == CurrentUser.Id(httpContext))
            .OrderBy(item => item.Name)
            .Select(item => new TagResponse(item.Id, item.Name))
            .ToListAsync(cancellationToken);
        return Results.Ok(tags);
    }

    private static async Task<IResult> CreateTag(TagWriteRequest? request, HttpContext httpContext, AppDbContext db, CancellationToken cancellationToken)
    {
        if (await AuthEndpoints.RejectCookieCsrf(httpContext) is { } csrf)
        {
            return csrf;
        }

        if (request is null || string.IsNullOrWhiteSpace(request.Name))
        {
            throw AppException.BadRequest("Tag name is required.");
        }

        var userId = CurrentUser.Id(httpContext);
        var tag = await GetOrCreateTag(db, userId, request.Name, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(new TagResponse(tag.Id, tag.Name));
    }

    private static async Task<IResult> DeleteTag(Guid id, HttpContext httpContext, AppDbContext db, CancellationToken cancellationToken)
    {
        if (await AuthEndpoints.RejectCookieCsrf(httpContext) is { } csrf)
        {
            return csrf;
        }

        var tag = await db.Tags.FirstOrDefaultAsync(item => item.Id == id && item.UserId == CurrentUser.Id(httpContext), cancellationToken)
            ?? throw AppException.NotFound("Tag not found.");
        db.Tags.Remove(tag);
        await db.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> Extract(
        ExtractRequest? request,
        HttpContext httpContext,
        SafeHttpFetcher fetcher,
        MetadataParser parser,
        CancellationToken cancellationToken)
    {
        if (await AuthEndpoints.RejectCookieCsrf(httpContext) is { } csrf)
        {
            return csrf;
        }

        if (request is null || string.IsNullOrWhiteSpace(request.Url))
        {
            throw AppException.BadRequest("URL is required.");
        }

        var document = await fetcher.FetchAsync(request.Url, cancellationToken);
        var metadata = await parser.ParseAsync(document.Html, document.FinalUri);
        return Results.Ok(metadata);
    }

    private static async Task<PageMetadata?> TryMetadata(
        BookmarkWriteRequest request,
        string normalized,
        SafeHttpFetcher fetcher,
        MetadataParser parser,
        CancellationToken cancellationToken)
    {
        if (!request.FetchMetadata)
        {
            return null;
        }

        try
        {
            var document = await fetcher.FetchAsync(request.Url, cancellationToken);
            return await parser.ParseAsync(document.Html, document.FinalUri);
        }
        catch (AppException)
        {
            return new PageMetadata(new Uri(normalized).Host, null, new Uri(normalized).Host, null, null, null);
        }
    }

    private static async Task AssignLinks(AppDbContext db, Bookmark bookmark, Guid userId, List<Guid>? collectionIds, List<string>? tags, CancellationToken cancellationToken)
    {
        foreach (var collectionId in (collectionIds ?? []).Distinct())
        {
            if (!await db.Collections.AnyAsync(item => item.Id == collectionId && item.UserId == userId, cancellationToken))
            {
                throw AppException.BadRequest("A selected collection does not exist.");
            }

            bookmark.Collections.Add(new BookmarkCollection { BookmarkId = bookmark.Id, CollectionId = collectionId });
        }

        foreach (var name in (tags ?? []).Where(name => !string.IsNullOrWhiteSpace(name)).Select(name => name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Take(20))
        {
            var tag = await GetOrCreateTag(db, userId, name, cancellationToken);
            bookmark.Tags.Add(new BookmarkTag { BookmarkId = bookmark.Id, TagId = tag.Id });
        }
    }

    private static async Task<Tag> GetOrCreateTag(AppDbContext db, Guid userId, string name, CancellationToken cancellationToken)
    {
        var trimmed = name.Trim();
        if (trimmed.Length is 0 or > 60)
        {
            throw AppException.BadRequest("Tag names must be 1 to 60 characters.");
        }

        var normalized = trimmed.ToLowerInvariant();
        var tag = await db.Tags.FirstOrDefaultAsync(item => item.UserId == userId && item.NormalizedName == normalized, cancellationToken);
        if (tag is not null)
        {
            return tag;
        }

        tag = new Tag { Id = Guid.NewGuid(), UserId = userId, Name = trimmed, NormalizedName = normalized, CreatedAt = DateTimeOffset.UtcNow };
        db.Tags.Add(tag);
        return tag;
    }

    private static async Task EnsureDepth(AppDbContext db, Guid userId, Guid? parentId, Guid? selfId, CancellationToken cancellationToken)
    {
        var depth = 1;
        var cursor = parentId;
        var guard = 0;
        while (cursor is not null)
        {
            if (cursor == selfId)
            {
                throw AppException.BadRequest("A collection cannot contain itself.");
            }

            var parent = await db.Collections.AsNoTracking().FirstOrDefaultAsync(item => item.Id == cursor && item.UserId == userId, cancellationToken)
                ?? throw AppException.BadRequest("Parent collection not found.");
            depth++;
            cursor = parent.ParentId;
            if (++guard > 8)
            {
                throw AppException.BadRequest("Collection nesting is too deep.");
            }
        }

        if (depth > 3)
        {
            throw AppException.BadRequest("Collections can be nested at most 3 levels deep.");
        }
    }

    private static async Task<BookmarkResponse> Load(AppDbContext db, Guid userId, Guid id, CancellationToken cancellationToken)
    {
        var bookmark = await db.Bookmarks.AsNoTracking()
            .Include(item => item.Collections)
            .Include(item => item.Tags).ThenInclude(link => link.Tag)
            .FirstOrDefaultAsync(item => item.Id == id && item.UserId == userId, cancellationToken)
            ?? throw AppException.NotFound("Bookmark not found.");
        return ToBookmark(bookmark);
    }

    private static BookmarkResponse ToBookmark(Bookmark bookmark) => new(
        bookmark.Id,
        bookmark.Url,
        bookmark.NormalizedUrl,
        bookmark.CanonicalUrl,
        bookmark.Title,
        bookmark.Description,
        bookmark.SiteName,
        bookmark.FaviconUrl,
        bookmark.ImageUrl,
        bookmark.Notes,
        bookmark.CreatedAt,
        bookmark.UpdatedAt,
        bookmark.Collections.Select(link => link.CollectionId).ToList(),
        bookmark.Tags.Select(link => new TagResponse(link.TagId, link.Tag.Name)).ToList());

    private static CollectionResponse ToCollection(Collection collection, int count) => new(
        collection.Id, collection.ParentId, collection.Name, collection.Description, collection.Icon, collection.Color, collection.IsPinned, collection.SortOrder, count);

    private static string FirstText(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "Untitled";

    private static string EncodeCursor(DateTimeOffset? time, string? title, Guid id)
    {
        var payload = JsonSerializer.Serialize(new Cursor(time, title, id));
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(payload));
    }

    private static bool TryReadCursor(string? cursor, out DateTimeOffset time, out string title, out Guid id)
    {
        time = default;
        title = "";
        id = default;
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return false;
        }

        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            var parsed = JsonSerializer.Deserialize<Cursor>(json);
            if (parsed is null)
            {
                return false;
            }

            time = parsed.Time ?? default;
            title = parsed.Title ?? "";
            id = parsed.Id;
            return true;
        }
        catch (FormatException)
        {
            throw AppException.BadRequest("Cursor is invalid.");
        }
    }

    private static async Task<IResult?> Validate<T>(T? model, AbstractValidator<T> validator)
    {
        if (model is null)
        {
            return Results.Problem(title: "Bad request", detail: "Request body is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        var result = await validator.ValidateAsync(model);
        return result.IsValid
            ? null
            : Results.ValidationProblem(result.Errors.GroupBy(error => error.PropertyName)
                .ToDictionary(group => group.Key, group => group.Select(error => error.ErrorMessage).ToArray()));
    }

    private sealed record Cursor(DateTimeOffset? Time, string? Title, Guid Id);
}
