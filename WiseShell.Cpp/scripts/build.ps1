param(
    [Parameter(Mandatory=$true)][string]$QtRoot,
    [Parameter(Mandatory=$true)][string]$OpenSslRoot,
    [Parameter(Mandatory=$true)][string]$ZlibRoot,
    [ValidateSet('debug','release')][string]$Configuration = 'release'
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
if (-not (Get-Command cl.exe -ErrorAction SilentlyContinue)) {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    $installation = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
    if (-not $installation) { throw 'Install Visual Studio C++ desktop tools first.' }
    $devCmd = Join-Path $installation 'Common7\Tools\VsDevCmd.bat'
    $vsEnvironment = & cmd /c "`"$devCmd`" -arch=x64 >nul && set"
    foreach ($entry in $vsEnvironment) {
        if ($entry -match '^([^=]+)=(.*)$') { [Environment]::SetEnvironmentVariable($matches[1], $matches[2], 'Process') }
    }
}
Push-Location $projectRoot
try {
    cmake --preset $Configuration -DCMAKE_C_COMPILER=cl -DCMAKE_CXX_COMPILER=cl "-DCMAKE_PREFIX_PATH=$QtRoot" "-DOPENSSL_ROOT_DIR=$OpenSslRoot" "-DZLIB_ROOT=$ZlibRoot"
    if ($LASTEXITCODE -ne 0) { throw 'CMake configure failed.' }
    cmake --build --preset $Configuration --parallel 4
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    $env:PATH = "$QtRoot\bin;$OpenSslRoot\bin;$ZlibRoot\bin;$env:PATH"
    ctest --preset $Configuration --output-on-failure
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
} finally { Pop-Location }
