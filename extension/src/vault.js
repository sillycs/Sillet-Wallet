// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
// The recovery phrase is stored encrypted with a key derived from the password:
// PBKDF2-SHA256 (600000 rounds) and AES-256-GCM. Nothing leaves the device
const enc = new TextEncoder();
const dec = new TextDecoder();
const ITER = 600000;
const AAD = enc.encode('sillet-vault-v1');

const toB64 = (u8) => btoa(String.fromCharCode(...u8));
const fromB64 = (s) => Uint8Array.from(atob(s), (c) => c.charCodeAt(0));

async function deriveKey(password, salt, iterations) {
  const base = await crypto.subtle.importKey('raw', enc.encode(password.normalize('NFKC')), 'PBKDF2', false, ['deriveKey']);
  return crypto.subtle.deriveKey({ name: 'PBKDF2', hash: 'SHA-256', salt, iterations }, base, { name: 'AES-GCM', length: 256 }, false, ['encrypt', 'decrypt']);
}

export async function encryptVault(mnemonic, password) {
  const salt = crypto.getRandomValues(new Uint8Array(16));
  const iv = crypto.getRandomValues(new Uint8Array(12));
  const key = await deriveKey(password, salt, ITER);
  const ct = new Uint8Array(await crypto.subtle.encrypt({ name: 'AES-GCM', iv, additionalData: AAD }, key, enc.encode(JSON.stringify({ mnemonic }))));
  return { v: 1, kdf: 'PBKDF2-SHA256', iter: ITER, salt: toB64(salt), iv: toB64(iv), ct: toB64(ct) };
}

export async function decryptVault(vault, password) {
  try {
    const key = await deriveKey(password, fromB64(vault.salt), vault.iter);
    const plain = await crypto.subtle.decrypt({ name: 'AES-GCM', iv: fromB64(vault.iv), additionalData: AAD }, key, fromB64(vault.ct));
    return JSON.parse(dec.decode(plain)).mnemonic;
  } catch (e) {
    throw new Error('Wrong password');
  }
}
