// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
// One handle for both browsers: Firefox exposes promise based "browser", Chromium exposes "chrome"
export const api = globalThis.browser || globalThis.chrome;
