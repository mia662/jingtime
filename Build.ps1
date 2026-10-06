param(
    [switch]$Verify,
    [string]$DotnetPath,
    [string]$OutputDirectory = 'Release'
)
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$workspaceRoot = Split-Path -Parent $projectRoot
$portableSdk = Join-Path $workspaceRoot '.tools\dotnet\dotnet.exe'
if ($DotnetPath) {
    $sdkPath = (Get-Command $DotnetPath -ErrorAction Stop).Source
} elseif (Test-Path -LiteralPath $portableSdk -PathType Leaf) {
    $sdkPath = $portableSdk
} else {
    $sdk = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($null -eq $sdk) { throw 'Install the .NET 8 SDK, or pass -DotnetPath with the full path to dotnet.exe.' }
    $sdkPath = $sdk.Source
}
if (-not $env:DOTNET_CLI_HOME) { $env:DOTNET_CLI_HOME = Join-Path $projectRoot '.dotnet-home' }
if ([IO.Path]::IsPathRooted($OutputDirectory)) {
    $publishPath = [IO.Path]::GetFullPath($OutputDirectory)
} else {
    $publishPath = [IO.Path]::GetFullPath((Join-Path $projectRoot $OutputDirectory))
}
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
& $sdkPath build (Join-Path $projectRoot 'App\BeijingClock.App.csproj') -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Clock build failed.' }
if ($Verify) {
    & $sdkPath run --project (Join-Path $projectRoot 'Tests\BeijingClock.Tests.csproj') -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Clock tests failed.' }
}
& $sdkPath publish (Join-Path $projectRoot 'App\BeijingClock.App.csproj') -c Release --no-build --no-restore --self-contained false -o $publishPath --nologo
if ($LASTEXITCODE -ne 0) { throw 'Clock publish failed.' }
Write-Output (Join-Path $publishPath 'BeijingClock.exe')
