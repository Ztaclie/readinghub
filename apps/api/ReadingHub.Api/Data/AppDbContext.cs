using Microsoft.EntityFrameworkCore;
using ReadingHub.Api.Domain;

namespace ReadingHub.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<WebSession> WebSessions => Set<WebSession>();
    public DbSet<DeviceSession> DeviceSessions => Set<DeviceSession>();
    public DbSet<DeviceCode> DeviceCodes => Set<DeviceCode>();
    public DbSet<UserSettings> UserSettings => Set<UserSettings>();
    public DbSet<Bookmark> Bookmarks => Set<Bookmark>();
    public DbSet<Collection> Collections => Set<Collection>();
    public DbSet<BookmarkCollection> BookmarkCollections => Set<BookmarkCollection>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<BookmarkTag> BookmarkTags => Set<BookmarkTag>();
    public DbSet<ReadingItem> ReadingItems => Set<ReadingItem>();
    public DbSet<ReadingSource> ReadingSources => Set<ReadingSource>();
    public DbSet<ReadingChapter> ReadingChapters => Set<ReadingChapter>();
    public DbSet<ChapterPosition> ChapterPositions => Set<ChapterPosition>();
    public DbSet<ReadingHistory> ReadingHistory => Set<ReadingHistory>();
    public DbSet<AppliedUpdate> AppliedUpdates => Set<AppliedUpdate>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("citext");
        modelBuilder.HasPostgresExtension("pg_trgm");

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasIndex(user => user.Email).IsUnique();
            entity.Property(user => user.Email).HasColumnType("citext").HasMaxLength(254);
            entity.Property(user => user.DisplayName).HasMaxLength(80);
            entity.Property(user => user.PasswordHash).HasMaxLength(500);
            entity.Property(user => user.SecurityStamp).HasMaxLength(64);
        });

        modelBuilder.Entity<WebSession>(entity =>
        {
            entity.HasIndex(session => session.UserId);
            entity.Property(session => session.UserAgent).HasMaxLength(512);
            entity.HasOne(session => session.User).WithMany(user => user.WebSessions).HasForeignKey(session => session.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DeviceSession>(entity =>
        {
            entity.HasIndex(session => session.RefreshTokenHash).IsUnique();
            entity.Property(session => session.RefreshTokenHash).HasMaxLength(128);
            entity.Property(session => session.PreviousRefreshTokenHash).HasMaxLength(128);
            entity.Property(session => session.DeviceName).HasMaxLength(120);
            entity.HasOne(session => session.User).WithMany(user => user.DeviceSessions).HasForeignKey(session => session.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DeviceCode>(entity =>
        {
            entity.HasIndex(code => code.CodeHash).IsUnique();
            entity.Property(code => code.CodeHash).HasMaxLength(128);
            entity.HasOne(code => code.User).WithMany().HasForeignKey(code => code.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserSettings>(entity =>
        {
            entity.HasKey(settings => settings.UserId);
            entity.Property(settings => settings.Theme).HasMaxLength(16);
            entity.Property(settings => settings.Locale).HasMaxLength(16);
            entity.Property(settings => settings.DashboardView).HasMaxLength(32);
            entity.Property(settings => settings.EnabledDomains).HasColumnType("text[]");
            entity.Property(settings => settings.DisabledDomains).HasColumnType("text[]");
            entity.HasOne(settings => settings.User).WithOne(user => user.Settings).HasForeignKey<UserSettings>(settings => settings.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(settings => settings.DefaultCollection).WithMany().HasForeignKey(settings => settings.DefaultCollectionId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Bookmark>(entity =>
        {
            entity.HasIndex(bookmark => new { bookmark.UserId, bookmark.NormalizedUrl }).IsUnique();
            entity.HasIndex(bookmark => new { bookmark.UserId, bookmark.CreatedAt, bookmark.Id });
            entity.Property(bookmark => bookmark.Url).HasMaxLength(2000);
            entity.Property(bookmark => bookmark.NormalizedUrl).HasMaxLength(2000);
            entity.Property(bookmark => bookmark.CanonicalUrl).HasMaxLength(2000);
            entity.Property(bookmark => bookmark.Title).HasMaxLength(500);
            entity.Property(bookmark => bookmark.SiteName).HasMaxLength(200);
            entity.Property(bookmark => bookmark.FaviconUrl).HasMaxLength(2000);
            entity.Property(bookmark => bookmark.ImageUrl).HasMaxLength(2000);
            entity.HasOne(bookmark => bookmark.User).WithMany(user => user.Bookmarks).HasForeignKey(bookmark => bookmark.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Collection>(entity =>
        {
            entity.HasIndex(collection => new { collection.UserId, collection.ParentKey, collection.Name }).IsUnique();
            entity.Property(collection => collection.Name).HasMaxLength(120);
            entity.Property(collection => collection.Icon).HasMaxLength(32);
            entity.Property(collection => collection.Color).HasMaxLength(16);
            entity.HasOne(collection => collection.User).WithMany(user => user.Collections).HasForeignKey(collection => collection.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(collection => collection.Parent).WithMany(collection => collection.Children).HasForeignKey(collection => collection.ParentId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<BookmarkCollection>(entity =>
        {
            entity.HasKey(link => new { link.BookmarkId, link.CollectionId });
            entity.HasOne(link => link.Bookmark).WithMany(bookmark => bookmark.Collections).HasForeignKey(link => link.BookmarkId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(link => link.Collection).WithMany(collection => collection.Bookmarks).HasForeignKey(link => link.CollectionId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Tag>(entity =>
        {
            entity.HasIndex(tag => new { tag.UserId, tag.NormalizedName }).IsUnique();
            entity.Property(tag => tag.Name).HasMaxLength(60);
            entity.Property(tag => tag.NormalizedName).HasMaxLength(60);
            entity.HasOne(tag => tag.User).WithMany(user => user.Tags).HasForeignKey(tag => tag.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<BookmarkTag>(entity =>
        {
            entity.HasKey(link => new { link.BookmarkId, link.TagId });
            entity.HasOne(link => link.Bookmark).WithMany(bookmark => bookmark.Tags).HasForeignKey(link => link.BookmarkId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(link => link.Tag).WithMany(tag => tag.Bookmarks).HasForeignKey(link => link.TagId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ReadingItem>(entity =>
        {
            entity.HasIndex(item => item.BookmarkId).IsUnique();
            entity.HasIndex(item => new { item.UserId, item.LastReadAt });
            entity.Property(item => item.Title).HasMaxLength(300);
            entity.Property(item => item.CoverImageUrl).HasMaxLength(2000);
            entity.Property(item => item.Status).HasConversion<string>().HasMaxLength(32);
            entity.HasOne(item => item.User).WithMany(user => user.ReadingItems).HasForeignKey(item => item.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(item => item.Bookmark).WithOne(bookmark => bookmark.ReadingItem).HasForeignKey<ReadingItem>(item => item.BookmarkId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(item => item.ActiveSource).WithMany().HasForeignKey(item => item.ActiveSourceId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(item => item.ActiveChapter).WithMany().HasForeignKey(item => item.ActiveChapterId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<ReadingSource>(entity =>
        {
            entity.HasIndex(source => new { source.ReadingItemId, source.NormalizedUrl }).IsUnique();
            entity.Property(source => source.Url).HasMaxLength(2000);
            entity.Property(source => source.NormalizedUrl).HasMaxLength(2000);
            entity.Property(source => source.Domain).HasMaxLength(255);
            entity.Property(source => source.ExternalKey).HasMaxLength(200);
            entity.Property(source => source.LastDetectedChapterLabel).HasMaxLength(200);
            entity.Property(source => source.Status).HasConversion<string>().HasMaxLength(32);
            entity.HasOne(source => source.ReadingItem).WithMany(item => item.Sources).HasForeignKey(source => source.ReadingItemId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(source => source.FurthestCompletedChapter).WithMany().HasForeignKey(source => source.FurthestCompletedChapterId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<ReadingChapter>(entity =>
        {
            entity.HasIndex(chapter => new { chapter.SourceId, chapter.NormalizedUrl }).IsUnique();
            entity.Property(chapter => chapter.Url).HasMaxLength(2000);
            entity.Property(chapter => chapter.NormalizedUrl).HasMaxLength(2000);
            entity.Property(chapter => chapter.Title).HasMaxLength(300);
            entity.Property(chapter => chapter.Label).HasMaxLength(200);
            entity.Property(chapter => chapter.Number).HasPrecision(12, 2);
            entity.Property(chapter => chapter.SortKey).HasPrecision(12, 2);
            entity.HasOne(chapter => chapter.Source).WithMany(source => source.Chapters).HasForeignKey(chapter => chapter.SourceId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ChapterPosition>(entity =>
        {
            entity.HasIndex(position => new { position.UserId, position.ChapterId }).IsUnique();
            entity.HasIndex(position => new { position.UserId, position.UpdatedAt });
            entity.Property(position => position.ProgressPercent).HasPrecision(5, 2);
            entity.HasOne(position => position.User).WithMany().HasForeignKey(position => position.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(position => position.Chapter).WithOne(chapter => chapter.Position).HasForeignKey<ChapterPosition>(position => position.ChapterId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ReadingHistory>(entity =>
        {
            entity.HasIndex(entry => new { entry.UserId, entry.OccurredAt });
            entity.Property(entry => entry.EventType).HasConversion<string>().HasMaxLength(32);
            entity.Property(entry => entry.ItemTitle).HasMaxLength(300);
            entity.Property(entry => entry.ChapterLabel).HasMaxLength(200);
            entity.Property(entry => entry.Detail).HasMaxLength(500);
            entity.HasOne(entry => entry.User).WithMany().HasForeignKey(entry => entry.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(entry => entry.ReadingItem).WithMany(item => item.History).HasForeignKey(entry => entry.ReadingItemId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(entry => entry.Chapter).WithMany().HasForeignKey(entry => entry.ChapterId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<AppliedUpdate>(entity =>
        {
            entity.HasKey(update => new { update.UserId, update.UpdateId });
        });
    }
}

public sealed class AppDbContextFactory : Microsoft.EntityFrameworkCore.Design.IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Database")
            ?? "Host=localhost;Port=5432;Database=readinghub;Username=readinghub;Password=readinghub";
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connectionString).Options;
        return new AppDbContext(options);
    }
}
