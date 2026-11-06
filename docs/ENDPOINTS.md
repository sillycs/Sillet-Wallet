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

## Optional, can be turned off in Settings, Network privacy, Optional connections

| Purpose | Service |
|---|---|
| News | raton.fun |
| Prediction markets | gamma-api.polymarket.com |
| Update check | raton.fun/wallet/version.json |

## Only when you use the feature

| Purpose | Service |
|---|---|
| Swaps | exolix.com. Before you confirm, the app shows the provider's deposit address, the order id and the terms |
| Buy and sell | MoonPay, Ramp, Transak, Buda, Orionx open in your browser |

## Not used any more

Country flags are bundled with the app. Sillet no longer contacts any third party for them
