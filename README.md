# Sillet

A wallet for Windows, Android and Linux with 16 networks and 42 assets, all from one 12-word phrase. Your keys never leave your device

Made by the Raton team → [raton.fun](https://raton.fun)

## Networks

| Network | Coin | Tokens |
|---|---|---|
| Bitcoin | BTC | |
| Ethereum | ETH | USDT, USDC, WLD, FDUSD, LINK |
| TRON | TRX | USDT |
| Solana | SOL | USDT, USDC, TRUMP |
| BNB Smart Chain | BNB | USDT, FDUSD, LINK |
| Polygon | POL | USDT, USDC, LINK |
| Arbitrum One | ETH | USDT, USDC, LINK |
| Optimism | ETH | USDT, USDC, WLD, LINK |
| Base | ETH | USDC, LINK |
| Avalanche C-Chain | AVAX | USDT, USDC |
| Litecoin | LTC | |
| Dogecoin | DOGE | |
| Kaia | KAIA | |
| Linea | ETH | |
| Blast | ETH | |
| Gnosis | xDAI | |

Standard derivation paths, so your phrase gives the same addresses in Trust Wallet, Exodus (Bitcoin, EVM, TRON) and Phantom (Solana). You can also add your own tokens by contract address

## Run it

Needs .NET Framework 4.7.2 (already in Windows 10 and 11)

Open `Sillet.sln` in Visual Studio, set `Sillet.App` as startup and hit F5. Or from the terminal:

```
dotnet build Sillet.sln -c Release
src\Sillet.App\bin\Release\net472\Sillet.exe
```

There's a console version too in `src/Sillet.Cli`

## What's in it

- Send with a live status: Pending → Confirmed → Sent. Open any sent payment in Activity and hit the eye to follow it again
- Warning when you're sending to an address for the first time
- Swap coins inside the app (Exolix, no account needed) and buy or sell through MoonPay, Ramp, Transak, Buda or Orionx
- Several wallets and accounts from the same phrase, plus an optional BIP-39 passphrase
- Watch-only mode for Ledger and Trezor addresses
- Contacts, price alerts, favorites, fee speed, CSV export and encrypted backups
- Rotating receive addresses for Bitcoin, Litecoin and Dogecoin
- Notifications and a few little sounds (you can turn them off)
- Tor or SOCKS5 proxy, and your own nodes
- Five themes and five languages: English, Español, Русский, 中文, Français
- Developer mode, hidden at the bottom of Settings under the version, to play with fake payments without moving anything

## Staying safe

- The wallet file is AES-256-GCM with a scrypt key (128 MB of memory per guess)
- On Windows the file is also locked to your PC, so a copied vault is useless anywhere else
- Local data is encrypted for your Windows user, and other apps can't read Sillet's memory
- Hidden from screenshots by default, and copied addresses stay out of clipboard history
- Sillet warns you if some other program swaps an address you just copied
- Weak passwords are rejected, and wrong guesses slow down
- Windows Hello can unlock the wallet and confirm sends

Try every network with a small amount first

## Android and Linux

```
dotnet publish src/Sillet.Android -c Release -f net9.0-android
```

The APK ends up in `src/Sillet.Android/bin/Release/net9.0-android/publish/`. Copy it to your phone and allow installs from that source

On Linux, with the .NET 9 SDK installed, run `./linux/install.sh` and you get `sillet` and `sillet-cli` in your app menu and `~/.local/bin`

Still coming to mobile: fingerprint unlock, swap and buy

## Command line

`sillet-cli` uses the same wallets as the app. Run it alone for a menu, or try `sillet-cli balance`, `sillet-cli address tron`, `sillet-cli prices`, `sillet-cli wallets`. Add `--wallet <name>` to pick one

## News and updates

The home screen news come from `https://raton.fun/wallet/news.json`. Check `docs/news.json` for the format

Sillet reads `https://raton.fun/wallet/version.json` to tell you when there's a new version, and on Windows it can install it by itself (Settings → About)

## Project layout

```
src/Sillet.Core      BIP39, derivation, signing, one class per network
src/Sillet.App       WPF app
src/Sillet.Cli       console app
src/Sillet.Mobile    shared Avalonia UI
src/Sillet.Android   Android app
src/Sillet.Desktop   Avalonia desktop window
```

Adding an EVM network is one small class, check `LineaChain` for an example and register it in `WalletManager`

## Credits

Prices and icons from [CoinGecko](https://www.coingecko.com), fear and greed index from [alternative.me](https://alternative.me/crypto/fear-and-greed-index/), font is [Inter](https://rsms.me/inter/)
