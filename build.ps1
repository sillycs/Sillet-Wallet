
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

function Quiet([scriptblock]$Block) {
    $old = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try { & $Block *> $null } finally { $ErrorActionPreference = $old }
}

function Show-Size([string]$Path) {
    $mb = [math]::Round((Get-Item $Path).Length / 1MB, 1)
    Write-Host ("   {0}  {1} MB" -f (Split-Path $Path -Leaf), $mb)
    if ($mb -ge 100) {
        Write-Host "   Too big for GitHub (100 MB limit)" -ForegroundColor Red
        exit 1
    }
}

Set-Location $Root
New-Item -ItemType Directory -Force $Dist | Out-Null

Step "Closing Sillet if it is open"
Close-Sillet

Step "Android app"
Remove-Item -Recurse -Force src\Sillet.Android\bin, src\Sillet.Android\obj, src\Sillet.Mobile\bin, src\Sillet.Mobile\obj -ErrorAction SilentlyContinue
Invoke-Checked "Android build" { dotnet publish src\Sillet.Android -c Release -f net9.0-android }
$apk = Get-ChildItem src\Sillet.Android\bin\Release\net9.0-android\publish\*-Signed.apk | Select-Object -First 1
if ($null -eq $apk) {
    Write-Host "Signed APK not found" -ForegroundColor Red
    exit 1
}
New-Item -ItemType Directory -Force $Dist | Out-Null
Copy-Item $apk.FullName (Join-Path $Dist "Sillet.apk") -Force

Step "Windows app (single Sillet.exe)"
Invoke-Checked "Windows build" { dotnet build src\Sillet.App -c Release }
New-Item -ItemType Directory -Force $Dist | Out-Null
Copy-Item src\Sillet.App\bin\Release\net472\Sillet.exe (Join-Path $Dist "Sillet.exe") -Force

Step "Windows command line (single sillet-cli.exe)"
Invoke-Checked "CLI build" { dotnet publish src\Sillet.Cli -c Release -f net8.0 -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -o (Join-Path $Dist "cli") }
Copy-Item (Join-Path $Dist "cli\sillet-cli.exe") (Join-Path $Dist "Sillet-CLI.exe") -Force
Remove-Item -Recurse -Force (Join-Path $Dist "cli")

Step "Linux app and command line (Sillet-Linux-x64.tar.gz)"
$stage = Join-Path $Dist "linux-stage"
Remove-Item -Recurse -Force $stage -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force (Join-Path $stage "app"), (Join-Path $stage "cli"), (Join-Path $stage "Sillet") | Out-Null
Invoke-Checked "Linux app build" { dotnet publish src\Sillet.Desktop -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o (Join-Path $stage "app") }
Invoke-Checked "Linux CLI build" { dotnet publish src\Sillet.Cli -c Release -f net8.0 -r linux-x64 --self-contained true -p:PublishSingleFile=true -o (Join-Path $stage "cli") }
$pack = Join-Path $stage "Sillet"
Copy-Item (Join-Path $stage "app\Sillet.Desktop") $pack -Force
Copy-Item (Join-Path $stage "cli\sillet-cli") $pack -Force
Copy-Item src\Sillet.App\Assets\silleticon.png (Join-Path $pack "sillet.png") -Force
Copy-Item linux\sillet.desktop (Join-Path $pack "sillet.desktop") -Force
Copy-Item linux\uninstall.sh (Join-Path $pack "uninstall.sh") -Force
Copy-Item linux\install-bin.sh (Join-Path $pack "install.sh") -Force
# Make sure the shell scripts use Unix line endings even if git converted them
foreach ($sh in @("install.sh", "uninstall.sh", "sillet.desktop")) {
    $file = Join-Path $pack $sh
    $text = [System.IO.File]::ReadAllText($file).Replace("`r`n", "`n")
    [System.IO.File]::WriteAllText($file, $text, (New-Object System.Text.UTF8Encoding $false))
}
$tar = Join-Path $Dist "Sillet-Linux-x64.tar.gz"
Remove-Item $tar -Force -ErrorAction SilentlyContinue
Invoke-Checked "Linux package" { tar -czf $tar -C $stage "Sillet" }
Remove-Item -Recurse -Force $stage

Step "Files"
Show-Size (Join-Path $Dist "Sillet.exe")
Show-Size (Join-Path $Dist "Sillet-CLI.exe")
Show-Size (Join-Path $Dist "Sillet.apk")
Show-Size (Join-Path $Dist "Sillet-Linux-x64.tar.gz")

Write-Host ""
Write-Host "Done. Test build is in $Dist. Official releases are built only by GitHub Actions (use release.ps1)" -ForegroundColor Green
