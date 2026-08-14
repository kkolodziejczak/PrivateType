[CmdletBinding()]
param(
    [string] $OutputDirectory
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $PSScriptRoot 'artifacts\PrivateType-win-x64'
}

$appProject = Join-Path $PSScriptRoot 'src\PrivateType.App\PrivateType.App.csproj'
$runtimeSource = Join-Path $PSScriptRoot '.engine\build-cpu-realtime-manual\bin'
$requiredEngineFiles = @(
    'nemo-speech.exe',
    'nemo_speech_asr.dll',
    'nemo_speech_asr_c.dll',
    'ggml.dll',
    'ggml-base.dll',
    'ggml-cpu.dll',
    'abseil_dll.dll',
    'libprotobuf.dll'
)

if (Test-Path -LiteralPath $OutputDirectory) {
    throw "Release output already exists and will not be overwritten: $OutputDirectory"
}

foreach ($file in $requiredEngineFiles) {
    if (!(Test-Path -LiteralPath (Join-Path $runtimeSource $file))) {
        throw "The verified local engine runtime is incomplete; missing: $file"
    }
}

$publishDirectory = Join-Path $OutputDirectory 'PrivateType'
$engineDestination = Join-Path $publishDirectory 'engine\bin'
$archivePath = "$OutputDirectory.zip"
$outputCreated = $false

try {
    New-Item -ItemType Directory -Force -Path $publishDirectory | Out-Null
    $outputCreated = $true
    & dotnet publish $appProject --configuration Release --runtime win-x64 --self-contained true --output $publishDirectory -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed (exit code $LASTEXITCODE)."
    }

    New-Item -ItemType Directory -Force -Path $engineDestination | Out-Null
    foreach ($file in $requiredEngineFiles) {
        Copy-Item -LiteralPath (Join-Path $runtimeSource $file) -Destination $engineDestination
    }

    if (!(Test-Path -LiteralPath (Join-Path $publishDirectory 'PrivateType.exe'))) {
        throw 'Portable app executable was not published.'
    }
    if (!(Test-Path -LiteralPath (Join-Path $engineDestination 'nemo-speech.exe'))) {
        throw 'Portable engine executable was not copied.'
    }
    if (Test-Path -LiteralPath (Join-Path $publishDirectory 'models')) {
        throw 'Portable release must not include a downloaded model.'
    }
    if (Test-Path -LiteralPath $archivePath) {
        throw "Archive already exists and will not be overwritten: $archivePath"
    }

    Compress-Archive -LiteralPath $publishDirectory -DestinationPath $archivePath -CompressionLevel Optimal
    $folderBytes = (Get-ChildItem -LiteralPath $publishDirectory -Recurse -File | Measure-Object -Property Length -Sum).Sum
    $archiveBytes = (Get-Item -LiteralPath $archivePath).Length
    Write-Host "SUCCESS: $publishDirectory"
    Write-Host "Folder bytes: $folderBytes"
    Write-Host "ZIP bytes: $archiveBytes"
}
catch {
    if (Test-Path -LiteralPath $archivePath) {
        Remove-Item -LiteralPath $archivePath -Force
    }
    if ($outputCreated -and (Test-Path -LiteralPath $OutputDirectory)) {
        Remove-Item -LiteralPath $OutputDirectory -Recurse -Force
    }
    throw
}
