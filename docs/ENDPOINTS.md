# What Sillet connects to

Sillet has no account and no server of its own for your wallet. The app talks directly to public services. You can send all of it through Tor or a SOCKS5 proxy, and point any network at your own node, in Settings, Network privacy

## Needed for the wallet to work

| Purpose | Service | When |
|---|---|---|
| Bitcoin, Litecoin | mempool.space, blockstream.info, litecoinspace.org | balances, fees, sending |
| Dogecoin | api.blockcypher.com | balances, sending |
| Bitcoin Cash | bch1.trezor.io, bch2.trezor.io | balances, sending |
| Ethereum and EVM networks | eth.llamarpc.com, *.drpc.org, *.publicnode.com, kaia.blockpi.network | balances, fees, sending |
| Solana | solana-rpc.publicnode.com | balances, sending |
| TRON | api.trongrid.io | balances, sending |
| XRP Ledger | xrplcluster.com, s1.ripple.com, s2.ripple.com | balances, sending |
| TON | toncenter.com | balances, sending |
| Sui | fullnode.mainnet.sui.io, sui-rpc.publicnode.com | balances, sending |
| Cosmos Hub | public LCD endpoints | balances, sending |
| Prices and charts | api.coingecko.com, open.er-api.com, api.alternative.me | prices, fiat rates, fear and greed |
| Coin icons | image URLs returned by CoinGecko | cached on disk |

Only public addresses and signed transactions leave the device. Recovery phrases and private keys never do

## Browser extension

By default the extension only sends JSON-RPC requests to the same public nodes of the EVM networks listed above (publicnode.com, drpc.org, llamarpc.com, the official nodes of Arbitrum, Optimism, Base, Polygon, BNB Chain, Avalanche, Kaia, Linea, Blast and Gnosis). On web pages it reads the page text on the device to find crypto addresses and payment links, and it reads QR codes from pictures on the device. None of that is sent anywhere. It does not contact raton.fun or any analytics service. For Bitcoin, Litecoin, Dogecoin and Bitcoin Cash in the Sillet window it also asks mempool.space, blockstream.info, litecoinspace.org, api.blockcypher.com and bch1.trezor.io (bch2.trezor.io as backup) for the balance, history and fees of your address and to broadcast a payment. Optional and off by default: USD prices and the Market tab from api.coingecko.com, which only receive coin names and the token addresses of the selected network, never your own address. WalletConnect is used only when you paste a wc: link: then the extension connects to the WalletConnect relay (wss://relay.walletconnect.org), which sees encrypted messages and your IP address, and to verify.walletconnect.org to check the app that asks to connect. Analytics are turned off. The privacy policy for the stores is at raton.fun/wallet/secure. Your IP address and public address are visible to the node you use

## Optional, can be turned off in Settings, Network privacy, Optional connections

| Purpose | Service |
|---|---|
| News | raton.fun |
| Prediction markets | gamma-api.polymarket.com |
| Update check | github.com/sillycs/Sillet-Wallet/releases/latest/download/version.json |

## Only when you use the feature

| Purpose | Service |
|---|---|
| Swaps | exolix.com. Before you confirm, the app shows the provider's deposit address, the order id and the terms |
| Buy and sell | MoonPay, Ramp, Transak, Buda, Orionx open in your browser |

## Not used any more

Country flags are bundled with the app. Sillet no longer contacts any third party for them


## WalletConnect in the Windows and Android apps

Only after you paste or open a wc: link, the app connects to the WalletConnect relay (wss://relay.walletconnect.org), which sees encrypted messages and your IP address. This traffic does not use the proxy or Tor setting. Setup for builders: put a WalletConnect project id in `src/Sillet.App/Services/WalletConnectConfig.cs` (and in `extension/walletconnect.json` for the extension). Links: `sillet://wc?uri=...`, and `https://raton.fun/wallet/wc/?uri=...` on Android once `/.well-known/assetlinks.json` holds the APK fingerprint. On Linux run `linux/install.sh` to register the sillet:// handler

Networks over WalletConnect in the apps: the 11 EVM networks, Solana (mainnet), Bitcoin, Litecoin and Dogecoin. Solana: solana_signMessage, solana_signTransaction, solana_signAndSendTransaction, solana_signAllTransactions. Bitcoin style: getAccountAddresses, sendTransfer, signMessage (ecdsa). Not included: signPsbt, BIP 322, Bitcoin Cash, Tron, TON, Cosmos, Sui and XRP. The browser extension supports only the EVM networks
