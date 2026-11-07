#Requires -Version 5.1
[CmdletBinding()]
param(
    [string]$Source = $PSScriptRoot,
    [string]$Output = (Join-Path $PSScriptRoot 'artifacts'),
    [switch]$InstallSdk,
    [switch]$Offline,
    [string]$SdkCache = (Join-Path $env:LOCALAPPDATA 'PoeAdaptive\Builder\SDK')
)
$ErrorActionPreference = 'Stop'
# The ready EXE needs neither a preinstalled SDK nor compiling Builder itself.
$builder = Join-Path $PSScriptRoot 'PoeAdaptive.Builder.exe'
$buildArgs = @('--source', $Source, '--output', $Output, '--sdk-cache', $SdkCache)
if ($Offline) { $buildArgs += '--offline' }
if ($InstallSdk) { $buildArgs += '--install-sdk' }
if (Test-Path $builder) {
    $start = New-Object System.Diagnostics.ProcessStartInfo
    $start.FileName = $builder
    $start.UseShellExecute = $false
    # .NET Framework used by PowerShell 5.1 has no ArgumentList; Windows paths cannot contain quotes.
    $quoted = @('--build') + $buildArgs | ForEach-Object { '"' + $_.TrimEnd('\') + '"' }
    $start.Arguments = $quoted -join ' '
    $process = [System.Diagnostics.Process]::Start($start)
    try { $process.WaitForExit(); exit $process.ExitCode } finally { $process.Dispose() }
}
$spec = Get-Content -Raw (Join-Path $Source 'build\sdk.json') | ConvertFrom-Json
$dotnet = Join-Path $SdkCache ($spec.version + '\dotnet.exe')
if (!(Test-Path $dotnet)) { $dotnet = 'dotnet' }
$sdks = @()
try { $sdks = & $dotnet --list-sdks } catch { }
if (!($sdks | Where-Object { $_ -match ('^' + [regex]::Escape($spec.version) + ' ') })) {
    if (!$InstallSdk) {
        if ((Read-Host "Install Microsoft .NET SDK $($spec.version) in $SdkCache ? [y/N]") -ne 'y') { throw 'Pinned SDK required. Use -InstallSdk to bootstrap without GUI.' }
    }
    if ($Offline) { throw 'SDK missing; first preparation requires Internet.' }
    New-Item -ItemType Directory -Force $SdkCache | Out-Null
    $uri = [uri]$spec.url
    if ($uri.Scheme -ne 'https' -or $uri.Host -ne 'builds.dotnet.microsoft.com' -or $spec.sha512 -notmatch '^[0-9a-fA-F]{128}$') { throw 'Invalid SDK source.' }
    $job = Join-Path $SdkCache ('.bootstrap-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory $job | Out-Null
    try {
        $zip = Join-Path $job 'sdk.zip'
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        Invoke-WebRequest -UseBasicParsing $spec.url -OutFile $zip
        if ((Get-FileHash $zip -Algorithm SHA512).Hash -ne $spec.sha512) { throw 'SDK SHA-512 mismatch.' }
        $extract = Join-Path $job 'sdk'
        Expand-Archive $zip $extract
        $dotnet = Join-Path $extract 'dotnet.exe'
        if ((& $dotnet --version) -ne $spec.version) { throw 'Wrong SDK.' }
        $destination = Join-Path $SdkCache $spec.version
        Move-Item $extract $destination
        $dotnet = Join-Path $destination 'dotnet.exe'
    } finally { Remove-Item -LiteralPath $job -Recurse -Force }
}
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
& $dotnet run --project (Join-Path $Source 'PoeAdaptive.BuildHost') -c Release -- @buildArgs --dotnet $dotnet
exit $LASTEXITCODE
