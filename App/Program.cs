using System;
using System.IO;
using System.Threading;
using System.Windows;

namespace BeijingClock.App;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            if (args.Length == 1 && args[0] == "--quit")
                return DesktopPlacement.CloseExisting(WindowTitle: ClockWindow.WindowTitle) ? 0 : 1;

            if (args.Length == 2 && args[0] == "--verify-ui")
                return Run(args[1]);

            if (args.Length != 0)
            {
                MessageBox.Show("双击程序即可打开北京时间小钟。", "北京时间小钟");
                return 2;
            }

            using var instance = new Mutex(true, @"Local\BeijingClock.ProgressLine.v1", out bool first);
            if (!first)
            {
                DesktopPlacement.ShowExisting(ClockWindow.WindowTitle);
                return 0;
            }

            try { return Run(null); }
            finally { instance.ReleaseMutex(); }
        }
        catch (Exception ex)
        {
            MessageBox.Show("小钟暂时无法启动。\n" + ex.Message, "北京时间小钟", MessageBoxButton.OK, MessageBoxImage.Error);
            return 1;
        }
    }

    private static int Run(string? verificationDirectory)
    {
        if (verificationDirectory is not null)
            Directory.CreateDirectory(Path.GetFullPath(verificationDirectory));

        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        var clock = new ClockWindow(verificationDirectory);
        return app.Run(clock);
    }
}
