[CmdletBinding()]
param()

# Downloads NVIDIA's prebuilt NeMo-Speech.cpp Windows release (CPU and Vulkan
# backends in one build) and the Khronos Vulkan loader it links, verifies their
# pinned SHA-256, and unpacks them into .engine\runtime. PrivateType ships these
# binaries unchanged. NVIDIA's release leaves the loader out because graphics
# drivers normally install it, but the engine cannot start without it, even on
# the processor.

$ErrorActionPreference = 'Stop'
# Windows PowerShell downloads many times slower while drawing a progress bar.
$ProgressPreference = 'SilentlyContinue'

$version = '0.2.0'
$archiveName = "nemo-speech-$version-windows-x86_64-vulkan.zip"
$archiveUrl = "https://github.com/NVIDIA/NeMo-Speech.cpp/releases/download/v$version/$archiveName"
$archiveSha256 = 'edf15a04ba98740aa0ab75ae4681ac7ad0a488b22b35ce2cbebd10590bb1c1e1'

$loaderVersion = '1.4.328.1'
$loaderName = "VulkanRT-X64-$loaderVersion-Components"
$loaderUrl = "https://sdk.lunarg.com/sdk/download/$loaderVersion/windows/$loaderName.zip"
$loaderSha256 = '293efbb2c11e60429df7770f40e3c5c811a7fa42af81de0cc41e8718ddb2b373'

$engineRoot = Join-Path $PSScriptRoot '.engine'
$runtimeDirectory = Join-Path $engineRoot 'runtime'
$marker = Join-Path $runtimeDirectory 'SOURCE-SHA256.txt'
$sourceHashes = "$archiveSha256 $loaderSha256"
$executable = Join-Path $runtimeDirectory 'bin\nemo-speech.exe'
$loader = Join-Path $runtimeDirectory 'bin\vulkan-1.dll'
$loaderLicense = Join-Path $runtimeDirectory 'share\licenses\vulkan-runtime\VulkanRT-License.txt'

if ((Test-Path -LiteralPath $executable) -and (Test-Path -LiteralPath $loader) -and (Test-Path -LiteralPath $loaderLicense) -and (Test-Path -LiteralPath $marker) -and (Get-Content -LiteralPath $marker -Raw).Trim() -eq $sourceHashes) {
    Write-Host "NeMo-Speech.cpp $version is already in $runtimeDirectory"
    return
}

function Save-PinnedArchive {
    param(
        [Parameter(Mandatory)] [string] $Url,
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [string] $Sha256
    )

    Write-Host "==> Downloading $Url" -ForegroundColor Cyan
    Invoke-WebRequest -Uri $Url -OutFile $Path -UseBasicParsing

    $actual = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $Sha256) {
        throw "Download of $Url does not match its pinned SHA-256 (expected $Sha256, got $actual)."
    }
}

New-Item -ItemType Directory -Force -Path $engineRoot | Out-Null
$archivePath = Join-Path $engineRoot $archiveName
$loaderPath = Join-Path $engineRoot "$loaderName.zip"
$extractDirectory = Join-Path $engineRoot 'runtime-extract'

try {
    Save-PinnedArchive -Url $archiveUrl -Path $archivePath -Sha256 $archiveSha256
    Save-PinnedArchive -Url $loaderUrl -Path $loaderPath -Sha256 $loaderSha256

    if (Test-Path -LiteralPath $extractDirectory) {
        Remove-Item -LiteralPath $extractDirectory -Recurse -Force
    }
    Expand-Archive -LiteralPath $archivePath -DestinationPath $extractDirectory
    Expand-Archive -LiteralPath $loaderPath -DestinationPath $extractDirectory
    $unpacked = Join-Path $extractDirectory "nemo-speech-$version-windows-x86_64-vulkan"
    if (!(Test-Path -LiteralPath (Join-Path $unpacked 'bin\nemo-speech.exe'))) {
        throw "The runtime archive does not contain bin\nemo-speech.exe."
    }

    $unpackedLoader = Join-Path $extractDirectory $loaderName
    Copy-Item -LiteralPath (Join-Path $unpackedLoader 'x64\vulkan-1.dll') -Destination (Join-Path $unpacked 'bin')
    $loaderLicenses = Join-Path $unpacked 'share\licenses\vulkan-runtime'
    New-Item -ItemType Directory -Force -Path $loaderLicenses | Out-Null
    Copy-Item -LiteralPath (Join-Path $unpackedLoader 'VulkanRT-License.txt') -Destination $loaderLicenses

    if (Test-Path -LiteralPath $runtimeDirectory) {
        Remove-Item -LiteralPath $runtimeDirectory -Recurse -Force
    }
    Move-Item -LiteralPath $unpacked -Destination $runtimeDirectory

    & $executable --version
    if ($LASTEXITCODE -ne 0) {
        throw "The downloaded runtime did not start (exit code $LASTEXITCODE)."
    }
    Set-Content -LiteralPath $marker -Value $sourceHashes -NoNewline
    Write-Host "SUCCESS: $runtimeDirectory" -ForegroundColor Green
}
finally {
    foreach ($download in @($archivePath, $loaderPath)) {
        if (Test-Path -LiteralPath $download) {
            Remove-Item -LiteralPath $download -Force
        }
    }
    if (Test-Path -LiteralPath $extractDirectory) {
        Remove-Item -LiteralPath $extractDirectory -Recurse -Force
    }
}
