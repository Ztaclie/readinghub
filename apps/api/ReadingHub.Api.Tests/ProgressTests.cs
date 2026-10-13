using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace ReadingHub.Api.Tests;

[Collection("api")]
public sealed class ProgressTests(ApiFactory factory) : IAsyncLifetime
{
    public Task InitializeAsync() => factory.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Stale_revisions_conflict_backward_scroll_is_ignored_and_earlier_chapters_do_not_reduce_furthest()
    {
        var client = await factory.RegisterAsync();
        var csrf = await factory.CsrfAsync(client);
        var itemResponse = await factory.SendAsync(client, HttpMethod.Post, "/api/v1/reading-items", new { title = "Shadow Slave", status = "reading" }, csrf);
        var itemId = (await itemResponse.Content.ReadFromJsonAsync<JsonElement>(factory.Json)).GetProperty("id").GetGuid();
        csrf = await factory.CsrfAsync(client);
        var sourceResponse = await factory.SendAsync(client, HttpMethod.Post, $"/api/v1/reading-items/{itemId}/sources", new { url = "https://www.royalroad.com/fiction/1/shadow", status = "active" }, csrf);
        var sourceId = (await sourceResponse.Content.ReadFromJsonAsync<JsonElement>(factory.Json)).GetProperty("id").GetGuid();

        csrf = await factory.CsrfAsync(client);
        var first = await factory.SendAsync(client, HttpMethod.Put, "/api/v1/reading-progress", Progress(itemId, sourceId, "https://example.com/145", "145", 145, 72, 0, "advance", false), csrf);
        first.EnsureSuccessStatusCode();
        var firstBody = await first.Content.ReadFromJsonAsync<JsonElement>(factory.Json);
        Assert.Equal(72, firstBody.GetProperty("progressPercent").GetDecimal());
        var chapterId = firstBody.GetProperty("chapterId").GetGuid();

        csrf = await factory.CsrfAsync(client);
        var stale = await factory.SendAsync(client, HttpMethod.Put, "/api/v1/reading-progress", Progress(itemId, sourceId, "https://example.com/145", "145", 145, 80, 0, "advance", false), csrf);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        csrf = await factory.CsrfAsync(client);
        var backward = await factory.SendAsync(client, HttpMethod.Put, "/api/v1/reading-progress", Progress(itemId, sourceId, "https://example.com/145", "145", 145, 10, 1, "advance", false), csrf);
        backward.EnsureSuccessStatusCode();
        var backwardBody = await backward.Content.ReadFromJsonAsync<JsonElement>(factory.Json);
        Assert.False(backwardBody.GetProperty("applied").GetBoolean());
        Assert.Equal(72, backwardBody.GetProperty("progressPercent").GetDecimal());

        csrf = await factory.CsrfAsync(client);
        var seek = await factory.SendAsync(client, HttpMethod.Put, "/api/v1/reading-progress", Progress(itemId, sourceId, "https://example.com/145", "145", 145, 20, 1, "seek", false), csrf);
        seek.EnsureSuccessStatusCode();
        Assert.Equal(20, (await seek.Content.ReadFromJsonAsync<JsonElement>(factory.Json)).GetProperty("progressPercent").GetDecimal());

        csrf = await factory.CsrfAsync(client);
        var completedLater = await factory.SendAsync(client, HttpMethod.Put, "/api/v1/reading-progress", Progress(itemId, sourceId, "https://example.com/146", "146", 146, 100, 0, "seek", true), csrf);
        completedLater.EnsureSuccessStatusCode();
        var furthest = (await completedLater.Content.ReadFromJsonAsync<JsonElement>(factory.Json)).GetProperty("chapterId").GetGuid();

        csrf = await factory.CsrfAsync(client);
        var completedEarlier = await factory.SendAsync(client, HttpMethod.Put, "/api/v1/reading-progress", Progress(itemId, sourceId, "https://example.com/140", "140", 140, 100, 0, "seek", true), csrf);
        completedEarlier.EnsureSuccessStatusCode();

        csrf = await factory.CsrfAsync(client);
        var cursor = await factory.SendAsync(client, HttpMethod.Put, $"/api/v1/reading-items/{itemId}/cursor", new
        {
            sourceId,
            chapterId,
            baseRevision = 0,
            updateId = Guid.NewGuid()
        }, csrf);
        cursor.EnsureSuccessStatusCode();

        var after = await client.GetFromJsonAsync<JsonElement>($"/api/v1/reading-items/{itemId}/progress", factory.Json);
        Assert.Equal(furthest, after.GetProperty("sources")[0].GetProperty("furthestCompletedChapterId").GetGuid());
        Assert.Equal(chapterId, after.GetProperty("activeChapterId").GetGuid());
        Assert.NotEqual(furthest, chapterId);
    }

    private static object Progress(Guid itemId, Guid sourceId, string url, string label, decimal number, decimal percent, long revision, string intent, bool complete) => new
    {
        readingItemId = itemId,
        sourceId,
        chapterUrl = url,
        chapterTitle = "Chapter " + label,
        chapterLabel = label,
        chapterNumber = number,
        progressPercent = percent,
        baseRevision = revision,
        intent,
        updateId = Guid.NewGuid(),
        markCompleted = complete
    };
}
