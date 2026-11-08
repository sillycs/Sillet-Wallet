// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
// Answers requests from websites (the window.ethereum provider)
import { Wallet, getAddress, hexlify, toUtf8String, verifyMessage, TypedDataEncoder, isHexString, getBytes } from 'ethers';
import * as store from './state.js';
import * as core from './core.js';
import * as approvals from './approvals.js';
import { E, RpcError } from './errors.js';
import { chainById, chainByHex, toHex, CHAINS } from './chains.js';
import { rpc } from './rpc.js';
import { describeTx, typedDataWarnings, tokenInfo } from './decode.js';
import { normalizeRequest, prepare, signAndSend } from './tx.js';
import { addHistory } from './history.js';
import { api } from './api.js';

const READ = new Set([
  'eth_blockNumber', 'eth_call', 'eth_estimateGas', 'eth_gasPrice', 'eth_getBalance', 'eth_getBlockByHash', 'eth_getBlockByNumber',
  'eth_getBlockTransactionCountByHash', 'eth_getBlockTransactionCountByNumber', 'eth_getCode', 'eth_getLogs', 'eth_getStorageAt',
  'eth_getTransactionByHash', 'eth_getTransactionByBlockHashAndIndex', 'eth_getTransactionByBlockNumberAndIndex', 'eth_getTransactionCount',
  'eth_getTransactionReceipt', 'eth_feeHistory', 'eth_maxPriorityFeePerGas', 'eth_sendRawTransaction', 'eth_syncing', 'eth_createAccessList',
  'eth_blobBaseFee', 'eth_getUncleCountByBlockHash', 'eth_getUncleCountByBlockNumber', 'eth_getProof',
]);

const addrOf = (v) => { try { return getAddress(v); } catch (e) { throw E.invalid('Invalid address'); } };

async function ensureReady() {
  const st = await store.load();
  if (!st.vault) {
    await api.tabs.create({ url: api.runtime.getURL('popup.html#/onboard') });
    throw new RpcError(4100, 'Sillet is not set up yet. Create a wallet and try again');
  }
  return st;
}

async function accountsFor(origin) {
  const st = await store.load();
  if (!(await core.isUnlocked())) return [];
  const known = new Set(st.accounts.map((a) => a.address));
  return store.permitted(st, origin).filter((a) => known.has(a));
}

async function requireAccount(origin, from) {
  const list = await accountsFor(origin);
  if (!list.length) throw E.unauthorized();
  if (from && !list.some((a) => a.toLowerCase() === String(from).toLowerCase())) throw E.unauthorized();
  return from ? addrOf(from) : list[0];
}

const permissionObject = (origin) => ({ parentCapability: 'eth_accounts', invoker: origin, caveats: [{ type: 'restrictReturnedAccounts', value: [] }], date: Date.now() });

async function connect(origin) {
  const st = await ensureReady();
  const have = await accountsFor(origin);
  if (have.length) return have;
  const decision = await approvals.request('connect', origin, {});
  await store.withLock(async () => {
    const cur = await store.load();
    const account = addrOf(decision.account);
    if (!cur.accounts.some((a) => a.address === account)) throw E.internal('Unknown account');
    await store.save({
      permissions: { ...cur.permissions, [origin]: { accounts: [account], connectedAt: Date.now() } },
      siteChains: { ...cur.siteChains, [origin]: cur.siteChains[origin] || cur.chainId },
    });
  });
  core.stateChanged();
  return accountsFor(origin);
}

function parseSignMessage(params) {
  let [msg, addr] = params;
  // Standard order is [message, address]. Some dapps send [address, message]
  if (!isHexString(addr, 20) && isHexString(msg, 20)) [msg, addr] = [addr, msg];
  if (typeof msg !== 'string') throw E.invalid('Message must be a string');
  const bytes = isHexString(msg) ? getBytes(msg) : new TextEncoder().encode(msg);
  let text = null;
  try { text = new TextDecoder('utf-8', { fatal: true }).decode(bytes); if (/[\x00-\x08\x0e-\x1f]/.test(text)) text = null; } catch (e) { text = null; }
  return { addr, bytes, text, hex: hexlify(bytes) };
}

function parseTyped(params, chainId) {
  let [addr, data] = params;
  if (isHexString(data, 20) && !isHexString(addr, 20)) [addr, data] = [data, addr];
  const typed = typeof data === 'string' ? JSON.parse(data) : data;
  if (!typed || !typed.types || !typed.domain || !typed.message || !typed.primaryType) throw E.invalid('Invalid typed data');
  if (typed.domain.chainId !== undefined && Number(typed.domain.chainId) !== Number(chainId)) throw E.invalid('The signature is for a different network than the one you are on');
  const types = { ...typed.types };
  delete types.EIP712Domain;
  return { addr, typed, types };
}

export async function handleRpc(ctx, method, params = []) {
  const { origin } = ctx;
  if (!Array.isArray(params)) params = params === undefined ? [] : [params];
  const st = await store.load();
  const chainId = store.siteChain(st, origin);
  const chain = chainById(chainId);
  await core.touch();

  switch (method) {
    case 'eth_chainId': return toHex(chainId);
    case 'net_version': return String(chainId);
    case 'web3_clientVersion': return 'Sillet/' + api.runtime.getManifest().version;
    case 'eth_accounts': return accountsFor(origin);
    case 'eth_requestAccounts': return connect(origin);
    case 'wallet_requestPermissions': { await connect(origin); return [permissionObject(origin)]; }
    case 'wallet_getPermissions': return (await accountsFor(origin)).length ? [permissionObject(origin)] : [];
    case 'wallet_revokePermissions': {
      const cur = await store.load();
      const permissions = { ...cur.permissions };
      delete permissions[origin];
      await store.save({ permissions });
      core.stateChanged();
      return null;
    }
    case 'personal_ecRecover': return verifyMessage(getBytes(params[0]), params[1]);

    case 'personal_sign': {
      const m = parseSignMessage(params);
      const from = await requireAccount(origin, m.addr);
      await approvals.request('sign', origin, { account: from, chainId, text: m.text, hex: m.hex });
      const w = await core.walletForAddress(from);
      if (!w) throw E.locked();
      return w.signMessage(m.bytes);
    }
    case 'eth_signTypedData_v3':
    case 'eth_signTypedData_v4': {
      const t = parseTyped(params, chainId);
      const from = await requireAccount(origin, t.addr);
      TypedDataEncoder.hash(t.typed.domain, t.types, t.typed.message);
      await approvals.request('signTyped', origin, { account: from, chainId, typed: t.typed, warnings: typedDataWarnings(t.typed) });
      const w = await core.walletForAddress(from);
      if (!w) throw E.locked();
      return w.signTypedData(t.typed.domain, t.types, t.typed.message);
    }
    case 'eth_sign':
    case 'eth_signTypedData':
    case 'eth_signTransaction':
      throw E.unsupported(method);

    case 'eth_sendTransaction': {
      if (!params[0] || typeof params[0] !== 'object') throw E.invalid('Missing transaction');
      const req = normalizeRequest({ ...params[0], from: params[0].from || (await accountsFor(origin))[0] });
      if (params[0].chainId !== undefined && Number(params[0].chainId) !== Number(chainId)) throw E.invalid('The transaction is for a different network than the one you are on');
      const from = await requireAccount(origin, req.from);
      const custom = st.tokens[chainId] || [];
      const [prep, info] = await Promise.all([prepare(chainId, req), describeTx(chainId, req, custom)]);
      const decision = await approvals.request('tx', origin, { account: from, chainId, tx: req, prep, info: serializeInfo(info) });
      const w = await core.walletForAddress(from);
      if (!w) throw E.locked();
      // The window may have been open for a while, so take fresh fee numbers before signing
      const fresh = await prepare(chainId, req).catch(() => prep);
      const fee = fresh.fees[decision.level] || fresh.fees.market;
      const sent = await signAndSend(chainId, w, req, prep.gasLimit, fee);
      await addHistory({ hash: sent.hash, chainId, from, to: req.to || null, value: req.value, symbol: chain.native.symbol, decimals: chain.native.decimals, origin, kind: info.kind, status: 'pending' });
      return sent.hash;
    }

    case 'wallet_switchEthereumChain': {
      const target = chainByHex(params[0]?.chainId);
      if (!target) throw E.unknownChain();
      if (target.chainId === chainId) return null;
      await ensureReady();
      await approvals.request('switchChain', origin, { from: chainId, to: target.chainId });
      await store.withLock(async () => {
        const cur = await store.load();
        await store.save({ siteChains: { ...cur.siteChains, [origin]: target.chainId } });
      });
      core.stateChanged();
      return null;
    }
    case 'wallet_addEthereumChain': {
      const target = chainByHex(params[0]?.chainId);
      if (!target) throw E.unknownChain();
      return handleRpc(ctx, 'wallet_switchEthereumChain', [{ chainId: toHex(target.chainId) }]);
    }
    case 'wallet_watchAsset': {
      const opts = params[0]?.options || params[0];
      if (!opts || !opts.address || (params[0].type && params[0].type !== 'ERC20')) throw E.invalid('Only ERC-20 tokens are supported');
      await requireAccount(origin);
      const address = addrOf(opts.address);
      const info = await tokenInfo(chainId, address, st.tokens[chainId] || []);
      if (!info.symbol) throw E.invalid('That address is not a token on this network');
      await approvals.request('watchAsset', origin, { chainId, token: { address, symbol: info.symbol, decimals: info.decimals } });
      await store.withLock(async () => {
        const cur = await store.load();
        const list = cur.tokens[chainId] || [];
        if (!list.some((t) => t.address.toLowerCase() === address.toLowerCase())) {
          await store.save({ tokens: { ...cur.tokens, [chainId]: [...list, { address, symbol: info.symbol, name: info.symbol, decimals: info.decimals }] } });
        }
      });
      return true;
    }
    default:
      if (READ.has(method)) return rpc(chainId, method, params);
      throw E.unsupported(method);
  }
}

// BigInt cannot cross extension messages, so amounts travel as text
export function serializeInfo(info) {
  const out = {};
  for (const [k, v] of Object.entries(info)) out[k] = typeof v === 'bigint' ? { big: v.toString() } : v;
  return out;
}
export function reviveInfo(info) {
  const out = {};
  for (const [k, v] of Object.entries(info)) out[k] = v && typeof v === 'object' && 'big' in v ? BigInt(v.big) : v;
  return out;
}
