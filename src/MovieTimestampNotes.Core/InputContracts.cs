namespace MovieTimestampNotes.Core;

public sealed record AudioInputDevice(int Id, string Name);

public interface ISpeechRecognizer : IDisposable
{
    bool IsReady { get; }
    bool IsRecording { get; }
    IReadOnlyList<AudioInputDevice> GetInputDevices();
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task StartAsync(int deviceId, CancellationToken cancellationToken = default);
    Task<string> StopAsync(CancellationToken cancellationToken = default);
    event Action<string>? PartialResultChanged;
    event Action<double>? InputLevelChanged;
}

public enum HotkeyAction
{
    ToggleTimeline,
    PushToTalk,
    ToggleVoice,
    FocusInput
}

public interface IGlobalHotkeyService : IDisposable
{
    event Action<HotkeyAction>? Pressed;
    event Action<HotkeyAction>? Released;
    bool Apply(IReadOnlyDictionary<HotkeyAction, string> gestures, out string error);
}
