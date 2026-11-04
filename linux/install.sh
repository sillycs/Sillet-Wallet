#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
ARCH="$(uname -m)"
case "$ARCH" in
  x86_64) RID="linux-x64" ;;
  aarch64|arm64) RID="linux-arm64" ;;
  *) echo "Unsupported architecture: $ARCH"; exit 1 ;;
esac
TARGET="$HOME/.local/share/sillet"
echo "Building Sillet for $RID..."
dotnet publish "$ROOT/src/Sillet.Desktop" -c Release -r "$RID" --self-contained true -p:PublishSingleFile=true -o "$TARGET/app"
dotnet publish "$ROOT/src/Sillet.Cli" -c Release -f net8.0 -r "$RID" --self-contained true -p:PublishSingleFile=true -o "$TARGET/cli"
mkdir -p "$HOME/.local/bin" "$HOME/.local/share/applications" "$HOME/.local/share/icons/hicolor/256x256/apps"
cp "$ROOT/src/Sillet.App/Assets/silleticon.png" "$HOME/.local/share/icons/hicolor/256x256/apps/sillet.png"
ln -sf "$TARGET/app/Sillet.Desktop" "$HOME/.local/bin/sillet"
ln -sf "$TARGET/cli/sillet-cli" "$HOME/.local/bin/sillet-cli"
sed "s|@EXEC@|$TARGET/app/Sillet.Desktop|" "$ROOT/linux/sillet.desktop" > "$HOME/.local/share/applications/sillet.desktop"
command -v notify-send >/dev/null 2>&1 || echo "Tip: install libnotify (notify-send) to get payment notifications on your desktop."
echo "Done. Open Sillet from your app menu, or run: sillet  (and sillet-cli for the terminal)"
