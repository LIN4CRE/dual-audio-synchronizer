<#
.SYNOPSIS
    Builds Dual Audio Synchronizer v2.0 (Brand Assets + Win32/WinForms WASAPI Executable)
.DESCRIPTION
    1. Generates multi-resolution app.ico, logo.png, and banner.png via tools\GenerateAssets.cs
    2. Compiles src\DualAudioStudio.cs into build\DualAudioStudio.exe using built-in .NET Framework csc.exe
#>
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

New-Item -ItemType Directory -Force -Path "$root\assets" | Out-Null
New-Item -ItemType Directory -Force -Path "$root\build"  | Out-Null

Write-Host "[1/2] Generating Studio Brand Assets (app.ico, logo.png, banner.png)..." -ForegroundColor Cyan
if (-not ("AssetGen" -as [type])) {
    Add-Type -Path "$root\tools\GenerateAssets.cs" -ReferencedAssemblies System.Drawing
}
[AssetGen]::GenerateAll("$root\assets", "$root\build")
Copy-Item "$root\assets\app.ico" "$root\src\app.ico" -Force

Write-Host "[2/2] Compiling DualAudioStudio.exe..." -ForegroundColor Cyan
$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) {
    throw ".NET Framework 4.x C# compiler not found at $csc"
}

& $csc /nologo /optimize+ /unsafe+ /target:winexe `
    /win32icon:assets\app.ico `
    /resource:assets\app.ico,DualAudioStudio.app.ico `
    /r:System.Windows.Forms.dll,System.Drawing.dll,System.dll,System.Core.dll `
    /out:build\DualAudioStudio.exe `
    src\DualAudioStudio.cs

if ($LASTEXITCODE -ne 0) {
    throw "Compilation failed with exit code $LASTEXITCODE"
}

Write-Host "Build completed successfully -> $root\build\DualAudioStudio.exe" -ForegroundColor Green
