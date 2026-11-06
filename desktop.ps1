param(
    [string]$Out = (Join-Path ([Environment]::GetFolderPath("Desktop")) "Sillet-build")
)

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

function Step([string]$Text) {
    Write-Host ""
    Write-Host "==> $Text" -ForegroundColor Cyan
}

function Check([string]$Label) {
    if ($LASTEXITCODE -ne 0) {
        Write-Host "$Label failed" -ForegroundColor Red
        exit 1
    }
}

Step "Closing Sillet if it is open"
Get-Process -Name Sillet -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 600

New-Item -ItemType Directory -Force $Out | Out-Null

Step "Windows app"
dotnet build src\Sillet.App -c Release
Check "Windows build"
Copy-Item src\Sillet.App\bin\Release\net472\Sillet.exe (Join-Path $Out "Sillet.exe") -Force

Step "Android app"
Remove-Item -Recurse -Force src\Sillet.Android\bin, src\Sillet.Android\obj, src\Sillet.Mobile\bin, src\Sillet.Mobile\obj -ErrorAction SilentlyContinue
dotnet publish src\Sillet.Android -c Release -f net9.0-android
Check "Android build"
$apk = Get-ChildItem src\Sillet.Android\bin\Release\net9.0-android\publish\*-Signed.apk | Select-Object -First 1
if ($null -eq $apk) {
    Write-Host "Signed APK not found" -ForegroundColor Red
    exit 1
}
Copy-Item $apk.FullName (Join-Path $Out "Sillet.apk") -Force

Write-Host ""
Write-Host "Done. Nothing was uploaded" -ForegroundColor Green
Get-ChildItem $Out | ForEach-Object { Write-Host ("   {0}  {1} MB" -f $_.Name, [math]::Round($_.Length / 1MB, 1)) }
Invoke-Item $Out
