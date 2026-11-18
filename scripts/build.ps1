<#
  KONGBOX build: the Windows agent (self-contained single file) and the Android panel APK, into dist/.
  Tools are looked up in E:\DevTools first (where this machine keeps them), then in PATH.

  .\scripts\build.ps1            # agent + APK (release)
  .\scripts\build.ps1 -Debug     # faster debug builds
  .\scripts\build.ps1 -AgentOnly
#>
param([switch]$Debug, [switch]$AgentOnly, [switch]$ApkOnly)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Force $dist | Out-Null

function Tool($name, $preferred) { if (Test-Path $preferred) { return $preferred } return (Get-Command $name -ErrorAction Stop).Source }

if (Test-Path 'E:\DevTools') {
  $env:DOTNET_ROOT = 'E:\DevTools\dotnet'
  $env:NUGET_PACKAGES = 'E:\DevTools\nuget'
  $env:JAVA_HOME = 'E:\DevTools\jdk17'
  $env:GRADLE_USER_HOME = 'E:\DevTools\gradle-home'
  $env:ANDROID_HOME = 'E:\DevTools\android-sdk'
}
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'

if (-not $AgentOnly) {
  Write-Host '> APK' -ForegroundColor Cyan
  Push-Location (Join-Path $root 'android')
  try {
    $task = if ($Debug) { 'assembleDebug' } else { 'assembleRelease' }
    & .\gradlew.bat $task --console=plain -q
    if ($LASTEXITCODE -ne 0) { throw "gradle $task falhou" }
    $apk = Get-ChildItem -Recurse 'app\build\outputs\apk' -Filter *.apk | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    Copy-Item $apk.FullName (Join-Path $dist 'KongBox.apk') -Force
  } finally { Pop-Location }
}

if (-not $ApkOnly) {
  Write-Host '> agente' -ForegroundColor Cyan
  $dotnet = Tool 'dotnet' 'E:\DevTools\dotnet\dotnet.exe'
  $cfg = if ($Debug) { 'Debug' } else { 'Release' }
  & $dotnet publish (Join-Path $root 'agent\KongBox.Agent') -c $cfg -r win-x64 --self-contained true `
      -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o (Join-Path $dist 'agent') --nologo -v q
  if ($LASTEXITCODE -ne 0) { throw 'dotnet publish falhou' }
  # the agent installs the APK it finds next to itself
  if (Test-Path (Join-Path $dist 'KongBox.apk')) { Copy-Item (Join-Path $dist 'KongBox.apk') (Join-Path $dist 'agent\KongBox.apk') -Force }
}

Write-Host "OK: pronto em $dist" -ForegroundColor Green
Get-ChildItem $dist -Recurse -File | Where-Object { $_.Extension -in '.exe', '.apk' } | ForEach-Object { '{0,-40} {1,8:N0} KB' -f $_.FullName.Substring($dist.Length + 1), ($_.Length / 1KB) }
