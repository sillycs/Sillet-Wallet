// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
import test from 'node:test';
import assert from 'node:assert/strict';
import { getAddress, Wallet, verifyMessage, TypedDataEncoder, Interface } from 'ethers';
import { walletAt, validPhrase, newPhrase } from '../src/wallet.js';
import { encryptVault, decryptVault } from '../src/vault.js';
import { CHAINS, chainById, chainByHex, toHex } from '../src/chains.js';

const JUNK = 'test test test test test test test test test test test junk';
const ABOUT = 'abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about';

test('derivation matches the standard Ethereum test vectors', () => {
  assert.equal(walletAt(JUNK, 0).address, '0xf39Fd6e51aad88F6F4ce6aB8827279cffFb92266');
  assert.equal(walletAt(JUNK, 1).address, '0x70997970C51812dc3A010C7d01b50e0d17dc79C8');
  assert.equal(walletAt(ABOUT, 0).address, '0x9858EfFD232B4033E47d90003D41EC34EcaEda94');
});

test('recovery phrases are validated', () => {
  assert.ok(validPhrase(JUNK));
  assert.ok(validPhrase('  ' + ABOUT.toUpperCase() + '  '));
  assert.ok(!validPhrase('abandon abandon abandon'));
  assert.ok(!validPhrase(ABOUT.replace('about', 'zoo')));
  const p = newPhrase();
  assert.equal(p.split(' ').length, 12);
  assert.ok(validPhrase(p));
  assert.notEqual(newPhrase(), p);
});

test('vault round trip, wrong password and fresh salt each time', async () => {
  const a = await encryptVault(JUNK, 'correct horse battery');
  const b = await encryptVault(JUNK, 'correct horse battery');
  assert.notEqual(a.salt, b.salt);
  assert.notEqual(a.ct, b.ct);
  assert.equal(await decryptVault(a, 'correct horse battery'), JUNK);
  await assert.rejects(() => decryptVault(a, 'wrong password'), /Wrong password/);
  const tampered = { ...a, ct: a.ct.slice(0, -4) + 'AAAA' };
  await assert.rejects(() => decryptVault(tampered, 'correct horse battery'));
  assert.ok(!JSON.stringify(a).includes('test test'));
  assert.ok(a.iter >= 600000);
});

test('network list mirrors the Sillet app', () => {
  assert.equal(CHAINS.length, 11);
  assert.deepEqual(CHAINS.map((c) => c.chainId).sort((x, y) => x - y), [1, 10, 56, 100, 137, 8217, 8453, 42161, 43114, 59144, 81457]);
  for (const c of CHAINS) {
    assert.ok(c.rpc.length >= 2 && c.rpc.every((u) => u.startsWith('https://')), c.name);
    for (const t of c.tokens) assert.equal(getAddress(t.address), t.address, c.name + ' ' + t.symbol);
  }
  assert.equal(chainByHex('0x1').name, 'Ethereum');
  assert.equal(chainByHex('0x89').name, 'Polygon');
  assert.equal(chainByHex('0xzz'), null);
  assert.equal(chainByHex('0x5'), null);
  assert.equal(toHex(8453), '0x2105');
  assert.equal(chainById(56).eip1559, false);
  assert.equal(chainById(1).eip1559, true);
});

test('network list matches EvmNetworks.cs in the Sillet app', async () => {
  const { readFileSync } = await import('node:fs');
  const cs = readFileSync(new URL('../../src/Sillet.Core/Chains/Evm/EvmNetworks.cs', import.meta.url), 'utf8');
  const ids = [...cs.matchAll(/ChainId \{ get \{ return (\d+)L?;/g)].map((m) => Number(m[1])).sort((a, b) => a - b);
  assert.deepEqual(CHAINS.map((c) => c.chainId).sort((a, b) => a - b), ids, 'run: npm run sync-chains');
});

test('personal_sign and typed data signatures recover to the account', async () => {
  const w = walletAt(JUNK, 0);
  const sig = await w.signMessage('hello sillet');
  assert.equal(verifyMessage('hello sillet', sig), w.address);
  const domain = { name: 'Test', version: '1', chainId: 1, verifyingContract: '0x' + '11'.repeat(20) };
  const types = { Mail: [{ name: 'to', type: 'address' }, { name: 'text', type: 'string' }] };
  const message = { to: '0x' + '22'.repeat(20), text: 'hi' };
  const s2 = await w.signTypedData(domain, types, message);
  const { recoverAddress } = await import('ethers');
  assert.equal(recoverAddress(TypedDataEncoder.hash(domain, types, message), s2), w.address);
});

// ---- network dependent logic with a fake node ----
function fakeNode(handlers) {
  globalThis.fetch = async (url, init) => {
    const body = JSON.parse(init.body);
    const h = handlers[body.method];
    if (!h) return new Response(JSON.stringify({ jsonrpc: '2.0', id: body.id, error: { code: -32601, message: 'no ' + body.method } }), { status: 200 });
    const result = typeof h === 'function' ? h(body.params) : h;
    return new Response(JSON.stringify({ jsonrpc: '2.0', id: body.id, result }), { status: 200 });
  };
}

test('risky approvals are flagged in plain terms', async () => {
  fakeNode({ eth_getCode: '0x', eth_call: '0x' });
  const { describeTx } = await import('../src/decode.js');
  const iface = new Interface(['function approve(address,uint256)', 'function setApprovalForAll(address,bool)', 'function transfer(address,uint256)']);
  const spender = '0x' + '33'.repeat(20);
  const usdt = '0xdAC17F958D2ee523a2206206994597C13D831ec7';
  const max = (1n << 256n) - 1n;
  let d = await describeTx(1, { to: usdt, data: iface.encodeFunctionData('approve', [spender, max]) });
  assert.equal(d.kind, 'approve');
  assert.equal(d.symbol, 'USDT');
  assert.ok(d.warnings.includes('unlimitedApproval'));
  assert.ok(d.warnings.includes('spenderIsWallet'));
  d = await describeTx(1, { to: usdt, data: iface.encodeFunctionData('approve', [spender, 5_000_000n]) });
  assert.ok(!d.warnings.includes('unlimitedApproval'));
  d = await describeTx(1, { to: usdt, data: iface.encodeFunctionData('transfer', [spender, 1_000_000n]) });
  assert.equal(d.kind, 'transfer');
  assert.equal(d.amount, 1_000_000n);
  d = await describeTx(1, { to: '0x' + '44'.repeat(20), data: iface.encodeFunctionData('setApprovalForAll', [spender, true]) });
  assert.ok(d.warnings.includes('approvalForAll'));
  d = await describeTx(1, { to: spender, value: '0xde0b6b3a7640000' });
  assert.equal(d.kind, 'native');
  assert.equal(d.value, 10n ** 18n);
  d = await describeTx(1, { to: spender, data: '0xdeadbeef' });
  assert.equal(d.kind, 'call');
  assert.ok(d.warnings.includes('unknownCall'));
});

test('typed data that grants spending is flagged', async () => {
  const { typedDataWarnings } = await import('../src/decode.js');
  assert.ok(typedDataWarnings({ primaryType: 'Permit', types: { Permit: [] } }).includes('permitSignature'));
  assert.ok(typedDataWarnings({ primaryType: 'PermitSingle', types: {} }).includes('permitSignature'));
  assert.deepEqual(typedDataWarnings({ primaryType: 'Mail', types: { Mail: [] } }), []);
});

test('fees: EIP-1559 levels are ordered and the Polygon minimum tip is respected', async () => {
  const { prepare, feeCost } = await import('../src/tx.js');
  fakeNode({ eth_estimateGas: '0x5208', eth_getBlockByNumber: { baseFeePerGas: '0x3b9aca00' }, eth_maxPriorityFeePerGas: '0x59682f00' });
  const req = { from: '0x' + '11'.repeat(20), to: '0x' + '22'.repeat(20), value: '0x1' };
  let p = await prepare(1, req);
  assert.equal(BigInt(p.gasLimit), 25200n);
  assert.ok(BigInt(p.fees.low.maxFeePerGas) < BigInt(p.fees.market.maxFeePerGas));
  assert.ok(BigInt(p.fees.market.maxFeePerGas) < BigInt(p.fees.fast.maxFeePerGas));
  assert.ok(feeCost(p.fees.fast, p.gasLimit) > feeCost(p.fees.low, p.gasLimit));
  p = await prepare(137, req);
  assert.ok(BigInt(p.fees.market.maxPriorityFeePerGas) >= 30_000_000_000n);
});

test('fees: legacy networks use gas price and failed estimates are reported', async () => {
  const { prepare } = await import('../src/tx.js');
  fakeNode({ eth_gasPrice: '0x2540be400' });
  const req = { from: '0x' + '11'.repeat(20), to: '0x' + '22'.repeat(20), value: '0x1' };
  const p = await prepare(56, req);
  assert.equal(p.fees.market.type, 0);
  assert.equal(p.willFail !== undefined, true);
  fakeNode({ eth_gasPrice: '0x1', eth_estimateGas: () => { throw new Error('x'); } });
});

test('rpc fails over to the next node', async () => {
  const { rpc } = await import('../src/rpc.js');
  const seen = [];
  globalThis.fetch = async (url, init) => {
    seen.push(url);
    const body = JSON.parse(init.body);
    if (seen.length === 1) return new Response('', { status: 503 });
    return new Response(JSON.stringify({ jsonrpc: '2.0', id: body.id, result: '0x10' }));
  };
  assert.equal(await rpc(8453, 'eth_blockNumber'), '0x10');
  assert.equal(seen.length, 2);
  assert.notEqual(seen[0], seen[1]);
});

test('transactions are signed for the right network and recipient', async () => {
  const { signAndSend, normalizeRequest } = await import('../src/tx.js');
  const { Transaction } = await import('ethers');
  let raw = null;
  fakeNode({ eth_getTransactionCount: '0x7', eth_sendRawTransaction: (p) => { raw = p[0]; return Transaction.from(p[0]).hash; } });
  const w = walletAt(JUNK, 0);
  const req = normalizeRequest({ from: w.address, to: '0x70997970C51812dc3A010C7d01b50e0d17dc79C8', value: '0xde0b6b3a7640000' });
  const out = await signAndSend(8453, w, req, '0x5208', { type: 2, maxFeePerGas: '0x77359400', maxPriorityFeePerGas: '0x3b9aca00' });
  const tx = Transaction.from(raw);
  assert.equal(tx.chainId, 8453n);
  assert.equal(tx.nonce, 7);
  assert.equal(tx.to, '0x70997970C51812dc3A010C7d01b50e0d17dc79C8');
  assert.equal(tx.value, 10n ** 18n);
  assert.equal(tx.from, w.address);
  assert.equal(out.hash, tx.hash);
});

// ---------- Bitcoin, Litecoin, Dogecoin ----------
import { NETS, addressesFor, scriptFor, plan, signTx, validAddress, parseAmount, fmtUnits, _test as U } from '../src/utxo.js';

test('first addresses match the standard test vector and the Sillet app paths', () => {
  const a = addressesFor(ABOUT, 0);
  assert.equal(a.bitcoin, 'bc1qcr8te4kr609gcawutmrza0j4xv80jy8z306fyu');
  assert.match(a.litecoin, /^ltc1q/);
  assert.match(a.dogecoin, /^D/);
  assert.notEqual(addressesFor(ABOUT, 1).bitcoin, a.bitcoin);
});

test('addresses are checked before sending', () => {
  assert.ok(validAddress('bitcoin', 'bc1qw508d6qejxtdg4y5r3zarvary0c5xw7kv8f3t4'));
  assert.ok(validAddress('bitcoin', '1BvBMSEYstWetqTFn5Au4m4GFg7xJaNVN2'));
  assert.ok(!validAddress('bitcoin', 'bc1qw508d6qejxtdg4y5r3zarvary0c5xw7kv8f3t5'), 'bad checksum');
  assert.ok(!validAddress('bitcoin', 'ltc1qjmxnz78nmc8nq77wuxh25n2es7rzm5c2rkk4wh'), 'wrong network');
  assert.ok(!validAddress('dogecoin', '1BvBMSEYstWetqTFn5Au4m4GFg7xJaNVN2'), 'bitcoin address on dogecoin');
  assert.ok(!validAddress('bitcoin', '0x70997970C51812dc3A010C7d01b50e0d17dc79C8'));
});

test('amounts are exact', () => {
  assert.equal(parseAmount('0.00000001', 8), 1n);
  assert.equal(parseAmount('1,5', 8), 150000000n);
  assert.throws(() => parseAmount('0.000000001', 8));
  assert.throws(() => parseAmount('abc', 8));
  assert.equal(fmtUnits(123456789n, 8), '1.23456789');
});

test('coin selection: change, exact spend and not enough', () => {
  const n = NETS.bitcoin;
  const dest = scriptFor(n, 'bc1qw508d6qejxtdg4y5r3zarvary0c5xw7kv8f3t4');
  const own = scriptFor(n, addressesFor(ABOUT, 0).bitcoin);
  const coins = [{ txid: 'a'.repeat(64), vout: 0, value: 100000n, confirmed: true }];
  const p = plan(n, coins, dest, own, 50000n, 5);
  assert.equal(p.outputs.length, 2);
  assert.equal(p.outputs[0].value + p.outputs[1].value + p.fee, 100000n);
  const all = plan(n, coins, dest, own, null, 5);
  assert.equal(all.outputs.length, 1);
  assert.equal(all.amount + all.fee, 100000n);
  assert.throws(() => plan(n, coins, dest, own, 100000n, 5), /enough/);
  assert.throws(() => plan(n, coins, dest, own, 100n, 5), /minimum/);
  assert.throws(() => plan(n, [], dest, own, 1000n, 5), /no BTC/);
});

test('signed Bitcoin and Dogecoin transactions are identical to bitcoinjs-lib output', () => {
  const key = U.deriveKey(U.rootOf(ABOUT), NETS.bitcoin, 0);
  const a = addressesFor(ABOUT, 0);
  const own = scriptFor(NETS.bitcoin, a.bitcoin);
  const coins = [{ txid: 'a'.repeat(63) + 'b', vout: 1, value: 100000n, confirmed: true }, { txid: 'c'.repeat(64), vout: 0, value: 50000n, confirmed: true }];
  const p = plan(NETS.bitcoin, coins, scriptFor(NETS.bitcoin, 'bc1qw508d6qejxtdg4y5r3zarvary0c5xw7kv8f3t4'), own, 120000n, 5);
  assert.equal(signTx(NETS.bitcoin, p, key, own), '02000000000102abaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa0100000000fdffffffcccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc0000000000fdffffff02c0d4010000000000160014751e76e8199196d454941c45d1b3a323f1433bd61b71000000000000160014c0cebcd6c3d3ca8c75dc5ec62ebe55330ef910e20247304402204950295ffee32dfba88dbaae4412851cddf0f3011e8d529f75f0b6711f660498022056e6b78fb87c3e674b8eb92101d750ec1093cf1e4312e7d91e428f975ab8486101210330d54fd0dd420a6e5f8d3624f5f3482cae350f79d5f0753bf5beef9c2d91af3c02483045022100c958b59f55128b6089b0c3f32408feab336d946073b1cfc1a683a5610736906d02202e0ea8e69234614bca05079eaac566586457794f7e26d6efce498e0f419e90cb01210330d54fd0dd420a6e5f8d3624f5f3482cae350f79d5f0753bf5beef9c2d91af3c00000000');
  const dkey = U.deriveKey(U.rootOf(ABOUT), NETS.dogecoin, 0);
  const downScript = scriptFor(NETS.dogecoin, a.dogecoin);
  const destDoge = U.b58check(0x1e, new Uint8Array(20).fill(7));
  const dcoins = [{ txid: 'd'.repeat(64), vout: 2, value: 900000000n, confirmed: true }, { txid: 'e'.repeat(64), vout: 0, value: 300000000n, confirmed: true }];
  const dp = plan(NETS.dogecoin, dcoins, scriptFor(NETS.dogecoin, destDoge), downScript, 1000000000n, 1000);
  assert.equal(signTx(NETS.dogecoin, dp, dkey, downScript), '0100000002dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd020000006a47304402206ce75d97d6fb2209c345b123a37cb1d638fe5862942c1afed91486544eb0a5b802207708738abcf142bcaa78fc48c536cb654db52a5bef55f2b8aa4032ce30069e63012102cc6b0dc33aabcf3a23643e5e2919a80c50fb3dd2129ce409bbc5f0d4643d05e0ffffffffeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee000000006b4830450221008acbc4bf8a58603fdbb80e27d068b79a02e134ed1a00f3fca495636a5e2f9ffd02205e8fb7a887bfc26488bbd84bf1d32562785beb00c38046df6add483617a4a080012102cc6b0dc33aabcf3a23643e5e2919a80c50fb3dd2129ce409bbc5f0d4643d05e0ffffffff0200ca9a3b000000001976a914070707070707070707070707070707070707070788ac100de60b000000001976a9144a483568665dcdfa68dd58a1f62893448a64333988ac00000000');
});

test('Bitcoin Cash: cashaddr, legacy addresses and fork id signing', () => {
  const hash = new Uint8Array(Buffer.from('76a04053bda0a88bda5177b86a15c3b29f559873', 'hex'));
  assert.equal(U.cashEncode(hash), 'bitcoincash:qpm2qsznhks23z7629mms6s4cwef74vcwvy22gdx6a');
  assert.ok(validAddress('bitcoincash', 'bitcoincash:qpm2qsznhks23z7629mms6s4cwef74vcwvy22gdx6a'));
  assert.ok(validAddress('bitcoincash', 'qpm2qsznhks23z7629mms6s4cwef74vcwvy22gdx6a'));
  assert.ok(validAddress('bitcoincash', '1BpEi6DfDAUFd7GtittLSdBeYJvcoaVggu'));
  assert.ok(!validAddress('bitcoincash', 'bitcoincash:qpm2qsznhks23z7629mms6s4cwef74vcwvy22gdx6b'), 'bad checksum');
  assert.ok(!validAddress('bitcoincash', 'bc1qw508d6qejxtdg4y5r3zarvary0c5xw7kv8f3t4'));
  assert.equal(addressesFor(ABOUT, 0).bitcoincash, 'bitcoincash:qqyx49mu0kkn9ftfj6hje6g2wfer34yfnq5tahq3q6');
  const key = U.deriveKey(U.rootOf(ABOUT), NETS.bitcoincash, 0);
  const own = scriptFor(NETS.bitcoincash, addressesFor(ABOUT, 0).bitcoincash);
  const dest = scriptFor(NETS.bitcoincash, 'bitcoincash:qpm2qsznhks23z7629mms6s4cwef74vcwvy22gdx6a');
  const coins = [{ txid: 'a'.repeat(64), vout: 1, value: 500000n, confirmed: true }, { txid: 'b'.repeat(64), vout: 0, value: 300000n, confirmed: true }];
  const p = plan(NETS.bitcoincash, coins, dest, own, 600000n, 2);
  assert.equal(signTx(NETS.bitcoincash, p, key, own), '0100000002aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa010000006b483045022100851ea76331176993b352529bbf494756b757133978a8e48f27eb99284e7a79c8022075db4b08dfd4be3195d1c55c8e272630bd4f802d485748bf9ed26774afb2956d412102bbe7dbcdf8b2261530a867df7180b17a90b482f74f2736b8a30d3f756e42e217ffffffffbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb00000000694630430220122105301768edc0552fbe60dbcf0510681d3af02fed4adb4b2764318eb703f6021f5edf5247b338f5600deed0b10ec552db0354249b61b9bd4b717e55b64c8776412102bbe7dbcdf8b2261530a867df7180b17a90b482f74f2736b8a30d3f756e42e217ffffffff02c0270900000000001976a91476a04053bda0a88bda5177b86a15c3b29f55987388ac540a0300000000001976a914086a977c7dad32a56996af2ce90a727238d4899888ac00000000');
});

// ---------- payment links, page addresses and QR reading ----------
import qrcode from 'qrcode-generator';
import { parsePayment, findAddresses, payHash } from '../src/payment.js';
import { decodePixels } from '../src/scan.js';

test('payment links and plain addresses are understood', () => {
  const btc = parsePayment('bitcoin:bc1qw508d6qejxtdg4y5r3zarvary0c5xw7kv8f3t4?amount=0.01&label=Coffee%20Shop');
  assert.deepEqual(btc, { net: 'bitcoin', to: 'bc1qw508d6qejxtdg4y5r3zarvary0c5xw7kv8f3t4', amount: '0.01', label: 'Coffee Shop' });
  assert.equal(parsePayment('bitcoin:bc1qw508d6qejxtdg4y5r3zarvary0c5xw7kv8f3t5'), null, 'bad checksum');
  assert.equal(parsePayment('litecoin:bc1qw508d6qejxtdg4y5r3zarvary0c5xw7kv8f3t4'), null, 'wrong network for the link');
  assert.equal(parsePayment('bitcoincash:qpm2qsznhks23z7629mms6s4cwef74vcwvy22gdx6a?amount=1.5').amount, '1.5');
  assert.equal(parsePayment('qpm2qsznhks23z7629mms6s4cwef74vcwvy22gdx6a').net, 'bitcoincash');
  assert.equal(parsePayment('1BpEi6DfDAUFd7GtittLSdBeYJvcoaVggu').net, 'bitcoin');
  const eth = parsePayment('ethereum:0x70997970C51812dc3A010C7d01b50e0d17dc79C8@1?value=2.014e18');
  assert.deepEqual(eth, { net: 1, to: '0x70997970C51812dc3A010C7d01b50e0d17dc79C8', raw: '2014000000000000000' });
  const tok = parsePayment('ethereum:0xdAC17F958D2ee523a2206206994597C13D831ec7@1/transfer?address=0x70997970C51812dc3A010C7d01b50e0d17dc79C8&uint256=1000000');
  assert.equal(tok.token, '0xdAC17F958D2ee523a2206206994597C13D831ec7'); assert.equal(tok.raw, '1000000');
  assert.equal(parsePayment('ethereum:0x70997970C51812dc3A010C7d01b50e0d17dc79C8@999999'), null, 'unknown chain');
  assert.equal(parsePayment('ethereum:0x70997970C51812dc3A010C7d01b50e0d17dc79C8/approve?x=1'), null, 'only transfers');
  for (const bad of ['javascript:alert(1)', 'hello', '', 'https://example.com', 'bitcoin:']) assert.equal(parsePayment(bad), null);
  assert.equal(parsePayment('0x70997970C51812dc3A010C7d01b50e0d17dc79C8').net, 'evm');
});

test('addresses are found in page text and checked', () => {
  const found = findAddresses('Pay to 1BpEi6DfDAUFd7GtittLSdBeYJvcoaVggu or 0x70997970C51812dc3A010C7d01b50e0d17dc79C8. Not 1BpEi6DfDAUFd7GtittLSdBeYJvcoaVggX nor 0x1234');
  assert.deepEqual(found.map((x) => x.net), ['bitcoin', 'evm']);
  assert.equal(findAddresses('nothing here, just words and 12345').length, 0);
  assert.match(payHash(found[0], 'https://shop.example'), /^\/pay\?net=bitcoin&to=1BpEi.*from=https%3A%2F%2Fshop\.example$/);
});

test('a QR code picture is read back', () => {
  const text = 'bitcoin:bc1qw508d6qejxtdg4y5r3zarvary0c5xw7kv8f3t4?amount=0.002';
  const qr = qrcode(0, 'M'); qr.addData(text); qr.make();
  const n = qr.getModuleCount(); const scale = 6; const pad = 4; const size = (n + pad * 2) * scale;
  const px = new Uint8ClampedArray(size * size * 4).fill(255);
  for (let y = 0; y < n * scale; y++) for (let x = 0; x < n * scale; x++) {
    if (qr.isDark(Math.floor(y / scale), Math.floor(x / scale))) { const i = (((y + pad * scale) * size) + x + pad * scale) * 4; px[i] = px[i + 1] = px[i + 2] = 0; }
  }
  assert.equal(decodePixels(px, size, size), text);
  assert.equal(decodePixels(new Uint8ClampedArray(100 * 100 * 4).fill(200), 100, 100), null);
});
