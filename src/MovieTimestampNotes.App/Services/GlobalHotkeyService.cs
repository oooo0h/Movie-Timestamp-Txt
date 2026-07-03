using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using MovieTimestampNotes.Core;

namespace MovieTimestampNotes.App.Services;

public sealed class GlobalHotkeyService : IGlobalHotkeyService
{
    private const int WmHotkey = 0x0312;
    private const int WhKeyboardLl = 13;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;
    private const uint ModNoRepeat = 0x4000;

    private readonly Window _window;
    private readonly Dictionary<int, HotkeyAction> _registered = [];
    private readonly LowLevelKeyboardProc _keyboardProc;
    private HwndSource? _source;
    private IntPtr _hook;
    private HotkeyGesture? _pushToTalk;
    private bool _pushToTalkDown;
    private bool _disposed;

    public GlobalHotkeyService(Window window)
    {
        _window = window;
        _keyboardProc = KeyboardHook;
    }

    public event Action<HotkeyAction>? Pressed;
    public event Action<HotkeyAction>? Released;

    public bool Apply(IReadOnlyDictionary<HotkeyAction, string> gestures, out string error)
    {
        error = string.Empty;
        if (_source is null)
        {
            var handle = new WindowInteropHelper(_window).Handle;
            _source = HwndSource.FromHwnd(handle);
            _source?.AddHook(WindowMessageHook);
            _hook = SetWindowsHookEx(WhKeyboardLl, _keyboardProc, GetModuleHandle(null), 0);
            if (_hook == IntPtr.Zero)
            {
                error = "无法安装按住说话键盘监听。";
                return false;
            }
        }

        var parsed = new Dictionary<HotkeyAction, HotkeyGesture>();
        foreach (var pair in gestures)
        {
            if (!HotkeyGesture.TryParse(pair.Value, out var gesture))
            {
                error = $"快捷键格式无效：{pair.Value}";
                return false;
            }
            if (parsed.Values.Contains(gesture))
            {
                error = $"快捷键重复：{pair.Value}";
                return false;
            }
            parsed[pair.Key] = gesture;
        }

        UnregisterAll();
        var nextId = 0x5100;
        foreach (var pair in parsed)
        {
            var id = nextId++;
            if (!RegisterHotKey(new WindowInteropHelper(_window).Handle, id,
                    pair.Value.NativeModifiers | ModNoRepeat, (uint)pair.Value.VirtualKey))
            {
                UnregisterAll();
                error = $"快捷键已被其他程序占用：{pair.Value}";
                return false;
            }
            _registered[id] = pair.Key;
        }
        _pushToTalk = parsed[HotkeyAction.PushToTalk];
        return true;
    }

    private IntPtr WindowMessageHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && _registered.TryGetValue(wParam.ToInt32(), out var action))
        {
            handled = true;
            if (action != HotkeyAction.PushToTalk) Pressed?.Invoke(action);
        }
        return IntPtr.Zero;
    }

    private IntPtr KeyboardHook(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0 && _pushToTalk.HasValue)
        {
            var data = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);
            var isDown = wParam.ToInt32() is WmKeyDown or WmSysKeyDown;
            var isUp = wParam.ToInt32() is WmKeyUp or WmSysKeyUp;
            if (data.VirtualKey == _pushToTalk.Value.VirtualKey && ModifiersMatch(_pushToTalk.Value))
            {
                if (isDown && !_pushToTalkDown)
                {
                    _pushToTalkDown = true;
                    _window.Dispatcher.BeginInvoke(() => Pressed?.Invoke(HotkeyAction.PushToTalk));
                }
                else if (isUp && _pushToTalkDown)
                {
                    _pushToTalkDown = false;
                    _window.Dispatcher.BeginInvoke(() => Released?.Invoke(HotkeyAction.PushToTalk));
                }
                return new IntPtr(1);
            }

            if (isUp && data.VirtualKey == _pushToTalk.Value.VirtualKey && _pushToTalkDown)
            {
                _pushToTalkDown = false;
                _window.Dispatcher.BeginInvoke(() => Released?.Invoke(HotkeyAction.PushToTalk));
            }
        }
        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    private static bool ModifiersMatch(HotkeyGesture gesture)
    {
        static bool Down(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;
        return gesture.Control == Down(0x11) && gesture.Alt == Down(0x12) &&
               gesture.Shift == Down(0x10) && gesture.Windows == (Down(0x5B) || Down(0x5C));
    }

    private void UnregisterAll()
    {
        var handle = new WindowInteropHelper(_window).Handle;
        foreach (var id in _registered.Keys) UnregisterHotKey(handle, id);
        _registered.Clear();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        UnregisterAll();
        if (_hook != IntPtr.Zero) UnhookWindowsHookEx(_hook);
        _source?.RemoveHook(WindowMessageHook);
    }

    private readonly record struct HotkeyGesture(int VirtualKey, bool Control, bool Alt, bool Shift, bool Windows)
    {
        public uint NativeModifiers => (Control ? 0x0002u : 0) | (Alt ? 0x0001u : 0) |
                                       (Shift ? 0x0004u : 0) | (Windows ? 0x0008u : 0);

        public static bool TryParse(string text, out HotkeyGesture gesture)
        {
            gesture = default;
            var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0) return false;

            var modifiers = parts[..^1];
            if (modifiers.Any(x => !x.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) &&
                                   !x.Equals("Alt", StringComparison.OrdinalIgnoreCase) &&
                                   !x.Equals("Shift", StringComparison.OrdinalIgnoreCase) &&
                                   !x.Equals("Win", StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            var ctrl = modifiers.Any(x => x.Equals("Ctrl", StringComparison.OrdinalIgnoreCase));
            var alt = modifiers.Any(x => x.Equals("Alt", StringComparison.OrdinalIgnoreCase));
            var shift = modifiers.Any(x => x.Equals("Shift", StringComparison.OrdinalIgnoreCase));
            var win = modifiers.Any(x => x.Equals("Win", StringComparison.OrdinalIgnoreCase));
            if (!Enum.TryParse<Key>(parts[^1], true, out var key) || key is Key.LeftCtrl or Key.RightCtrl or
                Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
                return false;
            var virtualKey = KeyInterop.VirtualKeyFromKey(key);
            if (virtualKey == 0) return false;
            gesture = new HotkeyGesture(virtualKey, ctrl, alt, shift, win);
            return true;
        }

        public override string ToString()
        {
            var parts = new List<string>();
            if (Control) parts.Add("Ctrl");
            if (Alt) parts.Add("Alt");
            if (Shift) parts.Add("Shift");
            if (Windows) parts.Add("Win");
            parts.Add(KeyInterop.KeyFromVirtualKey(VirtualKey).ToString());
            return string.Join('+', parts);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KbdLlHookStruct
    {
        public int VirtualKey;
        public int ScanCode;
        public int Flags;
        public int Time;
        public IntPtr ExtraInfo;
    }

    private delegate IntPtr LowLevelKeyboardProc(int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint virtualKey);
    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int hookId, LowLevelKeyboardProc callback, IntPtr module, uint threadId);
    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);
}
