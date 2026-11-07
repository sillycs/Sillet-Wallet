// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
using System.Security.Cryptography;
using Sillet.Storage;

namespace Sillet.Cli
{
    public static class WindowsProtection
    {
        private static readonly byte[] VaultEntropy = System.Text.Encoding.UTF8.GetBytes("Sillet.Vault.Device.v1|7c1f0e4b9a2d");
        private static readonly byte[] DataEntropy = System.Text.Encoding.UTF8.GetBytes("Sillet.Local.Data.v1|3e88d1a05b6f");

        public static void Use()
        {
            var scope = DataProtectionScope.CurrentUser;
            Vault.LocalProtect = d => ProtectedData.Protect(d, VaultEntropy, scope);
            Vault.LocalUnprotect = d => ProtectedData.Unprotect(d, VaultEntropy, scope);
            LocalData.Protect = d => ProtectedData.Protect(d, DataEntropy, scope);
            LocalData.Unprotect = d => ProtectedData.Unprotect(d, DataEntropy, scope);
        }
    }
}
