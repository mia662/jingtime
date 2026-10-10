using System;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace BeijingClock.App;

internal sealed class ForegroundWatcher : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly Action _refresh;
    private readonly WinEventCallback _callback;
    private IntPtr _hook;
    private DispatcherOperation? _pending;
    private bool _disposed;

    internal ForegroundWatcher(Dispatcher dispatcher, Action refresh)
    {
        _dispatcher = dispatcher;
        _refresh = refresh;
        _callback = OnForegroundChanged;
        // EVENT_SYSTEM_FOREGROUND, out of context, excluding our own popups.
        _hook = SetWinEventHook(3, 3, IntPtr.Zero, _callback, 0, 0, 2);
        // If Windows cannot install the hook, the clock's existing timer repairs Z-order.
    }

    private void OnForegroundChanged(IntPtr hook, uint eventType, IntPtr window,
        int objectId, int childId, uint threadId, uint time)
    {
        if (_disposed || _dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished) return;
        if (_pending?.Status == DispatcherOperationStatus.Pending) return;
        _pending = _dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            if (!_disposed) _refresh();
        }));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_hook != IntPtr.Zero) UnhookWinEvent(_hook);
        _hook = IntPtr.Zero;
        _pending?.Abort();
    }

    private delegate void WinEventCallback(IntPtr hook, uint eventType, IntPtr window,
        int objectId, int childId, uint threadId, uint time);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr module,
        WinEventCallback callback, uint processId, uint threadId, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWinEvent(IntPtr hook);
}
