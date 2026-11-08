// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
import * as store from './state.js';

export function addHistory(entry) {
  return store.withLock(async () => {
    const st = await store.load();
    const history = [{ time: Date.now(), ...entry }, ...st.history].slice(0, 200);
    await store.save({ history });
  });
}
