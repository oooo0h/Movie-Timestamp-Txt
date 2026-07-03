using System.IO;
using System.Text;
using System.Text.Json;
using MovieTimestampNotes.Core;
using NAudio.Wave;
using Vosk;

namespace MovieTimestampNotes.App.Services;

public sealed class VoskSpeechRecognizer : ISpeechRecognizer
{
    private readonly string _modelPath;
    private readonly object _gate = new();
    private Model? _model;
    private VoskRecognizer? _recognizer;
    private WaveInEvent? _waveIn;
    private readonly StringBuilder _confirmed = new();
    private bool _disposed;

    public VoskSpeechRecognizer(string modelPath)
    {
        _modelPath = modelPath;
        Vosk.Vosk.SetLogLevel(-1);
    }

    public bool IsReady => _model is not null;
    public bool IsRecording { get; private set; }

    public event Action<string>? PartialResultChanged;
    public event Action<double>? InputLevelChanged;

    public IReadOnlyList<AudioInputDevice> GetInputDevices()
    {
        var devices = new List<AudioInputDevice>();
        for (var i = 0; i < WaveIn.DeviceCount; i++)
        {
            var capabilities = WaveIn.GetCapabilities(i);
            devices.Add(new AudioInputDevice(i, capabilities.ProductName));
        }
        return devices;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_model is not null) return;
        if (!Directory.Exists(_modelPath))
        {
            throw new DirectoryNotFoundException($"未找到离线语音模型：{_modelPath}");
        }

        var model = await Task.Run(() => new Model(_modelPath), cancellationToken);
        lock (_gate)
        {
            if (_disposed) model.Dispose();
            else _model ??= model;
        }
    }

    public Task StartAsync(int deviceId, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_model is null) throw new InvalidOperationException("离线语音模型仍在加载。");
            if (IsRecording) throw new InvalidOperationException("语音识别已经启动。");
            if (deviceId < 0 || deviceId >= WaveIn.DeviceCount) throw new InvalidOperationException("所选麦克风不可用。");

            _confirmed.Clear();
            _recognizer = new VoskRecognizer(_model, 16000.0f);
            _recognizer.SetWords(false);
            _waveIn = new WaveInEvent
            {
                DeviceNumber = deviceId,
                WaveFormat = new WaveFormat(16000, 16, 1),
                BufferMilliseconds = 100,
                NumberOfBuffers = 3
            };
            _waveIn.DataAvailable += OnDataAvailable;
            _waveIn.StartRecording();
            IsRecording = true;
        }
        return Task.CompletedTask;
    }

    public Task<string> StopAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (!IsRecording || _waveIn is null || _recognizer is null) return Task.FromResult(string.Empty);

            _waveIn.StopRecording();
            _waveIn.DataAvailable -= OnDataAvailable;
            var final = ExtractText(_recognizer.FinalResult(), "text");
            AppendConfirmed(final);
            var result = _confirmed.ToString().Trim();

            _waveIn.Dispose();
            _waveIn = null;
            _recognizer.Dispose();
            _recognizer = null;
            IsRecording = false;
            InputLevelChanged?.Invoke(0);
            return Task.FromResult(result);
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        string combined;
        double level;
        lock (_gate)
        {
            if (!IsRecording || _recognizer is null) return;
            level = CalculateLevel(e.Buffer, e.BytesRecorded);
            if (_recognizer.AcceptWaveform(e.Buffer, e.BytesRecorded))
            {
                AppendConfirmed(ExtractText(_recognizer.Result(), "text"));
                combined = _confirmed.ToString().Trim();
            }
            else
            {
                var partial = ExtractText(_recognizer.PartialResult(), "partial");
                combined = JoinText(_confirmed.ToString(), partial);
            }
        }

        InputLevelChanged?.Invoke(level);
        PartialResultChanged?.Invoke(combined);
    }

    private void AppendConfirmed(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        if (_confirmed.Length > 0) _confirmed.Append(' ');
        _confirmed.Append(text.Trim());
    }

    private static string JoinText(string first, string second)
    {
        if (string.IsNullOrWhiteSpace(first)) return second.Trim();
        if (string.IsNullOrWhiteSpace(second)) return first.Trim();
        return first.Trim() + " " + second.Trim();
    }

    private static string ExtractText(string json, string property)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty(property, out var value) ? value.GetString() ?? string.Empty : string.Empty;
    }

    private static double CalculateLevel(byte[] buffer, int length)
    {
        if (length < 2) return 0;
        double sum = 0;
        var samples = length / 2;
        for (var i = 0; i + 1 < length; i += 2)
        {
            var sample = BitConverter.ToInt16(buffer, i) / 32768.0;
            sum += sample * sample;
        }
        return Math.Clamp(Math.Sqrt(sum / samples) * 3, 0, 1);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { StopAsync().GetAwaiter().GetResult(); } catch { }
        _model?.Dispose();
        _model = null;
    }
}
