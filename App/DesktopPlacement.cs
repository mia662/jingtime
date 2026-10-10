using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using BeijingClock.Core;
using Forms = System.Windows.Forms;

namespace BeijingClock.App;

internal static class DesktopPlacement
{
    private const uint NoSize = 0x0001, NoZOrder = 0x0004, NoActivate = 0x0010, ShowWindow = 0x0040;

    internal static void ConfigureFloatingWindow(Window window)
    {
        IntPtr handle = new WindowInteropHelper(window).Handle;
        // WPF's hidden taskbar owner makes independent floating windows undiscoverable.
        // A real tool window remains absent from the taskbar and Alt+Tab without that owner.
        long extendedStyle = GetWindowLongPtr(handle, -20).ToInt64();
        SetWindowLongPtr(handle, -20, new IntPtr((extendedStyle | 0x80L) & ~0x40000L));
        SetWindowLongPtr(handle, -8, IntPtr.Zero);
        SetWindowPos(handle, IntPtr.Zero, 0, 0, 0, 0, NoSize | 0x0002 | NoZOrder | NoActivate | 0x0020);
    }

    internal static WindowPosition Get(Window window)
    {
        if (!GetWindowRect(new WindowInteropHelper(window).Handle, out var rect))
            throw new InvalidOperationException("无法读取小钟位置。");
        return new WindowPosition(rect.Left, rect.Top);
    }

    internal static void Restore(Window window, WindowPosition? saved)
    {
        IntPtr handle = new WindowInteropHelper(window).Handle;
        if (!GetWindowRect(handle, out var rect)) return;
        int width = rect.Right - rect.Left, height = rect.Bottom - rect.Top;
        var primary = Forms.Screen.PrimaryScreen ?? Forms.Screen.AllScreens[0];
        var screen = saved is null ? primary : Forms.Screen.AllScreens.FirstOrDefault(s =>
            s.WorkingArea.Contains(new System.Drawing.Point(saved.X + width / 2, saved.Y + height / 2)));
        screen ??= primary;
        var area = screen.WorkingArea;
        int x = saved?.X ?? area.Right - width - 24;
        int y = saved?.Y ?? area.Top + 96;
        // If a saved monitor disappeared, return to the visible primary work area.
        if (saved is not null && !Forms.Screen.AllScreens.Any(s =>
                s.WorkingArea.Contains(new System.Drawing.Point(saved.X + width / 2, saved.Y + height / 2))))
        {
            x = area.Right - width - 24;
            y = area.Top + 96;
        }
        x = Math.Clamp(x, area.Left, Math.Max(area.Left, area.Right - width));
        y = Math.Clamp(y, area.Top, Math.Max(area.Top, area.Bottom - height));
        SetWindowPos(handle, IntPtr.Zero, x, y, 0, 0, NoSize | NoZOrder | NoActivate);
    }

    internal static void ShowExisting(string title)
    {
        IntPtr handle = FindWindow(null, title);
        if (handle == IntPtr.Zero) return;
        // A second launch reveals the existing clock; it never creates another one.
        RevealHandle(handle);
    }

    internal static void Reveal(Window window) => RevealHandle(new WindowInteropHelper(window).Handle);

    internal static void MaintainTopmost(Window window)
    {
        IntPtr handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero || !IsWindowVisible(handle) || IsIconic(handle)) return;
        if ((GetWindowLongPtr(handle, -20).ToInt64() & 8) == 0 || IsCovered(handle))
            RevealHandle(handle);
    }

    private static bool IsCovered(IntPtr handle)
    {
        if (!GetWindowRect(handle, out var clock)) return false;
        IntPtr above = GetWindow(handle, 3); // GW_HWNDPREV: next window above us.
        for (int count = 0; above != IntPtr.Zero && count < 256; count++, above = GetWindow(above, 3))
        {
            if (!IsWindowVisible(above) || IsIconic(above)) continue;
            GetWindowThreadProcessId(above, out uint pid);
            // Our context menus and tooltips must stay above the clock.
            if (pid == Environment.ProcessId || !GetWindowRect(above, out var other)) continue;
            if (other.Left >= clock.Right || other.Right <= clock.Left ||
                other.Top >= clock.Bottom || other.Bottom <= clock.Top) continue;
            if (DwmGetWindowAttribute(above, 14, out int cloaked, sizeof(int)) == 0 && cloaked != 0) continue;
            return true;
        }
        return false;
    }

    private static void RevealHandle(IntPtr handle)
    {
        SetWindowPos(handle, new IntPtr(-1), 0, 0, 0, 0, NoSize | 0x0002 | NoActivate | ShowWindow);
    }

    internal static bool CloseExisting(string WindowTitle)
    {
        IntPtr handle = FindWindow(null, WindowTitle);
        if (handle == IntPtr.Zero) return true;
        GetWindowThreadProcessId(handle, out uint pid);
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById((int)pid);
            if (!string.Equals(process.ProcessName, "BeijingClock", StringComparison.OrdinalIgnoreCase)) return false;
            return PostMessage(handle, 0x0010, IntPtr.Zero, IntPtr.Zero);
        }
        catch (ArgumentException) { return true; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? className, string windowName);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr window, uint command);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int size);
}
