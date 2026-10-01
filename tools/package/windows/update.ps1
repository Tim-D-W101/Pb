# Pb launcher (Play.bat runs this): brings the game up to date with the latest test build, then starts it.
# Only what changed is downloaded: the small update pack (the game itself), or the whole game when the
# engine changed too. Any problem (offline, GitHub down) just starts the version you have.
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'  # the progress bar makes downloads several times slower
# GitHub needs TLS 1.2, which Windows PowerShell 5.1 doesn't always offer by default.
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
$base = 'https://github.com/Tim-D-W101/Pb/releases/download/test-build'
$here = $PSScriptRoot

function Read-Manifest([string] $text) {
    $values = @{}
    foreach ($line in $text -split "`r?`n") {
        if ($line -match '^\s*([A-Za-z]+)\s*=\s*(.*?)\s*$') { $values[$Matches[1]] = $Matches[2] }
    }
    return $values
}

try {
    # Saved to a file and read back: GitHub serves it as binary, which older PowerShell won't hand back as text.
    $remoteFile = Join-Path $env:TEMP 'Pb-manifest.txt'
    Invoke-WebRequest "$base/manifest.txt" -OutFile $remoteFile -UseBasicParsing -TimeoutSec 15
    $remote = Read-Manifest (Get-Content $remoteFile -Raw)
    $localFile = Join-Path $here 'manifest.txt'
    $local = if (Test-Path $localFile) { Read-Manifest (Get-Content $localFile -Raw) } else { @{} }
    if ($remote.version -and $remote.version -ne $local.version) {
        $pack = if ($remote.engine -eq $local.engine) { 'Pb-update.zip' } else { 'Pb-windows.zip' }
        Write-Host "Updating Pb to $($remote.version) ($($remote.date)): downloading $pack..."
        $zip = Join-Path $env:TEMP "Pb-$($remote.version).zip"
        $unpacked = Join-Path $env:TEMP "Pb-$($remote.version)"
        Invoke-WebRequest "$base/$pack" -OutFile $zip -UseBasicParsing
        if (Test-Path $unpacked) { Remove-Item $unpacked -Recurse -Force }
        Expand-Archive $zip -DestinationPath $unpacked -Force
        # The whole game comes in a Pb folder; the update pack holds the files themselves.
        $from = if (Test-Path (Join-Path $unpacked 'Pb')) { Join-Path $unpacked 'Pb' } else { $unpacked }
        robocopy $from $here /E /R:2 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
        if ($LASTEXITCODE -ge 8) { throw "couldn't copy the new files in (is the game still running?)" }
        Remove-Item $zip, $unpacked -Recurse -Force -ErrorAction SilentlyContinue
        Write-Host "Updated."
    }
} catch {
    Write-Host "Couldn't update ($($_.Exception.Message)). Starting the version you have."
    Start-Sleep -Seconds 3
}

Start-Process (Join-Path $here 'Pb.exe') -WorkingDirectory $here
