using MovieTimestampNotes.Core;

namespace MovieTimestampNotes.Tests;

public sealed class NoteDraftTests
{
    [Fact]
    public void FirstNonWhitespaceText_CapturesStart_AndEmptyTextResetsIt()
    {
        var now = TimeSpan.FromSeconds(12);
        var draft = new NoteDraft(() => now);

        draft.SetText("   ");
        Assert.Null(draft.StartTime);

        draft.SetText("想法");
        Assert.Equal(now, draft.StartTime);

        now = TimeSpan.FromSeconds(20);
        draft.SetText(string.Empty);
        Assert.Null(draft.StartTime);
    }

    [Fact]
    public void Commit_CapturesEndAndRejectsEmptyDraft()
    {
        var now = TimeSpan.FromSeconds(5);
        var draft = new NoteDraft(() => now);
        draft.SetText("镜头很好");
        now = TimeSpan.FromSeconds(9);

        var entry = draft.Commit(NoteSource.Keyboard);

        Assert.NotNull(entry);
        Assert.Equal(TimeSpan.FromSeconds(5), entry.Start);
        Assert.Equal(TimeSpan.FromSeconds(9), entry.End);
        Assert.Null(draft.Commit(NoteSource.Keyboard));
    }

    [Fact]
    public void CommitAt_UsesVoiceReleaseTimeInsteadOfLaterProcessingTime()
    {
        var now = TimeSpan.FromSeconds(10);
        var draft = new NoteDraft(() => now);
        draft.BeginVoice();
        draft.SetText("语音内容");
        var releasedAt = TimeSpan.FromSeconds(13);
        now = TimeSpan.FromSeconds(15);

        var entry = draft.CommitAt(NoteSource.Voice, releasedAt);

        Assert.NotNull(entry);
        Assert.Equal(releasedAt, entry.End);
    }
}
