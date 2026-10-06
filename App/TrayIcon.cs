using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace BeijingClock.App;

internal static class TrayIcon
{
    internal static Icon Create()
    {
        using var bitmap = new Bitmap(32, 32);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);
        using var fill = new SolidBrush(Color.FromArgb(41, 121, 163));
        using var line = new Pen(Color.White, 2.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        graphics.FillEllipse(fill, 2, 2, 28, 28);
        graphics.DrawLine(line, 16, 8, 16, 16);
        graphics.DrawLine(line, 16, 16, 23, 20);
        IntPtr handle = bitmap.GetHicon();
        try
        {
            using var original = Icon.FromHandle(handle);
            return (Icon)original.Clone();
        }
        finally { DestroyIcon(handle); }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);
}
