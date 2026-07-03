using MovieTimestampNotes.Core;

namespace MovieTimestampNotes.Tests;

public sealed class NoteFileStoreTests
{
    [Fact]
    public void SerializeDeserialize_RoundTripsMultilineChineseText()
    {
        var session = new NoteSession
        {
            MovieTitle = "测试电影",
            Duration = TimeSpan.FromMinutes(90),
            CreatedAt = new DateTimeOffset(2026, 7, 3, 10, 0, 0, TimeSpan.FromHours(8))
        };
        session.Notes.Add(new NoteEntry
        {
            Start = TimeSpan.FromSeconds(12.345),
            End = TimeSpan.FromSeconds(18.9),
            Source = NoteSource.Voice,
            Text = "第一行\n---\n第三行"
        });

        var text = NoteFileStore.Serialize(session);
        var loaded = NoteFileStore.Deserialize(text);

        Assert.Equal(session.MovieTitle, loaded.MovieTitle);
        Assert.Equal(session.Duration, loaded.Duration);
        Assert.Equal(session.CreatedAt, loaded.CreatedAt);
        Assert.Single(loaded.Notes);
        Assert.Equal(session.Notes[0].Start, loaded.Notes[0].Start);
        Assert.Equal(session.Notes[0].End, loaded.Notes[0].End);
        Assert.Equal("第一行" + Environment.NewLine + "---" + Environment.NewLine + "第三行", loaded.Notes[0].Text);
    }

    [Theory]
    [InlineData("01:30:00", 5400)]
    [InlineData("100:00:00.500", 360000.5)]
    public void TimestampParser_SupportsCustomMovieDurations(string value, double expectedSeconds)
    {
        Assert.True(TimestampFormatter.TryParse(value, out var parsed));
        Assert.Equal(expectedSeconds, parsed.TotalSeconds, 3);
    }

    [Fact]
    public async Task SaveAsync_ReplacesExistingFileAndCanReloadIt()
    {
        var directory = Path.Combine(Path.GetTempPath(), "MovieTimestampNotes.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "记录.txt");
        try
        {
            var store = new NoteFileStore();
            var session = new NoteSession { MovieTitle = "第一版", Duration = TimeSpan.FromHours(2) };
            await store.SaveAsync(path, session);
            session.MovieTitle = "第二版";
            await store.SaveAsync(path, session);

            var loaded = await store.LoadAsync(path);
            Assert.Equal("第二版", loaded.MovieTitle);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
