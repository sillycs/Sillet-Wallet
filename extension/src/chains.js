// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
import raw from '../scripts/chains.json' with { type: 'json' };

export const CHAINS = raw;

export function chainById(id) {
  return CHAINS.find((c) => c.chainId === Number(id)) || null;
}

export function chainByHex(hex) {
  if (typeof hex !== 'string' || !/^0x[0-9a-fA-F]+$/.test(hex)) return null;
  return chainById(parseInt(hex, 16));
}

export const toHex = (n) => '0x' + Number(n).toString(16);
