[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

function Invoke-Checked {
    param(
        [Parameter(Mandatory)] [scriptblock] $Command,
        [Parameter(Mandatory)] [string] $FailureMessage
    )

    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw "$FailureMessage (exit code $LASTEXITCODE)."
    }
}

function Import-MsvcEnvironment {
    param([Parameter(Mandatory)] [string] $VcVarsPath)

    cmd /c "`"$VcVarsPath`" >NUL && set" |
        ForEach-Object {
            if ($_ -match '^([^=]+)=(.*)$') {
                Set-Item -Path "env:$($matches[1])" -Value $matches[2]
            }
        }
}

function Find-MsvcVcVarsPath {
    $vswhere = Join-Path ([Environment]::GetFolderPath('ProgramFilesX86')) 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path -LiteralPath $vswhere) {
        $installationPath = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath | Select-Object -First 1
        if ($LASTEXITCODE -eq 0 -and $installationPath) {
            $candidate = Join-Path $installationPath.Trim() 'VC\Auxiliary\Build\vcvars64.bat'
            if (Test-Path -LiteralPath $candidate) {
                return $candidate
            }
        }
    }

    $candidates = @(
        'C:\Program Files\Microsoft Visual Studio\2022\Enterprise\VC\Auxiliary\Build\vcvars64.bat',
        'C:\Program Files\Microsoft Visual Studio\2022\Professional\VC\Auxiliary\Build\vcvars64.bat',
        'C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvars64.bat',
        'C:\Program Files\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvars64.bat',
        'C:\Program Files\Microsoft Visual Studio\18\Community\VC\Auxiliary\Build\vcvars64.bat'
    )

    $vcVarsPath = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if ($vcVarsPath) {
        return $vcVarsPath
    }

    throw 'Visual Studio C++ Build Tools with the x64 toolchain are required but were not found.'
}
function Apply-WindowsSentencePiecePatch {
    param(
        [Parameter(Mandatory)] [string] $Repository,
        [Parameter(Mandatory)] [string] $PatchPath
    )

    $asrCmakePath = Join-Path $Repository 'src\asr\CMakeLists.txt'
    $asrCmake = Get-Content -LiteralPath $asrCmakePath -Raw
    if ($asrCmake -match 'absl::flags') {
        return
    }

    if (!(Test-Path -LiteralPath $PatchPath)) {
        throw "Required Windows SentencePiece linker patch is missing: $PatchPath"
    }

    Write-Host '==> Applying Windows SentencePiece linker patch' -ForegroundColor Cyan
    Invoke-Checked { git -C $Repository apply --check $PatchPath } `
        'The Windows SentencePiece linker patch does not match this NeMo-Speech.cpp source revision'
    Invoke-Checked { git -C $Repository apply $PatchPath } `
        'Could not apply the Windows SentencePiece linker patch'
}

try {
    $engineRoot = Join-Path $PSScriptRoot '.engine'
    $repository = Join-Path $engineRoot 'NeMo-Speech.cpp'
    $vcpkgRoot = Join-Path $engineRoot 'vcpkg'
    $buildDirectory = Join-Path $engineRoot 'build-cpu-realtime-manual'
    $windowsSentencePiecePatch = Join-Path $PSScriptRoot 'patches\nemo-speech-windows-sentencepiece-absl.patch'
    $vcVarsPath = Find-MsvcVcVarsPath

    if (!(Test-Path $repository)) {
        throw "NeMo-Speech.cpp sources are missing: $repository"
    }


    Write-Host '==> Initializing required NeMo-Speech.cpp submodules' -ForegroundColor Cyan
    Invoke-Checked { git -C $repository submodule update --init ggml third_party/cpp-httplib } `
        'Could not initialize NeMo-Speech.cpp submodules'

    Apply-WindowsSentencePiecePatch -Repository $repository -PatchPath $windowsSentencePiecePatch

    if (!(Test-Path "$vcpkgRoot\vcpkg.exe")) {
        Write-Host '==> Downloading vcpkg into the local engine directory' -ForegroundColor Cyan
        Invoke-Checked { git clone --depth 1 https://github.com/microsoft/vcpkg.git $vcpkgRoot } `
            'Could not clone vcpkg'
        Invoke-Checked { cmd /c "$vcpkgRoot\bootstrap-vcpkg.bat -disableMetrics" } `
            'Could not bootstrap vcpkg'
    }

    Write-Host '==> Preparing local SentencePiece dependency' -ForegroundColor Cyan
    Invoke-Checked { & "$vcpkgRoot\vcpkg.exe" install sentencepiece:x64-windows --disable-metrics } `
        'Could not install SentencePiece'

    $cmake = Get-ChildItem "$vcpkgRoot\downloads\tools\cmake-*-windows\*\bin\cmake.exe" |
        Sort-Object FullName -Descending |
        Select-Object -First 1 -ExpandProperty FullName

    if (!$cmake) {
        throw 'vcpkg did not provide the required CMake executable.'
    }

    Write-Host '==> Importing the MSVC x64 build environment' -ForegroundColor Cyan
    Import-MsvcEnvironment -VcVarsPath $vcVarsPath

    Write-Host '==> Configuring the CPU realtime-ASR runtime' -ForegroundColor Cyan
    Invoke-Checked {
        & $cmake -S $repository -B $buildDirectory -G Ninja `
            -DCMAKE_BUILD_TYPE=Release `
            -DCMAKE_TOOLCHAIN_FILE="$vcpkgRoot\scripts\buildsystems\vcpkg.cmake" `
            -DVCPKG_TARGET_TRIPLET=x64-windows `
            -DNEMO_SPEECH_GGML_PATCHED=OFF `
            -DNEMO_SPEECH_BUILD_ASR=ON `
            -DNEMO_SPEECH_BUILD_DIAR=OFF `
            -DNEMO_SPEECH_BUILD_TTS=OFF `
            -DNEMO_SPEECH_BUILD_NMT=OFF `
            -DNEMO_SPEECH_WITH_NMT=OFF `
            -DNEMO_SPEECH_BUILD_HTTP=ON `
            -DNEMO_SPEECH_BUILD_GRPC=OFF `
            -DNEMO_SPEECH_WITH_GRPC=OFF
    } 'CMake configuration failed'

    Write-Host '==> Building the CPU realtime-ASR runtime' -ForegroundColor Cyan
    Invoke-Checked { & $cmake --build $buildDirectory --parallel 8 } 'Runtime build failed'

    $runtimeDirectory = Join-Path $buildDirectory 'bin'
    $runtimeExecutable = Join-Path $runtimeDirectory 'nemo-speech.exe'
    $asrLibrary = Join-Path $runtimeDirectory 'nemo_speech_asr.dll'
    if (!(Test-Path $runtimeExecutable)) {
        throw "The expected runtime executable was not produced: $runtimeExecutable"
    }
    if (!(Test-Path $asrLibrary)) {
        throw "The expected ASR library was not produced: $asrLibrary"
    }

    $env:PATH = "$runtimeDirectory;$vcpkgRoot\installed\x64-windows\bin;$env:PATH"

    Write-Host '==> Verifying the built runtime' -ForegroundColor Cyan
    Invoke-Checked { & $runtimeExecutable --version } 'Runtime verification failed'

    Write-Host ''
    Write-Host 'SUCCESS' -ForegroundColor Green
    Write-Host "Built runtime: $runtimeExecutable"
}
catch {
    Write-Host ''
    Write-Host 'BUILD FAILED' -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 1
}
