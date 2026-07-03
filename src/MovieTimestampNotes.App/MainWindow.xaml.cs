using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using MovieTimestampNotes.App.Services;
using MovieTimestampNotes.Core;

namespace MovieTimestampNotes.App;

public partial class MainWindow : Window
{
    private readonly TimelineClock _clock = new(initialDuration: TimeSpan.FromMinutes(90));
    private readonly NoteFileStore _fileStore = new();
    private readonly SettingsService _settingsService = new();
    private readonly DispatcherTimer _uiTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private readonly ObservableCollection<NoteRow> _noteRows = [];
    private readonly NoteDraft _draft;
    private readonly VoskSpeechRecognizer _speech;
    private AppSettings _settings;
    private GlobalHotkeyService? _hotkeys;
    private NoteSession? _session;
    private string? _filePath;
    private bool _isExpanded;
    private bool _isRecording;
    private bool _toggleRecording;
    private bool _suppressInputChange;
    private bool _changingLayout;
    private bool _layoutInitialized;
    private double _compactWidth = 500;
    private double _compactHeight = 110;
    private string _voicePrefix = string.Empty;

    public MainWindow()
    {
        InitializeComponent();
        _settings = _settingsService.Load();
        _draft = new NoteDraft(() => _clock.Current);
        _speech = new VoskSpeechRecognizer(Path.Combine(AppContext.BaseDirectory, "models", "vosk-model-small-cn-0.22"));
        _speech.PartialResultChanged += Speech_PartialResultChanged;
        _speech.InputLevelChanged += level => Dispatcher.BeginInvoke(() => MicLevelProgress.Value = level);
        NotesDataGrid.ItemsSource = _noteRows;
        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
        SizeChanged += MainWindow_SizeChanged;
        _uiTimer.Tick += UiTimer_Tick;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        RestoreSettingsToUi();
        _hotkeys = new GlobalHotkeyService(this);
        _hotkeys.Pressed += Hotkeys_Pressed;
        _hotkeys.Released += Hotkeys_Released;
        ApplyHotkeys(showSuccess: false);
        SetExpanded(_settings.IsExpanded);
        _layoutInitialized = true;
        LoadMicrophones();
        _uiTimer.Start();

        try
        {
            MicStatusText.Text = "加载模型…";
            await _speech.InitializeAsync();
            MicStatusText.Text = "语音就绪";
            SetStatus("请先新建或打开一个 TXT 记录文件。");
        }
        catch (Exception ex)
        {
            MicStatusText.Text = "语音不可用";
            SetStatus($"文字记录可正常使用；离线语音初始化失败：{ex.Message}", true);
        }
    }

    private void RestoreSettingsToUi()
    {
        TimelineHotkeyTextBox.Text = _settings.TimelineHotkey;
        PushToTalkHotkeyTextBox.Text = _settings.PushToTalkHotkey;
        ToggleVoiceHotkeyTextBox.Text = _settings.ToggleVoiceHotkey;
        FocusInputHotkeyTextBox.Text = _settings.FocusInputHotkey;
        _compactWidth = Math.Clamp(_settings.CompactWidth, 380, 1000);
        _compactHeight = Math.Clamp(_settings.CompactHeight, 100, 320);

        if (_settings.WindowLeft.HasValue && _settings.WindowTop.HasValue &&
            _settings.WindowLeft >= SystemParameters.VirtualScreenLeft - Width + 80 &&
            _settings.WindowLeft <= SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 80 &&
            _settings.WindowTop >= SystemParameters.VirtualScreenTop &&
            _settings.WindowTop <= SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 50)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = _settings.WindowLeft.Value;
            Top = _settings.WindowTop.Value;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }
    }

    private void LoadMicrophones()
    {
        try
        {
            var devices = _speech.GetInputDevices();
            MicrophoneComboBox.ItemsSource = devices;
            MicrophoneComboBox.SelectedItem = devices.FirstOrDefault(x => x.Name == _settings.MicrophoneName) ?? devices.FirstOrDefault();
            if (devices.Count == 0)
            {
                MicStatusText.Text = "无麦克风";
                SetStatus("没有检测到麦克风，文字记录仍可使用。", true);
            }
        }
        catch (Exception ex)
        {
            MicStatusText.Text = "麦克风错误";
            SetStatus($"无法枚举麦克风：{ex.Message}", true);
        }
    }

    private void UiTimer_Tick(object? sender, EventArgs e)
    {
        var current = _clock.Current;
        var duration = _clock.Duration;
        CurrentTimeText.Text = $"{TimestampFormatter.Format(current, false)} / {TimestampFormatter.Format(duration, false)}";
        TimelineProgress.Value = duration.TotalMilliseconds <= 0 ? 0 : current.TotalMilliseconds / duration.TotalMilliseconds;
        TimelineButton.Content = _clock.IsRunning ? "Ⅱ" : "▶";
        CalibrateButton.IsEnabled = !_draft.IsStarted && !_isRecording && _session is not null;
    }

    private async void NewFileButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isRecording) await StopVoiceAsync();
        var dialog = new SaveFileDialog
        {
            Title = "新建电影感想记录",
            Filter = "文本文件 (*.txt)|*.txt",
            DefaultExt = ".txt",
            AddExtension = true,
            FileName = "电影感想.txt"
        };
        if (dialog.ShowDialog(this) != true) return;

        if (!TimestampFormatter.TryParse(DurationTextBox.Text, out var duration) || duration <= TimeSpan.Zero)
        {
            duration = TimeSpan.FromMinutes(90);
            DurationTextBox.Text = "01:30:00";
        }
        _session = new NoteSession
        {
            MovieTitle = string.IsNullOrWhiteSpace(MovieTitleTextBox.Text) ? Path.GetFileNameWithoutExtension(dialog.FileName) : MovieTitleTextBox.Text.Trim(),
            Duration = duration,
            CreatedAt = DateTimeOffset.Now
        };
        _filePath = dialog.FileName;
        _clock.Pause();
        _clock.SetDuration(duration);
        _clock.Reset();
        BindSession();
        await SaveSessionAsync("已新建记录，可以开始计时和输入感想。");
    }

    private async void OpenFileButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isRecording) await StopVoiceAsync();
        var dialog = new OpenFileDialog { Title = "打开电影感想记录", Filter = "文本文件 (*.txt)|*.txt" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var session = await _fileStore.LoadAsync(dialog.FileName);
            _session = session;
            _filePath = dialog.FileName;
            _clock.Pause();
            _clock.SetDuration(session.Duration);
            _clock.Reset();
            BindSession();
            SetStatus("记录已打开；计时从零开始，可用“当前校准”同步到电影进度。");
        }
        catch (Exception ex)
        {
            SetStatus($"打开失败：{ex.Message}", true);
        }
    }

    private void BindSession()
    {
        if (_session is null) return;
        MovieTitleTextBox.Text = _session.MovieTitle;
        MovieTitleTextBox.IsEnabled = true;
        DurationTextBox.Text = TimestampFormatter.Format(_session.Duration, false);
        FileNameText.Text = "  " + Path.GetFileName(_filePath);
        InputTextBox.IsEnabled = true;
        _noteRows.Clear();
        foreach (var note in _session.Notes) _noteRows.Add(new NoteRow(note));
        InputTextBox.Focus();
    }

    private void TimelineButton_Click(object sender, RoutedEventArgs e) => ToggleTimeline();

    private void ToggleTimeline()
    {
        if (_session is null)
        {
            SetExpanded(true);
            SetStatus("请先新建或打开记录文件。", true);
            return;
        }
        _clock.Toggle();
        SetStatus(_clock.IsRunning ? "电影时间正在计时。" : "电影时间已暂停。");
    }

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null) return;
        if (_draft.IsStarted || _isRecording)
        {
            SetStatus("请先提交或清空当前输入，再重置计时。", true);
            return;
        }
        if (MessageBox.Show(this, "确定将电影时间重置为 00:00:00 并暂停吗？", "重置计时",
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            _clock.Reset();
            SetStatus("电影时间已重置。保存的记录不会改变。");
        }
    }

    private async void ApplyDurationButton_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null) { SetStatus("请先新建或打开记录文件。", true); return; }
        if (_draft.IsStarted || _isRecording) { SetStatus("有未提交输入或正在录音，不能修改时长。", true); return; }
        if (!TimestampFormatter.TryParse(DurationTextBox.Text, out var duration) || duration <= TimeSpan.Zero)
        {
            SetStatus("总时长格式无效，请输入 HH:MM:SS，例如 01:30:00。", true);
            return;
        }
        _clock.SetDuration(duration);
        _session.Duration = duration;
        DurationTextBox.Text = TimestampFormatter.Format(duration, false);
        await SaveSessionAsync("电影总时长已更新。");
    }

    private void CalibrateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null) return;
        if (_draft.IsStarted || _isRecording) { SetStatus("请先提交或清空当前输入，再校准时间。", true); return; }
        if (!TimestampFormatter.TryParse(CalibrationTextBox.Text, out var position) || position < TimeSpan.Zero || position > _clock.Duration)
        {
            SetStatus("校准时间无效，必须位于 00:00:00 和电影总时长之间。", true);
            return;
        }
        _clock.Calibrate(position);
        CalibrationTextBox.Text = TimestampFormatter.Format(position, false);
        SetStatus("电影时间已校准；原有记录时间不会改变。");
    }

    private void InputTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressInputChange) return;
        _draft.SetText(InputTextBox.Text);
    }

    private async void InputTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) return;
        e.Handled = true;
        if (_isRecording) { SetStatus("请先结束语音输入，再提交记录。", true); return; }
        await CommitDraftAsync(NoteSource.Keyboard);
    }

    private async Task CommitDraftAsync(NoteSource source)
    {
        if (_session is null) return;
        _draft.SetText(InputTextBox.Text);
        var note = _draft.Commit(source);
        if (note is null) return;
        SetInputText(string.Empty);
        _session.Notes.Add(note);
        _noteRows.Add(new NoteRow(note));
        NotesDataGrid.ScrollIntoView(_noteRows[^1]);
        await SaveSessionAsync("感想已保存。继续输入即可创建下一条记录。");
    }

    private async void MovieTitleTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_session is null) return;
        _session.MovieTitle = string.IsNullOrWhiteSpace(MovieTitleTextBox.Text) ? "未命名电影" : MovieTitleTextBox.Text.Trim();
        MovieTitleTextBox.Text = _session.MovieTitle;
        await SaveSessionAsync("影片名称已更新。");
    }

    private async void NotesDataGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.Row.Item is not NoteRow row || e.EditAction != DataGridEditAction.Commit) return;
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
        row.Entry.Text = row.Text.Trim();
        await SaveSessionAsync("修改已保存。");
    }

    private async void DeleteNoteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null || sender is not Button { Tag: NoteRow row }) return;
        if (MessageBox.Show(this, "确定删除这条感想吗？", "删除记录", MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _session.Notes.Remove(row.Entry);
        _noteRows.Remove(row);
        await SaveSessionAsync("记录已删除。");
    }

    private async Task SaveSessionAsync(string successMessage)
    {
        if (_session is null || string.IsNullOrWhiteSpace(_filePath)) return;
        try
        {
            await _fileStore.SaveAsync(_filePath, _session);
            SetStatus(successMessage);
        }
        catch (Exception ex)
        {
            SetStatus($"保存失败：{ex.Message}", true);
        }
    }

    private async Task StartVoiceAsync(bool toggleMode)
    {
        if (_session is null) { SetStatus("请先新建或打开记录文件。", true); return; }
        if (_isRecording || (toggleMode == false && _toggleRecording)) return;
        if (!_speech.IsReady) { SetStatus("离线语音模型尚未就绪。", true); return; }
        if (MicrophoneComboBox.SelectedItem is not AudioInputDevice device) { SetStatus("没有可用的麦克风。", true); return; }

        _draft.SetText(InputTextBox.Text);
        _draft.BeginVoice();
        _voicePrefix = InputTextBox.Text.TrimEnd();
        try
        {
            await _speech.StartAsync(device.Id);
            _isRecording = true;
            _toggleRecording = toggleMode;
            MicStatusText.Text = toggleMode ? "开关录音中" : "按住录音中";
            SetStatus(toggleMode ? "语音输入已开启，再按一次开关快捷键结束。" : "正在语音输入，松开快捷键结束。");
        }
        catch (Exception ex)
        {
            _draft.SetText(InputTextBox.Text);
            MicStatusText.Text = "语音就绪";
            SetStatus($"无法开始录音：{ex.Message}", true);
        }
    }

    private async Task StopVoiceAsync()
    {
        if (!_isRecording) return;
        var voiceEnd = _clock.Current;
        try
        {
            var recognized = await _speech.StopAsync();
            var combined = JoinDraftAndSpeech(_voicePrefix, recognized);
            SetInputText(combined);
            _draft.SetText(combined);
            _isRecording = false;
            _toggleRecording = false;
            MicStatusText.Text = "语音就绪";
            var note = _draft.CommitAt(NoteSource.Voice, voiceEnd);
            if (note is not null && _session is not null)
            {
                SetInputText(string.Empty);
                _session.Notes.Add(note);
                _noteRows.Add(new NoteRow(note));
                NotesDataGrid.ScrollIntoView(_noteRows[^1]);
                await SaveSessionAsync("语音感想已保存。");
            }
        }
        catch (Exception ex)
        {
            _isRecording = false;
            _toggleRecording = false;
            MicStatusText.Text = "语音错误";
            SetStatus($"结束录音失败：{ex.Message}", true);
        }
    }

    private void Speech_PartialResultChanged(string partial)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (!_isRecording) return;
            var combined = JoinDraftAndSpeech(_voicePrefix, partial);
            SetInputText(combined);
            _draft.SetText(combined);
            InputTextBox.CaretIndex = InputTextBox.Text.Length;
        });
    }

    private static string JoinDraftAndSpeech(string prefix, string speech)
    {
        if (string.IsNullOrWhiteSpace(prefix)) return speech.Trim();
        if (string.IsNullOrWhiteSpace(speech)) return prefix.TrimEnd();
        return prefix.TrimEnd() + " " + speech.Trim();
    }

    private void SetInputText(string value)
    {
        _suppressInputChange = true;
        InputTextBox.Text = value;
        _suppressInputChange = false;
    }

    private async void Hotkeys_Pressed(HotkeyAction action)
    {
        switch (action)
        {
            case HotkeyAction.ToggleTimeline:
                ToggleTimeline();
                break;
            case HotkeyAction.PushToTalk:
                if (!_toggleRecording) await StartVoiceAsync(false);
                break;
            case HotkeyAction.ToggleVoice:
                if (_isRecording && _toggleRecording) await StopVoiceAsync();
                else if (!_isRecording) await StartVoiceAsync(true);
                break;
            case HotkeyAction.FocusInput:
                Show();
                WindowState = WindowState.Normal;
                Activate();
                InputTextBox.Focus();
                break;
        }
    }

    private async void Hotkeys_Released(HotkeyAction action)
    {
        if (action == HotkeyAction.PushToTalk && _isRecording && !_toggleRecording) await StopVoiceAsync();
    }

    private void ApplyHotkeysButton_Click(object sender, RoutedEventArgs e) => ApplyHotkeys(showSuccess: true);

    private void ApplyHotkeys(bool showSuccess)
    {
        if (_hotkeys is null) return;
        var values = new Dictionary<HotkeyAction, string>
        {
            [HotkeyAction.ToggleTimeline] = TimelineHotkeyTextBox.Text,
            [HotkeyAction.PushToTalk] = PushToTalkHotkeyTextBox.Text,
            [HotkeyAction.ToggleVoice] = ToggleVoiceHotkeyTextBox.Text,
            [HotkeyAction.FocusInput] = FocusInputHotkeyTextBox.Text
        };
        if (!_hotkeys.Apply(values, out var error))
        {
            SetStatus(error, true);
            return;
        }

        _settings.TimelineHotkey = TimelineHotkeyTextBox.Text.Trim();
        _settings.PushToTalkHotkey = PushToTalkHotkeyTextBox.Text.Trim();
        _settings.ToggleVoiceHotkey = ToggleVoiceHotkeyTextBox.Text.Trim();
        _settings.FocusInputHotkey = FocusInputHotkeyTextBox.Text.Trim();
        SaveSettings();
        if (showSuccess) SetStatus("全局快捷键已应用。");
    }

    private void MicrophoneComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MicrophoneComboBox.SelectedItem is AudioInputDevice device)
        {
            _settings.MicrophoneName = device.Name;
            SaveSettings();
        }
    }

    private void ExpandButton_Click(object sender, RoutedEventArgs e) => SetExpanded(!_isExpanded);

    private void SetExpanded(bool expanded)
    {
        if (_layoutInitialized && !_isExpanded && expanded && !_changingLayout)
        {
            CaptureCompactSize();
        }

        _changingLayout = true;
        _isExpanded = expanded;
        ExpandedPanel.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        MinWidth = 0;
        MinHeight = 0;
        MaxWidth = double.PositiveInfinity;
        MaxHeight = double.PositiveInfinity;

        if (expanded)
        {
            ResizeMode = ResizeMode.NoResize;
            CompactRow.Height = new GridLength(80);
            DetailRow.Height = new GridLength(1, GridUnitType.Star);
            Width = 680;
            Height = 738;
            MinWidth = MaxWidth = 680;
            MinHeight = MaxHeight = 738;
        }
        else
        {
            ResizeMode = ResizeMode.CanResize;
            CompactRow.Height = new GridLength(1, GridUnitType.Star);
            DetailRow.Height = new GridLength(0);
            MinWidth = 380;
            MinHeight = 100;
            MaxWidth = 1000;
            MaxHeight = 320;
            Width = _compactWidth;
            Height = _compactHeight;
        }

        ExpandButton.Content = expanded ? "⌃" : "⌄";
        _settings.IsExpanded = expanded;
        _changingLayout = false;
    }

    private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_layoutInitialized && !_isExpanded && !_changingLayout && WindowState == WindowState.Normal)
        {
            CaptureCompactSize();
        }
    }

    private void CaptureCompactSize()
    {
        _compactWidth = Math.Clamp(ActualWidth, 380, 1000);
        _compactHeight = Math.Clamp(ActualHeight, 100, 320);
        _settings.CompactWidth = _compactWidth;
        _settings.CompactHeight = _compactHeight;
    }

    private void SetStatus(string message, bool error = false)
    {
        StatusText.Text = message;
        StatusText.Foreground = error
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 139, 139))
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(174, 180, 194));
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void SaveSettings()
    {
        try { _settingsService.Save(_settings); } catch { }
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        _settings.WindowLeft = Left;
        _settings.WindowTop = Top;
        _settings.IsExpanded = _isExpanded;
        if (!_isExpanded) CaptureCompactSize();
        SaveSettings();
        _uiTimer.Stop();
        _hotkeys?.Dispose();
        _speech.Dispose();
    }

    private sealed class NoteRow
    {
        public NoteRow(NoteEntry entry)
        {
            Entry = entry;
            Text = entry.Text;
        }

        public NoteEntry Entry { get; }
        public string StartLabel => TimestampFormatter.Format(Entry.Start, false);
        public string EndLabel => TimestampFormatter.Format(Entry.End, false);
        public string SourceLabel => Entry.Source == NoteSource.Voice ? "语音" : "键盘";
        public string Text { get; set; }
    }
}
