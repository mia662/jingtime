$ErrorActionPreference = 'Stop'
$clockExe = Join-Path $PSScriptRoot 'Release\BeijingClock.exe'
if (-not (Test-Path -LiteralPath $clockExe -PathType Leaf)) {
    throw 'Build the clock first with ./Build.ps1.'
}

# Use the existing desktop's Shell object. Creating a new Shell.Application and
# calling ShellExecute directly can still inherit the caller's process job.
# See https://devblogs.microsoft.com/oldnewthing/20131118-00/?p=2643
$shellWindows = (New-Object -ComObject Shell.Application).Windows()
$desktopHandle = 0
$desktopWindow = $shellWindows.FindWindowSW(0, $null, 8, [ref]$desktopHandle, 1)
if ($null -eq $desktopWindow -or $null -eq $desktopWindow.Document.Application) {
    throw 'The Windows desktop is unavailable. Run this after signing in to the desktop.'
}

$desktopWindow.Document.Application.ShellExecute(
    $clockExe, '', (Split-Path -Parent $clockExe), 'open', 1)
