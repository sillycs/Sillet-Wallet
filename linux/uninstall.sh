#!/usr/bin/env bash
# Removes Sillet from this user account. Your wallet data in ~/.config/Sillet is NOT deleted.
set -euo pipefail
rm -rf "$HOME/.local/share/sillet"
rm -f "$HOME/.local/bin/sillet" "$HOME/.local/bin/sillet-cli"
rm -f "$HOME/.local/share/applications/sillet.desktop" "$HOME/.local/share/icons/hicolor/256x256/apps/sillet.png"
rm -f "$HOME/.config/autostart/sillet.desktop"
echo "Sillet removed. Your wallet files were left untouched (back them up before deleting ~/.config/Sillet)."
