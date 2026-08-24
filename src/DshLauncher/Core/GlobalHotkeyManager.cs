using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace DshLauncher.Core;

/// <summary>
/// 全局热键（C11）：通过 Win32 RegisterHotKey 注册系统级热键，随时唤起/打开界面/停止 DSH。
/// 热键：Ctrl+Alt+D 唤起启动器；Ctrl+Alt+E 打开 DSH 界面；Ctrl+Alt+S 停止 DSH。
/// </summary>
public static class GlobalHotkeyManager
{
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_ALT = 0x0001, MOD_CONTROL = 0x0002, MOD_NOREPEAT = 0x4000;
    private const int HOTKEY_SHOW = 0x2001, HOTKEY_OPEN = 0x2002, HOTKEY_STOP = 0x2003;

    private static IntPtr _hwnd = IntPtr.Zero;
    private static HwndSource? _source;

    /// <summary>触发动作（从窗口消息循环回调，天然在 UI 线程）。</summary>
    public static Action? OnShow;
    public static Action? OnOpen;
    public static Action? OnStop;

    /// <summary>绑定到主窗口（SourceInitialized 后注册热键）。</summary>
    public static void Attach(Window? window)
    {
        if (window is null) return;
        window.SourceInitialized += (_, _) =>
        {
            _source = (HwndSource?)PresentationSource.FromVisual(window);
            if (_source is null) return;
            _hwnd = _source.Handle;
            _source.AddHook(WndProc);
            RegisterHotKey(_hwnd, HOTKEY_SHOW, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, (uint)KeyInterop.VirtualKeyFromKey(Key.D));
            RegisterHotKey(_hwnd, HOTKEY_OPEN, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, (uint)KeyInterop.VirtualKeyFromKey(Key.E));
            RegisterHotKey(_hwnd, HOTKEY_STOP, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, (uint)KeyInterop.VirtualKeyFromKey(Key.S));
        };
    }

    private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_HOTKEY) return IntPtr.Zero;
        var id = wParam.ToInt32();
        switch (id)
        {
            case HOTKEY_SHOW: OnShow?.Invoke(); handled = true; break;
            case HOTKEY_OPEN: OnOpen?.Invoke(); handled = true; break;
            case HOTKEY_STOP: OnStop?.Invoke(); handled = true; break;
        }
        return IntPtr.Zero;
    }

    public static void Detach()
    {
        if (_hwnd != IntPtr.Zero)
        {
            UnregisterHotKey(_hwnd, HOTKEY_SHOW);
            UnregisterHotKey(_hwnd, HOTKEY_OPEN);
            UnregisterHotKey(_hwnd, HOTKEY_STOP);
            _hwnd = IntPtr.Zero;
        }
        if (_source is not null)
        {
            _source.RemoveHook(WndProc);
            _source = null;
        }
    }
}
