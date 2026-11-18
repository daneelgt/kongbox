<#
  KONGBOX release build. Produces, in dist/:
    KongBox-Setup.exe   the Windows app: run it on any PC and it installs itself (no admin needed).
                        The phone app is embedded inside it.
    KongBox.apk         the Android app, for installing on a phone by hand or over Wi-Fi.

  .\scripts\build.ps1          release (signed APK if the KONGBOX_* signing properties exist)
  .\scripts\build.ps1 -Debug   faster debug builds

  Tools are looked up in E:\DevTools first (where this machine keeps them), then in PATH.
#>
param([switch]$Debug)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Force $dist | Out-Null

if (Test-Path 'E:\DevTools') {
  $env:DOTNET_ROOT = 'E:\DevTools\dotnet'
  $env:NUGET_PACKAGES = 'E:\DevTools\nuget'
  $env:JAVA_HOME = 'E:\DevTools\jdk17'
  $env:GRADLE_USER_HOME = 'E:\DevTools\gradle-home'
  $env:ANDROID_HOME = 'E:\DevTools\android-sdk'
}
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$dotnet = if (Test-Path 'E:\DevTools\dotnet\dotnet.exe') { 'E:\DevTools\dotnet\dotnet.exe' } else { 'dotnet' }

# 1. the phone app first: the Windows app embeds it
Write-Host '> APK' -ForegroundColor Cyan
Push-Location (Join-Path $root 'android')
try {
  $task = if ($Debug) { 'assembleDebug' } else { 'assembleRelease' }
  & .\gradlew.bat $task --console=plain -q
  if ($LASTEXITCODE -ne 0) { throw "gradle $task failed" }
  $flavor = if ($Debug) { 'debug' } else { 'release' }
  $apk = Get-ChildItem "app\build\outputs\apk\$flavor" -Filter *.apk | Sort-Object LastWriteTime -Descending | Select-Object -First 1
  Copy-Item $apk.FullName (Join-Path $dist 'KongBox.apk') -Force
} finally { Pop-Location }

# 2. the Windows app: one self-contained exe that is also its own installer
Write-Host '> agente' -ForegroundColor Cyan
$cfg = if ($Debug) { 'Debug' } else { 'Release' }
$out = Join-Path $dist '_publish'
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
& $dotnet publish (Join-Path $root 'agent\KongBox.Agent') -c $cfg -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o $out --nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }
Copy-Item (Join-Path $out 'KongBoxAgent.exe') (Join-Path $dist 'KongBox-Setup.exe') -Force
Remove-Item $out -Recurse -Force
if (Test-Path (Join-Path $dist 'agent')) { Remove-Item (Join-Path $dist 'agent') -Recurse -Force }

Write-Host "OK: $dist" -ForegroundColor Green
Get-ChildItem $dist -File | ForEach-Object { '{0,-22} {1,10:N0} KB   sha256 {2}' -f $_.Name, ($_.Length / 1KB), (Get-FileHash $_.FullName -Algorithm SHA256).Hash.Substring(0, 16) }
