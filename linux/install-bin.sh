#!/usr/bin/env bash
# Installs the prebuilt Sillet for Linux (x86_64) into your user account. No .NET needed.
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
if [ "$(uname -m)" != "x86_64" ]; then echo "This download is for x86_64 (64-bit Intel/AMD). Build from source for other CPUs."; exit 1; fi
TARGET="$HOME/.local/share/sillet"
mkdir -p "$TARGET/app" "$TARGET/cli" "$HOME/.local/bin" "$HOME/.local/share/applications" "$HOME/.local/share/icons/hicolor/256x256/apps"
cp "$HERE/Sillet.Desktop" "$TARGET/app/Sillet.Desktop"
cp "$HERE/sillet-cli" "$TARGET/cli/sillet-cli"
chmod +x "$TARGET/app/Sillet.Desktop" "$TARGET/cli/sillet-cli"
cp "$HERE/sillet.png" "$HOME/.local/share/icons/hicolor/256x256/apps/sillet.png"
ln -sf "$TARGET/app/Sillet.Desktop" "$HOME/.local/bin/sillet"
ln -sf "$TARGET/cli/sillet-cli" "$HOME/.local/bin/sillet-cli"
sed "s|@EXEC@|$TARGET/app/Sillet.Desktop|" "$HERE/sillet.desktop" > "$HOME/.local/share/applications/sillet.desktop"
cp "$HERE/uninstall.sh" "$TARGET/uninstall.sh" && chmod +x "$TARGET/uninstall.sh"
command -v notify-send >/dev/null 2>&1 || echo "Tip: install libnotify (notify-send) to get payment notifications on your desktop."
echo "Done. Open Sillet from your app menu, or run: sillet  (and sillet-cli for the terminal)"
