# Compiles what Capture-Ui.ps1 compiles for a checkout, and nothing else: it launches and draws nothing.
# Usage: Build-Ui.ps1 <checkout>
# A design climb runs it before a capture waits for its turn, so a compile error shows in seconds.
# No build server or MSBuild node outlives it, and its output folder is deleted when it ends.
param(
    [Parameter(Mandatory)][string]$Checkout
)
$ErrorActionPreference = 'Stop'
$project = Join-Path $Checkout 'tests\PrivateType.App.LayoutProbe\PrivateType.App.LayoutProbe.csproj'
$build = Join-Path ([IO.Path]::GetTempPath()) ("privatetype-build-" + [Guid]::NewGuid().ToString('N'))
$env:PRIVATETYPE_ENGINE_ROOT = Join-Path $build 'no-engine'
try {
    dotnet build $project -c Debug --no-incremental -nologo -v quiet -clp:ErrorsOnly --artifacts-path $build -nodeReuse:false -p:UseSharedCompilation=false
    if ($LASTEXITCODE -ne 0) { throw "The build failed." }
}
finally {
    Remove-Item -Recurse -Force $build -ErrorAction SilentlyContinue
}
