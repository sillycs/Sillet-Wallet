// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using Sillet.App.Services;
using Sillet.Chains.Cosmos;
using Sillet.Chains.Evm;
using Sillet.Chains.Solana;
using Sillet.Chains.Ton;
using Sillet.Chains.Tron;
using Sillet.Chains.Utxo;
using Sillet.Chains.Xrp;
using Sillet.Core;
using Sillet.Crypto;
using Sillet.Encoding;

namespace Sillet.Tests
{
    internal static class Program
    {
        private static int _failed;
        private static int _passed;
        private const string Abandon = "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about";

        private static void Check(string name, object expected, object actual)
        {
            if (Equals(expected, actual)) { _passed++; return; }
            _failed++;
            Console.WriteLine("FAIL " + name);
            Console.WriteLine("  expected " + expected);
            Console.WriteLine("  actual   " + actual);
        }

        private static string AddressOf(ChainBase chain, byte[] seed)
        {
            chain.Load(seed);
            return chain.Address;
        }

        private static int Main()
        {
            var seed = Bip39.ToSeed(Abandon);
            Check("BIP39 seed", "5eb00bbddcf069084889a8ab9155568165f5c453ccb85e70811aaed6f6da5fc19a5ac40b389cd370d086206dec8aa6c43daea6690f20ad3d8d48b2d2ce9e38e4", Hex.Encode(seed));
            Check("BIP39 valid", null, Bip39.Validate(Abandon));
            Check("BIP39 bad checksum", true, Bip39.Validate(Abandon.Replace("about", "abandon")) != null);
            var trezor = Bip39.ToSeed(Abandon, "TREZOR");
            Check("BIP39 passphrase seed", "c55257c360c07c72029aebc1b53c05ed0362ada38ead3e3e9efa3708e53495531f09a6987599d18264c1e1c92f2cf141630c7a3c4ab7c81b2f001698e7463b04", Hex.Encode(trezor));

            var vector = Hex.Decode("000102030405060708090a0b0c0d0e0f");
            byte[] chainCode;
            var node = HdDerivation.DeriveSecp256k1Node(vector, "m/0'", out chainCode);
            Check("BIP32 vector 1 m/0'", "edb2e14f9ee77d26dd93b4ecede8d16ed408ce149b6cd80b0715a2d911a0afea", Hex.Encode(node));
            Check("BIP32 vector 1 chain code m/0'", "47fdacbd0f1097043b78c63c20c34ef4ed9a111d980047ad16282c7ae6236141", Hex.Encode(chainCode));
            Check("SLIP-10 ed25519 vector 1 m/0'", "68e0fe46dfb67e368c75379acec591dad19df3cde26e63b93a8e704f1dade7a3", Hex.Encode(HdDerivation.DeriveEd25519(vector, "m/0'")));
            Check("SLIP-10 ed25519 vector 1 m/0'/1'", "b1d0bad404bf35da785a64ca1ac54b2617211d2777696fbffaf208f746ae84f2", Hex.Encode(HdDerivation.DeriveEd25519(vector, "m/0'/1'")));

            Check("Bitcoin address", "bc1qcr8te4kr609gcawutmrza0j4xv80jy8z306fyu", AddressOf(new BitcoinChain(), seed));
            Check("Ethereum address", "0x9858EfFD232B4033E47d90003D41EC34EcaEda94", AddressOf(new EthereumChain(), seed));
            Check("Solana address", "HAgk14JpMQLgt6rVgv7cBQFJWFto5Dqxi472uT3DKpqk", AddressOf(new SolanaChain(), seed));
            Check("Tron address", "TUEZSdKsoDHQMeZwihtdoBiN46zxhGWYdH", AddressOf(new TronChain(), seed));
            Check("XRP address", "rHsMGQEkVNJmpGWs8XUBoTBiAAbwxZN5v3", AddressOf(new XrpChain(), seed));
            Check("Dogecoin address", "DBus3bamQjgJULBJtYXpEzDWQRwF5iwxgC", AddressOf(new DogecoinChain(), seed));
            Check("Cosmos address", "cosmos19rl4cm2hmr8afy4kldpxz3fka4jguq0auqdal4", AddressOf(new CosmosChain(), seed));

            Check("CashAddr vector", "bitcoincash:qpm2qsznhks23z7629mms6s4cwef74vcwvy22gdx6a", CashAddr.Encode(0, Hex.Decode("76a04053bda0a88bda5177b86a15c3b29f559873")));

            var ethereum = new EthereumChain();
            Check("EVM accepts checksummed", true, ethereum.IsValidAddress("0x9858EfFD232B4033E47d90003D41EC34EcaEda94"));
            Check("EVM rejects bad checksum", false, ethereum.IsValidAddress("0x9858EFFD232B4033E47d90003D41EC34EcaEda94"));
            Check("Bitcoin rejects corrupted", false, new BitcoinChain().IsValidAddress("bc1qcr8te4kr609gcawutmrza0j4xv80jy8z306fyx"));

            var ton = new TonChain();
            Check("TON code hash", "feb5ff6820e2ff0d9483e7e0d62c817d846789fb4ae580c878866d959dabd5c0", Hex.Encode(TonCode()));
            var key = new byte[32];
            for (int i = 0; i < 32; i++) key[i] = 1;
            Check("TON wallet address", "UQDvr_S6wiD4iy6Y6x2c_8yjv-O2bs4xp9bFiQ0w39evpYZV", TonChain.Friendly(TonChain.AddressHash(Ed25519Keys.PublicKey(key)), false));

            var signingSeed = new byte[32];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(signingSeed);
            var publicKey = Hex.Encode(Ed25519Keys.PublicKey(signingSeed));
            var sha = new string('a', 64);
            var signature = Convert.ToBase64String(Ed25519Keys.Sign(signingSeed, UpdateKey.Message("1.2.3", sha, "notes")));
            Check("Update signature valid", true, UpdateKey.Verify(publicKey, "1.2.3", sha, "notes", signature));
            Check("Update signature rejects other version", false, UpdateKey.Verify(publicKey, "1.2.4", sha, "notes", signature));
            Check("Update signature rejects other hash", false, UpdateKey.Verify(publicKey, "1.2.3", new string('b', 64), "notes", signature));
            Check("Update signature rejects other notes", false, UpdateKey.Verify(publicKey, "1.2.3", sha, "other", signature));

            Console.WriteLine(_passed + " passed, " + _failed + " failed");
            return _failed == 0 ? 0 : 1;
        }

        private static byte[] TonCode()
        {
            var field = typeof(TonChain).GetField("CodeBoc", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            return TonCell.ParseBoc(Convert.FromBase64String((string)field.GetRawConstantValue())).Hash();
        }
    }
}
