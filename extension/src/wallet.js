// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
import { HDNodeWallet, Mnemonic, getAddress } from 'ethers';

export const normalizePhrase = (p) => String(p || '').trim().toLowerCase().split(/\s+/).join(' ');

export function validPhrase(p) {
  const n = normalizePhrase(p);
  const words = n.split(' ').length;
  return (words === 12 || words === 24) && Mnemonic.isValidMnemonic(n);
}

export function newPhrase() {
  return Mnemonic.fromEntropy(crypto.getRandomValues(new Uint8Array(16))).phrase;
}

// Standard Ethereum path, the same addresses as MetaMask and the Sillet app
export function walletAt(mnemonic, index) {
  return HDNodeWallet.fromPhrase(normalizePhrase(mnemonic), undefined, "m/44'/60'/0'/0/" + index);
}

export const checksum = (a) => getAddress(a);
