[CmdletBinding()]
param()

# Downloads NVIDIA's prebuilt NeMo-Speech.cpp Windows release (CPU and Vulkan
# backends in one build), verifies its pinned SHA-256, and unpacks it into
# .engine\runtime. PrivateType ships these binaries unchanged.

$ErrorActionPreference = 'Stop'

$version = '0.2.0'
$archiveName = "nemo-speech-$version-windows-x86_64-vulkan.zip"
$archiveUrl = "https://github.com/NVIDIA/NeMo-Speech.cpp/releases/download/v$version/$archiveName"
$archiveSha256 = 'edf15a04ba98740aa0ab75ae4681ac7ad0a488b22b35ce2cbebd10590bb1c1e1'

$engineRoot = Join-Path $PSScriptRoot '.engine'
$runtimeDirectory = Join-Path $engineRoot 'runtime'
$marker = Join-Path $runtimeDirectory 'SOURCE-SHA256.txt'

if ((Test-Path -LiteralPath $marker) -and (Get-Content -LiteralPath $marker -Raw).Trim() -eq $archiveSha256) {
    Write-Host "NeMo-Speech.cpp $version is already in $runtimeDirectory"
    return
}

New-Item -ItemType Directory -Force -Path $engineRoot | Out-Null
$archivePath = Join-Path $engineRoot $archiveName
$extractDirectory = Join-Path $engineRoot 'runtime-extract'

try {
    Write-Host "==> Downloading $archiveUrl" -ForegroundColor Cyan
    Invoke-WebRequest -Uri $archiveUrl -OutFile $archivePath -UseBasicParsing

    $actual = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $archiveSha256) {
        throw "Downloaded runtime does not match its pinned SHA-256 (expected $archiveSha256, got $actual)."
    }

    if (Test-Path -LiteralPath $extractDirectory) {
        Remove-Item -LiteralPath $extractDirectory -Recurse -Force
    }
    Expand-Archive -LiteralPath $archivePath -DestinationPath $extractDirectory
    $unpacked = Join-Path $extractDirectory "nemo-speech-$version-windows-x86_64-vulkan"
    if (!(Test-Path -LiteralPath (Join-Path $unpacked 'bin\nemo-speech.exe'))) {
        throw "The runtime archive does not contain bin\nemo-speech.exe."
    }

    if (Test-Path -LiteralPath $runtimeDirectory) {
        Remove-Item -LiteralPath $runtimeDirectory -Recurse -Force
    }
    Move-Item -LiteralPath $unpacked -Destination $runtimeDirectory
    Set-Content -LiteralPath $marker -Value $archiveSha256 -NoNewline

    & (Join-Path $runtimeDirectory 'bin\nemo-speech.exe') --version
    if ($LASTEXITCODE -ne 0) {
        throw "The downloaded runtime did not start (exit code $LASTEXITCODE)."
    }
    Write-Host "SUCCESS: $runtimeDirectory" -ForegroundColor Green
}
finally {
    if (Test-Path -LiteralPath $archivePath) {
        Remove-Item -LiteralPath $archivePath -Force
    }
    if (Test-Path -LiteralPath $extractDirectory) {
        Remove-Item -LiteralPath $extractDirectory -Recurse -Force
    }
}
