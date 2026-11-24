param(
    [Parameter(Mandatory = $true)][string]$OtaZip,
    [Parameter(Mandatory = $true)][string]$BaselineZip,
    [string]$WorkDir = (Join-Path $env:RUNNER_TEMP 'nova-ota-test')
)
$ErrorActionPreference = 'Stop'
$OtaZip = (Resolve-Path $OtaZip).Path
$BaselineZip = (Resolve-Path $BaselineZip).Path
if (Test-Path $WorkDir) { Remove-Item $WorkDir -Recurse -Force }
New-Item -ItemType Directory -Force $WorkDir | Out-Null
Add-Type -AssemblyName System.IO.Compression.FileSystem
[IO.Compression.ZipFile]::ExtractToDirectory($BaselineZip, "$WorkDir/baseline")
[IO.Compression.ZipFile]::ExtractToDirectory($OtaZip, "$WorkDir/staged")
$root = Join-Path $WorkDir 'baseline/Nova'
$bin = Join-Path $root 'bin'
if (-not (Test-Path "$bin/Nova.exe")) { throw 'Unexpected baseline archive layout' }
foreach ($f in @('Nova.exe','Nova.Core.dll','nova-cli.exe','Nova.runtimeconfig.json','nova-cli.runtimeconfig.json','coreclr.dll','hostfxr.dll','hostpolicy.dll')) {
    if (-not (Test-Path "$WorkDir/staged/$f")) { throw "OTA missing $f" }
}
foreach ($f in @('Nova.runtimeconfig.json','nova-cli.runtimeconfig.json')) {
    $json = Get-Content "$WorkDir/staged/$f" -Raw | ConvertFrom-Json
    if (-not $json.runtimeOptions.includedFrameworks -or $json.runtimeOptions.frameworks) { throw "OTA has framework-dependent config: $f" }
}
# Simulate data outside bin and legacy CLI data under bin/data.
New-Item -Type Directory -Force "$root/instances/test/saves", "$bin/data", "$WorkDir/no-dotnet" | Out-Null
Set-Content -Path "$root/instances/test/saves/sentinel.txt" -Value 'world-preserved'
Set-Content -Path "$bin/data/sentinel.txt" -Value 'cli-data-preserved'
$oldRoot = $env:DOTNET_ROOT; $oldRootX64 = ${env:DOTNET_ROOT_X64}; $oldMulti = $env:DOTNET_MULTILEVEL_LOOKUP; $oldGui = $env:DOTNET_DISABLE_GUI_ERRORS
try {
    $env:DOTNET_ROOT = "$WorkDir/no-dotnet"; ${env:DOTNET_ROOT_X64} = "$WorkDir/no-dotnet"; $env:DOTNET_MULTILEVEL_LOOKUP = '0'; $env:DOTNET_DISABLE_GUI_ERRORS = '1'
    & "$bin/nova-cli.exe" help | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Baseline CLI could not run in isolated runtime environment' }
    # Same overlay operation used by the v2.1.0 LaunchDetachedSwapper.
    Copy-Item -Path "$WorkDir/staged/*" -Destination $bin -Recurse -Force -ErrorAction Stop
    & "$bin/nova-cli.exe" help | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Updated CLI could not run in isolated runtime environment' }
    if ((Get-Content "$root/instances/test/saves/sentinel.txt" -Raw).Trim() -ne 'world-preserved') { throw 'Instance data changed' }
    if ((Get-Content "$bin/data/sentinel.txt" -Raw).Trim() -ne 'cli-data-preserved') { throw 'CLI data changed' }
    Write-Host 'PASS: self-contained CLI starts after overlay with DOTNET_ROOT isolated; both sentinel files preserved.'
} finally {
    $env:DOTNET_ROOT = $oldRoot; ${env:DOTNET_ROOT_X64} = $oldRootX64; $env:DOTNET_MULTILEVEL_LOOKUP = $oldMulti; $env:DOTNET_DISABLE_GUI_ERRORS = $oldGui
}
