param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$OutputDir = '.'
)
$ErrorActionPreference = 'Stop'
$OutputDir = [IO.Path]::GetFullPath($OutputDir)
$work = Join-Path $OutputDir 'portable'
$root = Join-Path $work 'Nova'
$bin = Join-Path $root 'bin'
if (Test-Path $work) { Remove-Item $work -Recurse -Force }
New-Item -ItemType Directory -Force -Path $bin, "$root/instances", "$root/versions", "$root/logs" | Out-Null

dotnet publish src/Nova.App/Nova.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false "-p:Version=$Version" "-p:InformationalVersion=$Version" -o $bin
if ($LASTEXITCODE -ne 0) { throw 'Nova.App publish failed' }
dotnet publish src/Nova.Cli/nova-cli.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false "-p:Version=$Version" "-p:InformationalVersion=$Version" -o $bin
if ($LASTEXITCODE -ne 0) { throw 'nova-cli publish failed' }

# Both executables and their app-local .NET/WPF runtime must come from the SAME
# self-contained publish. Never overlay framework-dependent runtimeconfig files.
foreach ($config in @('Nova.runtimeconfig.json', 'nova-cli.runtimeconfig.json')) {
    $json = Get-Content (Join-Path $bin $config) -Raw | ConvertFrom-Json
    if (-not $json.runtimeOptions.includedFrameworks -or $json.runtimeOptions.frameworks) {
        throw "$config is not self-contained"
    }
}
foreach ($file in @('Nova.exe', 'nova-cli.exe', 'Nova.Core.dll', 'coreclr.dll', 'hostfxr.dll', 'hostpolicy.dll')) {
    if (-not (Test-Path (Join-Path $bin $file))) { throw "Missing $file" }
}

# Debug symbols stay out of release packages (smaller download, no source paths leak).
Get-ChildItem $bin -Filter '*.pdb' -ErrorAction SilentlyContinue | Remove-Item -Force

"@echo off`r`nstart `"`" `"%~dp0bin\Nova.exe`"" | Set-Content -Path "$root/Start Nova.bat" -Encoding ASCII
Add-Type -AssemblyName System.IO.Compression.FileSystem
$portableZip = Join-Path $OutputDir 'Nova-Portable-Full-win-x64.zip'
$otaZip = Join-Path $OutputDir 'nova-bin-win-x64.zip'
foreach ($archive in @($portableZip, $otaZip)) { if (Test-Path $archive) { Remove-Item $archive -Force } }
[IO.Compression.ZipFile]::CreateFromDirectory($root, $portableZip, [IO.Compression.CompressionLevel]::Optimal, $true)
# At archive root: legacy v2.0/v2.1 updaters recognize Nova.Core.dll and overlay
# these files into bin without deleting user data. Larger than previous OTA.
[IO.Compression.ZipFile]::CreateFromDirectory($bin, $otaZip, [IO.Compression.CompressionLevel]::Optimal, $false)
Write-Host "Portable bundle: $portableZip"
Write-Host "Self-contained OTA: $otaZip"
