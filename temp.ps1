param(
    [switch]$Debug,
    [switch]$Keep
)

$ErrorActionPreference = "Stop"
$Root = $PSScriptRoot
$config = "Release"
if ($Debug) { $config = "Debug" }
$Work = Join-Path $env:TEMP "Sillet-test"
$Src = Join-Path $Work "src"
$Out = Join-Path $Work "out"

function Step([string]$Text) {
    Write-Host ""
    Write-Host "==> $Text" -ForegroundColor Cyan
}

Step "Closing the test copy if it is open"
Get-Process -Name Sillet -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($Work) } | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 500

Step "Copying the project to $Work"
if (Test-Path $Work) { Remove-Item -Recurse -Force $Work -ErrorAction SilentlyContinue }
New-Item -ItemType Directory -Force -Path $Src | Out-Null
& robocopy (Join-Path $Root "src") (Join-Path $Src "src") /E /XD bin obj /NFL /NDL /NJH /NJS /NP | Out-Null
foreach ($file in @("Sillet.sln", "Directory.Build.props", "Directory.Packages.props", "nuget.config")) {
    $path = Join-Path $Root $file
    if (Test-Path $path) { Copy-Item $path $Src }
}

Step "Building Windows ($config) in temp"
dotnet build (Join-Path $Src "src\Sillet.App") -c $config -o $Out
if ($LASTEXITCODE -ne 0) {
    Write-Host "Windows build failed" -ForegroundColor Red
    exit 1
}

$exe = Join-Path $Out "Sillet.exe"
if (-not (Test-Path $exe)) {
    Write-Host "Sillet.exe not found" -ForegroundColor Red
    exit 1
}

Step "Opening Sillet"
Start-Process -FilePath $exe -WorkingDirectory $Out

if (-not $Keep) {
    # The source copy is only needed to build. The compiled app stays so it keeps running
    Remove-Item -Recurse -Force $Src -ErrorAction SilentlyContinue
}

Write-Host ""
Write-Host "Done. Test build running from $Out" -ForegroundColor Green
