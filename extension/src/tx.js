// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
import { Wallet, Transaction, getAddress, parseUnits } from 'ethers';
import { chainById } from './chains.js';
import { rpc } from './rpc.js';
import { RpcError } from './errors.js';

const hexToBig = (h) => BigInt(h);
const big = (n) => '0x' + n.toString(16);

export function normalizeRequest(req) {
  const out = {};
  if (req.to !== undefined && req.to !== null && req.to !== '') out.to = getAddress(req.to);
  out.from = getAddress(req.from);
  out.value = req.value ? big(hexToBig(req.value)) : '0x0';
  const data = req.data ?? req.input;
  if (data && data !== '0x') {
    if (!/^0x([0-9a-fA-F]{2})*$/.test(data)) throw new RpcError(-32602, 'Invalid transaction data');
    out.data = data;
  }
  if (req.gas) out.gas = big(hexToBig(req.gas));
  if (!out.to && !out.data) throw new RpcError(-32602, 'Transaction has no recipient and no data');
  return out;
}

// Gas limit and three fee levels (low, market, fast) for the network
export async function prepare(chainId, req) {
  const chain = chainById(chainId);
  const out = { warnings: [] };
  const base = { from: req.from, to: req.to, value: req.value, data: req.data };
  let gas;
  try {
    gas = hexToBig(await rpc(chainId, 'eth_estimateGas', [base]));
    gas = (gas * 12n) / 10n;
    out.gasEstimated = true;
  } catch (e) {
    out.willFail = String(e.message || 'The network says this transaction would fail');
    gas = req.gas ? hexToBig(req.gas) : 300000n;
  }
  if (req.gas && !out.gasEstimated) gas = hexToBig(req.gas);
  out.gasLimit = big(gas);

  let block = null;
  if (chain.eip1559) {
    try { block = await rpc(chainId, 'eth_getBlockByNumber', ['latest', false]); } catch (e) { /* legacy */ }
  }
  if (chain.eip1559 && block && block.baseFeePerGas) {
    const baseFee = hexToBig(block.baseFeePerGas);
    let prio;
    try { prio = hexToBig(await rpc(chainId, 'eth_maxPriorityFeePerGas', [])); } catch (e) { prio = 1500000000n; }
    const minPrio = parseUnits(String(chain.minPriorityGwei || 0), 'gwei');
    if (prio < minPrio) prio = minPrio;
    if (prio === 0n) prio = 1n;
    const level = (pm, bm) => ({ type: 2, maxPriorityFeePerGas: big((prio * pm) / 100n), maxFeePerGas: big((baseFee * bm) / 100n + (prio * pm) / 100n) });
    out.fees = { low: level(80n, 120n), market: level(100n, 200n), fast: level(150n, 250n) };
  } else {
    const price = hexToBig(await rpc(chainId, 'eth_gasPrice', []));
    const level = (m) => ({ type: 0, gasPrice: big((price * m) / 100n) });
    out.fees = { low: level(90n), market: level(100n), fast: level(130n) };
  }
  return out;
}

export const feeCost = (fee, gasLimit) => BigInt(gasLimit) * BigInt(fee.type === 2 ? fee.maxFeePerGas : fee.gasPrice);

export async function signAndSend(chainId, wallet, req, gasLimit, fee) {
  const nonce = Number(hexToBig(await rpc(chainId, 'eth_getTransactionCount', [wallet.address, 'pending'])));
  const tx = { chainId, nonce, to: req.to, value: BigInt(req.value), data: req.data || '0x', gasLimit: BigInt(gasLimit), type: fee.type };
  if (fee.type === 2) { tx.maxFeePerGas = BigInt(fee.maxFeePerGas); tx.maxPriorityFeePerGas = BigInt(fee.maxPriorityFeePerGas); }
  else tx.gasPrice = BigInt(fee.gasPrice);
  const raw = await wallet.signTransaction(tx);
  const hash = await rpc(chainId, 'eth_sendRawTransaction', [raw]);
  return { hash, nonce, expected: Transaction.from(raw).hash };
}
