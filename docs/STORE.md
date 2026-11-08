# Publishing the browser extension

The release workflow builds three files for the extension: `Sillet-Extension-Chromium.zip` (Chrome, Brave, Edge), `Sillet-Extension-Firefox.zip` and `Sillet-Extension-Source.zip` (needed by Firefox reviewers). Use the files from the GitHub release, never a build from your own computer, so the store copy matches the attested one

Store rules and screens change over time. If a step looks different, follow what the store shows

## Before you start

- Privacy policy address: `https://raton.fun/wallet/secure` (section 9, Browser extension)
- Images: `extension/store/` has four 1280x800 screenshots and a 440x280 tile. They use test balances, so take your own screenshots if you want real ones
- Icon 128x128: `extension/icons/icon128.png`
- Publisher name: Raton

## Chrome Web Store (also covers Brave)

1. Go to https://chrome.google.com/webstore/devconsole and sign in with a Google account for the team
2. Pay the one-time registration fee (about 5 USD)
3. New item, upload `Sillet-Extension-Chromium.zip`
4. Store listing: fill the texts below, upload the screenshots and the tile, choose category Productivity, language English
5. Privacy tab: paste the single purpose and the permission reasons below, say no remote code, tick the data usage boxes, set the privacy policy address (the Secure page)
6. Submit for review. Wallets ask for access to all sites, so the review can take longer (days to a few weeks)
7. When approved, copy the store address into `STORES.chromium` in `wallet/download/index.html`

Brave users install from this same listing

## Microsoft Edge Add-ons

1. Go to https://partner.microsoft.com/dashboard/microsoft-edge and register (free)
2. Create new extension, upload the same `Sillet-Extension-Chromium.zip`
3. Same texts and images, same privacy policy address
4. Submit. When approved, copy the address into `STORES.edge`

## Firefox Add-ons (AMO)

1. Go to https://addons.mozilla.org/developers and sign in with a Firefox account (free)
2. Submit a new add-on, choose On this site (listed)
3. Upload `Sillet-Extension-Firefox.zip`
4. When it asks if you use a build tool or minifier, answer yes and upload `Sillet-Extension-Source.zip`. In the notes write: `Node 22. Run npm ci, then npm run build. The Firefox files are in dist/firefox`
5. Fill the listing with the texts below and submit. When approved, copy the address into `STORES.firefox`

The add-on id is `wallet@raton.fun` and must never change, or updates stop working for existing users

## Updates

Each new version goes through the same review. Release as usual (`release.ps1`), then upload the new zip in each store. The `version` in `extension/package.json` must be higher than the one in the store

## Texts to paste

Name: Sillet

Short description (under 132 characters):
`Wallet for websites. Your keys stay encrypted on your device. Open source by the Raton team`

Long description:
```
Sillet is a free, open source wallet for the websites you use. Connect to apps on Ethereum and other EVM networks, sign messages and send payments, and keep your keys encrypted on your own device.

What you get
- Works with sites like other wallets do (EIP-1193 and EIP-6963)
- Ethereum, Arbitrum, Optimism, Base, Polygon, BNB Chain, Avalanche, Kaia, Linea, Blast and Gnosis
- You approve every connection, signature and payment in a separate window
- Clear warnings before unlimited token approvals, permits and payments to unknown contracts
- Choose slow, normal or fast network fees
- Optional USD prices (off by default)
- Bitcoin, Litecoin, Dogecoin and Bitcoin Cash: see, send and receive from the Sillet window
- Pay faster: right click a QR code or an address and choose Pay with Sillet, or press Alt+Shift+Q to scan the QR on the page
- Finds payment pages and opens bitcoin: and ethereum: links in Sillet (can be turned off)
- Market tab with prices, charts and favorites (optional, uses CoinGecko)
- Address book and private mode to hide balances
- Export your activity to CSV
- Several accounts from one recovery phrase
- English and Spanish

Private by design
- No account, no tracking, no analytics, no ads
- Your recovery phrase is encrypted with your password and never leaves your device
- The extension only talks to public blockchain nodes and explorers of the network you use

Open source (MIT). Code and builds: https://github.com/sillycs/Sillet-Wallet
Sillet is new and has not been independently audited, so please use amounts you are comfortable with. Keep your recovery phrase offline and never share it.
```

Single purpose:
`Sillet is a cryptocurrency wallet. It lets the user manage accounts, and lets websites the user approves request connections, signatures and transactions.`

Permission reasons:
- `storage`: saves the encrypted recovery phrase, settings and the list of sites the user approved
- `alarms`: locks the wallet after the time the user chooses
- `scripting`: when Sillet is installed or updated, it adds the wallet to pages that are already open, so the user does not have to reload them
- `contextMenus`: the right click items Scan QR with Sillet and Pay with Sillet, to read a crypto QR code or address on a page
- `activeTab`: when the user clicks one of those items or presses the shortcut, Sillet takes one picture of the tab to find a QR code. The picture is read on the device and is never saved or sent
- Page scanner (content script `scanner.js`): on web pages it looks for crypto payment links and addresses in the page text, on the device, and shows a Pay with Sillet button. Only strings that look like crypto addresses are checked by Sillet itself. The user can turn it off in Settings or hide it on one site
- WalletConnect: only after the user pastes a wc: link, Sillet connects to the WalletConnect relay (wss://relay.walletconnect.org) and verify.walletconnect.org so apps can request signatures. Messages are end-to-end encrypted and analytics are off
- Host access to public blockchain nodes: reads balances and sends signed transactions to the network the user selected
- Content scripts on all http and https pages: provides the wallet object (window.ethereum) so websites can ask the user to connect or sign. It only answers requests made by the page and gives no data until the user approves

Remote code: No. All code is inside the package

Data use: the extension does not collect or transmit user data to the developer. Wallet addresses are sent to the public blockchain node the user selects, only to read balances and send transactions. For Bitcoin, Litecoin, Dogecoin and Bitcoin Cash the address is sent to the public explorers mempool.space, blockstream.info, litecoinspace.org, api.blockcypher.com and bch1.trezor.io. If the user turns on the optional USD prices and Market tab, coin names and token addresses of the selected network are sent to CoinGecko. It is off by default
