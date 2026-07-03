using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MovieTimestampNotes.Core;

public interface INoteFileStore
{
    Task SaveAsync(string path, NoteSession session, CancellationToken cancellationToken = default);
    Task<NoteSession> LoadAsync(string path, CancellationToken cancellationToken = default);
}

public sealed partial class NoteFileStore : INoteFileStore
{
    private const string Header = "# 电影时间戳文本框 v1";

    public async Task SaveAsync(string path, NoteSession session, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(directory);
        var tempPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        var content = Serialize(session);

        try
        {
            await using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                             4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            await using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                await writer.WriteAsync(content.AsMemory(), cancellationToken);
                await writer.FlushAsync(cancellationToken);
                stream.Flush(true);
            }

            if (File.Exists(path))
            {
                File.Replace(tempPath, path, null, true);
            }
            else
            {
                File.Move(tempPath, path);
            }
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    public async Task<NoteSession> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        var content = await File.ReadAllTextAsync(path, Encoding.UTF8, cancellationToken);
        return Deserialize(content);
    }

    public static string Serialize(NoteSession session)
    {
        var builder = new StringBuilder();
        builder.AppendLine(Header);
        builder.Append("影片：").AppendLine(NormalizeHeader(session.MovieTitle));
        builder.Append("时长：").AppendLine(TimestampFormatter.Format(session.Duration));
        builder.Append("创建：").AppendLine(session.CreatedAt.ToString("O", CultureInfo.InvariantCulture));
        builder.AppendLine("===");

        foreach (var note in session.Notes)
        {
            builder.Append('[').Append(TimestampFormatter.Format(note.Start)).Append(" --> ")
                .Append(TimestampFormatter.Format(note.End)).Append("] [")
                .Append(note.Source == NoteSource.Voice ? "语音" : "键盘").AppendLine("]");

            foreach (var line in NormalizeBody(note.Text).Split('\n'))
            {
                builder.AppendLine(line == "---" ? "\\---" : line);
            }
            builder.AppendLine("---");
        }

        return builder.ToString();
    }

    public static NoteSession Deserialize(string content)
    {
        using var reader = new StringReader(content.Replace("\r\n", "\n"));
        if (!string.Equals(reader.ReadLine(), Header, StringComparison.Ordinal))
        {
            throw new InvalidDataException("不是受支持的电影时间戳记录文件。");
        }

        var titleLine = reader.ReadLine();
        var durationLine = reader.ReadLine();
        var createdLine = reader.ReadLine();
        if (titleLine is null || durationLine is null || createdLine is null || reader.ReadLine() != "===")
        {
            throw new InvalidDataException("记录文件头不完整。");
        }

        var title = ValueAfterPrefix(titleLine, "影片：");
        if (!TimestampFormatter.TryParse(ValueAfterPrefix(durationLine, "时长："), out var duration) || duration <= TimeSpan.Zero)
        {
            throw new InvalidDataException("记录文件中的电影时长无效。");
        }
        if (!DateTimeOffset.TryParse(ValueAfterPrefix(createdLine, "创建："), CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var createdAt))
        {
            throw new InvalidDataException("记录文件中的创建时间无效。");
        }

        var session = new NoteSession { MovieTitle = title, Duration = duration, CreatedAt = createdAt };
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var match = EntryHeaderRegex().Match(line);
            if (!match.Success ||
                !TimestampFormatter.TryParse(match.Groups["start"].Value, out var start) ||
                !TimestampFormatter.TryParse(match.Groups["end"].Value, out var end))
            {
                throw new InvalidDataException($"无法解析记录行：{line}");
            }

            var body = new List<string>();
            while ((line = reader.ReadLine()) is not null && line != "---")
            {
                body.Add(line == "\\---" ? "---" : line);
            }
            if (line is null) throw new InvalidDataException("记录正文缺少结束分隔线。");

            session.Notes.Add(new NoteEntry
            {
                Start = start,
                End = end < start ? start : end,
                Source = match.Groups["source"].Value == "语音" ? NoteSource.Voice : NoteSource.Keyboard,
                Text = string.Join(Environment.NewLine, body)
            });
        }

        return session;
    }

    private static string ValueAfterPrefix(string line, string prefix) =>
        line.StartsWith(prefix, StringComparison.Ordinal)
            ? line[prefix.Length..]
            : throw new InvalidDataException($"缺少字段：{prefix}");

    private static string NormalizeHeader(string value) =>
        string.IsNullOrWhiteSpace(value) ? "未命名电影" : value.Replace('\r', ' ').Replace('\n', ' ').Trim();

    private static string NormalizeBody(string value) => value.Replace("\r\n", "\n").Replace('\r', '\n');

    [GeneratedRegex(@"^\[(?<start>\d+:\d{2}:\d{2}\.\d{3}) --> (?<end>\d+:\d{2}:\d{2}\.\d{3})\] \[(?<source>键盘|语音)\]$")]
    private static partial Regex EntryHeaderRegex();
}
