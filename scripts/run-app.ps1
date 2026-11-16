<#
.SYNOPSIS
    Rebuild and restart the OpenTrackpad tray app (development helper).
#>

$ErrorActionPreference = "Stop"

$RepoRoot = Split-Path -Parent $PSScriptRoot
$Solution = Join-Path $RepoRoot "app\OpenTrackpad.slnx"
$Exe = Join-Path $RepoRoot "app\OpenTrackpad.App\bin\Debug\net10.0-windows\OpenTrackpad.App.exe"

Get-Process OpenTrackpad.App -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 300

dotnet build $Solution -v quiet
if ($LASTEXITCODE -ne 0) {
    throw "Build failed."
}

Start-Process -FilePath $Exe -WorkingDirectory (Split-Path -Parent $Exe)
Write-Host "OpenTrackpad started." -ForegroundColor Green
