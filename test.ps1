param(
    [switch]$Clean,
    [switch]$Debug,
    [switch]$Both,
    [string]$Device = ""
)

$ErrorActionPreference = "Stop"
$Root = $PSScriptRoot
Set-Location $Root

function Close-Sillet {
    $running = Get-Process -Name Sillet -ErrorAction SilentlyContinue
    if (-not $running) { Write-Host "   Not running"; return }
    $running | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 600
    if (Get-Process -Name Sillet -ErrorAction SilentlyContinue) {
        & taskkill /F /T /IM Sillet.exe 2>&1 | Out-Null
        Start-Sleep -Milliseconds 800
    }
    if (Get-Process -Name Sillet -ErrorAction SilentlyContinue) {
        Write-Host "   Sillet is still open. Close it from the tray icon (Quit) and run this again" -ForegroundColor Red
        exit 1
    }
    Write-Host "   Closed"
}

function Step([string]$Text) {
    Write-Host ""
    Write-Host "==> $Text" -ForegroundColor Cyan
}

$config = "Release"
if ($Debug) { $config = "Debug" }

Step "Closing Sillet if it is open"
Close-Sillet

if ($Clean) {
    Step "Cleaning old build"
    Remove-Item -Recurse -Force src\Sillet.App\bin, src\Sillet.App\obj, src\Sillet.Core\bin, src\Sillet.Core\obj -ErrorAction SilentlyContinue
}

Step "Building Windows ($config)"
dotnet build src\Sillet.App -c $config
if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed, cleaning and retrying once" -ForegroundColor Yellow
    Remove-Item -Recurse -Force src\Sillet.App\obj, src\Sillet.Core\obj -ErrorAction SilentlyContinue
    dotnet build src\Sillet.App -c $config
}
if ($LASTEXITCODE -ne 0) {
    Write-Host "Windows build failed" -ForegroundColor Red
    exit 1
}

$exe = Join-Path $Root ("src\Sillet.App\bin\" + $config + "\net472\Sillet.exe")
if (-not (Test-Path $exe)) {
    Write-Host "Sillet.exe not found" -ForegroundColor Red
    exit 1
}
$file = Get-Item $exe
Write-Host ("   " + $file.Name + "  " + [math]::Round($file.Length / 1MB, 1) + " MB")

Step "Opening Sillet"
Start-Process -FilePath $exe -WorkingDirectory (Split-Path $exe)

Write-Host ""
Write-Host "Done. Sillet is running on your PC" -ForegroundColor Green

if ($Both) {
    $phoneScript = Join-Path $Root "phone.ps1"
    if (-not (Test-Path $phoneScript)) {
        Write-Host "phone.ps1 not found" -ForegroundColor Red
        exit 1
    }
    $phoneArgs = @{}
    if ($Clean) { $phoneArgs["Clean"] = $true }
    if ($Device -ne "") { $phoneArgs["Device"] = $Device }
    & $phoneScript @phoneArgs
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
