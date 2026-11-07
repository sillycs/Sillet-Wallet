// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
using System;
using System.Collections.Generic;
using System.Linq;
using Sillet.Crypto;
using Sillet.Encoding;

namespace Sillet.Chains.Solana
{
    public sealed class AccountMeta
    {
        public AccountMeta(byte[] publicKey, bool isSigner, bool isWritable)
        {
            PublicKey = publicKey;
            IsSigner = isSigner;
            IsWritable = isWritable;
        }

        public byte[] PublicKey { get; }
        public bool IsSigner { get; }
        public bool IsWritable { get; }
    }

    public sealed class SolanaInstruction
    {
        public SolanaInstruction(byte[] programId, IList<AccountMeta> accounts, byte[] data)
        {
            ProgramId = programId;
            Accounts = accounts;
            Data = data;
        }

        public byte[] ProgramId { get; }
        public IList<AccountMeta> Accounts { get; }
        public byte[] Data { get; }
    }

    public static class SolanaPrograms
    {
        public static readonly byte[] System = Base58.Bitcoin.Decode("11111111111111111111111111111111");
        public static readonly byte[] Token = Base58.Bitcoin.Decode("TokenkegQfeZyiNwAJbNbGKPFXCWuBvf9Ss623VQ5DA");
        public static readonly byte[] AssociatedToken = Base58.Bitcoin.Decode("ATokenGPvbdGVxr1b2hvZbsiqW5xWH25efTNsLJA8knL");

        public static SolanaInstruction TransferSol(byte[] from, byte[] to, ulong lamports)
        {
            var data = Bytes.Concat(Bytes.UInt32LE(2), Bytes.UInt64LE(lamports));
            return new SolanaInstruction(System, new[] { new AccountMeta(from, true, true), new AccountMeta(to, false, true) }, data);
        }

        public static SolanaInstruction TransferChecked(byte[] source, byte[] mint, byte[] destination, byte[] owner, ulong amount, byte decimals)
        {
            var data = Bytes.Concat(new byte[] { 12 }, Bytes.UInt64LE(amount), new[] { decimals });
            return new SolanaInstruction(Token, new[]
            {
                new AccountMeta(source, false, true), new AccountMeta(mint, false, false),
                new AccountMeta(destination, false, true), new AccountMeta(owner, true, false),
            }, data);
        }

        public static SolanaInstruction CreateAssociatedTokenAccountIdempotent(byte[] payer, byte[] ata, byte[] owner, byte[] mint)
        {
            return new SolanaInstruction(AssociatedToken, new[]
            {
                new AccountMeta(payer, true, true), new AccountMeta(ata, false, true), new AccountMeta(owner, false, false),
                new AccountMeta(mint, false, false), new AccountMeta(System, false, false), new AccountMeta(Token, false, false),
            }, new byte[] { 1 });
        }

        public static byte[] AssociatedTokenAddress(byte[] owner, byte[] mint)
        {
            byte bump;
            return FindProgramAddress(new[] { owner, Token, mint }, AssociatedToken, out bump);
        }

        public static byte[] FindProgramAddress(byte[][] seeds, byte[] programId, out byte bump)
        {
            var marker = global::System.Text.Encoding.ASCII.GetBytes("ProgramDerivedAddress");
            for (int candidate = 255; candidate >= 0; candidate--)
            {
                var buffer = Bytes.Concat(Bytes.Concat(seeds), new[] { (byte)candidate }, programId, marker);
                var hash = Hashes.Sha256(buffer);
                if (!Ed25519Keys.IsOnCurve(hash))
                {
                    bump = (byte)candidate;
                    return hash;
                }
            }
            throw new InvalidOperationException("No derived address was found");
        }
    }

    public sealed class SignedSolanaTransaction
    {
        public SignedSolanaTransaction(byte[] transaction, byte[] signature)
        {
            Transaction = transaction;
            Signature = signature;
        }

        public byte[] Transaction { get; }
        public byte[] Signature { get; }
    }

    public static class SolanaTransaction
    {
        private sealed class KeyEntry
        {
            public byte[] Key;
            public bool Signer;
            public bool Writable;
        }

        public static byte[] CompileMessage(byte[] feePayer, IList<SolanaInstruction> instructions, byte[] recentBlockhash)
        {
            var keys = new List<KeyEntry>();
            Action<byte[], bool, bool> add = (key, signer, writable) =>
            {
                var existing = keys.FirstOrDefault(k => Bytes.Equal(k.Key, key));
                if (existing == null)
                {
                    keys.Add(new KeyEntry { Key = key, Signer = signer, Writable = writable });
                }
                else
                {
                    existing.Signer |= signer;
                    existing.Writable |= writable;
                }
            };

            add(feePayer, true, true);
            foreach (var ix in instructions)
            {
                foreach (var meta in ix.Accounts) add(meta.PublicKey, meta.IsSigner, meta.IsWritable);
                add(ix.ProgramId, false, false);
            }

            var payer = keys[0];
            var rest = keys.Skip(1).OrderBy(k => Hex.Encode(k.Key), StringComparer.Ordinal).ToList();
            var ordered = new List<KeyEntry> { payer };
            ordered.AddRange(rest.Where(k => k.Signer && k.Writable));
            ordered.AddRange(rest.Where(k => k.Signer && !k.Writable));
            ordered.AddRange(rest.Where(k => !k.Signer && k.Writable));
            ordered.AddRange(rest.Where(k => !k.Signer && !k.Writable));
            Func<byte[], int> indexOf = key => ordered.FindIndex(k => Bytes.Equal(k.Key, key));

            var msg = new List<byte>
            {
                (byte)ordered.Count(k => k.Signer),
                (byte)ordered.Count(k => k.Signer && !k.Writable),
                (byte)ordered.Count(k => !k.Signer && !k.Writable),
            };
            msg.AddRange(CompactU16(ordered.Count));
            foreach (var k in ordered) msg.AddRange(k.Key);
            msg.AddRange(recentBlockhash);
            msg.AddRange(CompactU16(instructions.Count));
            foreach (var ix in instructions)
            {
                msg.Add((byte)indexOf(ix.ProgramId));
                msg.AddRange(CompactU16(ix.Accounts.Count));
                foreach (var meta in ix.Accounts) msg.Add((byte)indexOf(meta.PublicKey));
                msg.AddRange(CompactU16(ix.Data.Length));
                msg.AddRange(ix.Data);
            }
            return msg.ToArray();
        }

        public static SignedSolanaTransaction Sign(byte[] message, byte[] privateSeed)
        {
            var signature = Ed25519Keys.Sign(privateSeed, message);
            return new SignedSolanaTransaction(Bytes.Concat(CompactU16(1), signature, message), signature);
        }

        public static SignedSolanaTransaction WithSignature(byte[] message, byte[] signature)
        {
            return new SignedSolanaTransaction(Bytes.Concat(CompactU16(1), signature, message), signature);
        }

        private static byte[] CompactU16(int value)
        {
            var result = new List<byte>();
            while (true)
            {
                int b = value & 0x7f;
                value >>= 7;
                if (value == 0)
                {
                    result.Add((byte)b);
                    return result.ToArray();
                }
                result.Add((byte)(b | 0x80));
            }
        }
    }
}
