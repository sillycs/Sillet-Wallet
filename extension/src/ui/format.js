// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
import { formatUnits } from 'ethers';

export function fmt(value, decimals, max = 6) {
  if (value == null) return '–';
  const s = formatUnits(BigInt(value), decimals);
  const [i, f = ''] = s.split('.');
  const ff = f.slice(0, max).replace(/0+$/, '');
  if (!ff && BigInt(value) > 0n && i === '0') return '<0.' + '0'.repeat(max - 1) + '1';
  return ff ? i + '.' + ff : i;
}

export const short = (a) => (a ? a.slice(0, 6) + '…' + a.slice(-4) : '');

export function ago(ts) {
  const s = Math.max(1, Math.floor((Date.now() - ts) / 1000));
  if (s < 60) return s + 's';
  if (s < 3600) return Math.floor(s / 60) + 'm';
  if (s < 86400) return Math.floor(s / 3600) + 'h';
  return Math.floor(s / 86400) + 'd';
}

export async function copy(text) {
  try { await navigator.clipboard.writeText(text); return true; } catch (e) { return false; }
}
