// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
// Checks the C# WalletConnect wallet (Windows, Linux and Android share it) against the official JavaScript WalletConnect SDK.
// Run: node tests/wc-interop.cjs <path to WcInterop.dll>   (or dotnet run --project ../tests/WcInterop is started for you with no argument)
const http = require('node:http');
const os = require('node:os');
const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');
const { spawn } = require('node:child_process');
const { Transaction, verifyMessage, verifyTypedData } = require('ethers');
const { startRelay } = require('./wc-relay.cjs');
const { SignClient } = require('@walletconnect/sign-client');

const ACCOUNT = '0xf39Fd6e51aad88F6F4ce6aB8827279cffFb92266';
const RELAY_PORT = 18601, RPC_PORT = 18602;
const dll = process.argv[2];
const ok = (m) => console.log('  ✓ ' + m);
const raws = [];

function startRpc() {
  return new Promise((resolve) => {
    const srv = http.createServer((req, res) => {
      let body = '';
      req.on('data', (c) => (body += c));
      req.on('end', () => {
        const one = (m) => {
          const r = { jsonrpc: '2.0', id: m.id };
          switch (m.method) {
            case 'eth_getBalance': r.result = '0x4563918244f40000'; break;
            case 'eth_getTransactionCount': r.result = '0x7'; break;
            case 'eth_gasPrice': r.result = '0x3b9aca00'; break;
            case 'eth_getBlockByNumber': r.result = { baseFeePerGas: '0x3b9aca00' }; break;
            case 'eth_maxPriorityFeePerGas': r.result = '0x59682f00'; break;
            case 'eth_estimateGas': r.result = '0x5208'; break;
            case 'eth_sendRawTransaction': raws.push(m.params[0]); r.result = Transaction.from(m.params[0]).hash; break;
            default: r.error = { code: -32601, message: 'no ' + m.method };
          }
          return r;
        };
        const j = JSON.parse(body || '{}');
        res.setHeader('content-type', 'application/json');
        res.end(JSON.stringify(Array.isArray(j) ? j.map(one) : one(j)));
      });
    }).listen(RPC_PORT, '127.0.0.1', () => resolve(srv));
  });
}

// the SDK rejects with a plain object { code, message }
const rejectsWith = (p, re) => p.then(() => { throw new Error('should have been refused'); }, (e) => assert.match(String(e && e.message), re));

(async () => {
  process.chdir(fs.mkdtempSync(path.join(os.tmpdir(), 'wc-interop-js-')));
  const relay = await startRelay(RELAY_PORT);
  const rpc = await startRpc();
  const dapp = await SignClient.init({ projectId: '0123456789abcdef0123456789abcdef', relayUrl: `ws://127.0.0.1:${RELAY_PORT}`, metadata: { name: 'Interop Swap', description: 'test', url: 'https://swap.example', icons: [] } });
  const { uri, approval } = await dapp.connect({
    optionalNamespaces: { eip155: { methods: ['personal_sign', 'eth_signTypedData_v4', 'eth_sendTransaction'], chains: ['eip155:1'], events: ['chainChanged'] } },
  });
  const lines = [];
  const child = dll ? spawn('dotnet', [dll, `ws://127.0.0.1:${RELAY_PORT}`, uri, `http://127.0.0.1:${RPC_PORT}`]) : spawn('dotnet', ['run', '--project', path.join(__dirname, '../../tests/WcInterop'), '--', `ws://127.0.0.1:${RELAY_PORT}`, uri, `http://127.0.0.1:${RPC_PORT}`]);
  child.stdout.on('data', (d) => String(d).split('\n').filter(Boolean).forEach((l) => { lines.push(l); console.log('   [wallet]', l); }));
  child.stderr.on('data', (d) => console.log('   [wallet err]', String(d).trim()));
  let code = 0;
  try {
    const session = await Promise.race([approval(), new Promise((_, rej) => setTimeout(() => rej(new Error('session was not approved in time')), 40000))]);
    assert.equal(session.namespaces.eip155.accounts[0], 'eip155:1:' + ACCOUNT);
    assert.ok(session.namespaces.eip155.methods.includes('personal_sign'));
    assert.ok(lines.some((l) => l.startsWith('PROMPT connect Sillet') || l.startsWith('PROMPT connect Interop Swap')));
    ok('the C# wallet pairs with the official SDK and the session is approved with the right account');

    const msg = 'hello from the C# wallet';
    const sig = await dapp.request({ topic: session.topic, chainId: 'eip155:1', request: { method: 'personal_sign', params: ['0x' + Buffer.from(msg).toString('hex'), ACCOUNT] } });
    assert.equal(verifyMessage(msg, sig), ACCOUNT);
    ok('personal_sign signature recovers to the account');

    const typed = {
      types: { EIP712Domain: [{ name: 'name', type: 'string' }, { name: 'version', type: 'string' }, { name: 'chainId', type: 'uint256' }, { name: 'verifyingContract', type: 'address' }], Person: [{ name: 'name', type: 'string' }, { name: 'wallet', type: 'address' }], Mail: [{ name: 'from', type: 'Person' }, { name: 'to', type: 'Person[]' }, { name: 'contents', type: 'string' }, { name: 'n', type: 'int256' }, { name: 'tag', type: 'bytes4' }, { name: 'ok', type: 'bool' }, { name: 'blob', type: 'bytes' }] },
      primaryType: 'Mail',
      domain: { name: 'Ether Mail', version: '1', chainId: 1, verifyingContract: '0xcccccccccccccccccccccccccccccccccccccccc' },
      message: { from: { name: 'Cow', wallet: '0xcd2a3d9f938e13cd947ec05abc7fe734df8dd826' }, to: [{ name: 'Bob', wallet: '0xbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb' }, { name: 'Eve', wallet: '0xeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee' }], contents: 'Hello, Bob!', n: '-5', tag: '0xdeadbeef', ok: true, blob: '0x0102' },
    };
    const tsig = await dapp.request({ topic: session.topic, chainId: 'eip155:1', request: { method: 'eth_signTypedData_v4', params: [ACCOUNT, JSON.stringify(typed)] } });
    const { EIP712Domain, ...types } = typed.types;
    assert.equal(verifyTypedData(typed.domain, types, typed.message, tsig), ACCOUNT);
    ok('eth_signTypedData_v4 (nested structs, arrays, int, bytes, bool) recovers to the account');

    const badChain = { ...typed, domain: { ...typed.domain, chainId: 5 } };
    await rejectsWith(dapp.request({ topic: session.topic, chainId: 'eip155:1', request: { method: 'eth_signTypedData_v4', params: [ACCOUNT, JSON.stringify(badChain)] } }), /another network/);
    ok('typed data made for another network is refused');

    await rejectsWith(dapp.request({ topic: session.topic, chainId: 'eip155:1', request: { method: 'personal_sign', params: ['0x00', '0x70997970C51812dc3A010C7d01b50e0d17dc79C8'] } }), /different account/);
    ok('a request for an account that was not approved is refused');

    const to = '0x70997970C51812dc3A010C7d01b50e0d17dc79C8';
    const hash = await dapp.request({ topic: session.topic, chainId: 'eip155:1', request: { method: 'eth_sendTransaction', params: [{ from: ACCOUNT, to, value: '0xde0b6b3a7640000', data: '0x' }] } });
    assert.match(hash, /^0x[0-9a-f]{64}$/);
    assert.equal(raws.length, 1);
    const tx = Transaction.from(raws[0]);
    assert.equal(tx.to.toLowerCase(), to.toLowerCase());
    assert.equal(tx.value, 10n ** 18n);
    assert.equal(tx.from, ACCOUNT);
    assert.equal(tx.chainId, 1n);
    assert.equal(tx.nonce, 7);
    ok('eth_sendTransaction is signed by the right account for the right network and broadcast');

    const approve = '0x095ea7b3' + '000000000000000000000000' + to.slice(2).toLowerCase() + 'f'.repeat(64);
    await dapp.request({ topic: session.topic, chainId: 'eip155:1', request: { method: 'eth_sendTransaction', params: [{ from: ACCOUNT, to: '0xdAC17F958D2ee523a2206206994597C13D831ec7', data: approve }] } });
    assert.ok(lines.some((l) => l.startsWith('PROMPT tx') && l.includes('warnings=1')));
    ok('an unlimited token approval is flagged to the person');

    await dapp.disconnect({ topic: session.topic, reason: { code: 6000, message: 'bye' } });
    const end = Date.now() + 10000;
    while (!lines.includes('SESSIONS 0') && Date.now() < end) await new Promise((r) => setTimeout(r, 200));
    assert.ok(lines.includes('SESSIONS 0'));
    ok('when the app disconnects the wallet forgets the session');
    console.log('\nAll interop tests passed');
  } catch (e) {
    console.error('\nFAILED:', e);
    code = 1;
  } finally {
    child.kill();
    relay.close();
    rpc.close();
    process.exit(code);
  }
})();
