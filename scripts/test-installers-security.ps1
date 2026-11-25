param([string]$FullInstaller = '')
$ErrorActionPreference = 'Stop'
$root = Join-Path ([IO.Path]::GetTempPath()) ('nova-setup-test-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $root -Force | Out-Null
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
try {
    $csc = "$env:windir\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
    $offlineSource = (Resolve-Path 'installer\NovaOfflineSetup.cs').Path
    $helperSource = (Resolve-Path 'installer\SafeInstallerZip.cs').Path
    foreach ($kind in @('good','bad')) {
        $zipPath = Join-Path $root "$kind.zip"
        $zip = [IO.Compression.ZipFile]::Open($zipPath, [IO.Compression.ZipArchiveMode]::Create)
        try {
            $entries = @('Nova/bin/Nova.exe', 'Nova/bin/Nova.Core.dll', 'Nova/bin/THIRD-PARTY-NOTICES.md', 'Nova/THIRD-PARTY-NOTICES.md')
            if ($kind -eq 'bad') { $entries += 'Nova/../../outside.txt' }
            foreach ($name in $entries) {
                $entry = $zip.CreateEntry($name)
                $writer = New-Object IO.StreamWriter($entry.Open())
                try { $writer.Write('synthetic installer test') } finally { $writer.Dispose() }
            }
        } finally { $zip.Dispose() }
        $exe = Join-Path $root "$kind.exe"
        & $csc /nologo /target:winexe "/out:$exe" "/resource:$zipPath,NovaFullPayload.zip" `
            /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll `
            $offlineSource $helperSource
        if ($LASTEXITCODE -ne 0) { throw 'Synthetic installer compilation failed' }
        $dest = Join-Path $root "install-$kind"
        New-Item -ItemType Directory -Path "$dest/instances/demo/saves" -Force | Out-Null
        Set-Content -Path "$dest/instances/demo/saves/sentinel.txt" -Value 'preserve'
        $proc = Start-Process $exe -ArgumentList @('--silent','--dir',"`"$dest`"") -Wait -PassThru
        if ($kind -eq 'good' -and ($proc.ExitCode -ne 0 -or !(Test-Path "$dest/bin/Nova.exe") -or !(Test-Path "$dest/THIRD-PARTY-NOTICES.md"))) { throw 'Normal installer test failed' }
        if ($kind -eq 'bad' -and ($proc.ExitCode -eq 0 -or (Test-Path "$dest/bin/Nova.exe") -or (Test-Path "$root/outside.txt"))) { throw 'Installer accepted path traversal' }
        if ((Get-Content "$dest/instances/demo/saves/sentinel.txt" -Raw).Trim() -ne 'preserve') { throw 'Installer changed world data' }
    }
    if ($FullInstaller) {
        $exe = (Resolve-Path $FullInstaller).Path
        $dest = Join-Path $root 'install-real'
        New-Item -ItemType Directory -Path "$dest/instances/demo/saves" -Force | Out-Null
        Set-Content -Path "$dest/instances/demo/saves/sentinel.txt" -Value 'preserve'
        $proc = Start-Process $exe -ArgumentList @('--silent','--dir',"`"$dest`"") -Wait -PassThru
        if ($proc.ExitCode -ne 0 -or !(Test-Path "$dest/bin/Nova.exe")) { throw "Full installer failed with $($proc.ExitCode)" }
        & "$dest/bin/nova-cli.exe" help | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Installed CLI could not start' }
        if ((Get-Content "$dest/instances/demo/saves/sentinel.txt" -Raw).Trim() -ne 'preserve') { throw 'Full installer changed world data' }
        $version = ([xml](Get-Content Directory.Build.props -Raw)).Project.PropertyGroup.Version
        if (-not ([Diagnostics.FileVersionInfo]::GetVersionInfo($exe).FileVersion.StartsWith($version))) { throw 'Full installer version metadata mismatch' }
        Write-Host 'PASS: actual full installer ran in an isolated directory; CLI and sentinel verified.'
    }
    Write-Host 'PASS: synthetic full installer accepts valid ZIP, rejects traversal and preserves sentinel.'
} finally { if (Test-Path $root) { Remove-Item $root -Recurse -Force } }
