// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
// Loads the built extension in Chromium and drives it like a real dapp would.
// Run: node tests/e2e.cjs   (needs playwright with chromium installed)
const http = require('node:http');
const path = require('node:path');
const fs = require('node:fs');
const { execFileSync } = require('node:child_process');
const assert = require('node:assert/strict');
const { chromium } = require('playwright');
const os = require('node:os');
const { startRelay } = require('./wc-relay.cjs');
const qrcode = require('qrcode-generator');
const { Transaction, AbiCoder, verifyMessage, verifyTypedData, Interface } = require('ethers');

const root = path.join(__dirname, '..');
const SHOTS = process.env.SHOTS || '';
const RPC_PORT = 18545, DAPP_PORT = 18080, RELAY_PORT = 18546;
const JUNK = 'test test test test test test test test test test test junk';
const ACCOUNT = '0xf39Fd6e51aad88F6F4ce6aB8827279cffFb92266';
const USDT = '0xdAC17F958D2ee523a2206206994597C13D831ec7';
const rawTxs = [];
const btcTxs = [];
const bchTxs = [];
const FAKE_TXID = 'f'.repeat(64);

// Fake Bitcoin explorer and CoinGecko, so the test never touches the internet
function mockExplorer(req, body, res) {
  const u = new URL(req.url, 'http://x');
  const p = u.pathname;
  const json = (o) => { res.setHeader('content-type', 'application/json'); res.end(JSON.stringify(o)); return true; };
  if (p.startsWith('/esplora')) {
    const r = p.slice('/esplora'.length);
    if (r === '/v1/fees/recommended') return json({ fastestFee: 10, halfHourFee: 5, hourFee: 2 });
    if (r === '/tx' && req.method === 'POST') { btcTxs.push(body); res.end(FAKE_TXID); return true; }
    if (/^\/tx\/.+\/status$/.test(r)) return json({ confirmed: false });
    if (/^\/address\/[^/]+\/utxo$/.test(r)) return json([{ txid: 'a'.repeat(64), vout: 0, value: 200000000, status: { confirmed: true } }]);
    if (/^\/address\/[^/]+\/txs$/.test(r)) {
      const a = r.split('/')[2];
      return json([{ txid: 'b'.repeat(64), status: { confirmed: true, block_time: 1700000000 }, vin: [{ prevout: { scriptpubkey_address: 'bc1qw508d6qejxtdg4y5r3zarvary0c5xw7kv8f3t4', value: 250000000 } }], vout: [{ scriptpubkey_address: a, value: 250000000 }] }]);
    }
    if (/^\/address\/[^/]+$/.test(r)) return json({ chain_stats: { funded_txo_sum: 250000000, spent_txo_sum: 50000000 }, mempool_stats: { funded_txo_sum: 0, spent_txo_sum: 0 } });
  }
  if (p.startsWith('/blockbook')) {
    const r = p.slice('/blockbook'.length);
    if (r.startsWith('/address/')) return json({ balance: '123456789', unconfirmedBalance: '0', transactions: [{ txid: 'c'.repeat(64), blockTime: 1700000000, confirmations: 5, vin: [{ addresses: ['qpm2qsznhks23z7629mms6s4cwef74vcwvy22gdx6a'], value: '100000000' }], vout: [{ addresses: [decodeURIComponent(r.split('/')[2])], value: '100000000' }] }] });
    if (r.startsWith('/utxo/')) return json([{ txid: 'a'.repeat(64), vout: 0, value: '123456789', confirmations: 3 }]);
    if (r.startsWith('/sendtx/')) { bchTxs.push(r.slice(8)); return json({ result: FAKE_TXID }); }
  }
  if (p.startsWith('/cg/simple/token_price/')) return json({});
  if (p === '/cg/simple/price') return json({ bitcoin: { usd: 60000, usd_24h_change: 1.5 }, ethereum: { usd: 3000, usd_24h_change: -2 } });
  if (p === '/cg/coins/markets') {
    const line = Array.from({ length: 60 }, (_, i) => 100 + Math.sin(i / 5) * 10 + i / 3);
    const mk = (id, symbol, name, rank, price, c24) => ({ id, symbol, name, market_cap_rank: rank, current_price: price, market_cap: price * 1e7, total_volume: price * 1e6, high_24h: price * 1.02, low_24h: price * 0.97, ath: price * 2, price_change_percentage_24h: c24, price_change_percentage_7d_in_currency: c24 * 2, sparkline_in_7d: { price: line } });
    return json([mk('bitcoin', 'btc', 'Bitcoin', 1, 60000, 1.5), mk('ethereum', 'eth', 'Ethereum', 2, 3000, -2), mk('solana', 'sol', 'Solana', 5, 150, 6.2), mk('dogecoin', 'doge', 'Dogecoin', 9, 0.1234, -4.1)]);
  }
  return false;
}

function startRpc() {
  const abi = AbiCoder.defaultAbiCoder();
  return new Promise((resolve) => {
    const srv = http.createServer((req, res) => {
      let body = '';
      req.on('data', (c) => (body += c));
      req.on('end', () => {
        res.setHeader('access-control-allow-origin', '*');
        res.setHeader('access-control-allow-headers', '*');
        if (req.method === 'OPTIONS') { res.end(); return; }
        if (mockExplorer(req, body, res)) return;
        if (!body) { res.statusCode = 404; res.end('not found: ' + req.url); return; }
        const { id, method, params } = JSON.parse(body);
        let result = null;
        switch (method) {
          case 'eth_chainId': result = '0x1'; break;
          case 'eth_blockNumber': result = '0x10'; break;
          case 'eth_getBalance': result = '0x' + (5n * 10n ** 18n).toString(16); break;
          case 'eth_getCode': result = '0x'; break;
          case 'eth_estimateGas': result = '0x5208'; break;
          case 'eth_gasPrice': result = '0x3b9aca00'; break;
          case 'eth_maxPriorityFeePerGas': result = '0x3b9aca00'; break;
          case 'eth_getTransactionCount': result = '0x3'; break;
          case 'eth_getBlockByNumber': result = { number: '0x10', baseFeePerGas: '0x3b9aca00' }; break;
          case 'eth_getTransactionReceipt': result = null; break;
          case 'eth_call': {
            const sel = String(params[0].data).slice(0, 10);
            if (sel === '0x70a08231') result = abi.encode(['uint256'], [12_340_000n]);
            else if (sel === '0x313ce567') result = abi.encode(['uint8'], [6]);
            else if (sel === '0x95d89b41') result = abi.encode(['string'], ['USDT']);
            else result = '0x';
            break;
          }
          case 'eth_sendRawTransaction': rawTxs.push(params[0]); result = Transaction.from(params[0]).hash; break;
          default: res.end(JSON.stringify({ jsonrpc: '2.0', id, error: { code: -32601, message: 'no ' + method } })); return;
        }
        res.end(JSON.stringify({ jsonrpc: '2.0', id, result }));
      });
    }).listen(RPC_PORT, '127.0.0.1', () => resolve(srv));
  });
}

function startDapp() {
  return new Promise((resolve) => {
    const srv = http.createServer((req, res) => {
      res.setHeader('content-type', 'text/html');
      if (req.url === '/pay-test') res.end('<!doctype html><title>Shop</title><h1>Checkout</h1><p>Send exactly 0.002 BTC to complete your payment</p><p>Wallet address: <code>bc1qw508d6qejxtdg4y5r3zarvary0c5xw7kv8f3t4</code></p>');
      else if (req.url === '/plain-test') res.end('<!doctype html><title>Blog</title><p>Just words. Address-like text 1BpEi6DfDAUFd7GtittLSdBeYJvcoaVggX is not valid. No payment here</p>');
      else if (req.url === '/link-test') res.end('<!doctype html><title>Donate</title><a id="d" href="bitcoin:bc1qw508d6qejxtdg4y5r3zarvary0c5xw7kv8f3t4?amount=0.01&label=Tip">Donate</a>');
      else res.end('<!doctype html><title>Test dapp</title><h1>dapp</h1>');
    })
      .listen(DAPP_PORT, '127.0.0.1', () => resolve(srv));
  });
}

// The approval window closes itself after the click, so a closed page is the expected outcome
async function press(page, name) {
  try { await page.getByRole('button', { name, exact: true }).click({ noWaitAfter: true, timeout: 10000 }); } catch (e) { if (!/closed/i.test(String(e.message))) throw e; }
}

const ok = (m) => console.log('  ✓ ' + m);

(async () => {
  execFileSync('node', ['build.mjs'], { cwd: root, env: { ...process.env, SILLET_TEST_RPC: `http://127.0.0.1:${RPC_PORT}`, SILLET_TEST_WC_RELAY: `ws://127.0.0.1:${RPC_PORT + 1}` }, stdio: 'inherit' });
  const rpc = await startRpc();
  const dappSrv = await startDapp();
  const relay = await startRelay(RELAY_PORT);
  const ext = path.join(root, 'dist', 'test');
  const ctx = await chromium.launchPersistentContext('', {
    channel: 'chromium', headless: true,
    args: [`--disable-extensions-except=${ext}`, `--load-extension=${ext}`],
  });
  try {
    let [sw] = ctx.serviceWorkers();
    if (!sw) sw = await ctx.waitForEvent('serviceworker');
    const id = new URL(sw.url()).host;
    ok('extension loaded: ' + id);

    // --- onboarding by importing a known phrase
    const ob = await ctx.newPage();
    await ob.goto(`chrome-extension://${id}/popup.html#/onboard/import`);
    await ob.locator('textarea').fill(JUNK);
    await ob.getByRole('button', { name: 'Continue' }).click();
    await ob.locator('input[type=password]').nth(0).fill('sillet-test-pass');
    await ob.locator('input[type=password]').nth(1).fill('sillet-test-pass');
    await ob.getByRole('button', { name: 'Continue' }).click();
    await ob.getByText('Your wallet is ready').waitFor();
    ok('wallet imported');

    // --- home screen
    const home = await ctx.newPage();
    await home.setViewportSize({ width: 380, height: 600 });
    await home.goto(`chrome-extension://${id}/popup.html#/`);
    await home.locator('.account-btn').waitFor();
    await home.getByText('5', { exact: true }).waitFor({ timeout: 10000 });
    assert.ok((await home.locator('.token').allInnerTexts()).join().includes('ETH'));
    if (SHOTS) {
      await home.screenshot({ path: path.join(SHOTS, 'home.png') });
      for (const [name, hash] of [['settings', '/settings'], ['sites', '/sites'], ['activity', '/activity'], ['receive', '/receive'], ['send', '/send'], ['contacts', '/contacts']]) {
        await home.evaluate((x) => { location.hash = x; }, hash);
        await home.waitForTimeout(500);
        await home.screenshot({ path: path.join(SHOTS, name + '.png') });
      }
      await home.evaluate(() => { location.hash = '/'; });
      await home.locator('.account-btn').click();
      await home.waitForTimeout(300);
      await home.screenshot({ path: path.join(SHOTS, 'sheet.png') });
      await home.evaluate(() => { location.hash = '/'; location.reload(); });
      await home.locator('.account-btn').waitFor();
    }
    ok('home shows account and ETH balance (5)');

    // --- dapp
    const dapp = await ctx.newPage();
    await dapp.goto(`http://127.0.0.1:${DAPP_PORT}/`);
    const announced = await dapp.evaluate(() => new Promise((res) => {
      const got = [];
      window.addEventListener('eip6963:announceProvider', (e) => got.push(e.detail.info.rdns));
      window.dispatchEvent(new Event('eip6963:requestProvider'));
      setTimeout(() => res(got), 300);
    }));
    assert.ok(announced.includes('fun.raton.sillet'));
    assert.equal(await dapp.evaluate(() => window.ethereum.isSillet), true);
    assert.equal(await dapp.evaluate(() => window.ethereum.request({ method: 'eth_chainId' })), '0x1');
    assert.deepEqual(await dapp.evaluate(() => window.ethereum.request({ method: 'eth_accounts' })), []);
    assert.equal(await dapp.evaluate(() => window.ethereum.request({ method: 'eth_blockNumber' })), '0x10');
    ok('EIP-6963 announce, window.ethereum, chainId, no accounts before connecting, reads forwarded');

    await dapp.evaluate(() => {
      window.__events = [];
      window.ethereum.on('accountsChanged', (a) => window.__events.push(['accounts', a]));
      window.ethereum.on('chainChanged', (c) => window.__events.push(['chain', c]));
    });
    const call = async (fn, arg) => {
      await dapp.evaluate(([f, a]) => { window.__out = undefined; window.__err = undefined; new Function('a', 'return ' + f)(a).then((r) => (window.__out = r === undefined ? null : r), (e) => (window.__err = { code: e.code, message: e.message })); }, [fn, arg]);
    };
    const result = async () => { await dapp.waitForFunction(() => window.__out !== undefined || window.__err !== undefined, null, { timeout: 15000 }); return dapp.evaluate(() => ({ out: window.__out, err: window.__err })); };
    const approval = async (fn, arg) => {
      const [win] = await Promise.all([ctx.waitForEvent('page', { timeout: 15000 }), call(fn, arg)]);
      await win.waitForLoadState('domcontentloaded');
      await win.getByRole('heading', { level: 2 }).first().waitFor();
      return win;
    };

    // --- connect
    let win = await approval("window.ethereum.request({method:'eth_requestAccounts'})");
    assert.ok((await win.locator('body').innerText()).includes('127.0.0.1:' + DAPP_PORT));
    if (SHOTS) await win.screenshot({ path: path.join(SHOTS, 'connect.png') });
    await press(win, 'Connect');
    let r = await result();
    assert.deepEqual(r.out, [ACCOUNT]);
    assert.deepEqual(await dapp.evaluate(() => window.ethereum.request({ method: 'eth_accounts' })), [ACCOUNT]);
    ok('connect approved and account returned');

    // --- personal_sign
    const msg = 'Sign in to Test dapp\nNonce: 12345';
    win = await approval("window.ethereum.request({method:'personal_sign',params:[a, '" + ACCOUNT + "']})", '0x' + Buffer.from(msg).toString('hex'));
    assert.ok((await win.locator('.msg').innerText()).includes('Nonce: 12345'));
    if (SHOTS) await win.screenshot({ path: path.join(SHOTS, 'sign.png') });
    await press(win, 'Sign');
    r = await result();
    assert.equal(verifyMessage(msg, r.out), ACCOUNT);
    ok('personal_sign signature recovers to the account');

    // --- typed data v4
    const typed = {
      types: { EIP712Domain: [{ name: 'name', type: 'string' }, { name: 'chainId', type: 'uint256' }], Mail: [{ name: 'to', type: 'address' }, { name: 'text', type: 'string' }] },
      primaryType: 'Mail', domain: { name: 'Test', chainId: 1 }, message: { to: USDT, text: 'hello' },
    };
    win = await approval("window.ethereum.request({method:'eth_signTypedData_v4',params:['" + ACCOUNT + "', JSON.stringify(a)]})", typed);
    await press(win, 'Sign');
    r = await result();
    assert.equal(verifyTypedData({ name: 'Test', chainId: 1 }, { Mail: typed.types.Mail }, typed.message, r.out), ACCOUNT);
    ok('eth_signTypedData_v4 signature recovers to the account');

    // --- permit warning and rejection
    const permit = { types: { EIP712Domain: [{ name: 'chainId', type: 'uint256' }], Permit: [{ name: 'spender', type: 'address' }, { name: 'value', type: 'uint256' }] }, primaryType: 'Permit', domain: { chainId: 1 }, message: { spender: USDT, value: '1' } };
    win = await approval("window.ethereum.request({method:'eth_signTypedData_v4',params:['" + ACCOUNT + "', JSON.stringify(a)]})", permit);
    assert.ok((await win.locator('body').innerText()).includes('without a transaction'));
    await press(win, 'Cancel');
    r = await result();
    assert.equal(r.err.code, 4001);
    ok('permit signature warns and rejection gives 4001');

    // wrong chain in typed data
    await call("window.ethereum.request({method:'eth_signTypedData_v4',params:['" + ACCOUNT + "', JSON.stringify(a)]})", { ...typed, domain: { name: 'Test', chainId: 137 } });
    r = await result();
    assert.equal(r.err.code, -32602);
    ok('typed data for another chain is refused');

    // --- send transaction
    const to = '0x70997970C51812dc3A010C7d01b50e0d17dc79C8';
    win = await approval("window.ethereum.request({method:'eth_sendTransaction',params:[a]})", { from: ACCOUNT, to, value: '0xde0b6b3a7640000' });
    const txt = await win.locator('body').innerText();
    assert.ok(txt.includes(to) && txt.includes('1 ETH'));
    if (SHOTS) await win.screenshot({ path: path.join(SHOTS, 'tx.png') });
    await press(win, 'Fast');
    await press(win, 'Confirm');
    r = await result();
    assert.equal(rawTxs.length, 1);
    const tx = Transaction.from(rawTxs[0]);
    assert.equal(r.out, tx.hash);
    assert.equal(tx.from, ACCOUNT); assert.equal(tx.to, to); assert.equal(tx.value, 10n ** 18n); assert.equal(tx.chainId, 1n); assert.equal(tx.nonce, 3);
    ok('eth_sendTransaction signed and broadcast: ' + tx.hash.slice(0, 12) + '…');

    // --- unlimited approval warning
    const iface = new Interface(['function approve(address,uint256)']);
    const data = iface.encodeFunctionData('approve', [to, (1n << 256n) - 1n]);
    win = await approval("window.ethereum.request({method:'eth_sendTransaction',params:[a]})", { from: ACCOUNT, to: USDT, data });
    const t2 = await win.locator('body').innerText();
    assert.ok(t2.includes('ALL your USDT'), t2);
    assert.ok(t2.includes('spend ALL of this token'));
    assert.ok(t2.includes('normal wallet, not a contract'));
    if (SHOTS) await win.screenshot({ path: path.join(SHOTS, 'approve.png') });
    await press(win, 'Cancel');
    r = await result();
    assert.equal(r.err.code, 4001);
    assert.equal(rawTxs.length, 1);
    ok('unlimited approval is explained and can be rejected');

    // --- chain switching
    await call("window.ethereum.request({method:'wallet_switchEthereumChain',params:[{chainId:'0x5'}]})");
    r = await result();
    assert.equal(r.err.code, 4902);
    win = await approval("window.ethereum.request({method:'wallet_switchEthereumChain',params:[{chainId:'0x89'}]})");
    await press(win, 'Switch');
    await result();
    assert.equal(await dapp.evaluate(() => window.ethereum.request({ method: 'eth_chainId' })), '0x89');
    await dapp.waitForFunction(() => window.__events.some((e) => e[0] === 'chain' && e[1] === '0x89'));
    ok('switch to unknown chain gives 4902; known chain switches and chainChanged fires');

    // --- other origin has no access
    const other = await ctx.newPage();
    await other.goto(`http://localhost:${DAPP_PORT}/`);
    assert.deepEqual(await other.evaluate(() => window.ethereum.request({ method: 'eth_accounts' })), []);
    const err = await other.evaluate(() => window.ethereum.request({ method: 'eth_sendTransaction', params: [{ from: '0xf39Fd6e51aad88F6F4ce6aB8827279cffFb92266', to: '0x70997970C51812dc3A010C7d01b50e0d17dc79C8', value: '0x1' }] }).then(() => null, (e) => e.code));
    assert.equal(err, 4100);
    const eth_sign = await dapp.evaluate(() => window.ethereum.request({ method: 'eth_sign', params: ['0xf39Fd6e51aad88F6F4ce6aB8827279cffFb92266', '0x00'] }).then(() => null, (e) => e.code));
    assert.equal(eth_sign, 4200);
    ok('another site cannot use the account; eth_sign is blocked');

    // --- a web page cannot drive the wallet UI actions
    const spoof = await dapp.evaluate(() => window.ethereum.request({ method: 'ui' }).then(() => 'ok', (e) => e.code));
    assert.equal(spoof, 4200);
    ok('pages cannot reach internal wallet actions');

    // --- lock hides accounts
    const popup = await ctx.newPage();
    await popup.goto(`chrome-extension://${id}/popup.html#/`);
    await popup.getByTitle('Lock').click();
    await popup.getByRole('button', { name: 'Unlock' }).waitFor();
    if (SHOTS) await popup.screenshot({ path: path.join(SHOTS, 'unlock.png') });
    assert.deepEqual(await dapp.evaluate(() => window.ethereum.request({ method: 'eth_accounts' })), []);
    await dapp.waitForFunction(() => window.__events.some((e) => e[0] === 'accounts' && e[1].length === 0));
    ok('locking hides the account from sites and fires accountsChanged');
    await popup.getByPlaceholder('Password').fill('wrong password');
    await popup.getByRole('button', { name: 'Unlock' }).click();
    await popup.getByText('Wrong password').waitFor();
    await popup.getByPlaceholder('Password').fill('sillet-test-pass');
    await popup.getByRole('button', { name: 'Unlock' }).click();
    await popup.locator('.account-btn').waitFor();
    ok('wrong password refused, right password unlocks');

    // --- contacts, private mode, CSV
    await popup.goto(`chrome-extension://${id}/popup.html#/contacts`);
    await popup.getByPlaceholder('Name').fill('Alice');
    await popup.locator('input[placeholder="0x…"]').fill(USDT);
    await popup.getByRole('button', { name: 'Save contact' }).click();
    await popup.getByText('Alice', { exact: true }).waitFor();
    await popup.locator('input[placeholder="0x…"]').fill('not an address');
    await popup.getByPlaceholder('Name').fill('Bad');
    await popup.getByRole('button', { name: 'Save contact' }).click();
    await popup.getByText('That is not a valid address').waitFor();
    ok('contacts: saved with a name, bad address refused');
    await popup.goto(`chrome-extension://${id}/popup.html#/send/${USDT}`);
    await popup.getByText('✓ Alice').waitFor();
    assert.equal(await popup.locator('input[placeholder="0x…"]').inputValue(), USDT);
    ok('send opens with the contact filled in and named');
    await popup.goto(`chrome-extension://${id}/popup.html#/`);
    await popup.locator('.eye').click();
    await popup.getByText('••••').first().waitFor();
    assert.ok(!(await popup.locator('.token').allInnerTexts()).join().includes('12.34'));
    await popup.locator('.eye').click();
    await popup.getByText('12.34', { exact: true }).first().waitFor();
    ok('private mode hides and shows balances');
    await popup.goto(`chrome-extension://${id}/popup.html#/activity`);
    const [dl] = await Promise.all([popup.waitForEvent('download'), popup.getByRole('button', { name: 'Export CSV' }).click()]);
    const csv = fs.readFileSync(await dl.path(), 'utf8');
    assert.ok(csv.includes('Transaction') && csv.includes('0x'));
    ok('activity exports to CSV');

    // --- send from the Sillet window
    await popup.goto(`chrome-extension://${id}/popup.html#/send`);
    await popup.locator('input[placeholder="0x…"]').fill(to);
    await popup.locator('input[placeholder="0.0"]').fill('0.5');
    await popup.getByRole('button', { name: 'Review' }).click();
    await popup.getByRole('button', { name: 'Confirm and send' }).click();
    await popup.getByText('Transaction sent').waitFor();
    assert.equal(rawTxs.length, 2);
    const t3 = Transaction.from(rawTxs[1]);
    assert.equal(t3.value, 5n * 10n ** 17n); assert.equal(t3.chainId, 1n);
    ok('send from the wallet window uses the wallet network (Ethereum), not the site network (Polygon)');

    // --- Bitcoin, Litecoin and Dogecoin in the wallet window
    await popup.goto(`chrome-extension://${id}/popup.html#/`);
    if (SHOTS) await popup.setViewportSize({ width: 380, height: 600 });
    await popup.locator('.network-btn').click();
    await popup.getByText('Bitcoin and more').waitFor();
    if (SHOTS) { await popup.waitForTimeout(400); await popup.screenshot({ path: path.join(SHOTS, 'networks.png') }); }
    for (const n of ['Bitcoin', 'Litecoin', 'Dogecoin', 'Bitcoin Cash']) await popup.locator('.sheet .item .name', { hasText: new RegExp('^' + n + '$') }).waitFor();
    await popup.locator('.sheet .item .name', { hasText: /^Bitcoin$/ }).click();
    await popup.getByText('2 BTC').first().waitFor({ timeout: 10000 });
    if (SHOTS) { await popup.waitForTimeout(900); await popup.screenshot({ path: path.join(SHOTS, 'btc-home.png') }); }
    ok('Bitcoin selected: balance from the explorer (2 BTC)');
    await popup.goto(`chrome-extension://${id}/popup.html#/receive`);
    await popup.locator('.addrbox').waitFor();
    const btcAddr = await popup.locator('.addrbox').innerText();
    assert.match(btcAddr, /^bc1q[a-z0-9]{38}$/);
    assert.equal(btcAddr, 'bc1q4qw42stdzjqs59xvlrlxr8526e3nunw7mp73te');
    ok('receive shows a Bitcoin address: ' + btcAddr);
    await popup.goto(`chrome-extension://${id}/popup.html#/send`);
    await popup.locator('input[placeholder="bc1…"]').fill('bc1qw508d6qejxtdg4y5r3zarvary0c5xw7kv8f3t5');
    await popup.locator('input[placeholder="0.0"]').fill('0.5');
    await popup.getByRole('button', { name: 'Review' }).click();
    await popup.getByText('That is not a valid Bitcoin address').waitFor();
    ok('Bitcoin send refuses an address with a bad checksum');
    await popup.locator('input[placeholder="bc1…"]').fill('bc1qw508d6qejxtdg4y5r3zarvary0c5xw7kv8f3t4');
    await popup.getByRole('button', { name: 'Review' }).click();
    await popup.getByRole('button', { name: 'Confirm and send' }).waitFor();
    await popup.getByRole('button', { name: 'Fast', exact: true }).click();
    if (SHOTS) { await popup.waitForTimeout(500); await popup.screenshot({ path: path.join(SHOTS, 'btc-confirm.png') }); }
    await popup.getByRole('button', { name: 'Confirm and send' }).click();
    await popup.getByText('Transaction sent').waitFor();
    assert.equal(btcTxs.length, 1);
    assert.match(btcTxs[0], /^02000000000101a{64}00000000/);
    assert.ok(btcTxs[0].length > 200 && btcTxs[0].endsWith('00000000'));
    ok('Bitcoin payment signed and sent to the network');
    await popup.goto(`chrome-extension://${id}/popup.html#/activity`);
    await popup.getByText('Received 2.5 BTC').waitFor();
    ok('Bitcoin activity comes from the explorer');
    await popup.locator('.network-btn').click();
    await popup.locator('.sheet .item .name', { hasText: /^Ethereum$/ }).click();
    await popup.waitForTimeout(300);
    const siteChain = await dapp.evaluate(() => window.ethereum.request({ method: 'eth_chainId' }));
    assert.ok(/^0x[0-9a-f]+$/.test(siteChain));
    ok('websites never see Bitcoin, they keep an EVM chain id (' + siteChain + ')');

    // --- Bitcoin Cash
    await popup.goto(`chrome-extension://${id}/popup.html#/`);
    await popup.locator('.network-btn').click();
    await popup.locator('.sheet .item .name', { hasText: /^Bitcoin Cash$/ }).click();
    await popup.getByText('1.23456789 BCH').first().waitFor({ timeout: 10000 });
    await popup.goto(`chrome-extension://${id}/popup.html#/receive`);
    await popup.locator('.addrbox').waitFor();
    assert.match(await popup.locator('.addrbox').innerText(), /^bitcoincash:q[a-z0-9]{41}$/);
    await popup.goto(`chrome-extension://${id}/popup.html#/send`);
    await popup.locator('input[placeholder="bitcoincash:q…"]').fill('1BpEi6DfDAUFd7GtittLSdBeYJvcoaVggu');
    await popup.locator('input[placeholder="0.0"]').fill('0.5');
    await popup.getByRole('button', { name: 'Review' }).click();
    await popup.getByRole('button', { name: 'Confirm and send' }).click();
    await popup.getByText('Transaction sent').waitFor();
    assert.equal(bchTxs.length, 1);
    assert.ok(bchTxs[0].startsWith('01000000') && bchTxs[0].includes('41'));
    await popup.goto(`chrome-extension://${id}/popup.html#/activity`);
    await popup.getByText('Received 1 BCH').waitFor();
    await popup.locator('.network-btn').click();
    await popup.locator('.sheet .item .name', { hasText: /^Ethereum$/ }).click();
    ok('Bitcoin Cash: balance, cashaddr receive, send to a legacy address, activity');

    // --- Market
    await popup.goto(`chrome-extension://${id}/popup.html#/market`);
    await popup.getByRole('button', { name: 'Turn on' }).waitFor();
    await popup.getByRole('button', { name: 'Turn on' }).click();
    await popup.getByRole('button', { name: 'Turn on' }).last().click();
    await popup.locator('.item.mk').first().waitFor({ timeout: 10000 });
    assert.equal(await popup.locator('.item.mk').count(), 4);
    await popup.getByRole('button', { name: 'Gainers' }).click();
    assert.equal((await popup.locator('.item.mk .sym').first().innerText()).trim(), 'SOL');
    await popup.getByPlaceholder('Search coins').fill('doge');
    assert.equal(await popup.locator('.item.mk').count(), 1);
    if (SHOTS) { await popup.setViewportSize({ width: 380, height: 600 }); await popup.getByPlaceholder('Search coins').fill(''); await popup.getByRole('button', { name: 'Top' }).click(); await popup.waitForTimeout(900); await popup.screenshot({ path: path.join(SHOTS, 'market.png') }); }
    await popup.getByPlaceholder('Search coins').fill('doge');
    await popup.locator('.item.mk').first().click();
    await popup.getByText('Market cap').waitFor();
    if (SHOTS) { await popup.waitForTimeout(900); await popup.screenshot({ path: path.join(SHOTS, 'coin.png') }); }
    await popup.locator('.coinhead .iconbtn').click();
    await popup.getByRole('button', { name: 'Starred' }).click();
    await popup.getByPlaceholder('Search coins').fill('');
    await popup.getByRole('button', { name: 'Starred' }).click();
    await popup.locator('.item.mk').first().waitFor();
    assert.equal(await popup.locator('.item.mk').count(), 1);
    ok('market: list, gainers, search, coin details and favorites');

    // --- payment pages, links, QR results
    const shop = await ctx.newPage();
    await shop.goto(`http://127.0.0.1:${DAPP_PORT}/pay-test`);
    await shop.locator('sillet-pay .pill').waitFor({ timeout: 15000 });
    await shop.locator('sillet-pay .pill').click();
    await shop.locator('sillet-pay .card').getByText('bc1qw508', { exact: false }).waitFor();
    const [payWin] = await Promise.all([ctx.waitForEvent('page'), shop.locator('sillet-pay .card').getByRole('button', { name: 'Pay with Sillet' }).click()]);
    await payWin.locator('input[placeholder="bc1…"]').waitFor({ timeout: 10000 });
    assert.equal(await payWin.locator('input[placeholder="bc1…"]').inputValue(), 'bc1qw508d6qejxtdg4y5r3zarvary0c5xw7kv8f3t4');
    await payWin.getByText('This payment came from 127.0.0.1:18080').waitFor();
    ok('payment page detected, "Pay with Sillet" opens Send on Bitcoin with the address filled in');
    await payWin.close();
    const blog = await ctx.newPage();
    await blog.goto(`http://127.0.0.1:${DAPP_PORT}/plain-test`);
    await blog.waitForTimeout(3500);
    assert.equal(await blog.locator('sillet-pay').count(), 0);
    ok('an ordinary page with a fake address shows nothing');
    const don = await ctx.newPage();
    await don.goto(`http://127.0.0.1:${DAPP_PORT}/link-test`);
    await don.locator('sillet-pay .pill').waitFor({ timeout: 15000 });
    const [linkWin] = await Promise.all([ctx.waitForEvent('page'), don.locator('#d').click()]);
    await linkWin.locator('input[placeholder="bc1…"]').waitFor({ timeout: 10000 });
    assert.equal(await linkWin.locator('input[placeholder="0.0"]').inputValue(), '0.01');
    await linkWin.getByText('Tip').waitFor();
    ok('a bitcoin: link opens Sillet with the amount and label');
    await linkWin.close();
    await popup.goto(`chrome-extension://${id}/popup.html#/pay?net=1&to=0x70997970C51812dc3A010C7d01b50e0d17dc79C8&token=${USDT}&raw=2500000&from=https%3A%2F%2Fshop.example`);
    await popup.locator('input[placeholder="0.0"]').waitFor();
    assert.equal(await popup.locator('input[placeholder="0.0"]').inputValue(), '2.5');
    await popup.getByText('USDT', { exact: true }).first().waitFor();
    await popup.getByText('This payment came from shop.example').waitFor();
    if (SHOTS) { await popup.setViewportSize({ width: 380, height: 640 }); await popup.waitForTimeout(500); await popup.screenshot({ path: path.join(SHOTS, 'pay.png') }); }
    ok('an ethereum: token link fills in the token, the amount and the sender');
    await popup.goto(`chrome-extension://${id}/popup.html#/scan?text=hello%20world`);
    await popup.getByText('This QR code is not a crypto address or payment link').first().waitFor();
    await popup.goto(`chrome-extension://${id}/popup.html#/scan?none=1`);
    await popup.getByText('No QR code found').first().waitFor();
    if (SHOTS) { await popup.waitForTimeout(400); await popup.screenshot({ path: path.join(SHOTS, 'noqr.png') }); }
    ok('QR results: unknown text and no QR are explained');
    if (!(await shop.locator('sillet-pay .card').count())) await shop.locator('sillet-pay .pill').click();
    await shop.locator('sillet-pay .card').getByRole('button', { name: 'Hide on this site' }).click();
    await shop.reload();
    await shop.waitForTimeout(3500);
    assert.equal(await shop.locator('sillet-pay').count(), 0);
    ok('"Hide on this site" is remembered');
    // QR picture decoded by the extension itself (what the right click "Scan QR" does)
    const payload = 'ethereum:0x70997970C51812dc3A010C7d01b50e0d17dc79C8@1?value=1e18';
    const qr = qrcode(0, 'M'); qr.addData(payload); qr.make();
    const mods = Array.from({ length: qr.getModuleCount() }, (_, r) => Array.from({ length: qr.getModuleCount() }, (_, c) => qr.isDark(r, c)));
    const dataUrl = await popup.evaluate((m) => {
      const sc = 5; const pad = 4; const size = (m.length + pad * 2) * sc;
      const cv = document.createElement('canvas'); cv.width = cv.height = size;
      const cx = cv.getContext('2d'); cx.fillStyle = '#fff'; cx.fillRect(0, 0, size, size); cx.fillStyle = '#000';
      m.forEach((row, r) => row.forEach((d, c) => { if (d) cx.fillRect((c + pad) * sc, (r + pad) * sc, sc, sc); }));
      return cv.toDataURL('image/png');
    }, mods);
    const decoded = await popup.evaluate((u) => chrome.runtime.sendMessage({ type: 'test-scan-image', url: u }), dataUrl);
    assert.equal(decoded.result, payload);
    const blank = await popup.evaluate(() => { const cv = document.createElement('canvas'); cv.width = cv.height = 200; return cv.toDataURL('image/png'); });
    assert.equal((await popup.evaluate((u) => chrome.runtime.sendMessage({ type: 'test-scan-image', url: u }), blank)).result, null);
    ok('the extension reads a QR picture in a real browser, and a blank picture gives nothing');

    // --- WalletConnect: a dapp that talks to Sillet through a (fake) relay
    process.chdir(fs.mkdtempSync(path.join(os.tmpdir(), 'wc-dapp-')));
    const { SignClient } = require(path.join(root, 'node_modules/@walletconnect/sign-client'));
    const wcDapp = await SignClient.init({ projectId: '0123456789abcdef0123456789abcdef', relayUrl: `ws://127.0.0.1:${RELAY_PORT}`, metadata: { name: 'Test Swap', description: 'test', url: `http://127.0.0.1:${DAPP_PORT}`, icons: [] } });
    const { uri, approval: wcApproval } = await wcDapp.connect({ requiredNamespaces: { eip155: { methods: ['personal_sign', 'eth_sendTransaction'], chains: ['eip155:1'], events: ['chainChanged'] } } });
    assert.match(uri, /^wc:[0-9a-f]{64}@2\?/);
    const wcPage = await ctx.newPage();
    await wcPage.setViewportSize({ width: 380, height: 640 });
    await wcPage.goto(`chrome-extension://${id}/popup.html#/wc?uri=${encodeURIComponent(uri)}`);
    const [wcWin] = await Promise.all([ctx.waitForEvent('page', { timeout: 30000 }), wcPage.getByRole('button', { name: 'Connect', exact: true }).click()]).catch(async (e) => { console.log('PAGE TEXT:', await wcPage.locator('body').innerText().catch(() => '?')); for (const w of ctx.serviceWorkers()) console.log('SW', w.url()); throw e; });
    await wcWin.waitForLoadState('domcontentloaded');
    await wcWin.getByRole('heading', { level: 2 }).first().waitFor();
    assert.ok((await wcWin.locator('body').innerText()).includes('Test Swap'));
    await press(wcWin, 'Connect');
    const wcSession = await wcApproval();
    assert.equal(wcSession.namespaces.eip155.accounts[0], 'eip155:1:' + ACCOUNT);
    ok('WalletConnect: link pasted, approval window shows the app, session approved with the account');

    const wcMsg = 'hello from walletconnect';
    const [wcSignWin, wcSig] = await Promise.all([
      ctx.waitForEvent('page', { timeout: 30000 }),
      wcDapp.request({ topic: wcSession.topic, chainId: 'eip155:1', request: { method: 'personal_sign', params: ['0x' + Buffer.from(wcMsg).toString('hex'), ACCOUNT] } }).then(async (sig) => sig, (e) => { throw e; }),
    ].map((p, i) => i === 0 ? p.then(async (w) => { await w.waitForLoadState('domcontentloaded'); await w.getByRole('heading', { level: 2 }).first().waitFor(); await press(w, 'Sign'); return w; }) : p));
    assert.equal(verifyMessage(wcMsg, wcSig), ACCOUNT);
    ok('WalletConnect: personal_sign goes through the approval window and returns a valid signature');

    const rawBefore = rawTxs.length;
    const wcTo = '0x70997970C51812dc3A010C7d01b50e0d17dc79C8';
    const wcTxP = wcDapp.request({ topic: wcSession.topic, chainId: 'eip155:1', request: { method: 'eth_sendTransaction', params: [{ from: ACCOUNT, to: wcTo, value: '0xde0b6b3a7640000' }] } });
    const wcTxWin = await ctx.waitForEvent('page', { timeout: 30000 });
    await wcTxWin.waitForLoadState('domcontentloaded');
    await wcTxWin.getByRole('heading', { level: 2 }).first().waitFor();
    await press(wcTxWin, 'Confirm');
    const wcHash = await wcTxP;
    assert.match(wcHash, /^0x[0-9a-f]{64}$/);
    assert.equal(rawTxs.length, rawBefore + 1);
    assert.equal(Transaction.from(rawTxs[rawTxs.length - 1]).to.toLowerCase(), wcTo.toLowerCase());
    ok('WalletConnect: eth_sendTransaction is approved, signed and broadcast');

    await wcPage.goto(`chrome-extension://${id}/popup.html#/sites`);
    await wcPage.getByText('Test Swap').first().waitFor();
    await wcPage.getByText('WalletConnect').first().waitFor();
    ok('WalletConnect session is listed in Connected sites');
    await wcDapp.core.relayer.transportClose().catch(() => {});
  } finally {
    await ctx.close();
    rpc.close();
    dappSrv.close();
    relay.close();
  }
  console.log('\nAll browser tests passed');
})().catch((e) => { console.error('\nFAILED:', e); process.exit(1); });
