// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
import { Interface, getAddress } from 'ethers';
import { chainById } from './chains.js';
import { rpc } from './rpc.js';

const IFACE = new Interface([
  'function approve(address spender, uint256 amount)',
  'function increaseAllowance(address spender, uint256 amount)',
  'function transfer(address to, uint256 amount)',
  'function transferFrom(address from, address to, uint256 amount)',
  'function permit(address owner, address spender, uint256 value, uint256 deadline, uint8 v, bytes32 r, bytes32 s)',
  'function setApprovalForAll(address operator, bool approved)',
  'function symbol() view returns (string)',
  'function decimals() view returns (uint8)',
]);

export const UNLIMITED = (1n << 255n);

export async function tokenInfo(chainId, address, customTokens = []) {
  const chain = chainById(chainId);
  const lower = String(address).toLowerCase();
  const known = [...(chain?.tokens || []), ...customTokens].find((t) => t.address.toLowerCase() === lower);
  if (known) return { symbol: known.symbol, decimals: known.decimals, known: true };
  try {
    const [s, d] = await Promise.all([
      rpc(chainId, 'eth_call', [{ to: address, data: IFACE.encodeFunctionData('symbol') }, 'latest']),
      rpc(chainId, 'eth_call', [{ to: address, data: IFACE.encodeFunctionData('decimals') }, 'latest']),
    ]);
    return { symbol: IFACE.decodeFunctionResult('symbol', s)[0], decimals: Number(IFACE.decodeFunctionResult('decimals', d)[0]), known: false };
  } catch (e) {
    return { symbol: null, decimals: 18, known: false };
  }
}

async function isContract(chainId, address) {
  try {
    const code = await rpc(chainId, 'eth_getCode', [address, 'latest']);
    return code && code !== '0x';
  } catch (e) {
    return null;
  }
}

// Turns raw transaction data into something a person can read, with warnings for risky calls
export async function describeTx(chainId, tx, customTokens = []) {
  const warnings = [];
  const data = tx.data && tx.data !== '0x' ? tx.data : '0x';
  const value = tx.value ? BigInt(tx.value) : 0n;
  if (!tx.to) {
    warnings.push('contractCreation');
    return { kind: 'create', warnings };
  }
  const to = getAddress(tx.to);
  if (data === '0x') return { kind: 'native', to, value, warnings };

  let parsed = null;
  try { parsed = IFACE.parseTransaction({ data }); } catch (e) { /* unknown call */ }
  if (!parsed) return { kind: 'call', to, value, selector: data.slice(0, 10), dataBytes: (data.length - 2) / 2, warnings: ['unknownCall'] };

  const info = await tokenInfo(chainId, to, customTokens);
  const out = { kind: parsed.name, token: to, symbol: info.symbol, decimals: info.decimals, value, warnings };
  if (!info.known) warnings.push('unknownToken');
  if (parsed.name === 'approve' || parsed.name === 'increaseAllowance' || parsed.name === 'permit') {
    out.spender = getAddress(parsed.args[parsed.name === 'permit' ? 1 : 0]);
    out.amount = parsed.args[parsed.name === 'permit' ? 2 : 1];
    if (out.amount >= UNLIMITED) warnings.push('unlimitedApproval');
    if ((await isContract(chainId, out.spender)) === false) warnings.push('spenderIsWallet');
  } else if (parsed.name === 'setApprovalForAll') {
    out.spender = getAddress(parsed.args[0]);
    out.approved = parsed.args[1];
    if (out.approved) warnings.push('approvalForAll');
    if ((await isContract(chainId, out.spender)) === false) warnings.push('spenderIsWallet');
  } else if (parsed.name === 'transfer') {
    out.recipient = getAddress(parsed.args[0]);
    out.amount = parsed.args[1];
  } else if (parsed.name === 'transferFrom') {
    out.sender = getAddress(parsed.args[0]);
    out.recipient = getAddress(parsed.args[1]);
    out.amount = parsed.args[2];
  }
  return out;
}

// Typed data that grants spending rights without a transaction
export function typedDataWarnings(typed) {
  const w = [];
  const t = String(typed?.primaryType || '');
  if (/permit/i.test(t) || /permit/i.test(JSON.stringify(Object.keys(typed?.types || {})))) w.push('permitSignature');
  if (/order|seaport|listing/i.test(t)) w.push('orderSignature');
  return w;
}

export const erc20 = IFACE;
