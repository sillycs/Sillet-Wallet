// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Sillet.Core;
using Sillet.Encoding;

namespace Sillet.Chains.Solana
{
    public sealed class SolanaParsedInstruction
    {
        public int ProgramIndex { get; set; }
        public int[] Accounts { get; set; }
        public byte[] Data { get; set; }
    }

    // A Solana transaction made by an app, read just enough to show it to the person and add our signature
    public sealed class SolanaParsedTransaction
    {
        public byte[] Raw { get; set; }
        public int SignatureCount { get; set; }
        public int SignaturesStart { get; set; }
        public int MessageStart { get; set; }
        public int RequiredSignatures { get; set; }
        public bool IsVersioned { get; set; }
        public bool UsesLookupTables { get; set; }
        public List<byte[]> Keys { get; } = new List<byte[]>();
        public List<SolanaParsedInstruction> Instructions { get; } = new List<SolanaParsedInstruction>();

        public byte[] Message
        {
            get { return Bytes.Slice(Raw, MessageStart, Raw.Length - MessageStart); }
        }
    }

    public sealed class SolanaSummary
    {
        public BigInteger SolOut { get; set; }
        public List<string> Recipients { get; } = new List<string>();
        public List<string> Lines { get; } = new List<string>();
        public List<string> Warnings { get; } = new List<string>();
    }

    public static class SolanaWire
    {
        private const string SystemProgram = "11111111111111111111111111111111";
        private const string TokenProgram = "TokenkegQfeZyiNwAJbNbGKPFXCWuBvf9Ss623VQ5DA";
        private const string Token2022Program = "TokenzQdBNbLqP5VEhdkAS6EPFLC1PHnBqCXEpPxuEb";
        private static readonly HashSet<string> Harmless = new HashSet<string>
        {
            "ComputeBudget111111111111111111111111111111",
            "ATokenGPvbdGVxr1b2hvZbsiqW5xWH25efTNsLJA8knL",
            "MemoSq4gqABAXKb96qnH8TysNcWxMyWCqXgDLGmfcHr",
            "Memo1UhkJRfHyvLMcVucJwxXeuD728EqVDDwQDxFMNo",
        };

        // ---------- reading ----------

        private static int ReadCompact(byte[] data, ref int pos)
        {
            int value = 0, shift = 0;
            for (int i = 0; i < 3; i++)
            {
                if (pos >= data.Length) throw new WalletException("The transaction is not valid");
                int b = data[pos++];
                value |= (b & 0x7f) << shift;
                if ((b & 0x80) == 0) return value;
                shift += 7;
            }
            throw new WalletException("The transaction is not valid");
        }

        private static void Need(byte[] data, int pos, int count)
        {
            if (count < 0 || pos < 0 || pos + count > data.Length) throw new WalletException("The transaction is not valid");
        }

        // Reads a whole transaction (signatures then message)
        public static SolanaParsedTransaction Parse(byte[] raw)
        {
            if (raw == null || raw.Length < 70 || raw.Length > 1232 + 200) throw new WalletException("The transaction is not valid");
            var pos = 0;
            var tx = new SolanaParsedTransaction { Raw = raw };
            tx.SignatureCount = ReadCompact(raw, ref pos);
            tx.SignaturesStart = pos;
            Need(raw, pos, tx.SignatureCount * 64);
            pos += tx.SignatureCount * 64;
            tx.MessageStart = pos;
            ParseMessage(raw, pos, tx, true);
            return tx;
        }

        // True if the bytes are exactly a Solana transaction message. A "message to sign" must never be one
        public static bool LooksLikeMessage(byte[] data)
        {
            try
            {
                var tx = new SolanaParsedTransaction { Raw = data };
                return ParseMessage(data, 0, tx, false) == data.Length;
            }
            catch (WalletException) { return false; }
            catch (Exception) { return false; }
        }

        private static int ParseMessage(byte[] raw, int pos, SolanaParsedTransaction tx, bool keep)
        {
            Need(raw, pos, 4);
            if ((raw[pos] & 0x80) != 0)
            {
                if ((raw[pos] & 0x7f) != 0) throw new WalletException("This kind of transaction is not supported yet");
                tx.IsVersioned = true;
                pos++;
            }
            tx.RequiredSignatures = raw[pos];
            pos += 3; // required signatures, read-only signed, read-only unsigned
            if (tx.RequiredSignatures == 0) throw new WalletException("The transaction is not valid");
            var keyCount = ReadCompact(raw, ref pos);
            if (keyCount < tx.RequiredSignatures || keyCount > 256) throw new WalletException("The transaction is not valid");
            Need(raw, pos, keyCount * 32);
            for (int i = 0; i < keyCount; i++) { tx.Keys.Add(Bytes.Slice(raw, pos, 32)); pos += 32; }
            Need(raw, pos, 32);
            pos += 32; // recent blockhash
            var instructionCount = ReadCompact(raw, ref pos);
            if (instructionCount > 64) throw new WalletException("The transaction is not valid");
            for (int i = 0; i < instructionCount; i++)
            {
                Need(raw, pos, 1);
                var ix = new SolanaParsedInstruction { ProgramIndex = raw[pos++] };
                var accounts = ReadCompact(raw, ref pos);
                Need(raw, pos, accounts);
                ix.Accounts = new int[accounts];
                for (int a = 0; a < accounts; a++) ix.Accounts[a] = raw[pos++];
                var length = ReadCompact(raw, ref pos);
                Need(raw, pos, length);
                ix.Data = Bytes.Slice(raw, pos, length);
                pos += length;
                if (ix.ProgramIndex >= keyCount) { if (!tx.IsVersioned) throw new WalletException("The transaction is not valid"); }
                tx.Instructions.Add(ix);
            }
            if (tx.IsVersioned)
            {
                var tables = ReadCompact(raw, ref pos);
                tx.UsesLookupTables = tables > 0;
                for (int t = 0; t < tables; t++)
                {
                    Need(raw, pos, 32);
                    pos += 32;
                    var writable = ReadCompact(raw, ref pos);
                    Need(raw, pos, writable);
                    pos += writable;
                    var readOnly = ReadCompact(raw, ref pos);
                    Need(raw, pos, readOnly);
                    pos += readOnly;
                }
            }
            return pos;
        }

        // ---------- signing ----------

        // Index of our key among the accounts that must sign, or -1
        public static int SignerIndex(SolanaParsedTransaction tx, byte[] publicKey)
        {
            for (int i = 0; i < tx.RequiredSignatures && i < tx.Keys.Count; i++)
                if (Bytes.Equal(tx.Keys[i], publicKey)) return i;
            return -1;
        }

        // Returns the transaction with our signature in its place. Other signatures stay as they were
        public static byte[] WithSignature(SolanaParsedTransaction tx, int index, byte[] signature)
        {
            var copy = (byte[])tx.Raw.Clone();
            Array.Copy(signature, 0, copy, tx.SignaturesStart + index * 64, 64);
            return copy;
        }

        // ---------- what it does, in plain words ----------

        public static SolanaSummary Describe(SolanaParsedTransaction tx, byte[] me)
        {
            var summary = new SolanaSummary();
            var unknown = new List<string>();
            Func<int, string> keyOf = i => i >= 0 && i < tx.Keys.Count ? Base58.Bitcoin.Encode(tx.Keys[i]) : "(from a lookup table)";
            foreach (var ix in tx.Instructions)
            {
                var program = keyOf(ix.ProgramIndex);
                var data = ix.Data;
                if (program == SystemProgram && data.Length >= 4)
                {
                    var kind = BitConverter.ToUInt32(LittleEndian(data, 0, 4), 0);
                    if (kind == 2 && data.Length >= 12 && ix.Accounts.Length >= 2)
                    {
                        var lamports = new BigInteger(Bytes.Concat(LittleEndian(data, 4, 8), new byte[] { 0 }));
                        var from = keyOf(ix.Accounts[0]);
                        var to = keyOf(ix.Accounts[1]);
                        summary.Lines.Add("Sends " + Units.FromRaw(lamports, 9) + " SOL to " + Short(to));
                        if (Bytes.Equal(ix.Accounts[0] < tx.Keys.Count ? tx.Keys[ix.Accounts[0]] : new byte[0], me))
                        {
                            summary.SolOut += lamports;
                            if (!summary.Recipients.Contains(to)) summary.Recipients.Add(to);
                        }
                        continue;
                    }
                    if (kind == 1 || kind == 9 || kind == 10 || kind == 11)
                    {
                        summary.Warnings.Add("This hands one of your accounts over to a program. Only sign it on a site you trust");
                        summary.Lines.Add("Changes who controls an account");
                        continue;
                    }
                    summary.Lines.Add("Runs a Solana system action");
                    continue;
                }
                if ((program == TokenProgram || program == Token2022Program) && data.Length >= 1)
                {
                    var kind = data[0];
                    if ((kind == 3 && data.Length >= 9) || (kind == 12 && data.Length >= 10))
                    {
                        var amount = new BigInteger(Bytes.Concat(LittleEndian(data, 1, 8), new byte[] { 0 }));
                        summary.Lines.Add("Moves " + amount + " token units" + (kind == 12 ? " (" + data[9] + " decimals)" : ""));
                        continue;
                    }
                    if (kind == 4 || kind == 13)
                    {
                        summary.Warnings.Add("This lets another address spend your tokens. Only sign it on a site you trust");
                        summary.Lines.Add("Lets another address spend your tokens");
                        continue;
                    }
                    if (kind == 6)
                    {
                        summary.Warnings.Add("This gives control of a token account to another address");
                        summary.Lines.Add("Changes who controls a token account");
                        continue;
                    }
                    if (kind == 9) { summary.Lines.Add("Closes a token account"); continue; }
                    if (kind == 8 || kind == 15) { summary.Lines.Add("Burns tokens"); continue; }
                    summary.Lines.Add("Runs a token action");
                    continue;
                }
                if (Harmless.Contains(program)) continue;
                if (!unknown.Contains(program)) unknown.Add(program);
            }
            if (unknown.Count > 0)
            {
                summary.Lines.Add("Calls " + unknown.Count + " other program" + (unknown.Count == 1 ? "" : "s") + ": " + string.Join(", ", unknown.Take(3).Select(Short)) + (unknown.Count > 3 ? "…" : ""));
                summary.Warnings.Add("Sillet cannot read everything this does. Only sign it if you trust the app");
            }
            if (tx.UsesLookupTables) summary.Warnings.Add("Part of this transaction is stored elsewhere and cannot be shown. Only sign it if you trust the app");
            if (summary.Lines.Count == 0) summary.Lines.Add("A Solana transaction");
            return summary;
        }

        private static byte[] LittleEndian(byte[] data, int start, int length)
        {
            return Bytes.Slice(data, start, length);
        }

        private static string Short(string key)
        {
            return key.Length > 14 ? key.Substring(0, 6) + "…" + key.Substring(key.Length - 6) : key;
        }
    }
}
