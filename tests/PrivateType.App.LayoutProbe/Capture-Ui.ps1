# Builds a checkout's layout probe and draws every window as <screen>/<state>-dark.png into the output folder, for design review.
# Usage: Capture-Ui.ps1 <checkout> <output folder>
# It launches nothing but the probe, which draws offscreen and exits. The engine fetch is skipped: drawing needs no engine.
param(
    [Parameter(Mandatory)][string]$Checkout,
    [Parameter(Mandatory)][string]$Output
)
$ErrorActionPreference = 'Stop'
$project = Join-Path $Checkout 'tests\PrivateType.App.LayoutProbe\PrivateType.App.LayoutProbe.csproj'
$build = Join-Path ([IO.Path]::GetTempPath()) ("privatetype-capture-" + [Guid]::NewGuid().ToString('N'))
$env:PRIVATETYPE_ENGINE_ROOT = Join-Path $build 'no-engine'
try {
    # A full build into a private folder: no stale XAML, and overlapping captures never share an output.
    dotnet build $project -c Debug --no-incremental -nologo -v quiet -clp:ErrorsOnly --artifacts-path $build
    if ($LASTEXITCODE -ne 0) { throw "The build failed." }
    $probe = Get-ChildItem -Path "$build\bin" -Recurse -Filter 'PrivateType.App.LayoutProbe.exe' | Select-Object -First 1
    if (-not $probe) { throw "The build wrote no probe." }
    New-Item -ItemType Directory -Force $Output | Out-Null
    $process = Start-Process -FilePath $probe.FullName -ArgumentList '--capture', "`"$Output`"" -NoNewWindow -PassThru
    $null = $process.Handle  # keeps the exit code readable after the process ends
    if (-not $process.WaitForExit(180000)) {
        taskkill /PID $process.Id /T /F | Out-Null
        throw "The probe took longer than three minutes."
    }
    if ($process.ExitCode -ne 0) { throw "The probe failed with exit code $($process.ExitCode)." }
}
finally {
    Remove-Item -Recurse -Force $build -ErrorAction SilentlyContinue
}
