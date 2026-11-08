// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
export class RpcError extends Error {
  constructor(code, message, data) {
    super(message);
    this.code = code;
    this.data = data;
  }
}

export const E = {
  userRejected: () => new RpcError(4001, 'User rejected the request'),
  unauthorized: () => new RpcError(4100, 'The requested account has not been authorized by the user'),
  unsupported: (m) => new RpcError(4200, 'The method ' + m + ' is not supported'),
  locked: () => new RpcError(4100, 'Sillet is locked or not set up. Open Sillet and try again'),
  unknownChain: () => new RpcError(4902, 'Unrecognized chain ID. Sillet only supports its built-in networks'),
  invalid: (m) => new RpcError(-32602, m || 'Invalid params'),
  pending: () => new RpcError(-32002, 'A request is already pending. Open Sillet to continue'),
  internal: (m) => new RpcError(-32603, m || 'Internal error'),
};

export function serializeError(e) {
  const code = typeof e?.code === 'number' ? e.code : -32603;
  return { code, message: String(e?.message || 'Internal error'), data: e?.data };
}
