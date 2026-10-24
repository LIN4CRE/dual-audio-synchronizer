<#
.SYNOPSIS
    One-line web installer for Dual Audio Synchronizer v2.0
    Usage: irm https://lin4cre.github.io/dual-audio-synchronizer/install.ps1 | iex
#>
$ErrorActionPreference = "Stop"
Write-Host ""
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "  Installing Dual Audio Synchronizer v2.0 (by LIN4CRE)" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan

$appDir = Join-Path $env:LOCALAPPDATA "DualAudio"
New-Item -ItemType Directory -Force -Path $appDir | Out-Null

# Stop any running instance before updating
Get-Process -Name "DualAudioStudio" -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 300

$exeUrl = "https://github.com/LIN4CRE/dual-audio-synchronizer/releases/latest/download/DualAudioStudio.exe"
$icoUrl = "https://github.com/LIN4CRE/dual-audio-synchronizer/releases/latest/download/app.ico"

$exePath = Join-Path $appDir "DualAudioStudio.exe"
$icoPath = Join-Path $appDir "app.ico"

Write-Host "[1/3] Downloading DualAudioStudio.exe & Studio Icon..." -ForegroundColor Yellow
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
Invoke-WebRequest -Uri $exeUrl -OutFile $exePath -UseBasicParsing
Invoke-WebRequest -Uri $icoUrl -OutFile $icoPath -UseBasicParsing

Write-Host "[2/3] Creating Desktop & Startup Shortcuts..." -ForegroundColor Yellow
$wsh = New-Object -ComObject WScript.Shell

$desktopPath = Join-Path ([Environment]::GetFolderPath("Desktop")) "Dual Audio Synchronizer.lnk"
$desktopLnk = $wsh.CreateShortcut($desktopPath)
$desktopLnk.TargetPath = $exePath
$desktopLnk.WorkingDirectory = $appDir
$desktopLnk.IconLocation = "$icoPath,0"
$desktopLnk.Description = "Dual Audio Synchronizer v2.0 - Phase-Locked Multi-Speaker Engine & Hearing Clarity Studio"
$desktopLnk.Save()

$startupPath = Join-Path ([Environment]::GetFolderPath("Startup")) "Dual Audio Synchronizer.lnk"
$startupLnk = $wsh.CreateShortcut($startupPath)
$startupLnk.TargetPath = $exePath
$startupLnk.Arguments = "--minimized"
$startupLnk.WorkingDirectory = $appDir
$startupLnk.IconLocation = "$icoPath,0"
$startupLnk.Description = "Dual Audio Synchronizer v2.0"
$startupLnk.Save()

Write-Host "[3/3] Launching Dual Audio Synchronizer..." -ForegroundColor Green
Start-Process -FilePath $exePath -WorkingDirectory $appDir

Write-Host ""
Write-Host "Installation complete! Dual Audio Synchronizer is now running." -ForegroundColor Green
Write-Host "Enjoying the app? Buy the dev a coffee or a smoke: https://paypal.me/dlinacre16" -ForegroundColor Magenta
Write-Host ""
