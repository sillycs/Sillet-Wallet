// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
// Rebuilds scripts/chains.json from the Sillet app's EvmNetworks.cs so the extension and the app list the same networks
import { readFileSync, writeFileSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));
const src = readFileSync(join(here, '../../src/Sillet.Core/Chains/Evm/EvmNetworks.cs'), 'utf8');
const prev = JSON.parse(readFileSync(join(here, 'chains.json'), 'utf8'));
const minTip = Object.fromEntries(prev.map((c) => [c.chainId, c.minPriorityGwei || 0]));
const out = [];
for (const m of src.matchAll(/public sealed class \w+ : EvmChain\s*\{([\s\S]*?)\n    \}\n/g)) {
  const b = m[1];
  const pick = (re) => { const x = b.match(re); return x ? x[1] : null; };
  const chainId = Number(pick(/ChainId \{ get \{ return (\d+)L?;/));
  const na = b.match(/new Asset\("([^"]+)", "([^"]+)", (\d+)\)/);
  const tokens = [...b.matchAll(/Token\("([^"]+)", "([^"]+)", (\d+), "(0x[0-9a-fA-F]{40})"\)/g)].map((t) => ({ symbol: t[1], name: t[2], decimals: Number(t[3]), address: t[4] }));
  const rpcBlock = b.slice(b.indexOf('RpcUrls')).split('}; } }')[0];
  out.push({
    id: pick(/Id \{ get \{ return "([^"]+)"/), name: pick(/Name \{ get \{ return "([^"]+)"/), chainId,
    native: { symbol: na[1], name: na[2], decimals: Number(na[3]) }, tokens, explorer: pick(/ExplorerBase \{ get \{ return "([^"]+)"/),
    rpc: [...rpcBlock.matchAll(/"(https:\/\/[^"]+)"/g)].map((r) => r[1]),
    eip1559: !b.includes('SupportsEip1559 { get { return false'), minPriorityGwei: minTip[chainId] || 0,
  });
}
writeFileSync(join(here, 'chains.json'), JSON.stringify(out, null, 1) + '\n');
console.log('chains.json updated with', out.length, 'networks');
