namespace MovieTimestampNotes.Core;

public enum NoteSource
{
    Keyboard,
    Voice
}

public sealed class NoteEntry
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public TimeSpan Start { get; init; }
    public TimeSpan End { get; init; }
    public string Text { get; set; } = string.Empty;
    public NoteSource Source { get; init; }
}

public sealed class NoteSession
{
    public string MovieTitle { get; set; } = "未命名电影";
    public TimeSpan Duration { get; set; } = TimeSpan.FromMinutes(90);
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public List<NoteEntry> Notes { get; } = [];
}

public sealed class NoteDraft
{
    private readonly Func<TimeSpan> _getCurrentTime;

    public NoteDraft(Func<TimeSpan> getCurrentTime)
    {
        _getCurrentTime = getCurrentTime;
    }

    public string Text { get; private set; } = string.Empty;
    public TimeSpan? StartTime { get; private set; }
    public bool HasContent => !string.IsNullOrWhiteSpace(Text);
    public bool IsStarted => StartTime.HasValue;

    public void SetText(string text)
    {
        Text = text;
        if (string.IsNullOrWhiteSpace(text))
        {
            StartTime = null;
        }
        else if (!StartTime.HasValue)
        {
            StartTime = _getCurrentTime();
        }
    }

    public void BeginVoice()
    {
        StartTime ??= _getCurrentTime();
    }

    public NoteEntry? Commit(NoteSource source)
    {
        return CommitAt(source, _getCurrentTime());
    }

    public NoteEntry? CommitAt(NoteSource source, TimeSpan end)
    {
        if (!HasContent || !StartTime.HasValue)
        {
            Clear();
            return null;
        }
        if (end < StartTime.Value) end = StartTime.Value;
        var entry = new NoteEntry
        {
            Start = StartTime.Value,
            End = end,
            Text = Text.Trim(),
            Source = source
        };
        Clear();
        return entry;
    }

    public void Clear()
    {
        Text = string.Empty;
        StartTime = null;
    }
}
