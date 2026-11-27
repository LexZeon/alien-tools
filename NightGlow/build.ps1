# Builds nightglow_core.dll (if MSBuild with the C++ tools is installed) and publishes the NightGlow UI.
# Usage (from the repo root or anywhere):  powershell -ExecutionPolicy Bypass -File NightGlow\build.ps1 [-Configuration Release]
param([ValidateSet('Debug','Release')][string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

# 1. native core
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$msbuild = $null
if (Test-Path $vswhere) {
    $msbuild = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
}
if ($msbuild) {
    Write-Host "Building nightglow_core.dll with $msbuild"
    & $msbuild (Join-Path $root 'native\NightGlowCore\NightGlowCore.vcxproj') /p:Configuration=$Configuration /p:Platform=x64 /m /nologo /v:minimal
    if ($LASTEXITCODE -ne 0) { throw "native build failed ($LASTEXITCODE)" }
} else {
    Write-Warning 'MSBuild with C++ tools not found: skipping nightglow_core.dll. The app will run in demo mode.'
}

# 2. UI
$out = Join-Path $root 'publish'
dotnet publish (Join-Path $root 'src\NightGlow\NightGlow.csproj') -c $Configuration -p:Platform=x64 -o $out --nologo
if ($LASTEXITCODE -ne 0) { throw "UI publish failed ($LASTEXITCODE)" }
Copy-Item (Join-Path $root 'LICENSE'), (Join-Path $root 'THIRD_PARTY_NOTICES.md') $out -Force
Write-Host "Done: $out\NightGlow.exe"
