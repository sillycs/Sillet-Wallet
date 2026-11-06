param(
    [string]$Repo = "C:\Users\Cristian\Desktop\raton-repo",
    [switch]$NoUpload,
    [switch]$Clean
)

# Builds only the Android app and uploads only Sillet.apk and version-android.json.
# Windows, Linux and the CLI are not touched, and Windows users do not see an update notice.

$ErrorActionPreference = "Stop"
$Root = $PSScriptRoot
$Dist = Join-Path $Root "dist"

function Step([string]$Text) {
    Write-Host ""
    Write-Host "==> $Text" -ForegroundColor Cyan
}

function Invoke-Checked([string]$Label, [scriptblock]$Block) {
    & $Block
    if ($LASTEXITCODE -ne 0) {
        Write-Host "$Label failed" -ForegroundColor Red
        exit 1
    }
}

Set-Location $Root
New-Item -ItemType Directory -Force $Dist | Out-Null

$csproj = Join-Path $Root "src\Sillet.Android\Sillet.Android.csproj"
$versionLine = Select-String -Path $csproj -Pattern "<ApplicationDisplayVersion>([0-9.]+)</ApplicationDisplayVersion>" | Select-Object -First 1
if (-not $versionLine) {
    Write-Host "ApplicationDisplayVersion not found in Sillet.Android.csproj" -ForegroundColor Red
    exit 1
}
$ver = $versionLine.Matches[0].Groups[1].Value
Write-Host ("Android version " + $ver) -ForegroundColor Yellow

Step "Android app"
if ($Clean) {
    Remove-Item -Recurse -Force src\Sillet.Android\bin, src\Sillet.Android\obj, src\Sillet.Mobile\bin, src\Sillet.Mobile\obj -ErrorAction SilentlyContinue
}
Invoke-Checked "Android build" { dotnet publish src\Sillet.Android -c Release -f net9.0-android }
$apk = Get-ChildItem src\Sillet.Android\bin\Release\net9.0-android\publish\*-Signed.apk | Select-Object -First 1
if ($null -eq $apk) {
    Write-Host "Signed APK not found" -ForegroundColor Red
    exit 1
}
Copy-Item $apk.FullName (Join-Path $Dist "Sillet.apk") -Force
$mb = [math]::Round((Get-Item (Join-Path $Dist "Sillet.apk")).Length / 1MB, 1)
Write-Host ("   Sillet.apk  {0} MB" -f $mb)
if ($mb -ge 100) {
    Write-Host "   Too big for GitHub (100 MB limit)" -ForegroundColor Red
    exit 1
}

if ($NoUpload) {
    Write-Host ""
    Write-Host "Done. File is in $Dist" -ForegroundColor Green
    exit 0
}

Step "Upload to GitHub"
if (-not (Test-Path (Join-Path $Repo ".git"))) {
    Write-Host "No git repo at $Repo" -ForegroundColor Red
    exit 1
}
Invoke-Checked "git pull" { git -C $Repo pull }
$target = Join-Path $Repo "wallet\download"
New-Item -ItemType Directory -Force $target | Out-Null
Copy-Item (Join-Path $Dist "Sillet.apk") $target -Force

$json = '{"version":"' + $ver + '","url":"https://raton.fun/wallet/download","apk":"https://raton.fun/wallet/download/Sillet.apk"}'
[System.IO.File]::WriteAllText((Join-Path $Repo "wallet\version-android.json"), $json, (New-Object System.Text.UTF8Encoding $false))
Write-Host "   version-android.json -> $ver"

git -C $Repo add (Join-Path $target "Sillet.apk") (Join-Path $Repo "wallet\version-android.json")
$changes = git -C $Repo status --porcelain
if ([string]::IsNullOrWhiteSpace($changes)) {
    Write-Host "Nothing new to upload" -ForegroundColor Yellow
    exit 0
}
Invoke-Checked "git commit" { git -C $Repo commit -m ("Update Sillet Android " + $ver + " " + (Get-Date -Format "yyyy-MM-dd HH:mm")) }
Invoke-Checked "git push" { git -C $Repo push }

Write-Host ""
Write-Host "Done. Android $ver is online" -ForegroundColor Green
Write-Host "APK: https://raton.fun/wallet/download/Sillet.apk"
