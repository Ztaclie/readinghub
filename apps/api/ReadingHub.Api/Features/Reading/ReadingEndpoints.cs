using System.Text;
using System.Text.Json;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using ReadingHub.Api.Data;
using ReadingHub.Api.Domain;
using ReadingHub.Api.Features.Auth;
using ReadingHub.Api.Infrastructure;
using ReadingHub.Api.Security;

namespace ReadingHub.Api.Features.Reading;

public sealed record ReadingItemWriteRequest(string Title, string? Description, string? CoverImageUrl, string? Notes, ReadingStatus Status, Guid? BookmarkId);
public sealed record SourceWriteRequest(string Url, string? ExternalKey, SourceStatus Status);
public sealed record ChapterWriteRequest(string Url, string? Title, string Label, decimal? Number);
public sealed record ProgressWriteRequest(
    Guid ReadingItemId,
    Guid SourceId,
    string ChapterUrl,
    string? ChapterTitle,
    string ChapterLabel,
    decimal? ChapterNumber,
    decimal ProgressPercent,
    long BaseRevision,
    ProgressIntent Intent,
    Guid UpdateId,
    bool MarkCompleted);
public sealed record CursorWriteRequest(Guid SourceId, Guid ChapterId, long BaseRevision, Guid UpdateId);

public sealed record ChapterSummary(Guid Id, string Url, string Label, string? Title, decimal? Number, decimal? SortKey, DateTimeOffset? CompletedAt, decimal? ProgressPercent, long? Revision);
public sealed record SourceResponse(Guid Id, string Url, string Domain, string? ExternalKey, SourceStatus Status, string? LastDetectedChapterLabel, DateTimeOffset? LastCheckedAt, Guid? FurthestCompletedChapterId, ChapterSummary? CurrentChapter);
public sealed record ReadingItemResponse(
    Guid Id,
    string Title,
    string? Description,
    string? CoverImageUrl,
    string? Notes,
    ReadingStatus Status,
    Guid? BookmarkId,
    Guid? ActiveSourceId,
    Guid? ActiveChapterId,
    long CursorRevision,
    DateTimeOffset? LastReadAt,
    DateTimeOffset CreatedAt,
    decimal? ProgressPercent,
    string? ActiveChapterLabel,
    IReadOnlyList<SourceResponse> Sources);
public sealed record ProgressResponse(Guid ChapterId, decimal ProgressPercent, long Revision, bool Applied, bool Completed);
public sealed record CursorResponse(Guid? ActiveSourceId, Guid? ActiveChapterId, long CursorRevision, bool Applied);
public sealed record HistoryResponse(Guid Id, string EventType, string ItemTitle, string? ChapterLabel, string? Detail, DateTimeOffset OccurredAt, Guid? ReadingItemId);
public sealed record SettingsResponse(
    string Theme,
    string Locale,
    string DashboardView,
    Guid? DefaultCollectionId,
    bool AutoTracking,
    bool AutoComplete,
    int CompletionThreshold,
    bool HistoryEnabled,
    bool PositionRestoration,
    int SyncDebounceSeconds,
    IReadOnlyList<string> EnabledDomains,
    IReadOnlyList<string> DisabledDomains);
public sealed record SettingsWriteRequest(
    string Theme,
    string Locale,
    string DashboardView,
    Guid? DefaultCollectionId,
    bool AutoTracking,
    bool AutoComplete,
    int CompletionThreshold,
    bool HistoryEnabled,
    bool PositionRestoration,
    int SyncDebounceSeconds,
    List<string>? EnabledDomains,
    List<string>? DisabledDomains);
public sealed record ContinueCard(Guid Id, string Title, string? CoverImageUrl, string? ChapterLabel, decimal? ProgressPercent, DateTimeOffset? LastReadAt);
public sealed record DashboardResponse(
    IReadOnlyList<ContinueCard> ContinueReading,
    IReadOnlyList<Bookmarks.BookmarkResponse> RecentBookmarks,
    IReadOnlyList<HistoryResponse> RecentlyRead,
    IReadOnlyList<Bookmarks.CollectionResponse> PinnedCollections);

public sealed class ReadingItemWriteValidator : AbstractValidator<ReadingItemWriteRequest>
{
    public ReadingItemWriteValidator()
    {
        RuleFor(request => request.Title).NotEmpty().MaximumLength(300);
        RuleFor(request => request.Description).MaximumLength(8000);
        RuleFor(request => request.Notes).MaximumLength(8000);
        RuleFor(request => request.CoverImageUrl).MaximumLength(2000);
        RuleFor(request => request.Status).IsInEnum();
    }
}

public sealed class SettingsWriteValidator : AbstractValidator<SettingsWriteRequest>
{
    public SettingsWriteValidator()
    {
        RuleFor(request => request.Theme).Must(theme => theme is "light" or "dark" or "system");
        RuleFor(request => request.Locale).NotEmpty().MaximumLength(16);
        RuleFor(request => request.CompletionThreshold).InclusiveBetween(50, 100);
        RuleFor(request => request.SyncDebounceSeconds).InclusiveBetween(1, 60);
    }
}

public static class ReadingEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        var items = group.MapGroup("/reading-items").WithTags("Reading").RequireAuthorization();
        items.MapGet("/", List);
        items.MapPost("/", Create);
        items.MapGet("/{id:guid}", Get);
        items.MapPut("/{id:guid}", Update);
        items.MapDelete("/{id:guid}", Delete);
        items.MapPost("/{id:guid}/sources", AddSource);
        items.MapPut("/{id:guid}/sources/{sourceId:guid}", UpdateSource);
        items.MapDelete("/{id:guid}/sources/{sourceId:guid}", DeleteSource);
        items.MapPost("/{id:guid}/sources/{sourceId:guid}/chapters", AddChapter);
        items.MapGet("/{id:guid}/progress", GetProgress);
        items.MapPut("/{id:guid}/cursor", UpdateCursor);

        group.MapPut("/reading-progress", UpdateProgress).WithTags("Reading").RequireAuthorization();
        group.MapGet("/reading-history", ListHistory).WithTags("History").RequireAuthorization();
        group.MapGet("/reading-history/export", ExportHistory).WithTags("History").RequireAuthorization();
        group.MapDelete("/reading-history", DeleteHistory).WithTags("History").RequireAuthorization();
        group.MapGet("/settings", GetSettings).WithTags("Settings").RequireAuthorization();
        group.MapPut("/settings", UpdateSettings).WithTags("Settings").RequireAuthorization();
        group.MapGet("/dashboard", Dashboard).WithTags("Dashboard").RequireAuthorization();
    }

    private static async Task<IResult> List(
        HttpContext httpContext,
        AppDbContext db,
        string? q,
        ReadingStatus? status,
        string? sort,
        string? cursor,
        int limit = 24,
        CancellationToken cancellationToken = default)
    {
        limit = Math.Clamp(limit, 1, 100);
        var userId = CurrentUser.Id(httpContext);
        var query = db.ReadingItems.AsNoTracking().Where(item => item.UserId == userId);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var pattern = $"%{q.Trim()}%";
            query = query.Where(item => EF.Functions.ILike(item.Title, pattern));
        }

        if (status is not null)
        {
            query = query.Where(item => item.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(cursor))
        {
            try
            {
                var json = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
                var parsed = JsonSerializer.Deserialize<ItemCursor>(json);
                if (parsed is not null)
                {
                    query = query.Where(item => item.UpdatedAt < parsed.UpdatedAt || (item.UpdatedAt == parsed.UpdatedAt && item.Id.CompareTo(parsed.Id) < 0));
                }
            }
            catch (FormatException)
            {
                throw AppException.BadRequest("Cursor is invalid.");
            }
        }

        query = sort == "title"
            ? query.OrderBy(item => item.Title)
            : query.OrderByDescending(item => item.UpdatedAt).ThenByDescending(item => item.Id);
        var rows = await query.Include(item => item.Sources).ThenInclude(source => source.Chapters).ThenInclude(chapter => chapter.Position)
            .Include(item => item.ActiveChapter)
            .Take(limit + 1)
            .ToListAsync(cancellationToken);
        string? next = rows.Count > limit
            ? Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new ItemCursor(rows[limit - 1].UpdatedAt, rows[limit - 1].Id))))
            : null;
        return Results.Ok(new Bookmarks.PageResponse<ReadingItemResponse>(rows.Take(limit).Select(ToItem).ToList(), next));
    }

    private static async Task<IResult> Create(ReadingItemWriteRequest? request, HttpContext httpContext, AppDbContext db, CancellationToken cancellationToken)
    {
        if (await AuthEndpoints.RejectCookieCsrf(httpContext) is { } csrf)
        {
            return csrf;
        }

        var problem = await Validate(request, new ReadingItemWriteValidator());
        if (problem is not null || request is null)
        {
            return problem!;
        }

        var userId = CurrentUser.Id(httpContext);
        await EnsureBookmark(db, userId, request.BookmarkId, null, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var item = new ReadingItem
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Title = request.Title.Trim(),
            Description = request.Description,
            CoverImageUrl = MetadataParser.SafeRemote(request.CoverImageUrl),
            Notes = request.Notes,
            Status = request.Status,
            BookmarkId = request.BookmarkId,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.ReadingItems.Add(item);
        await AddHistory(db, userId, item, null, HistoryEventType.ReadingItemAdded, null, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return Results.Created($"/api/v1/reading-items/{item.Id}", ToItem(item));
    }

    private static async Task<IResult> Get(Guid id, HttpContext httpContext, AppDbContext db, CancellationToken cancellationToken)
    {
        return Results.Ok(ToItem(await Load(db, CurrentUser.Id(httpContext), id, cancellationToken)));
    }

    private static async Task<IResult> Update(Guid id, ReadingItemWriteRequest? request, HttpContext httpContext, AppDbContext db, CancellationToken cancellationToken)
    {
        if (await AuthEndpoints.RejectCookieCsrf(httpContext) is { } csrf)
        {
            return csrf;
        }

        var problem = await Validate(request, new ReadingItemWriteValidator());
        if (problem is not null || request is null)
        {
            return problem!;
        }

        var userId = CurrentUser.Id(httpContext);
        var item = await Load(db, userId, id, cancellationToken);
        await EnsureBookmark(db, userId, request.BookmarkId, item.Id, cancellationToken);
        var previous = item.Status;
        item.Title = request.Title.Trim();
        item.Description = request.Description;
        item.CoverImageUrl = MetadataParser.SafeRemote(request.CoverImageUrl);
        item.Notes = request.Notes;
        item.Status = request.Status;
        item.BookmarkId = request.BookmarkId;
        item.UpdatedAt = DateTimeOffset.UtcNow;
        if (previous != request.Status)
        {
            await AddHistory(db, userId, item, null, HistoryEventType.StatusChanged, request.Status.ToString(), cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(ToItem(item));
    }

    private static async Task<IResult> Delete(Guid id, HttpContext httpContext, AppDbContext db, CancellationToken cancellationToken)
    {
        if (await AuthEndpoints.RejectCookieCsrf(httpContext) is { } csrf)
        {
            return csrf;
        }

        var item = await db.ReadingItems.FirstOrDefaultAsync(entry => entry.Id == id && entry.UserId == CurrentUser.Id(httpContext), cancellationToken)
            ?? throw AppException.NotFound("Reading item not found.");
        db.ReadingItems.Remove(item);
        await db.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> AddSource(Guid id, SourceWriteRequest? request, HttpContext httpContext, AppDbContext db, CancellationToken cancellationToken)
    {
        if (await AuthEndpoints.RejectCookieCsrf(httpContext) is { } csrf)
        {
            return csrf;
        }

        var item = await Load(db, CurrentUser.Id(httpContext), id, cancellationToken);
        var source = await CreateSource(db, item, request, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return Results.Created($"/api/v1/reading-items/{id}/sources/{source.Id}", ToSource(source));
    }

    private static async Task<IResult> UpdateSource(Guid id, Guid sourceId, SourceWriteRequest? request, HttpContext httpContext, AppDbContext db, CancellationToken cancellationToken)
    {
        if (await AuthEndpoints.RejectCookieCsrf(httpContext) is { } csrf)
        {
            return csrf;
        }

        if (request is null || string.IsNullOrWhiteSpace(request.Url))
        {
            throw AppException.BadRequest("Source URL is required.");
        }

        var item = await Load(db, CurrentUser.Id(httpContext), id, cancellationToken);
        var source = item.Sources.FirstOrDefault(entry => entry.Id == sourceId) ?? throw AppException.NotFound("Source not found.");
        var normalized = UrlNormalizer.Normalize(request.Url);
        if (item.Sources.Any(entry => entry.Id != sourceId && entry.NormalizedUrl == normalized))
        {
            throw AppException.Conflict("This source URL is already linked.");
        }

        source.Url = request.Url.Trim();
        source.NormalizedUrl = normalized;
        source.Domain = UrlNormalizer.DomainOf(normalized);
        source.ExternalKey = request.ExternalKey;
        source.Status = request.Status;
        source.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(ToSource(source));
    }

    private static async Task<IResult> DeleteSource(Guid id, Guid sourceId, HttpContext httpContext, AppDbContext db, CancellationToken cancellationToken)
    {
        if (await AuthEndpoints.RejectCookieCsrf(httpContext) is { } csrf)
        {
            return csrf;
        }

        var item = await Load(db, CurrentUser.Id(httpContext), id, cancellationToken);
        var source = item.Sources.FirstOrDefault(entry => entry.Id == sourceId) ?? throw AppException.NotFound("Source not found.");
        if (item.ActiveSourceId == sourceId)
        {
            item.ActiveSourceId = null;
            item.ActiveChapterId = null;
        }

        db.ReadingSources.Remove(source);
        await db.SaveChangesAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> AddChapter(Guid id, Guid sourceId, ChapterWriteRequest? request, HttpContext httpContext, AppDbContext db, CancellationToken cancellationToken)
    {
        if (await AuthEndpoints.RejectCookieCsrf(httpContext) is { } csrf)
        {
            return csrf;
        }

        var item = await Load(db, CurrentUser.Id(httpContext), id, cancellationToken);
        var source = item.Sources.FirstOrDefault(entry => entry.Id == sourceId) ?? throw AppException.NotFound("Source not found.");
        var chapter = await UpsertChapter(db, source, request?.Url, request?.Title, request?.Label, request?.Number, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(ToChapter(chapter));
    }

    private static async Task<IResult> GetProgress(Guid id, HttpContext httpContext, AppDbContext db, CancellationToken cancellationToken)
    {
        var item = await Load(db, CurrentUser.Id(httpContext), id, cancellationToken);
        ChapterPosition? position = null;
        if (item.ActiveChapterId is not null)
        {
            position = await db.ChapterPositions.AsNoTracking().FirstOrDefaultAsync(entry => entry.ChapterId == item.ActiveChapterId, cancellationToken);
        }

        return Results.Ok(new
        {
            item.Id,
            item.CursorRevision,
            item.ActiveSourceId,
            item.ActiveChapterId,
            ActiveChapterLabel = item.ActiveChapter?.Label,
            ProgressPercent = position?.ProgressPercent,
            PositionRevision = position?.Revision,
            Sources = item.Sources.Select(ToSource)
        });
    }

    private static async Task<IResult> UpdateProgress(ProgressWriteRequest? request, HttpContext httpContext, AppDbContext db, CancellationToken cancellationToken)
    {
        if (await AuthEndpoints.RejectCookieCsrf(httpContext) is { } csrf)
        {
            return csrf;
        }

        if (request is null || string.IsNullOrWhiteSpace(request.ChapterUrl) || string.IsNullOrWhiteSpace(request.ChapterLabel))
        {
            throw AppException.BadRequest("Chapter URL and label are required.");
        }

        if (request.ProgressPercent is < 0 or > 100)
        {
            throw AppException.BadRequest("Progress must be between 0 and 100.");
        }

        var userId = CurrentUser.Id(httpContext);
        if (await db.AppliedUpdates.AnyAsync(entry => entry.UserId == userId && entry.UpdateId == request.UpdateId, cancellationToken))
        {
            var existingChapter = await FindChapter(db, request.SourceId, request.ChapterUrl, cancellationToken);
            var existingPosition = existingChapter is null
                ? null
                : await db.ChapterPositions.AsNoTracking().FirstOrDefaultAsync(entry => entry.UserId == userId && entry.ChapterId == existingChapter.Id, cancellationToken);
            return Results.Ok(new ProgressResponse(existingChapter?.Id ?? Guid.Empty, existingPosition?.ProgressPercent ?? request.ProgressPercent, existingPosition?.Revision ?? request.BaseRevision, false, existingChapter?.CompletedAt is not null));
        }

        var item = await db.ReadingItems.Include(entry => entry.Sources).ThenInclude(source => source.Chapters)
            .FirstOrDefaultAsync(entry => entry.Id == request.ReadingItemId && entry.UserId == userId, cancellationToken)
            ?? throw AppException.NotFound("Reading item not found.");
        var source = item.Sources.FirstOrDefault(entry => entry.Id == request.SourceId) ?? throw AppException.NotFound("Source not found.");
        var chapter = await UpsertChapter(db, source, request.ChapterUrl, request.ChapterTitle, request.ChapterLabel, request.ChapterNumber, cancellationToken);
        var position = await db.ChapterPositions.FirstOrDefaultAsync(entry => entry.UserId == userId && entry.ChapterId == chapter.Id, cancellationToken);
        var currentRevision = position?.Revision ?? 0;
        if (request.BaseRevision != currentRevision)
        {
            throw AppException.Conflict("Progress was updated somewhere else.", new Dictionary<string, object?>
            {
                ["chapterId"] = chapter.Id,
                ["progressPercent"] = position?.ProgressPercent ?? 0,
                ["revision"] = currentRevision
            });
        }

        var applied = true;
        if (position is null)
        {
            position = new ChapterPosition
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                ChapterId = chapter.Id,
                ProgressPercent = request.ProgressPercent,
                Revision = 1,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            db.ChapterPositions.Add(position);
        }
        else if (request.Intent == ProgressIntent.Advance && request.ProgressPercent < position.ProgressPercent)
        {
            applied = false;
        }
        else
        {
            position.ProgressPercent = request.ProgressPercent;
            position.Revision += 1;
            position.UpdatedAt = DateTimeOffset.UtcNow;
        }

        var completed = false;
        if (request.MarkCompleted && applied)
        {
            completed = true;
            chapter.CompletedAt ??= DateTimeOffset.UtcNow;
            await AdvanceFurthest(db, source, chapter, cancellationToken);
            await AddHistory(db, userId, item, chapter, HistoryEventType.ChapterCompleted, null, cancellationToken);
        }

        source.LastDetectedChapterLabel = chapter.Label;
        source.LastCheckedAt = DateTimeOffset.UtcNow;
        item.LastReadAt = DateTimeOffset.UtcNow;
        item.UpdatedAt = DateTimeOffset.UtcNow;
        db.AppliedUpdates.Add(new AppliedUpdate { UserId = userId, UpdateId = request.UpdateId, AppliedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(new ProgressResponse(chapter.Id, position.ProgressPercent, position.Revision, applied, completed || chapter.CompletedAt is not null));
    }

    private static async Task<IResult> UpdateCursor(Guid id, CursorWriteRequest? request, HttpContext httpContext, AppDbContext db, CancellationToken cancellationToken)
    {
        if (await AuthEndpoints.RejectCookieCsrf(httpContext) is { } csrf)
        {
            return csrf;
        }

        if (request is null)
        {
            throw AppException.BadRequest("Request body is required.");
        }

        var userId = CurrentUser.Id(httpContext);
        if (await db.AppliedUpdates.AnyAsync(entry => entry.UserId == userId && entry.UpdateId == request.UpdateId, cancellationToken))
        {
            var current = await db.ReadingItems.AsNoTracking().FirstAsync(entry => entry.Id == id && entry.UserId == userId, cancellationToken);
            return Results.Ok(new CursorResponse(current.ActiveSourceId, current.ActiveChapterId, current.CursorRevision, false));
        }

        var item = await db.ReadingItems.Include(entry => entry.Sources).ThenInclude(source => source.Chapters)
            .FirstOrDefaultAsync(entry => entry.Id == id && entry.UserId == userId, cancellationToken)
            ?? throw AppException.NotFound("Reading item not found.");
        if (request.BaseRevision != item.CursorRevision)
        {
            throw AppException.Conflict("The resume point was updated on another device.", new Dictionary<string, object?>
            {
                ["activeSourceId"] = item.ActiveSourceId,
                ["activeChapterId"] = item.ActiveChapterId,
                ["cursorRevision"] = item.CursorRevision
            });
        }

        var source = item.Sources.FirstOrDefault(entry => entry.Id == request.SourceId) ?? throw AppException.NotFound("Source not found.");
        var chapter = source.Chapters.FirstOrDefault(entry => entry.Id == request.ChapterId) ?? throw AppException.NotFound("Chapter not found.");
        item.ActiveSourceId = source.Id;
        item.ActiveChapterId = chapter.Id;
        item.CursorRevision += 1;
        item.LastReadAt = DateTimeOffset.UtcNow;
        item.UpdatedAt = DateTimeOffset.UtcNow;
        if (item.Status == ReadingStatus.PlanToRead)
        {
            item.Status = ReadingStatus.Reading;
        }

        await AddHistory(db, userId, item, chapter, HistoryEventType.ChapterOpened, null, cancellationToken);
        db.AppliedUpdates.Add(new AppliedUpdate { UserId = userId, UpdateId = request.UpdateId, AppliedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(new CursorResponse(item.ActiveSourceId, item.ActiveChapterId, item.CursorRevision, true));
    }

    private static async Task<IResult> ListHistory(HttpContext httpContext, AppDbContext db, string? cursor, int limit = 40, CancellationToken cancellationToken = default)
    {
        limit = Math.Clamp(limit, 1, 100);
        var query = db.ReadingHistory.AsNoTracking().Where(entry => entry.UserId == CurrentUser.Id(httpContext));
        if (!string.IsNullOrWhiteSpace(cursor))
        {
            try
            {
                var json = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
                var parsed = JsonSerializer.Deserialize<HistoryCursor>(json);
                if (parsed is not null)
                {
                    query = query.Where(entry => entry.OccurredAt < parsed.OccurredAt || (entry.OccurredAt == parsed.OccurredAt && entry.Id.CompareTo(parsed.Id) < 0));
                }
            }
            catch (FormatException)
            {
                throw AppException.BadRequest("Cursor is invalid.");
            }
        }

        var rows = await query.OrderByDescending(entry => entry.OccurredAt).ThenByDescending(entry => entry.Id).Take(limit + 1).ToListAsync(cancellationToken);
        string? next = rows.Count > limit
            ? Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new HistoryCursor(rows[limit - 1].OccurredAt, rows[limit - 1].Id))))
            : null;
        return Results.Ok(new Bookmarks.PageResponse<HistoryResponse>(rows.Take(limit).Select(ToHistory).ToList(), next));
    }

    private static async Task<IResult> ExportHistory(HttpContext httpContext, AppDbContext db, CancellationToken cancellationToken)
    {
        var rows = await db.ReadingHistory.AsNoTracking()
            .Where(entry => entry.UserId == CurrentUser.Id(httpContext))
            .OrderByDescending(entry => entry.OccurredAt)
            .ToListAsync(cancellationToken);
        var json = JsonSerializer.Serialize(rows.Select(ToHistory), new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
        return Results.File(Encoding.UTF8.GetBytes(json), "application/json", "readinghub-history.json");
    }

    private static async Task<IResult> DeleteHistory(HttpContext httpContext, AppDbContext db, CancellationToken cancellationToken)
    {
        if (await AuthEndpoints.RejectCookieCsrf(httpContext) is { } csrf)
        {
            return csrf;
        }

        await db.ReadingHistory.Where(entry => entry.UserId == CurrentUser.Id(httpContext)).ExecuteDeleteAsync(cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> GetSettings(HttpContext httpContext, AppDbContext db, CancellationToken cancellationToken)
    {
        var settings = await db.UserSettings.FirstAsync(entry => entry.UserId == CurrentUser.Id(httpContext), cancellationToken);
        return Results.Ok(ToSettings(settings));
    }

    private static async Task<IResult> UpdateSettings(SettingsWriteRequest? request, HttpContext httpContext, AppDbContext db, CancellationToken cancellationToken)
    {
        if (await AuthEndpoints.RejectCookieCsrf(httpContext) is { } csrf)
        {
            return csrf;
        }

        var problem = await Validate(request, new SettingsWriteValidator());
        if (problem is not null || request is null)
        {
            return problem!;
        }

        var userId = CurrentUser.Id(httpContext);
        if (request.DefaultCollectionId is not null &&
            !await db.Collections.AnyAsync(item => item.Id == request.DefaultCollectionId && item.UserId == userId, cancellationToken))
        {
            throw AppException.BadRequest("Default collection does not exist.");
        }

        var settings = await db.UserSettings.FirstAsync(entry => entry.UserId == userId, cancellationToken);
        settings.Theme = request.Theme;
        settings.Locale = request.Locale.Trim();
        settings.DashboardView = string.IsNullOrWhiteSpace(request.DashboardView) ? "default" : request.DashboardView.Trim();
        settings.DefaultCollectionId = request.DefaultCollectionId;
        settings.AutoTracking = request.AutoTracking;
        settings.AutoComplete = request.AutoComplete;
        settings.CompletionThreshold = request.CompletionThreshold;
        settings.HistoryEnabled = request.HistoryEnabled;
        settings.PositionRestoration = request.PositionRestoration;
        settings.SyncDebounceSeconds = request.SyncDebounceSeconds;
        settings.EnabledDomains = CleanDomains(request.EnabledDomains);
        settings.DisabledDomains = CleanDomains(request.DisabledDomains);
        await db.SaveChangesAsync(cancellationToken);
        return Results.Ok(ToSettings(settings));
    }

    private static async Task<IResult> Dashboard(HttpContext httpContext, AppDbContext db, CancellationToken cancellationToken)
    {
        var userId = CurrentUser.Id(httpContext);
        var continueItems = await db.ReadingItems.AsNoTracking()
            .Where(item => item.UserId == userId && item.LastReadAt != null)
            .OrderByDescending(item => item.LastReadAt)
            .Include(item => item.ActiveChapter).ThenInclude(chapter => chapter!.Position)
            .Take(8)
            .ToListAsync(cancellationToken);
        var bookmarks = await db.Bookmarks.AsNoTracking()
            .Where(item => item.UserId == userId)
            .OrderByDescending(item => item.CreatedAt)
            .AsSplitQuery()
            .Include(item => item.Collections)
            .Include(item => item.Tags).ThenInclude(link => link.Tag)
            .Take(8)
            .ToListAsync(cancellationToken);
        var history = await db.ReadingHistory.AsNoTracking()
            .Where(item => item.UserId == userId && (item.EventType == HistoryEventType.ChapterOpened || item.EventType == HistoryEventType.ChapterCompleted))
            .OrderByDescending(item => item.OccurredAt)
            .Take(8)
            .ToListAsync(cancellationToken);
        var pinned = await db.Collections.AsNoTracking()
            .Where(item => item.UserId == userId && item.IsPinned)
            .OrderBy(item => item.SortOrder)
            .Select(item => new Bookmarks.CollectionResponse(item.Id, item.ParentId, item.Name, item.Description, item.Icon, item.Color, item.IsPinned, item.SortOrder, item.Bookmarks.Count))
            .ToListAsync(cancellationToken);
        return Results.Ok(new DashboardResponse(
            continueItems.Select(item => new ContinueCard(item.Id, item.Title, item.CoverImageUrl, item.ActiveChapter?.Label, item.ActiveChapter?.Position?.ProgressPercent, item.LastReadAt)).ToList(),
            bookmarks.Select(item => new Bookmarks.BookmarkResponse(item.Id, item.Url, item.NormalizedUrl, item.CanonicalUrl, item.Title, item.Description, item.SiteName, item.FaviconUrl, item.ImageUrl, item.Notes, item.CreatedAt, item.UpdatedAt, item.Collections.Select(link => link.CollectionId).ToList(), item.Tags.Select(link => new Bookmarks.TagResponse(link.TagId, link.Tag.Name)).ToList())).ToList(),
            history.Select(ToHistory).ToList(),
            pinned));
    }

    private static async Task<ReadingSource> CreateSource(AppDbContext db, ReadingItem item, SourceWriteRequest? request, CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Url))
        {
            throw AppException.BadRequest("Source URL is required.");
        }

        var normalized = UrlNormalizer.Normalize(request.Url);
        if (await db.ReadingSources.AnyAsync(entry => entry.ReadingItemId == item.Id && entry.NormalizedUrl == normalized, cancellationToken))
        {
            throw AppException.Conflict("This source URL is already linked.");
        }

        var now = DateTimeOffset.UtcNow;
        var source = new ReadingSource
        {
            Id = Guid.NewGuid(),
            ReadingItemId = item.Id,
            Url = request.Url.Trim(),
            NormalizedUrl = normalized,
            Domain = UrlNormalizer.DomainOf(normalized),
            ExternalKey = request.ExternalKey,
            Status = request.Status,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.ReadingSources.Add(source);
        item.Sources.Add(source);
        return source;
    }

    private static async Task<ReadingChapter> UpsertChapter(AppDbContext db, ReadingSource source, string? url, string? title, string? label, decimal? number, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(label))
        {
            throw AppException.BadRequest("Chapter URL and label are required.");
        }

        var normalized = UrlNormalizer.Normalize(url);
        var chapter = source.Chapters.FirstOrDefault(entry => entry.NormalizedUrl == normalized)
            ?? await db.ReadingChapters.FirstOrDefaultAsync(entry => entry.SourceId == source.Id && entry.NormalizedUrl == normalized, cancellationToken);
        if (chapter is null)
        {
            chapter = new ReadingChapter
            {
                Id = Guid.NewGuid(),
                SourceId = source.Id,
                Url = url.Trim(),
                NormalizedUrl = normalized,
                CreatedAt = DateTimeOffset.UtcNow
            };
            db.ReadingChapters.Add(chapter);
            source.Chapters.Add(chapter);
        }

        chapter.Title = string.IsNullOrWhiteSpace(title) ? chapter.Title : title.Trim();
        chapter.Label = label.Trim();
        chapter.Number = number ?? chapter.Number;
        chapter.SortKey = number ?? chapter.SortKey;
        chapter.UpdatedAt = DateTimeOffset.UtcNow;
        return chapter;
    }

    private static async Task<ReadingChapter?> FindChapter(AppDbContext db, Guid sourceId, string url, CancellationToken cancellationToken)
    {
        try
        {
            var normalized = UrlNormalizer.Normalize(url);
            return await db.ReadingChapters.AsNoTracking().FirstOrDefaultAsync(entry => entry.SourceId == sourceId && entry.NormalizedUrl == normalized, cancellationToken);
        }
        catch (AppException)
        {
            return null;
        }
    }

    private static async Task AdvanceFurthest(AppDbContext db, ReadingSource source, ReadingChapter chapter, CancellationToken cancellationToken)
    {
        if (source.FurthestCompletedChapterId is null)
        {
            source.FurthestCompletedChapterId = chapter.Id;
            return;
        }

        if (source.FurthestCompletedChapterId == chapter.Id || chapter.SortKey is null)
        {
            return;
        }

        var current = source.FurthestCompletedChapter
            ?? await db.ReadingChapters.FirstOrDefaultAsync(entry => entry.Id == source.FurthestCompletedChapterId, cancellationToken);
        if (current?.SortKey is null || chapter.SortKey > current.SortKey)
        {
            source.FurthestCompletedChapterId = chapter.Id;
        }
    }

    private static async Task AddHistory(AppDbContext db, Guid userId, ReadingItem item, ReadingChapter? chapter, HistoryEventType type, string? detail, CancellationToken cancellationToken)
    {
        var enabled = await db.UserSettings.Where(entry => entry.UserId == userId).Select(entry => entry.HistoryEnabled).FirstOrDefaultAsync(cancellationToken);
        if (!enabled)
        {
            return;
        }

        if (type == HistoryEventType.ChapterOpened && chapter is not null)
        {
            var since = DateTimeOffset.UtcNow.AddMinutes(-10);
            var duplicate = await db.ReadingHistory.AnyAsync(entry =>
                entry.UserId == userId &&
                entry.EventType == HistoryEventType.ChapterOpened &&
                entry.ChapterId == chapter.Id &&
                entry.OccurredAt >= since, cancellationToken);
            if (duplicate)
            {
                return;
            }
        }

        db.ReadingHistory.Add(new ReadingHistory
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ReadingItemId = item.Id,
            ChapterId = chapter?.Id,
            EventType = type,
            ItemTitle = item.Title,
            ChapterLabel = chapter?.Label,
            Detail = detail,
            OccurredAt = DateTimeOffset.UtcNow
        });
    }

    private static async Task EnsureBookmark(AppDbContext db, Guid userId, Guid? bookmarkId, Guid? itemId, CancellationToken cancellationToken)
    {
        if (bookmarkId is null)
        {
            return;
        }

        var bookmark = await db.Bookmarks.FirstOrDefaultAsync(item => item.Id == bookmarkId && item.UserId == userId, cancellationToken)
            ?? throw AppException.BadRequest("Bookmark not found.");
        var linked = await db.ReadingItems.AnyAsync(item => item.BookmarkId == bookmark.Id && item.Id != itemId, cancellationToken);
        if (linked)
        {
            throw AppException.Conflict("That bookmark is already linked to a reading item.");
        }
    }

    private static async Task<ReadingItem> Load(AppDbContext db, Guid userId, Guid id, CancellationToken cancellationToken)
    {
        return await db.ReadingItems
            .Include(item => item.Sources).ThenInclude(source => source.Chapters).ThenInclude(chapter => chapter.Position)
            .Include(item => item.ActiveChapter)
            .FirstOrDefaultAsync(item => item.Id == id && item.UserId == userId, cancellationToken)
            ?? throw AppException.NotFound("Reading item not found.");
    }

    private static ReadingItemResponse ToItem(ReadingItem item)
    {
        var active = item.ActiveChapter;
        return new ReadingItemResponse(
            item.Id,
            item.Title,
            item.Description,
            item.CoverImageUrl,
            item.Notes,
            item.Status,
            item.BookmarkId,
            item.ActiveSourceId,
            item.ActiveChapterId,
            item.CursorRevision,
            item.LastReadAt,
            item.CreatedAt,
            active?.Position?.ProgressPercent,
            active?.Label,
            item.Sources.OrderBy(source => source.CreatedAt).Select(ToSource).ToList());
    }

    private static SourceResponse ToSource(ReadingSource source) => new(
        source.Id,
        source.Url,
        source.Domain,
        source.ExternalKey,
        source.Status,
        source.LastDetectedChapterLabel,
        source.LastCheckedAt,
        source.FurthestCompletedChapterId,
        source.Chapters.OrderByDescending(chapter => chapter.UpdatedAt).Select(ToChapter).FirstOrDefault());

    private static ChapterSummary ToChapter(ReadingChapter chapter) => new(
        chapter.Id, chapter.Url, chapter.Label, chapter.Title, chapter.Number, chapter.SortKey, chapter.CompletedAt, chapter.Position?.ProgressPercent, chapter.Position?.Revision);

    private static HistoryResponse ToHistory(ReadingHistory entry) => new(
        entry.Id, JsonNamingPolicy.CamelCase.ConvertName(entry.EventType.ToString()), entry.ItemTitle, entry.ChapterLabel, entry.Detail, entry.OccurredAt, entry.ReadingItemId);

    private static SettingsResponse ToSettings(UserSettings settings) => new(
        settings.Theme,
        settings.Locale,
        settings.DashboardView,
        settings.DefaultCollectionId,
        settings.AutoTracking,
        settings.AutoComplete,
        settings.CompletionThreshold,
        settings.HistoryEnabled,
        settings.PositionRestoration,
        settings.SyncDebounceSeconds,
        settings.EnabledDomains,
        settings.DisabledDomains);

    private static List<string> CleanDomains(List<string>? domains) =>
        (domains ?? []).Select(domain => domain.Trim().TrimStart('.').ToLowerInvariant()).Where(domain => domain.Length > 0 && domain.Length <= 255).Distinct().Take(100).ToList();

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

    private sealed record ItemCursor(DateTimeOffset UpdatedAt, Guid Id);
    private sealed record HistoryCursor(DateTimeOffset OccurredAt, Guid Id);
}
