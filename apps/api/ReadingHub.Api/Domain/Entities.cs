namespace ReadingHub.Api.Domain;

public sealed class User
{
    public Guid Id { get; set; }
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string SecurityStamp { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public UserSettings? Settings { get; set; }
    public List<WebSession> WebSessions { get; set; } = [];
    public List<DeviceSession> DeviceSessions { get; set; } = [];
    public List<Bookmark> Bookmarks { get; set; } = [];
    public List<Collection> Collections { get; set; } = [];
    public List<Tag> Tags { get; set; } = [];
    public List<ReadingItem> ReadingItems { get; set; } = [];
}

public sealed class WebSession
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset LastSeenAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? UserAgent { get; set; }
}

public sealed class DeviceSession
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public string RefreshTokenHash { get; set; } = "";
    public string? PreviousRefreshTokenHash { get; set; }
    public string DeviceName { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset LastUsedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}

public sealed class DeviceCode
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public string CodeHash { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
}

public sealed class UserSettings
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public string Theme { get; set; } = "system";
    public string Locale { get; set; } = "en";
    public string DashboardView { get; set; } = "default";
    public Guid? DefaultCollectionId { get; set; }
    public Collection? DefaultCollection { get; set; }
    public bool AutoTracking { get; set; }
    public bool AutoComplete { get; set; } = true;
    public int CompletionThreshold { get; set; } = 95;
    public bool HistoryEnabled { get; set; } = true;
    public bool PositionRestoration { get; set; } = true;
    public int SyncDebounceSeconds { get; set; } = 2;
    public List<string> EnabledDomains { get; set; } = [];
    public List<string> DisabledDomains { get; set; } = [];
}

public sealed class Bookmark
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public string Url { get; set; } = "";
    public string NormalizedUrl { get; set; } = "";
    public string? CanonicalUrl { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public string? SiteName { get; set; }
    public string? FaviconUrl { get; set; }
    public string? ImageUrl { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public List<BookmarkCollection> Collections { get; set; } = [];
    public List<BookmarkTag> Tags { get; set; } = [];
    public ReadingItem? ReadingItem { get; set; }
}

public sealed class Collection
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public Guid? ParentId { get; set; }
    public Collection? Parent { get; set; }
    public Guid ParentKey { get; set; }
    public List<Collection> Children { get; set; } = [];
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public string? Icon { get; set; }
    public string? Color { get; set; }
    public bool IsPinned { get; set; }
    public int SortOrder { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public List<BookmarkCollection> Bookmarks { get; set; } = [];
}

public sealed class BookmarkCollection
{
    public Guid BookmarkId { get; set; }
    public Bookmark Bookmark { get; set; } = null!;
    public Guid CollectionId { get; set; }
    public Collection Collection { get; set; } = null!;
}

public sealed class Tag
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public string Name { get; set; } = "";
    public string NormalizedName { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public List<BookmarkTag> Bookmarks { get; set; } = [];
}

public sealed class BookmarkTag
{
    public Guid BookmarkId { get; set; }
    public Bookmark Bookmark { get; set; } = null!;
    public Guid TagId { get; set; }
    public Tag Tag { get; set; } = null!;
}

public enum ReadingStatus
{
    PlanToRead,
    Reading,
    OnHold,
    Completed,
    Dropped
}

public enum SourceStatus
{
    Active,
    Paused,
    Broken
}

public enum ProgressIntent
{
    Advance,
    Seek
}

public enum HistoryEventType
{
    ReadingItemAdded,
    StatusChanged,
    ChapterOpened,
    ChapterCompleted
}

public sealed class ReadingItem
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public string Title { get; set; } = "";
    public string? CoverImageUrl { get; set; }
    public string? Description { get; set; }
    public ReadingStatus Status { get; set; } = ReadingStatus.PlanToRead;
    public string? Notes { get; set; }
    public Guid? BookmarkId { get; set; }
    public Bookmark? Bookmark { get; set; }
    public Guid? ActiveSourceId { get; set; }
    public ReadingSource? ActiveSource { get; set; }
    public Guid? ActiveChapterId { get; set; }
    public ReadingChapter? ActiveChapter { get; set; }
    public long CursorRevision { get; set; }
    public DateTimeOffset? LastReadAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public List<ReadingSource> Sources { get; set; } = [];
    public List<ReadingHistory> History { get; set; } = [];
}

public sealed class ReadingSource
{
    public Guid Id { get; set; }
    public Guid ReadingItemId { get; set; }
    public ReadingItem ReadingItem { get; set; } = null!;
    public string Url { get; set; } = "";
    public string NormalizedUrl { get; set; } = "";
    public string Domain { get; set; } = "";
    public string? ExternalKey { get; set; }
    public SourceStatus Status { get; set; } = SourceStatus.Active;
    public string? LastDetectedChapterLabel { get; set; }
    public DateTimeOffset? LastCheckedAt { get; set; }
    public Guid? FurthestCompletedChapterId { get; set; }
    public ReadingChapter? FurthestCompletedChapter { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public List<ReadingChapter> Chapters { get; set; } = [];
}

public sealed class ReadingChapter
{
    public Guid Id { get; set; }
    public Guid SourceId { get; set; }
    public ReadingSource Source { get; set; } = null!;
    public string Url { get; set; } = "";
    public string NormalizedUrl { get; set; } = "";
    public string? Title { get; set; }
    public string Label { get; set; } = "";
    public decimal? Number { get; set; }
    public decimal? SortKey { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public ChapterPosition? Position { get; set; }
}

public sealed class ChapterPosition
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public Guid ChapterId { get; set; }
    public ReadingChapter Chapter { get; set; } = null!;
    public decimal ProgressPercent { get; set; }
    public long Revision { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class ReadingHistory
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public Guid? ReadingItemId { get; set; }
    public ReadingItem? ReadingItem { get; set; }
    public Guid? ChapterId { get; set; }
    public ReadingChapter? Chapter { get; set; }
    public HistoryEventType EventType { get; set; }
    public string ItemTitle { get; set; } = "";
    public string? ChapterLabel { get; set; }
    public string? Detail { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
}

public sealed class AppliedUpdate
{
    public Guid UpdateId { get; set; }
    public Guid UserId { get; set; }
    public DateTimeOffset AppliedAt { get; set; }
}
