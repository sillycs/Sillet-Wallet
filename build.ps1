param(
    [string]$Repo = "C:\Users\Cristian\Desktop\raton-repo",
    [switch]$NoUpload,
    [switch]$Release
)

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

if ($Release) {
    Step "GitHub release"
    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
        Write-Host "GitHub CLI not found. Install it from https://cli.github.com and run: gh auth login" -ForegroundColor Red
        exit 1
    }
    $relLine = Select-String -Path (Join-Path $Root "src\Sillet.App\Services\UpdateService.cs") -Pattern 'Version = "([0-9.]+)"' | Select-Object -First 1
    if (-not $relLine) { Write-Host "Could not read the version" -ForegroundColor Red; exit 1 }
    $tag = "v" + $relLine.Matches[0].Groups[1].Value

    Step "Source code to GitHub"
    if (-not (Test-Path (Join-Path $Root ".git"))) {
        Write-Host "This folder is not a git repo. Run: git init, git remote add origin <your repo url>" -ForegroundColor Red
        exit 1
    }
    git -C $Root add -A
    $srcChanges = git -C $Root status --porcelain
    if (-not [string]::IsNullOrWhiteSpace($srcChanges)) {
        Invoke-Checked "git commit" { git -C $Root commit -m ("Sillet " + $tag) }
    } else {
        Write-Host "   No source changes"
    }
    git -C $Root fetch origin 2>$null
    git -C $Root rev-parse --verify origin/main 2>$null | Out-Null
    if ($LASTEXITCODE -eq 0) {
        Invoke-Checked "git pull" { git -C $Root pull --rebase --allow-unrelated-histories -X theirs origin main }
    }
    Invoke-Checked "git push" { git -C $Root push -u origin HEAD }
    $files = @("Sillet.exe", "Sillet-CLI.exe", "Sillet.apk", "Sillet-Linux-x64.tar.gz") | ForEach-Object { Join-Path $Dist $_ }
    $sums = Join-Path $Dist "SHA256SUMS.txt"
    ($files | ForEach-Object { (Get-FileHash $_ -Algorithm SHA256).Hash.ToLowerInvariant() + "  " + (Split-Path $_ -Leaf) }) | Set-Content -Encoding ascii $sums
    $exists = gh release view $tag 2>$null
    if ($LASTEXITCODE -eq 0) {
        Invoke-Checked "gh upload" { gh release upload $tag @($files + $sums) --clobber }
    } else {
        Invoke-Checked "gh release" { gh release create $tag @($files + $sums) --title "Sillet $tag" --generate-notes --target (git -C $Root rev-parse HEAD) }
    }
    Write-Host "   Release $tag is live" -ForegroundColor Green
}

if ($NoUpload) {
    Write-Host ""
    Write-Host "Done. Files are in $Dist" -ForegroundColor Green
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
Copy-Item (Join-Path $Dist "Sillet.exe") $target -Force
Copy-Item (Join-Path $Dist "Sillet.apk") $target -Force
Copy-Item (Join-Path $Dist "Sillet-CLI.exe") $target -Force
Copy-Item (Join-Path $Dist "Sillet-Linux-x64.tar.gz") $target -Force

$versionLine = Select-String -Path (Join-Path $Root "src\Sillet.App\Services\UpdateService.cs") -Pattern 'Version = "([0-9.]+)"' | Select-Object -First 1
if ($versionLine) {
    $ver = $versionLine.Matches[0].Groups[1].Value
    $hash = (Get-FileHash (Join-Path $target "Sillet.exe") -Algorithm SHA256).Hash.ToLowerInvariant()
    $json = '{"version":"' + $ver + '","url":"https://raton.fun/wallet/download","exe":"https://raton.fun/wallet/download/Sillet.exe","sha256":"' + $hash + '"}'
    [System.IO.File]::WriteAllText((Join-Path $Repo "wallet\version.json"), $json, (New-Object System.Text.UTF8Encoding $false))
    Write-Host "   version.json -> $ver ($hash)"
}

$androidLine = Select-String -Path (Join-Path $Root "src\Sillet.Android\Sillet.Android.csproj") -Pattern "<ApplicationDisplayVersion>([0-9.]+)</ApplicationDisplayVersion>" | Select-Object -First 1
if ($androidLine) {
    $androidVer = $androidLine.Matches[0].Groups[1].Value
    $androidJson = '{"version":"' + $androidVer + '","url":"https://raton.fun/wallet/download","apk":"https://raton.fun/wallet/download/Sillet.apk"}'
    [System.IO.File]::WriteAllText((Join-Path $Repo "wallet\version-android.json"), $androidJson, (New-Object System.Text.UTF8Encoding $false))
    Write-Host "   version-android.json -> $androidVer"
}

foreach ($page in @("wallet\download.html", "wallet\download\index.html")) {
    $file = Join-Path $Repo $page
    if (Test-Path $file) {
        $html = [System.IO.File]::ReadAllText($file)
        $fixed = $html.Replace("/wallet/download/Sillet-Windows.zip", "/wallet/download/Sillet.exe").Replace("Unzip and open <code>Sillet.exe</code>", "Open <code>Sillet.exe</code>")
        if ($fixed -ne $html) { [System.IO.File]::WriteAllText($file, $fixed, (New-Object System.Text.UTF8Encoding $false)) }
    }
}

$old = Join-Path $target "Sillet-Windows.zip"
if (Test-Path $old) { Remove-Item $old -Force }

git -C $Repo add -A wallet
$changes = git -C $Repo status --porcelain
if ([string]::IsNullOrWhiteSpace($changes)) {
    Write-Host "Nothing new to upload" -ForegroundColor Yellow
    exit 0
}
Invoke-Checked "git commit" { git -C $Repo commit -m ("Update Sillet downloads " + (Get-Date -Format "yyyy-MM-dd HH:mm")) }
Invoke-Checked "git push" { git -C $Repo push }

Write-Host ""
Write-Host "Done. Uploaded Sillet.exe, Sillet-CLI.exe, Sillet.apk and Sillet-Linux-x64.tar.gz" -ForegroundColor Green
Write-Host "Linux link: https://raton.fun/wallet/download/Sillet-Linux-x64.tar.gz"
