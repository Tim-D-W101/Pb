# Pb launcher (Play.bat runs this): brings the game up to date with the latest test build, then starts it.
# Only what changed is downloaded: each of the game's own files whose fingerprint (SHA-256) differs from
# the newest build's (the art comes in packs of its own, one per asset, so a pack only comes down when its
# asset changed), or the whole game when the engine changed. Any problem (offline, GitHub down) just
# starts the version you have.
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'  # the progress bar makes downloads several times slower
# GitHub needs TLS 1.2, which Windows PowerShell 5.1 doesn't always offer by default.
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
$base = 'https://github.com/Tim-D-W101/Pb/releases/download/test-build'
$here = $PSScriptRoot

# key=value lines, and "file=<sha256> <bytes> <path>" lines listing the game's own files.
function Read-Manifest([string] $path) {
    $values = @{ files = @() }
    if (-not (Test-Path $path)) { return $values }
    foreach ($line in (Get-Content $path)) {
        if ($line -match '^\s*file\s*=\s*([0-9a-fA-F]{64})\s+(\d+)\s+(.+?)\s*$') {
            $values.files += [pscustomobject]@{ Hash = $Matches[1].ToLower(); Size = [long]$Matches[2]; Path = $Matches[3] }
        } elseif ($line -match '^\s*([A-Za-z]+)\s*=\s*(.*?)\s*$') {
            $values[$Matches[1]] = $Matches[2]
        }
    }
    return $values
}

function Get-Sha256([string] $path) {
    if (-not (Test-Path $path)) { return '' }
    return (Get-FileHash $path -Algorithm SHA256).Hash.ToLower()
}

# Removes art packs the newest build no longer lists (an asset that's gone), and the single art pack of
# builds before there was one per asset.
function Remove-OldArt($files) {
    if ($files.Count -eq 0) { return }
    $listed = @{}
    foreach ($f in $files) { $listed[(Split-Path $f.Path -Leaf)] = $true }
    $old = @(Get-ChildItem (Join-Path $here 'art') -Filter '*.pck' -File -ErrorAction SilentlyContinue)
    $old += @(Get-Item (Join-Path $here 'Pb-art.pck') -ErrorAction SilentlyContinue)
    foreach ($item in $old) {
        if (-not $listed.ContainsKey($item.Name)) { Remove-Item $item.FullName -Force -ErrorAction SilentlyContinue }
    }
}

try {
    # Saved to a file and read back: GitHub serves it as binary, which older PowerShell won't hand back as text.
    $remoteFile = Join-Path $env:TEMP 'Pb-manifest.txt'
    Invoke-WebRequest "$base/manifest.txt" -OutFile $remoteFile -UseBasicParsing -TimeoutSec 15
    $remote = Read-Manifest $remoteFile
    $localFile = Join-Path $here 'manifest.txt'
    $local = Read-Manifest $localFile
    if ($remote.version -and $remote.version -ne $local.version) {
        if ($remote.engine -ne $local.engine -or $remote.files.Count -eq 0) {
            # The engine changed: the whole game.
            Write-Host "Updating Pb to $($remote.version) ($($remote.date)): downloading the whole game..."
            $zip = Join-Path $env:TEMP "Pb-$($remote.version).zip"
            $unpacked = Join-Path $env:TEMP "Pb-$($remote.version)"
            Invoke-WebRequest "$base/Pb-windows.zip" -OutFile $zip -UseBasicParsing
            if (Test-Path $unpacked) { Remove-Item $unpacked -Recurse -Force }
            Expand-Archive $zip -DestinationPath $unpacked -Force
            robocopy (Join-Path $unpacked 'Pb') $here /E /R:2 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
            if ($LASTEXITCODE -ge 8) { throw "couldn't copy the new files in (is the game still running?)" }
            Remove-Item $zip, $unpacked -Recurse -Force -ErrorAction SilentlyContinue
            Remove-OldArt $remote.files
            Write-Host "Updated."
        } else {
            # Just the files that differ, each checked against its fingerprint before it replaces yours.
            $changed = @($remote.files | Where-Object { (Get-Sha256 (Join-Path $here $_.Path)) -ne $_.Hash })
            if ($changed.Count -gt 0) {
                $bytes = ($changed | Measure-Object -Property Size -Sum).Sum
                $amount = if ($bytes -ge 1MB) { '{0:N1} MB' -f ($bytes / 1MB) } else { '{0:N0} KB' -f [math]::Max(1, $bytes / 1KB) }
                Write-Host ("Updating Pb to {0} ({1}): {2} file(s), {3}..." -f $remote.version, $remote.date, $changed.Count, $amount)
                $staging = Join-Path $env:TEMP "Pb-$($remote.version)"
                if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
                New-Item -ItemType Directory -Path $staging | Out-Null
                foreach ($f in $changed) {
                    $name = Split-Path $f.Path -Leaf
                    $download = Join-Path $staging $name
                    Invoke-WebRequest "$base/$name" -OutFile $download -UseBasicParsing
                    if ((Get-Sha256 $download) -ne $f.Hash) { throw "$name didn't download properly" }
                }

                foreach ($f in $changed) {
                    $target = Join-Path $here $f.Path
                    New-Item -ItemType Directory -Path (Split-Path $target -Parent) -Force | Out-Null
                    Move-Item (Join-Path $staging (Split-Path $f.Path -Leaf)) $target -Force
                }

                Remove-Item $staging -Recurse -Force -ErrorAction SilentlyContinue
                Write-Host "Updated."
            }

            Remove-OldArt $remote.files
            Copy-Item $remoteFile $localFile -Force
        }
    }
} catch {
    Write-Host "Couldn't update ($($_.Exception.Message)). Starting the version you have."
    Start-Sleep -Seconds 3
}

Start-Process (Join-Path $here 'Pb.exe') -WorkingDirectory $here
