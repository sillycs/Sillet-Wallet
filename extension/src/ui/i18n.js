// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
const es = {};
let lang = 'en';

export function setLang(pref) {
  const l = pref === 'auto' || !pref ? (navigator.language || 'en').slice(0, 2) : pref;
  lang = l === 'es' ? 'es' : 'en';
}
export const currentLang = () => lang;

export function t(text, vars) {
  let s = (lang === 'es' && es[text]) || text;
  if (vars) for (const [k, v] of Object.entries(vars)) s = s.split('{' + k + '}').join(String(v));
  return s;
}

export function addSpanish(map) { Object.assign(es, map); }
