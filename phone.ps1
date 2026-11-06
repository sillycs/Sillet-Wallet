param(
    [string]$Device = "",
    [switch]$Clean,
    [switch]$Fresh
)

$ErrorActionPreference = "Stop"
$Root = $PSScriptRoot
Set-Location $Root

function Step([string]$Text) {
    Write-Host ""
    Write-Host "==> $Text" -ForegroundColor Cyan
}

$adb = $null
foreach ($candidate in @(
    (Join-Path $env:LOCALAPPDATA "Android\Sdk\platform-tools\adb.exe"),
    "C:\Program Files (x86)\Android\android-sdk\platform-tools\adb.exe",
    "C:\Program Files\Android\android-sdk\platform-tools\adb.exe")) {
    if (Test-Path $candidate) { $adb = $candidate; break }
}
if ($null -eq $adb) {
    $found = Get-Command adb -ErrorAction SilentlyContinue
    if ($found) { $adb = $found.Source }
}
if ($null -eq $adb) {
    Write-Host "adb.exe not found" -ForegroundColor Red
    exit 1
}

Step "Looking for your phone"
& $adb start-server | Out-Null
$lines = & $adb devices | Select-Object -Skip 1 | Where-Object { $_ -match "\S" }
$phones = @()
foreach ($line in $lines) {
    $parts = $line -split "\s+"
    if ($parts.Length -ge 2 -and $parts[0] -notmatch "^emulator-") { $phones += [pscustomobject]@{ Serial = $parts[0]; State = $parts[1] } }
}
if ($Device -ne "") { $phones = @($phones | Where-Object { $_.Serial -eq $Device }) }
if ($phones.Count -eq 0) {
    Write-Host "No phone found. Plug it in with USB debugging on" -ForegroundColor Red
    exit 1
}
$phone = $phones[0]
if ($phone.State -eq "unauthorized") {
    Write-Host "Accept the USB debugging prompt on the phone, then run this again" -ForegroundColor Yellow
    exit 1
}
if ($phone.State -ne "device") {
    Write-Host ("Phone state: " + $phone.State) -ForegroundColor Red
    exit 1
}
Write-Host ("   Using " + $phone.Serial)

Step "Building Android"
if ($Clean) {
    Remove-Item -Recurse -Force src\Sillet.Android\bin, src\Sillet.Android\obj, src\Sillet.Mobile\bin, src\Sillet.Mobile\obj -ErrorAction SilentlyContinue
}
dotnet publish src\Sillet.Android -c Release -f net9.0-android
if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed, cleaning Sillet.Mobile and retrying once" -ForegroundColor Yellow
    Remove-Item -Recurse -Force src\Sillet.Mobile\bin, src\Sillet.Mobile\obj, src\Sillet.Android\obj -ErrorAction SilentlyContinue
    dotnet publish src\Sillet.Android -c Release -f net9.0-android
}
if ($LASTEXITCODE -ne 0) {
    Write-Host "Android build failed" -ForegroundColor Red
    exit 1
}
$apk = Get-ChildItem src\Sillet.Android\bin\Release\net9.0-android\publish\*-Signed.apk | Select-Object -First 1
if ($null -eq $apk) {
    Write-Host "Signed APK not found" -ForegroundColor Red
    exit 1
}
Write-Host ("   " + $apk.Name + "  " + [math]::Round($apk.Length / 1MB, 1) + " MB")

if ($Fresh) {
    Step "Removing the old app (this deletes its data)"
    & $adb -s $phone.Serial uninstall fun.raton.sillet | Out-Null
}

Step "Installing on the phone"
& $adb -s $phone.Serial install -r $apk.FullName
if ($LASTEXITCODE -ne 0) {
    Write-Host "Install failed. If it says the signatures don't match, run again with -Fresh" -ForegroundColor Red
    exit 1
}

Step "Opening Sillet"
& $adb -s $phone.Serial shell monkey -p fun.raton.sillet -c android.intent.category.LAUNCHER 1 | Out-Null

Write-Host ""
Write-Host "Done. Sillet is running on your phone" -ForegroundColor Green
