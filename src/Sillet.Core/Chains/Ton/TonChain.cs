// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Sillet.Core;
using Sillet.Crypto;
using Sillet.Encoding;
using Sillet.Net;

namespace Sillet.Chains.Ton
{
    public sealed class TonChain : ChainBase
    {
        private const string CodeBoc = "te6ccgECFAEAAtQAART/APSkE/S88sgLAQIBIAIPAgFIAwYC5tAB0NMDIXGwkl8E4CLXScEgkl8E4ALTHyGCEHBsdWe9IoIQZHN0cr2wkl8F4AP6QDAg+kQByMoHy//J0O1E0IEBQNch9AQwXIEBCPQKb6Exs5JfB+AF0z/IJYIQcGx1Z7qSODDjDQOCEGRzdHK6kl8G4w0EBQB4AfoA9AQw+CdvIjBQCqEhvvLgUIIQcGx1Z4MesXCAGFAEywUmzxZY+gIZ9ADLaRfLH1Jgyz8gyYBA+wAGAIpQBIEBCPRZMO1E0IEBQNcgyAHPFvQAye1UAXKwjiOCEGRzdHKDHrFwgBhQBcsFUAPPFiP6AhPLassfyz/JgED7AJJfA+ICASAHDgIBIAgNAgFYCQoAPbKd+1E0IEBQNch9AQwAsjKB8v/ydABgQEI9ApvoTGACASALDAAZrc52omhAIGuQ64X/wAAZrx32omhAEGuQ64WPwAARuMl+1E0NcLH4AFm9JCtvaiaECAoGuQ+gIYRw1AgIR6STfSmRDOaQPp/5g3gSgBt4EBSJhxWfMYQE+PKDCNcYINMf0x/THwL4I7vyZO1E0NMf0x/T//QE0VFDuvKhUVG68qIF+QFUEGT5EPKj+AAkpMjLH1JAyx9SMMv/UhD0AMntVPgPAdMHIcAAn2xRkyDXSpbTB9QC+wDoMOAhwAHjACHAAuMAAcADkTDjDQOkyMsfEssfy/8QERITAG7SB/oA1NQi+QAFyMoHFcv/ydB3dIAYyMsFywIizxZQBfoCFMtrEszMyXP7AMhAFIEBCPRR8qcCAHCBAQjXGPoA0z/IVCBHgQEI9FHyp4IQbm90ZXB0gBjIywXLAlAGzxZQBPoCFMtqEssfyz/Jc/sAAgBsgQEI1xj6ANM/MFIkgQEI9Fnyp4IQZHN0cnB0gBjIywXLAlAFzxZQA/oCE8tqyx8Syz/Jc/sAAAr0AMntVA==";
        public const uint SubwalletId = 698983191;
        private static readonly BigInteger Fee = 10000000;
        private static TonCell _code;

        private readonly ApiPool _api = new ApiPool("ton", "https://toncenter.com/api/v2");
        private readonly ApiPool _api3 = new ApiPool("ton3", "https://toncenter.com/api/v3");

        public override string Id { get { return "ton"; } }
        public override string Name { get { return "TON"; } }
        public override Asset Native { get; } = new Asset("TON", "Toncoin", 9);
        public override string DerivationPath { get { return "m/44'/607'/" + Account + "'"; } }

        protected override byte[] DeriveKey(byte[] seed)
        {
            return HdDerivation.DeriveEd25519(seed, DerivationPath);
        }

        private static TonCell Code
        {
            get { return _code ?? (_code = TonCell.ParseBoc(Convert.FromBase64String(CodeBoc))); }
        }

        public static TonCell DataCell(byte[] publicKey)
        {
            return new TonBuilder().UInt(0, 32).UInt(SubwalletId, 32).Bytes(publicKey).Bit(false).Build();
        }

        public static TonCell StateInit(byte[] publicKey)
        {
            return new TonBuilder().Bit(false).Bit(false).Bit(true).Bit(true).Bit(false).Ref(Code).Ref(DataCell(publicKey)).Build();
        }

        public static byte[] AddressHash(byte[] publicKey)
        {
            return StateInit(publicKey).Hash();
        }

        private static ushort Crc16(byte[] data)
        {
            int crc = 0;
            foreach (var b in data)
            {
                crc ^= b << 8;
                for (int i = 0; i < 8; i++) crc = (crc & 0x8000) != 0 ? (crc << 1) ^ 0x1021 : crc << 1;
            }
            return (ushort)(crc & 0xFFFF);
        }

        public static string Friendly(byte[] hash, bool bounceable)
        {
            var body = new byte[34];
            body[0] = (byte)(bounceable ? 0x11 : 0x51);
            body[1] = 0;
            Array.Copy(hash, 0, body, 2, 32);
            var crc = Crc16(body);
            var full = Bytes.Concat(body, new[] { (byte)(crc >> 8), (byte)crc });
            return Convert.ToBase64String(full).Replace('+', '-').Replace('/', '_');
        }

        public static bool TryDecodeAddress(string text, out byte[] hash, out bool bounceable)
        {
            hash = null;
            bounceable = true;
            if (string.IsNullOrEmpty(text)) return false;
            text = text.Trim();
            if (text.StartsWith("0:", StringComparison.Ordinal) && text.Length == 66)
            {
                try { hash = Hex.Decode(text.Substring(2)); }
                catch (FormatException) { return false; }
                return hash.Length == 32;
            }
            if (text.Length != 48) return false;
            byte[] full;
            try { full = Convert.FromBase64String(text.Replace('-', '+').Replace('_', '/')); }
            catch (FormatException) { return false; }
            if (full.Length != 36) return false;
            var crc = Crc16(Bytes.Take(full, 34));
            if (full[34] != (byte)(crc >> 8) || full[35] != (byte)crc) return false;
            int tag = full[0] & ~0x80;
            if (tag != 0x11 && tag != 0x51) return false;
            if (full[1] != 0) return false;
            bounceable = tag == 0x11;
            hash = Bytes.Slice(full, 2, 32);
            return true;
        }

        public static bool TrySplit(string text, out string address, out string memo)
        {
            address = (text ?? "").Trim();
            memo = null;
            int cut = address.LastIndexOf(':');
            if (cut <= 1) return true;
            var head = address.Substring(0, cut);
            byte[] hash;
            bool bounce;
            if (!TryDecodeAddress(head, out hash, out bounce)) return true;
            memo = address.Substring(cut + 1);
            address = head;
            return true;
        }

        protected override string ComputeAddress(byte[] privateKey)
        {
            return Friendly(AddressHash(Ed25519Keys.PublicKey(privateKey)), false);
        }

        public override bool IsValidAddress(string address)
        {
            string a, memo;
            TrySplit(address, out a, out memo);
            byte[] hash;
            bool bounce;
            return TryDecodeAddress(a, out hash, out bounce);
        }

        public override string ExplorerAddressUrl(string address)
        {
            return "https://tonviewer.com/" + address;
        }

        public override string ExplorerTxUrl(string txId)
        {
            return "https://tonviewer.com/transaction/" + txId;
        }

        private static string Q(string address)
        {
            return Uri.EscapeDataString(address);
        }

        private static void Check(JsonValue response)
        {
            if (response.IsObject && response.Has("ok") && !response["ok"].IsTrue)
                throw new WalletException("TON network error: " + (response.Has("error") ? response["error"].AsString() : "request failed"));
        }

        public override async Task<AssetBalance> GetBalanceAsync(Asset asset, CancellationToken ct = default(CancellationToken))
        {
            var response = await _api.GetAsync("/getAddressBalance?address=" + Q(Address), ct).ConfigureAwait(false);
            Check(response);
            return Balance(asset, BigInteger.Parse(response["result"].AsString()));
        }

        private static TonCell InternalMessage(byte[] destination, bool bounce, BigInteger value, string memo)
        {
            var b = new TonBuilder().Bit(false).Bit(true).Bit(bounce).Bit(false).UInt(0, 2)
                .UInt(2, 2).Bit(false).UInt(0, 8).Bytes(destination)
                .Coins(value).Bit(false).Coins(0).Coins(0).UInt(0, 64).UInt(0, 32).Bit(false);
            if (string.IsNullOrEmpty(memo)) b.Bit(false);
            else
            {
                var text = new TonBuilder().UInt(0, 32).Bytes(Encoding_UTF8(memo)).Build();
                b.Bit(true).Ref(text);
            }
            return b.Build();
        }

        private static byte[] Encoding_UTF8(string text)
        {
            return System.Text.Encoding.UTF8.GetBytes(text);
        }

        public static TonCell SigningBody(uint validUntil, uint seqno, int mode, TonCell internalMessage)
        {
            return new TonBuilder().UInt(SubwalletId, 32).UInt(validUntil, 32).UInt(seqno, 32).UInt(0, 8).UInt(mode, 8).Ref(internalMessage).Build();
        }

        public static TonCell BuildTransfer(byte[] privateKey, uint seqno, uint validUntil, int mode, byte[] destination, bool bounce, BigInteger value, string memo, bool withInit)
        {
            var publicKey = Ed25519Keys.PublicKey(privateKey);
            var walletHash = AddressHash(publicKey);
            var internalMessage = InternalMessage(destination, bounce, value, memo);
            var signing = SigningBody(validUntil, seqno, mode, internalMessage);
            var signature = Ed25519Keys.Sign(privateKey, signing.Hash());
            var body = new TonBuilder().Bytes(signature).UInt(SubwalletId, 32).UInt(validUntil, 32).UInt(seqno, 32).UInt(0, 8).UInt(mode, 8).Ref(internalMessage).Build();
            var b = new TonBuilder().UInt(2, 2).UInt(0, 2).UInt(2, 2).Bit(false).UInt(0, 8).Bytes(walletHash).Coins(0);
            if (withInit) b.Bit(true).Bit(true).Ref(StateInit(publicKey));
            else b.Bit(false);
            b.Bit(true).Ref(body);
            return b.Build();
        }

        public override async Task<PreparedTransfer> PrepareTransferAsync(Asset asset, string to, decimal amount, bool sendMax = false, CancellationToken ct = default(CancellationToken))
        {
            string destination, memo;
            TrySplit(to, out destination, out memo);
            var raw = CheckTransfer(asset, destination, amount, sendMax);
            byte[] hash;
            bool bounce;
            TryDecodeAddress(destination, out hash, out bounce);

            var info = await _api.GetAsync("/getWalletInformation?address=" + Q(Address), ct).ConfigureAwait(false);
            Check(info);
            var result = info["result"];
            var balanceRaw = await _api.GetAsync("/getAddressBalance?address=" + Q(Address), ct).ConfigureAwait(false);
            Check(balanceRaw);
            var total = BigInteger.Parse(balanceRaw["result"].AsString());
            JsonValue seq;
            uint seqno = 0;
            if (result.TryGet("seqno", out seq) && !seq.IsNull) seqno = seq.AsUInt();
            bool active = result.Has("account_state") && result["account_state"].AsString() == "active";

            BigInteger value = sendMax ? total : raw;
            if (!sendMax && value + Fee > total) throw Insufficient(Native, value + Fee, total);
            if (sendMax && total <= Fee) throw new WalletException("You do not have enough TON to cover the network fee");
            int mode = sendMax ? 128 : 3;

            uint validUntil = (uint)(DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 300);
            bool withInit = !active || seqno == 0;
            var message = BuildTransfer(Key, seqno, validUntil, mode, hash, bounce, value, memo, withInit);

            var transfer = new PreparedTransfer
            {
                Chain = this,
                Asset = asset,
                To = destination + (string.IsNullOrEmpty(memo) ? "" : ":" + memo),
                Amount = Units.FromRaw(sendMax ? total - Fee : value, 9),
                Fee = Units.FromRaw(Fee, 9),
                FeeAsset = Native,
                TxId = Hex.Encode(message.Hash()),
                Payload = Convert.ToBase64String(message.ToBoc()),
            };
            if (bounce && !string.IsNullOrEmpty(memo)) transfer.Notes.Add("Exchanges usually need a memo and a non-bounceable address");
            return transfer;
        }

        protected override async Task<string> BroadcastPayloadAsync(PreparedTransfer transfer, CancellationToken ct)
        {
            var response = await _api.PostAsync("/sendBoc", new { boc = (string)transfer.Payload }, ct).ConfigureAwait(false);
            Check(response);
            JsonValue result;
            if (response.TryGet("result", out result) && result.IsObject && result.Has("hash")) return Hex.Encode(Convert.FromBase64String(result["hash"].AsString()));
            return transfer.TxId;
        }

        public override async Task<TxStage> GetTxStageAsync(string txId, CancellationToken ct = default(CancellationToken))
        {
            try
            {
                var response = await _api3.GetAsync("/transactionsByMessage?direction=in&msg_hash=" + txId + "&limit=1", ct).ConfigureAwait(false);
                if (!response.Has("transactions") || response["transactions"].Count == 0) return TxStage.Pending;
                var tx = response["transactions"][0];
                JsonValue description;
                if (tx.TryGet("description", out description) && description.IsObject && description.Has("aborted") && description["aborted"].IsTrue) return TxStage.Failed;
                return TxStage.Final;
            }
            catch (Exception e) when (e is WalletException || e is KeyNotFoundException || e is InvalidOperationException || e is FormatException)
            {
                return TxStage.Pending;
            }
        }

        public override async Task<IncomingTransfer> FindIncomingAsync(Asset asset, decimal amount, CancellationToken ct = default(CancellationToken))
        {
            try
            {
                var raw = Units.ToRaw(amount, 9);
                var response = await _api.GetAsync("/getTransactions?limit=15&address=" + Q(Address), ct).ConfigureAwait(false);
                if (!response.Has("result")) return null;
                foreach (var tx in response["result"].Items)
                {
                    JsonValue inMsg;
                    if (!tx.TryGet("in_msg", out inMsg) || !inMsg.IsObject) continue;
                    if (!inMsg.Has("value") || BigInteger.Parse(inMsg["value"].AsString()) != raw) continue;
                    var source = inMsg.Has("source") ? inMsg["source"].AsString() : "";
                    if (string.IsNullOrEmpty(source)) continue;
                    var id = tx["transaction_id"]["hash"].AsString();
                    return new IncomingTransfer(Hex.Encode(Convert.FromBase64String(id)), source, amount);
                }
            }
            catch (Exception e) when (e is WalletException || e is KeyNotFoundException || e is InvalidOperationException || e is FormatException)
            {
            }
            return null;
        }
    }
}
