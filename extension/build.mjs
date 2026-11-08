// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
// Builds the extension for Chromium browsers (Chrome, Edge, Brave) and Firefox into dist/
import { build } from 'esbuild';
import { readFileSync, writeFileSync, mkdirSync, cpSync, rmSync, existsSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const pkg = JSON.parse(readFileSync(join(here, 'package.json'), 'utf8'));
const chains = JSON.parse(readFileSync(join(here, 'scripts/chains.json'), 'utf8'));
const testRpc = process.env.SILLET_TEST_RPC;
// WalletConnect needs a free project id from dashboard.walletconnect.com. Put it in walletconnect.json, or set SILLET_WC_PROJECT when building
const wcFile = JSON.parse(readFileSync(join(here, 'walletconnect.json'), 'utf8'));
const wcProject = testRpc ? '0123456789abcdef0123456789abcdef' : (process.env.SILLET_WC_PROJECT || wcFile.projectId || '');
const wcRelay = process.env.SILLET_TEST_WC_RELAY || ''; // only for the automated browser test
const icon = 'data:image/png;base64,' + readFileSync(join(here, 'icons/icon48.png')).toString('base64');

// The test build points every network at a local fake node
const swapChains = { name: 'chains', setup(b) { b.onResolve({ filter: /scripts\/chains\.json$/ }, () => ({ path: join(here, 'scripts/chains.build.json') })); } };


// Real coin and network pictures. They come from two open source packages and are copied into the build, so nothing is loaded from the internet
const W3 = join(here, 'node_modules/@web3icons/core/dist/svgs');
const CI = join(here, 'node_modules/cryptocurrency-icons/svg/color');
const CHAIN_ICON = { 1: 'ethereum', 42161: 'arbitrum-one', 10: 'optimism', 8453: 'base', 137: 'polygon', 56: 'binance-smart-chain', 43114: 'avalanche', 8217: 'kaia', 59144: 'linea', 81457: 'blast', 100: 'gnosis', bitcoin: 'bitcoin', litecoin: 'litecoin', dogecoin: 'dogecoin', bitcoincash: 'bitcoin-cash' };
// Wallet tokens plus the coins the Market tab usually lists. A coin with no picture gets a drawn disc
const TOKEN_ICONS = ['ETH', 'USDT', 'USDC', 'LINK', 'BNB', 'DAI', 'WBTC', 'AVAX', 'POL', 'FDUSD', 'UNI', 'ARB', 'OP', 'USDE', 'MATIC',
  'BTC', 'LTC', 'DOGE', 'BCH', 'XRP', 'SOL', 'ADA', 'DOT', 'TRX', 'XLM', 'XMR', 'ATOM', 'ETC', 'NEAR', 'FIL', 'ALGO', 'VET', 'ICP', 'AAVE', 'MKR', 'GRT', 'SAND', 'MANA', 'AXS',
  'THETA', 'EOS', 'XTZ', 'NEO', 'DASH', 'ZEC', 'IOTA', 'QTUM', 'ZIL', 'ENJ', 'BAT', 'COMP', 'SNX', 'YFI', 'CRV', 'SUSHI', 'ONE', 'HBAR', 'FTM', 'EGLD', 'FLOW', 'KSM', 'RUNE',
  'CAKE', 'LDO', 'APE', 'SHIB', 'STX', 'KAVA', 'WAVES', 'ZRX', 'LRC', 'CHZ', 'BNT', 'OMG', 'ANKR', 'CELO', 'RVN', 'XEM', 'DCR', 'BTG', 'STEEM', 'HIVE', 'OCEAN', 'REP', 'STORJ', 'SC', 'ONT', 'ICX', 'LSK', 'BTS', 'GNO', 'TUSD', 'USDP', 'BUSD', 'LEO', 'CRO', 'KCS', 'GAS'];
const TOKEN_FROM_CHAIN = { KAIA: 8217, XDAI: 100 };
function unpack(file) {
  const s = readFileSync(file, 'utf8');
  return s.slice(s.indexOf('<svg'), s.lastIndexOf('</svg>') + 6).replaceAll('\\n', '\n').replaceAll("\\'", "'");
}
function makeCoins() {
  const files = {};
  const have = { chains: [], tokens: [] };
  for (const [id, name] of Object.entries(CHAIN_ICON)) {
    const f = join(W3, 'networks/background', name + '.svg.js');
    if (existsSync(f)) { files['chain-' + id + '.svg'] = unpack(f); have.chains.push(/^\d+$/.test(id) ? Number(id) : id); }
  }
  for (const sym of TOKEN_ICONS) {
    const classic = join(CI, sym.toLowerCase() + '.svg');
    const modern = join(W3, 'tokens/background', sym + '.svg.js');
    if (existsSync(classic)) files['token-' + sym + '.svg'] = readFileSync(classic, 'utf8');
    else if (existsSync(modern)) files['token-' + sym + '.svg'] = unpack(modern);
    else continue;
    have.tokens.push(sym);
  }
  for (const [sym, id] of Object.entries(TOKEN_FROM_CHAIN)) { if (files['chain-' + id + '.svg']) { files['token-' + sym + '.svg'] = files['chain-' + id + '.svg']; have.tokens.push(sym); } }
  files['token-WLD.svg'] = readFileSync(join(here, 'static/coins-extra/WLD.svg'), 'utf8'); have.tokens.push('WLD');
  return { files, have };
}
const coins = makeCoins();
writeFileSync(join(here, 'scripts/coins.build.json'), JSON.stringify(coins.have));

const targets = testRpc ? ['test'] : ['chromium', 'firefox'];
if (testRpc) for (const c of chains) c.rpc = [testRpc];
writeFileSync(join(here, 'scripts/chains.build.json'), JSON.stringify(chains));

// Bitcoin, Litecoin and Dogecoin explorers and the optional CoinGecko prices
const extra = ['https://mempool.space/*', 'https://blockstream.info/*', 'https://litecoinspace.org/*', 'https://api.blockcypher.com/*', 'https://bch1.trezor.io/*', 'https://bch2.trezor.io/*', 'https://api.coingecko.com/*'];
const hosts = [...new Set([...chains.flatMap((c) => c.rpc).map((u) => new URL(u).origin + '/*'), ...extra])];
const scripts = ['http://*/*', 'https://*/*'];

function manifest(target) {
  const m = {
    manifest_version: 3,
    name: 'Sillet',
    short_name: 'Sillet',
    version: pkg.version,
    description: 'Sillet wallet for websites. Your keys stay encrypted on your device. Open source by the Raton team',
    homepage_url: 'https://raton.fun/wallet',
    icons: { 16: 'icons/icon16.png', 32: 'icons/icon32.png', 48: 'icons/icon48.png', 128: 'icons/icon128.png' },
    action: { default_title: 'Sillet', default_popup: 'popup.html', default_icon: { 16: 'icons/icon16.png', 32: 'icons/icon32.png' } },
    permissions: ['storage', 'alarms', 'scripting', 'contextMenus', 'activeTab'],
    commands: { scan_qr: { suggested_key: { default: 'Alt+Shift+Q' }, description: 'Scan the QR code on this page with Sillet' } },
    host_permissions: [...new Set([...hosts, ...scripts])],
    content_scripts: [
      { matches: scripts, js: ['inpage.js'], run_at: 'document_start', all_frames: true, world: 'MAIN' },
      { matches: scripts, js: ['content.js'], run_at: 'document_start', all_frames: true },
      { matches: scripts, js: ['scanner.js'], run_at: 'document_idle' },
    ],
  };
  if (target === 'firefox') {
    // Firefox only runs content scripts on sites that are also in host permissions
    m.host_permissions = [...new Set([...m.host_permissions, ...scripts])];
    m.background = { scripts: ['background.js'] };
    // Sillet sends nothing to its authors. Requests go to the public node of the network the person uses
    m.browser_specific_settings = { gecko_android: { strict_min_version: '142.0' }, gecko: { id: 'wallet@raton.fun', strict_min_version: '140.0', data_collection_permissions: { required: ['none'] } } };
  } else {
    m.background = { service_worker: 'background.js' };
    m.minimum_chrome_version = '111';
  }
  return m;
}

for (const target of targets) {
  const out = join(here, 'dist', target);
  rmSync(out, { recursive: true, force: true });
  mkdirSync(out, { recursive: true });
  const common = { bundle: true, format: 'iife', minify: !testRpc, define: { __SILLET_TEST_BASE__: JSON.stringify(testRpc || ''), __SILLET_WC_PROJECT__: JSON.stringify(wcProject), __SILLET_WC_RELAY__: JSON.stringify(wcRelay) }, target: ['chrome111', 'firefox128'], legalComments: 'none', logLevel: 'warning', plugins: [swapChains] };
  await build({ ...common, entryPoints: { background: 'src/background.js', content: 'src/content.js', scanner: 'src/scanner.js', popup: 'src/ui/popup.js' }, outdir: out });
  await build({ ...common, entryPoints: { inpage: 'src/inpage.js' }, outdir: out, define: { __SILLET_ICON__: JSON.stringify(icon) } });
  cpSync(join(here, 'static'), out, { recursive: true });
  rmSync(join(out, 'coins-extra'), { recursive: true, force: true });
  cpSync(join(here, 'icons'), join(out, 'icons'), { recursive: true });
  mkdirSync(join(out, 'coins'), { recursive: true });
  for (const [name, svg] of Object.entries(coins.files)) writeFileSync(join(out, 'coins', name), svg);
  writeFileSync(join(out, 'manifest.json'), JSON.stringify(manifest(target), null, 2));
  console.log('built', target);
}
rmSync(join(here, 'scripts/chains.build.json'), { force: true });
rmSync(join(here, 'scripts/coins.build.json'), { force: true });
