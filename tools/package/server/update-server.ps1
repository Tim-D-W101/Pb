# Brings this Pb dedicated server up to date with the latest test build (Server.bat runs it before each start). When
# the release's version differs from this one's (version.txt), it downloads Pb-server-windows.zip and puts its files
# in place of these, keeping your server.jsonc. Any problem (offline, GitHub down) just starts the version you have.
# Players' copies update with Play.bat, and a server only lets in copies of its own version, so start the server
# again after a new build (docs/hosting.md).
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'  # the progress bar makes downloads several times slower
# GitHub needs TLS 1.2, which Windows PowerShell 5.1 doesn't always offer by default.
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
$base = 'https://github.com/Tim-D-W101/Pb/releases/download/test-build'
$here = $PSScriptRoot

try {
    # Saved to a file and read back: GitHub serves it as binary, which older PowerShell won't hand back as text.
    $manifest = Join-Path $env:TEMP 'Pb-server-manifest.txt'
    Invoke-WebRequest "$base/manifest.txt" -OutFile $manifest -UseBasicParsing -TimeoutSec 15
    $newest = ''
    foreach ($line in (Get-Content $manifest)) {
        if ($line -match '^\s*version\s*=\s*(.+?)\s*$') { $newest = $Matches[1]; break }
    }
    if (-not $newest) { throw 'the release names no version' }
    $versionFile = Join-Path $here 'version.txt'
    $have = if (Test-Path $versionFile) { (Get-Content $versionFile -TotalCount 1).Trim() } else { 'none' }
    if ($newest -eq $have) {
        Write-Host "This Pb server ($have) is the newest."
    } else {
        Write-Host "Updating the Pb server from $have to $newest..."
        $zip = Join-Path $env:TEMP "Pb-server-$newest.zip"
        $unpacked = Join-Path $env:TEMP "Pb-server-$newest"
        Invoke-WebRequest "$base/Pb-server-windows.zip" -OutFile $zip -UseBasicParsing
        if (Test-Path $unpacked) { Remove-Item $unpacked -Recurse -Force }
        Expand-Archive $zip -DestinationPath $unpacked -Force
        # Everything but your settings.
        robocopy (Join-Path $unpacked 'Pb-server') $here /E /XF server.jsonc /R:2 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
        if ($LASTEXITCODE -ge 8) { throw "couldn't copy the new files in (is the server still running?)" }
        Remove-Item $zip, $unpacked -Recurse -Force -ErrorAction SilentlyContinue
        Write-Host "Updated to $((Get-Content $versionFile -TotalCount 1).Trim())."
    }
} catch {
    Write-Host "Couldn't update ($($_.Exception.Message)). Starting the version you have."
    Start-Sleep -Seconds 3
}
