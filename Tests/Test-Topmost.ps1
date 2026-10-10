param(
    [string]$ExecutablePath = (Join-Path $PSScriptRoot '..\Release\BeijingClock.exe'),
    [string]$ReportPath = (Join-Path $PSScriptRoot '..\artifacts\topmost-regression.json')
)
$ErrorActionPreference = 'Stop'
$ExecutablePath = [IO.Path]::GetFullPath($ExecutablePath)
$clock = Get-Process -Name BeijingClock -ErrorAction Stop |
    Where-Object { $_.Path -eq $ExecutablePath } | Select-Object -First 1
if ($null -eq $clock) { throw 'Start the expected clock on this desktop before running the test.' }

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class JingTimeTopmostRegression {
    public delegate bool EnumProc(IntPtr hwnd, IntPtr data);
    [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc callback, IntPtr data);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll", EntryPoint="GetWindowLongPtrW")] public static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr hwnd, uint command);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", SetLastError=true)] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    public static extern IntPtr CreateWindowEx(uint extendedStyle, string className, string title, uint style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
    [DllImport("user32.dll")] public static extern bool DestroyWindow(IntPtr hwnd);
    public static IntPtr Find(uint expectedPid) {
        IntPtr result = IntPtr.Zero;
        EnumWindows((hwnd, data) => { uint pid; GetWindowThreadProcessId(hwnd, out pid);
            if (pid == expectedPid && IsWindowVisible(hwnd)) { result = hwnd; return false; }
            return true;
        }, IntPtr.Zero);
        return result;
    }
    public static bool Topmost(IntPtr hwnd) => (GetWindowLongPtr(hwnd, -20).ToInt64() & 8) != 0;
    public static bool Above(IntPtr upper, IntPtr lower) {
        IntPtr current = GetWindow(lower, 3);
        for (int count = 0; current != IntPtr.Zero && count < 512; count++, current = GetWindow(current, 3))
            if (current == upper) return true;
        return false;
    }
    public static bool CoverAndConfirm(IntPtr cover, IntPtr clock) =>
        SetWindowPos(cover, new IntPtr(-1), 0, 0, 0, 0, 0x13) && Topmost(clock) && Above(cover, clock);
    public static bool DemoteAndConfirm(IntPtr clock) =>
        SetWindowPos(clock, new IntPtr(-2), 0, 0, 0, 0, 0x13) && !Topmost(clock);
}
'@

$clockWindow = [JingTimeTopmostRegression]::Find($clock.Id)
if ($clockWindow -eq [IntPtr]::Zero) { throw 'No visible clock window was found.' }
$originalBounds = New-Object JingTimeTopmostRegression+Rect
$null = [JingTimeTopmostRegression]::GetWindowRect($clockWindow, [ref]$originalBounds)
$foregroundBefore = [JingTimeTopmostRegression]::GetForegroundWindow()
$cover = [IntPtr]::Zero
$checks = [Collections.Generic.List[object]]::new()
try {
    # A real, temporary topmost window covers only the clock and never takes focus.
    $cover = [JingTimeTopmostRegression]::CreateWindowEx(0x08000088, 'STATIC', 'JingTime regression cover',
        ([uint32]2415919104), $originalBounds.Left, $originalBounds.Top,
        $originalBounds.Right - $originalBounds.Left, $originalBounds.Bottom - $originalBounds.Top,
        [IntPtr]::Zero, [IntPtr]::Zero, [IntPtr]::Zero, [IntPtr]::Zero)
    if ($cover -eq [IntPtr]::Zero) { throw 'Could not create the temporary test window.' }
    # Sample immediately within one native helper: a working guard can repair the
    # cover between separate PowerShell statements, before setup is observed.
    $coveredWhileStillTopmost = $false
    for ($attempt = 0; $attempt -lt 3 -and -not $coveredWhileStillTopmost; $attempt++) {
        $coveredWhileStillTopmost = [JingTimeTopmostRegression]::CoverAndConfirm($cover, $clockWindow)
    }
    $watch = [Diagnostics.Stopwatch]::StartNew()
    do {
        if ([JingTimeTopmostRegression]::Above($clockWindow, $cover)) { break }
        Start-Sleep -Milliseconds 50
    } while ($watch.ElapsedMilliseconds -lt 3500)
    $checks.Add([pscustomobject]@{ Name='Recover when another topmost window covers the clock';
        SetupVerified=$coveredWhileStillTopmost; Passed=($coveredWhileStillTopmost -and [JingTimeTopmostRegression]::Above($clockWindow, $cover));
        RecoveryMs=$watch.ElapsedMilliseconds })

    $demotionVerified = [JingTimeTopmostRegression]::DemoteAndConfirm($clockWindow)
    $watch.Restart()
    do {
        if ([JingTimeTopmostRegression]::Topmost($clockWindow) -and [JingTimeTopmostRegression]::Above($clockWindow, $cover)) { break }
        Start-Sleep -Milliseconds 50
    } while ($watch.ElapsedMilliseconds -lt 3500)
    $checks.Add([pscustomobject]@{ Name='Recover after native topmost status is removed';
        SetupVerified=$demotionVerified; Passed=($demotionVerified -and [JingTimeTopmostRegression]::Topmost($clockWindow) -and [JingTimeTopmostRegression]::Above($clockWindow, $cover));
        RecoveryMs=$watch.ElapsedMilliseconds })

    $finalBounds = New-Object JingTimeTopmostRegression+Rect
    $null = [JingTimeTopmostRegression]::GetWindowRect($clockWindow, [ref]$finalBounds)
    $checks.Add([pscustomobject]@{ Name='Keyboard focus is unchanged'; Passed=($foregroundBefore -eq [JingTimeTopmostRegression]::GetForegroundWindow()) })
    $checks.Add([pscustomobject]@{ Name='Clock position and size are unchanged'; Passed=($originalBounds.Equals($finalBounds)) })
} finally {
    if ($cover -ne [IntPtr]::Zero) { $null = [JingTimeTopmostRegression]::DestroyWindow($cover) }
    $null = [JingTimeTopmostRegression]::SetWindowPos($clockWindow, [IntPtr](-1), 0, 0, 0, 0, 0x13)
}

$passed = @($checks | Where-Object { -not $_.Passed }).Count -eq 0 -and $checks.Count -eq 4
$report = [pscustomobject]@{ Passed=$passed; ClockPid=$clock.Id; Checks=$checks.ToArray() }
$ReportPath = [IO.Path]::GetFullPath($ReportPath)
New-Item -ItemType Directory -Path (Split-Path -Parent $ReportPath) -Force | Out-Null
$report | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $ReportPath -Encoding utf8
$report | ConvertTo-Json -Depth 5
if (-not $passed) { throw 'Topmost regression check failed. See the saved report.' }
