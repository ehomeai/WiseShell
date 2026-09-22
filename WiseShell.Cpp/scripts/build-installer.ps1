param(
    [Parameter(Mandatory=$true)][string]$PayloadDirectory,
    [Parameter(Mandatory=$true)][string]$OutputDirectory,
    [string]$Version = '1.0.0',
    [string]$Compiler
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$payload = (Resolve-Path -LiteralPath $PayloadDirectory).Path
foreach ($required in @('WiseShellCpp.exe', 'Qt6Core.dll', 'Qt6Gui.dll', 'Qt6Widgets.dll',
                        'ssh.dll', 'wiseshell-keychain.dll', 'libcrypto-3-x64.dll',
                        'libssl-3-x64.dll', 'zlib1.dll', 'vcruntime140.dll', 'msvcp140.dll',
                        'platforms\qwindows.dll', 'licenses\WiseShell-MIT.txt')) {
    if (-not (Test-Path -LiteralPath (Join-Path $payload $required))) {
        throw "Missing packaged file: $required. Run package.py first."
    }
}
if (-not $Compiler) {
    $candidates = @(
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
    )
    $Compiler = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if (-not $Compiler) { throw 'Install Inno Setup 6 or specify -Compiler.' }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$output = (Resolve-Path -LiteralPath $OutputDirectory).Path
& $Compiler "/DPayloadDir=$payload" "/DOutputDir=$output" "/DAppVersion=$Version" "$projectRoot\installer\WiseShell.iss"
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
$installer = Join-Path $output "WiseShell-$Version-windows-x64-Setup.exe"
$hash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
"$hash  $(Split-Path $installer -Leaf)" | Set-Content -LiteralPath "$installer.sha256" -Encoding ascii
Write-Output $installer
