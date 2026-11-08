# Sillet browser extension

A wallet for websites on Chrome, Edge, Brave and Firefox. It injects the standard `window.ethereum` provider (EIP-1193) and announces itself with EIP-6963, so any dapp that works with MetaMask can use it

> Sillet is new software and has not had an independent security audit. Use only small amounts you can afford to lose

## What it does

- Create a wallet or import a recovery phrase (the same phrase gives the same addresses as the Sillet app, MetaMask and Trust Wallet)
- Connect to websites, sign messages (`personal_sign`), typed data (`eth_signTypedData_v3` and `v4`) and send transactions
- 11 EVM networks, the same as the Sillet app: Ethereum, Base, Arbitrum, Optimism, Polygon, BNB Chain, Avalanche, Linea, Blast, Kaia and Gnosis
- Bitcoin, Litecoin, Dogecoin and Bitcoin Cash in the Sillet window: balance, activity, receive and send with slow, normal and fast fees. Same recovery phrase and same first address as the Sillet app. Websites cannot use these coins, only EVM networks
- Market tab: top coins with 24 hour and 7 day change, charts, search, favorites and gainers or losers (off until you turn on prices)
- Send and receive from the Sillet window, with slow, normal and fast fees
- Plain-language warnings for unlimited token approvals, NFT approvals, permit signatures, unknown contracts and transactions that would fail
- Per-site network and account, connected sites list, auto-lock, English and Spanish
- Optional USD prices and total balance (off by default, uses CoinGecko)
- Right click a QR code on any page and choose Scan QR with Sillet, or select an address and choose Pay with Sillet. The shortcut Alt+Shift+Q scans the QR on the page you are looking at. A payment opens the Send screen already filled in, and you always review it before it is sent
- Payment pages: when a page looks like a crypto checkout, a small Pay with Sillet button appears. bitcoin:, litecoin:, dogecoin:, bitcoincash: and ethereum: links open in Sillet. It can be turned off in Settings or hidden on one site
- Address book: save names for addresses and send with one tap
- Private mode: the eye button hides balances
- Rename accounts, export activity to CSV
- Smooth animations (balance count-up, view transitions, toasts), reduced for people who prefer less motion
- Pages that were already open get the wallet when Sillet is installed, no reload needed
- Sets `isMetaMask` so sites that only look for MetaMask still find it. Sites that support EIP-6963 see it as Sillet
- WalletConnect: paste a wc: link (Sites, WalletConnect) to connect apps that show a QR code, then sign and send through the same approval windows. Needs a WalletConnect project id in `walletconnect.json` or `SILLET_WC_PROJECT` when building

## What it does not do (yet)

Hardware wallets, custom networks and RPC servers, swaps, NFT galleries, `eth_subscribe` (dapps fall back to polling), websites using Bitcoin or other non-EVM coins, and Solana and the other networks of the app. `eth_sign` is blocked on purpose

Bitcoin, Litecoin, Dogecoin and Bitcoin Cash use the first address of each account, the same one the Sillet app shows first. Funds on other addresses of the same account in the app are not shown here

## Security

- The recovery phrase is encrypted with PBKDF2-SHA256 (600000 rounds) and AES-256-GCM. The decrypted phrase lives only in the browser's in-memory session storage and is dropped when the wallet locks
- Websites never touch the extension directly. They talk to a small bridge that only forwards RPC requests, and wallet actions only work from Sillet's own pages
- Every signature and transaction needs your approval in a Sillet window that shows the real site address
- No telemetry. The only network traffic is JSON-RPC to the public nodes listed in [docs/ENDPOINTS.md](../docs/ENDPOINTS.md)

## Build and test

```
cd extension
npm ci
npm test            # unit tests: derivation vectors, vault, signing, fees, warnings
npm run build       # dist/chromium and dist/firefox
npm run test:e2e    # loads the extension in Chromium and drives it like a dapp (needs: npm i -D playwright)
```

Load it without a store:

- Chrome, Edge, Brave: open `chrome://extensions` (or `edge://extensions`, `brave://extensions`), turn on Developer mode, Load unpacked, choose `dist/chromium`
- Firefox 140 or newer: open `about:debugging`, This Firefox, Load Temporary Add-on, choose `dist/firefox/manifest.json`

`npm run sync-chains` regenerates the network list from the Sillet app's `EvmNetworks.cs`, so the app and the extension never disagree

## Publishing

Releases are built by GitHub Actions and attached to each GitHub Release as `Sillet-Extension-Chromium.zip` (Chrome Web Store, Edge Add-ons and Brave) and `Sillet-Extension-Firefox.zip` (Firefox Add-ons). For Firefox review upload the source (this repository) and the build steps above
