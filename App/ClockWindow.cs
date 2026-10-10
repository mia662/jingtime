using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using BeijingClock.Core;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace BeijingClock.App;

internal sealed class ClockWindow : Window
{
    internal const string WindowTitle = "北京时间小钟 · 中线进度";
    private const double CardWidth = 158, CardHeight = 62, ShadowPadding = 8;
    private const double ContentPadding = 9, ProgressTrackWidth = CardWidth - 2 * ContentPadding;
    private static readonly Brush Ink = BrushOf("#183B4F");
    private static readonly Brush SecondaryInk = BrushOf("#517180");
    private readonly TextBlock _date = Text(11, SecondaryInk);
    private readonly TextBlock _time = Text(24, Ink);
    private readonly TextBlock _remainingLabel = Text(11, SecondaryInk);
    private readonly TextBlock _remaining = Text(11, Ink);
    private readonly Border _fill = new() { Background = BrushOf("#2979A3"), HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Border _card;
    private readonly Border _track;
    private readonly DispatcherTimer _timer = new(DispatcherPriority.Background);
    private readonly PositionStore _positions = new(Path.Combine(AppContext.BaseDirectory, "clock-position.json"));
    private readonly string? _verificationDirectory;
    private Forms.NotifyIcon? _tray;
    private System.Drawing.Icon? _trayIcon;
    private ForegroundWatcher? _foregroundWatcher;
    private bool _positionReady;
    private bool _warnedSave;
    private ClockSnapshot? _snapshot;

    internal ClockWindow(string? verificationDirectory)
    {
        _verificationDirectory = verificationDirectory;
        Title = WindowTitle;
        Width = CardWidth + 2 * ShadowPadding;
        Height = CardHeight + 2 * ShadowPadding;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        FontFamily = new FontFamily("Microsoft YaHei UI");
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);

        var canvas = new Grid { Width = CardWidth, Height = CardHeight };
        canvas.Clip = new RectangleGeometry(new Rect(0, 0, CardWidth, CardHeight), 6, 6);
        _card = new Border
        {
            Width = CardWidth, Height = CardHeight, CornerRadius = new CornerRadius(6),
            Background = BrushOf("#F0F7FA"), Child = canvas,
            Effect = new DropShadowEffect { BlurRadius = 12, ShadowDepth = 2, Opacity = 0.18, Color = Color.FromRgb(20, 55, 81) }
        };
        Content = _card;

        _date.HorizontalAlignment = HorizontalAlignment.Right;
        _date.VerticalAlignment = VerticalAlignment.Center;
        _time.HorizontalAlignment = HorizontalAlignment.Left;
        _time.VerticalAlignment = VerticalAlignment.Center;
        _time.FontFamily = new FontFamily("Segoe UI");
        _time.FontWeight = FontWeights.Medium;
        _time.LineHeight = 28;
        _time.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
        var header = new Grid { Margin = new Thickness(ContentPadding, 5, ContentPadding, 0), Height = 28, VerticalAlignment = VerticalAlignment.Top };
        header.Children.Add(_time);
        header.Children.Add(_date);
        _remainingLabel.Text = "今日剩余";
        _remainingLabel.HorizontalAlignment = HorizontalAlignment.Left;
        _remainingLabel.VerticalAlignment = VerticalAlignment.Center;
        _remaining.VerticalAlignment = VerticalAlignment.Center;
        _remaining.HorizontalAlignment = HorizontalAlignment.Right;
        _remaining.FontWeight = FontWeights.Medium;
        foreach (TextBlock text in new[] { _date, _remainingLabel, _remaining })
        {
            text.LineHeight = 14;
            text.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
        }
        var footer = new Grid { Margin = new Thickness(ContentPadding, 44, ContentPadding, 0), Height = 14, VerticalAlignment = VerticalAlignment.Top };
        footer.Children.Add(_remainingLabel);
        footer.Children.Add(_remaining);
        canvas.Children.Add(header);
        canvas.Children.Add(footer);
        _track = new Border
        {
            Width = ProgressTrackWidth, Height = 3, Margin = new Thickness(0, 36, 0, 0),
            VerticalAlignment = VerticalAlignment.Top, CornerRadius = new CornerRadius(1.5),
            Clip = new RectangleGeometry(new Rect(0, 0, ProgressTrackWidth, 3), 1.5, 1.5),
            Background = BrushOf("#D5E6EF"), Child = _fill
        };
        canvas.Children.Add(_track);
        AutomationProperties.SetName(_date, "北京时间日期");
        AutomationProperties.SetName(_time, "北京时间");
        AutomationProperties.SetName(_remaining, "距北京时间今日结束");
        AutomationProperties.SetName(_fill, "今日已过比例");
        _card.ToolTip = "北京时间（UTC+8）\n按住拖动 · 右键退出";
        ToolTipService.SetInitialShowDelay(_card, 900);
        _card.MouseLeftButtonDown += (_, e) =>
        {
            if (_verificationDirectory is not null) return;
            try { DragMove(); }
            catch (InvalidOperationException) { /* Mouse button was released before dragging began. */ }
            SavePosition();
            e.Handled = true;
        };
        var menu = new ContextMenu();
        var reset = new MenuItem { Header = "回到右上角" };
        reset.Click += (_, _) => ResetPosition();
        var exit = new MenuItem { Header = "退出小钟" };
        exit.Click += (_, _) => Close();
        menu.Items.Add(reset);
        menu.Items.Add(new Separator());
        menu.Items.Add(exit);
        _card.ContextMenu = menu;

        Loaded += OnLoaded;
        Closing += (_, _) => SavePosition();
        Closed += OnClosed;
        _timer.Interval = TimeSpan.FromSeconds(1);
        _timer.Tick += (_, _) =>
        {
            UpdateClock(DateTimeOffset.UtcNow);
            MaintainTopmost();
        };
        UpdateClock(DateTimeOffset.UtcNow);
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        DesktopPlacement.ConfigureFloatingWindow(this);
        if (_verificationDirectory is not null)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(VerifyUi));
            return;
        }
        WindowPosition? savedPosition;
        try { savedPosition = _positions.Load(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { savedPosition = null; }
        DesktopPlacement.Restore(this, savedPosition);
        _positionReady = true;
        CreateTray();
        SystemEvents.DisplaySettingsChanged += DisplaySettingsChanged;
        _foregroundWatcher = new ForegroundWatcher(Dispatcher, MaintainTopmost);
        _timer.Start();
        // A terminal or startup launcher may pass a hidden initial show state.
        // Reveal after WPF finishes startup without stealing keyboard focus.
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => DesktopPlacement.Reveal(this)));
    }

    private void UpdateClock(DateTimeOffset instant)
    {
        ClockSnapshot next = BeijingTime.At(instant);
        if (_snapshot?.DateText != next.DateText) _date.Text = next.BeijingTime.ToString("yyyy.MM.dd", CultureInfo.InvariantCulture);
        if (_snapshot?.TimeText != next.TimeText) _time.Text = next.TimeText;
        if (_snapshot?.RemainingText != next.RemainingText) _remaining.Text = next.RemainingText;
        double width = Math.Round((1 - next.RemainingFraction) * ProgressTrackWidth, 1);
        if (Math.Abs(_fill.Width - width) > 0.01 || double.IsNaN(_fill.Width)) _fill.Width = width;
        if (_tray is not null && _snapshot?.TimeText != next.TimeText)
            _tray.Text = "北京时间 " + next.TimeText + " · 今日剩余 " + next.RemainingText;
        _snapshot = next;
    }

    private void CreateTray()
    {
        _trayIcon = TrayIcon.Create();
        _tray = new Forms.NotifyIcon { Icon = _trayIcon, Text = "北京时间小钟", Visible = true };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("显示小钟", null, (_, _) => Dispatcher.Invoke(ShowClock));
        menu.Items.Add("回到右上角", null, (_, _) => Dispatcher.Invoke(ResetPosition));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出小钟", null, (_, _) => Dispatcher.Invoke(Close));
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => Dispatcher.Invoke(ShowClock);
    }

    private void ShowClock()
    {
        Show();
        DesktopPlacement.Reveal(this);
    }

    private void MaintainTopmost()
    {
        if (!_positionReady || _card.ContextMenu?.IsOpen == true || _tray?.ContextMenuStrip?.Visible == true) return;
        DesktopPlacement.MaintainTopmost(this);
    }

    private void ResetPosition()
    {
        DesktopPlacement.Restore(this, null);
        SavePosition();
    }

    private void SavePosition()
    {
        if (!_positionReady || _verificationDirectory is not null) return;
        try { _positions.Save(DesktopPlacement.Get(this)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            if (!_warnedSave)
            {
                _warnedSave = true;
                _tray?.ShowBalloonTip(5000, "北京时间小钟", "当前位置无法保存，下次打开会回到右上角。", Forms.ToolTipIcon.Info);
            }
        }
    }

    private void DisplaySettingsChanged(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(new Action(EnsureVisible));
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        // WPF may resize after moving to a screen with a different DPI.
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(EnsureVisible));
    }

    private void EnsureVisible()
    {
        if (!_positionReady) return;
        WindowPosition? position;
        try { position = DesktopPlacement.Get(this); }
        catch (InvalidOperationException) { position = null; }
        DesktopPlacement.Restore(this, position);
        SavePosition();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _timer.Stop();
        _foregroundWatcher?.Dispose();
        SystemEvents.DisplaySettingsChanged -= DisplaySettingsChanged;
        _positionReady = false;
        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.ContextMenuStrip?.Dispose();
            _tray.Dispose();
        }
        _trayIcon?.Dispose();
    }

    private void VerifyUi()
    {
        var reports = new List<object>();
        bool passed = true;
        try
        {
            var samples = new[]
            {
                (Name: "selected-design", Instant: new DateTimeOffset(2026, 10, 6, 9, 12, 0, TimeSpan.Zero)),
                (Name: "noon-half", Instant: new DateTimeOffset(2026, 10, 6, 4, 0, 0, TimeSpan.Zero)),
                (Name: "before-midnight", Instant: new DateTimeOffset(2026, 12, 31, 15, 59, 59, TimeSpan.Zero)),
                (Name: "new-year-midnight", Instant: new DateTimeOffset(2026, 12, 31, 16, 0, 0, TimeSpan.Zero)),
                (Name: "live-preview", Instant: DateTimeOffset.UtcNow)
            };
            foreach (var sample in samples)
            {
                UpdateClock(sample.Instant);
                UpdateLayout();
                Rect dateBounds = BoundsInCard(_date), timeBounds = BoundsInCard(_time);
                Rect labelBounds = BoundsInCard(_remainingLabel), remainingBounds = BoundsInCard(_remaining);
                Rect trackBounds = BoundsInCard(_track);
                Rect cardBounds = new(0, 0, CardWidth, CardHeight);
                bool layoutPassed = new[] { dateBounds, timeBounds, labelBounds, remainingBounds, trackBounds }.All(cardBounds.Contains) &&
                    timeBounds.Right + 4 <= dateBounds.Left && labelBounds.Right + 8 <= remainingBounds.Left &&
                    Math.Max(timeBounds.Bottom, dateBounds.Bottom) + 2 <= trackBounds.Top &&
                    trackBounds.Bottom + 3 <= Math.Min(labelBounds.Top, remainingBounds.Top);
                bool progressPassed = _fill.Width >= 0 && _fill.Width <= _track.ActualWidth &&
                    Math.Abs(_fill.Width - (1 - _snapshot!.RemainingFraction) * _track.ActualWidth) <= 0.051;
                passed &= layoutPassed && progressPassed;
                SavePreview(Path.Combine(_verificationDirectory!, sample.Name + ".png"));
                reports.Add(new { sample.Name, Utc = sample.Instant, Snapshot = _snapshot, DisplayedDate = _date.Text,
                    LayoutPassed = layoutPassed, ProgressPassed = progressPassed,
                    DateWidth = _date.DesiredSize.Width, TimeWidth = _time.DesiredSize.Width,
                    RemainingWidth = _remaining.DesiredSize.Width, LabelWidth = _remainingLabel.DesiredSize.Width,
                    DateHeight = _date.ActualHeight, RemainingHeight = _remaining.ActualHeight,
                    ElapsedFraction = 1 - _snapshot!.RemainingFraction, ProgressWidth = _fill.Width, TrackWidth = _track.ActualWidth,
                    LogicalWindowWidth = ActualWidth, LogicalWindowHeight = ActualHeight, Topmost, ShowInTaskbar });
            }
            File.WriteAllText(Path.Combine(_verificationDirectory!, "ui-report.json"), JsonSerializer.Serialize(new
            { Passed = passed, CardWidth, CardHeight, Samples = reports }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            passed = false;
            File.WriteAllText(Path.Combine(_verificationDirectory!, "ui-error.txt"), ex.ToString());
        }
        Application.Current.Shutdown(passed ? 0 : 1);
    }

    private void SavePreview(string path)
    {
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(ActualWidth * 2), (int)Math.Ceiling(ActualHeight * 2), 192, 192, PixelFormats.Pbgra32);
        bitmap.Render(this);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(path);
        encoder.Save(output);
    }

    private Rect BoundsInCard(FrameworkElement element) =>
        new(element.TranslatePoint(new Point(0, 0), _card), new Size(element.ActualWidth, element.ActualHeight));

    private static TextBlock Text(double size, Brush color) => new()
    {
        FontSize = size, Foreground = color, VerticalAlignment = VerticalAlignment.Top,
        TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.None
    };

    private static Brush BrushOf(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }
}
