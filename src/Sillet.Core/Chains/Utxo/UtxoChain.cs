// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Sillet.Core;
using Sillet.Crypto;
using Sillet.Encoding;

namespace Sillet.Chains.Utxo
{
    public enum UtxoAddressType
    {
        P2WPKH,
        P2PKH,
    }

    public abstract class UtxoChain : ChainBase
    {
        protected abstract UtxoAddressType AddressType { get; }
        protected abstract string Bech32Hrp { get; }
        protected abstract byte P2pkhVersion { get; }
        protected abstract byte[] P2shVersions { get; }
        protected abstract IUtxoProvider Provider { get; }
        protected abstract string ExplorerBase { get; }

        public override bool SupportsFeeSpeed
        {
            get { return true; }
        }

        protected virtual long DustLimit
        {
            get { return AddressType == UtxoAddressType.P2WPKH ? 294 : 546; }
        }

        protected virtual decimal MinFeeRate
        {
            get { return 1m; }
        }

        protected virtual uint TxVersion
        {
            get { return 2; }
        }

        protected virtual uint Sequence
        {
            get { return 0xFFFFFFFD; }
        }

        private const int MaxChangeScan = 40;
        private byte[] _changeKey;
        private byte[] _changeChain;
        private byte[] _extKey;
        private byte[] _extChain;
        private string _receiveAddress;
        private string[] _recentReceive;
        private string[] _known;

        public static bool RotateReceive { get; set; }

        public override string ReceiveAddress
        {
            get { return RotateReceive && HasChangeKeys && _receiveAddress != null ? _receiveAddress : Address; }
        }

        public override IEnumerable<string> KnownAddresses
        {
            get { return _known != null ? _known.Concat(new[] { Address }).Distinct().ToArray() : new[] { Address }; }
        }

        protected override byte[] DeriveKey(byte[] seed)
        {
            return HdDerivation.DeriveSecp256k1(seed, DerivationPath);
        }

        public override void Load(byte[] seed)
        {
            base.Load(seed);
            var parts = DerivationPath.Split('/');
            var node = string.Join("/", parts.Take(parts.Length - 2)) + "/1";
            _changeKey = HdDerivation.DeriveSecp256k1Node(seed, node, out _changeChain);
            var external = string.Join("/", parts.Take(parts.Length - 1));
            _extKey = HdDerivation.DeriveSecp256k1Node(seed, external, out _extChain);
        }

        public override void Unload()
        {
            base.Unload();
            _receiveBalance = null;
            if (_changeKey != null) Array.Clear(_changeKey, 0, _changeKey.Length);
            if (_changeChain != null) Array.Clear(_changeChain, 0, _changeChain.Length);
            if (_extKey != null) Array.Clear(_extKey, 0, _extKey.Length);
            if (_extChain != null) Array.Clear(_extChain, 0, _extChain.Length);
            _changeKey = null;
            _changeChain = null;
            _extKey = null;
            _extChain = null;
            _receiveAddress = null;
            _recentReceive = null;
            _known = null;
        }

        public bool HasChangeKeys
        {
            get { return _changeKey != null; }
        }

        private sealed class OwnedAddress
        {
            public int Index;
            public byte[] Key;
            public string Address;
            public long Balance;
        }

        private sealed class Scan
        {
            public readonly List<OwnedAddress> UsedReceive = new List<OwnedAddress>();
            public OwnedAddress NextReceive;
            public readonly List<OwnedAddress> UsedChange = new List<OwnedAddress>();
            public OwnedAddress NextChange;
            public long Total;
        }

        private decimal? _receiveBalance;

        public override decimal? PaymentWatchBalance(Asset asset)
        {
            return _receiveBalance;
        }

        private OwnedAddress ChangeAt(int index)
        {
            var key = HdDerivation.ChildKey(_changeKey, _changeChain, (uint)index);
            return new OwnedAddress { Index = index, Key = key, Address = ComputeAddress(key) };
        }

        private OwnedAddress ReceiveAt(int index)
        {
            var key = HdDerivation.ChildKey(_extKey, _extChain, (uint)index);
            return new OwnedAddress { Index = index, Key = key, Address = ComputeAddress(key) };
        }

        private async Task ScanChangeAsync(Scan scan, CancellationToken ct)
        {
            for (int i = 0; i < MaxChangeScan; i++)
            {
                var slot = ChangeAt(i);
                var info = await Provider.GetAddressInfoAsync(slot.Address, ct);
                if (info.TxCount == 0)
                {
                    scan.NextChange = slot;
                    break;
                }
                slot.Balance = info.Balance;
                scan.UsedChange.Add(slot);
            }
        }

        private async Task ScanReceiveAsync(Scan scan, CancellationToken ct)
        {
            for (int i = 0; i < MaxChangeScan; i++)
            {
                var slot = ReceiveAt(i);
                var info = await Provider.GetAddressInfoAsync(slot.Address, ct);
                slot.Balance = info.Balance;
                if (info.TxCount == 0)
                {
                    scan.NextReceive = slot;
                    scan.UsedReceive.Add(slot);
                    break;
                }
                scan.UsedReceive.Add(slot);
                if (i == MaxChangeScan - 1) scan.NextReceive = slot;
            }
        }

        private async Task<Scan> ScanAsync(CancellationToken ct)
        {
            var scan = new Scan();
            await Task.WhenAll(ScanReceiveAsync(scan, ct), ScanChangeAsync(scan, ct));
            var received = scan.UsedReceive.Sum(a => a.Balance);
            _receiveBalance = Units.FromRaw(received, Native.Decimals);
            _receiveAddress = scan.NextReceive == null ? null : scan.NextReceive.Address;
            var usedWithHistory = scan.UsedReceive.Where(a => a != scan.NextReceive).Select(a => a.Address).ToList();
            _recentReceive = usedWithHistory.Skip(Math.Max(0, usedWithHistory.Count - 2)).Concat(RotateReceive && _receiveAddress != null ? new[] { _receiveAddress } : new string[0]).Distinct().ToArray();
            _known = scan.UsedReceive.Select(a => a.Address).Concat(scan.UsedChange.Select(a => a.Address)).Concat(scan.NextChange == null ? new string[0] : new[] { scan.NextChange.Address }).Distinct().ToArray();
            scan.Total = received + scan.UsedChange.Sum(a => a.Balance);
            return scan;
        }

        protected override string ComputeAddress(byte[] privateKey)
        {
            var hash = Hashes.Hash160(Secp256k1.PublicKey(privateKey, true));
            return AddressType == UtxoAddressType.P2WPKH
                ? Bech32.EncodeSegwit(Bech32Hrp, 0, hash)
                : Base58.Bitcoin.EncodeCheck(Bytes.Concat(new[] { P2pkhVersion }, hash));
        }

        protected virtual bool UsesForkId { get { return false; } }

        // Text that is signed when an app asks for a message signature. Never a transaction
        protected virtual string MessagePrefix { get { return "Bitcoin Signed Message:\n"; } }

        public bool CanSignDirectly
        {
            get { return IsLoaded && !IsWatchOnly; }
        }

        public string PublicKeyHex
        {
            get { return Hex.Encode(Secp256k1.PublicKey(Key, true)); }
        }

        private static byte[] VarInt(int value)
        {
            if (value < 0xfd) return new[] { (byte)value };
            return new[] { (byte)0xfd, (byte)(value & 0xff), (byte)(value >> 8) };
        }

        public byte[] MessageHash(byte[] message)
        {
            if (message.Length > 60000) throw new WalletException("That message is too long to sign");
            var prefix = System.Text.Encoding.UTF8.GetBytes(MessagePrefix);
            return Hashes.Sha256d(Bytes.Concat(VarInt(prefix.Length), prefix, VarInt(message.Length), message));
        }

        // Standard signed message of the first address (base64, 65 bytes). Segwit addresses use the 39 to 42 header (BIP 137)
        public string SignMessageBase64(byte[] message)
        {
            var signature = Secp256k1.Sign(MessageHash(message), Key);
            var header = (byte)((AddressType == UtxoAddressType.P2WPKH ? 39 : 31) + signature.RecoveryId);
            return Convert.ToBase64String(Bytes.Concat(new[] { header }, signature.R, signature.S));
        }

        public virtual byte[] ScriptFor(string address)
        {
            address = address.Trim();
            if (Bech32Hrp != null && address.StartsWith(Bech32Hrp + "1", StringComparison.OrdinalIgnoreCase))
            {
                var segwit = Bech32.DecodeSegwit(Bech32Hrp, address);
                return segwit == null ? null : UtxoTransactionBuilder.WitnessScript(segwit.Version, segwit.Program);
            }
            var payload = Base58.Bitcoin.DecodeCheck(address);
            if (payload == null || payload.Length != 21) return null;
            if (payload[0] == P2pkhVersion) return UtxoTransactionBuilder.P2pkhScript(Bytes.Skip(payload, 1));
            if (P2shVersions.Contains(payload[0])) return UtxoTransactionBuilder.P2shScript(Bytes.Skip(payload, 1));
            return null;
        }

        public override bool IsValidAddress(string address)
        {
            return address != null && ScriptFor(address) != null;
        }

        public override string ExplorerAddressUrl(string address)
        {
            return ExplorerBase + "/address/" + address;
        }

        public override string ExplorerTxUrl(string txId)
        {
            return ExplorerBase + "/tx/" + txId;
        }

        public override async Task<IncomingTransfer> FindIncomingAsync(Asset asset, decimal amount, CancellationToken ct = default(CancellationToken))
        {
            try
            {
                var raw = (long)Units.ToRaw(amount, Native.Decimals);
                var candidates = _recentReceive != null && _recentReceive.Length > 0 ? _recentReceive : new[] { Address };
                foreach (var candidate in candidates)
                    foreach (var tx in await Provider.GetRecentTxsAsync(candidate, ct))
                        if (tx.Received == raw) return new IncomingTransfer(tx.TxId, tx.From, amount);
            }
            catch (Exception e) when (e is WalletException || e is KeyNotFoundException || e is InvalidOperationException || e is FormatException || e is System.Net.Http.HttpRequestException)
            {
            }
            return null;
        }

        private const int UtxoFinalDepth = 3;

        public override async Task<TxStage> GetTxStageAsync(string txId, CancellationToken ct = default(CancellationToken))
        {
            try
            {
                var confirmations = await Provider.GetConfirmationsAsync(txId, ct);
                if (confirmations < 0) return TxStage.Unknown;
                if (confirmations == 0) return TxStage.Pending;
                return confirmations >= UtxoFinalDepth ? TxStage.Final : TxStage.Confirmed;
            }
            catch (Exception e) when (e is WalletException || e is KeyNotFoundException || e is InvalidOperationException || e is FormatException || e is System.Net.Http.HttpRequestException)
            {
                return TxStage.Unknown;
            }
        }

        public override async Task<AssetBalance> GetBalanceAsync(Asset asset, CancellationToken ct = default(CancellationToken))
        {
            if (!HasChangeKeys || IsWatchOnly) return Balance(asset, await Provider.GetBalanceAsync(Address, ct));
            return Balance(asset, (await ScanAsync(ct)).Total);
        }

        public override async Task<PreparedTransfer> PrepareTransferAsync(Asset asset, string to, decimal amount, bool sendMax = false, CancellationToken ct = default(CancellationToken))
        {
            var amountRaw = (long)CheckTransfer(asset, to, amount, sendMax);
            var destScript = ScriptFor(to);
            var rateTask = Provider.GetFeeRateAsync(FeeSpeed, ct);
            var owned = new List<OwnedAddress>();
            byte[] changeScript;
            if (HasChangeKeys)
            {
                var scan = await ScanAsync(ct);
                owned.AddRange(scan.UsedReceive);
                owned.AddRange(scan.UsedChange);
                if (scan.NextChange == null) throw new WalletException("Too many used change addresses. Move your funds to a new account");
                changeScript = ScriptFor(scan.NextChange.Address);
            }
            else
            {
                owned.Add(new OwnedAddress { Index = -1, Key = Key, Address = Address });
                changeScript = ScriptFor(Address);
            }
            var utxoTasks = owned.Where(a => a.Balance != 0 || !HasChangeKeys).Select(async a =>
            {
                var list = await Provider.GetUtxosAsync(a.Address, ct);
                foreach (var u in list) u.Key = a.Key;
                return list;
            }).ToList();
            var rate = Math.Max(await rateTask, MinFeeRate);
            var found = new List<Utxo>();
            foreach (var t in utxoTasks) found.AddRange(await t);
            var utxos = found.OrderByDescending(u => u.Confirmed).ThenByDescending(u => u.Value).ToList();
            if (utxos.Count == 0) throw new WalletException("You have no " + Native.Symbol + " available to send");

            Func<int, byte[][], long> fee = (inputs, scripts) => (long)Math.Ceiling(EstimateSize(inputs, scripts) * rate);

            var selected = new List<Utxo>();
            var outputs = new List<TxOutput>();
            long total = 0;

            if (sendMax)
            {
                selected.AddRange(utxos);
                total = selected.Sum(u => u.Value);
                amountRaw = total - fee(selected.Count, new[] { destScript });
                if (amountRaw < DustLimit) throw new WalletException("The balance does not cover the fee");
                outputs.Add(new TxOutput(amountRaw, destScript));
            }
            else
            {
                if (amountRaw < DustLimit)
                    throw new WalletException("The minimum amount is " + Units.FromRaw(DustLimit, Native.Decimals) + " " + Native.Symbol);
                foreach (var utxo in utxos)
                {
                    selected.Add(utxo);
                    total += utxo.Value;
                    long withChange = fee(selected.Count, new[] { destScript, changeScript });
                    if (total >= amountRaw + withChange + DustLimit)
                    {
                        outputs.Add(new TxOutput(amountRaw, destScript));
                        outputs.Add(new TxOutput(total - amountRaw - withChange, changeScript));
                        break;
                    }
                    if (total >= amountRaw + fee(selected.Count, new[] { destScript }))
                    {
                        outputs.Add(new TxOutput(amountRaw, destScript));
                        break;
                    }
                }
                if (outputs.Count == 0)
                {
                    var available = utxos.Sum(u => u.Value);
                    throw Insufficient(Native, amountRaw + fee(utxos.Count, new[] { destScript }), available);
                }
            }

            var signed = AddressType == UtxoAddressType.P2WPKH
                ? UtxoTransactionBuilder.SignP2wpkh(selected, outputs, u => u.Key ?? Key, TxVersion, Sequence)
                : UsesForkId
                    ? UtxoTransactionBuilder.SignBchP2pkh(selected, outputs, u => u.Key ?? Key, TxVersion, Sequence)
                    : UtxoTransactionBuilder.SignP2pkh(selected, outputs, u => u.Key ?? Key, TxVersion, Sequence);

            long paidFee = total - outputs.Sum(o => o.Value);
            var transfer = new PreparedTransfer
            {
                Chain = this,
                Asset = asset,
                To = to,
                Amount = Units.FromRaw(amountRaw, Native.Decimals),
                Fee = Units.FromRaw(paidFee, Native.Decimals),
                FeeAsset = Native,
                TxId = signed.TxId,
                Payload = Hex.Encode(signed.Raw),
            };
            if (selected.Any(u => !u.Confirmed)) transfer.Notes.Add("Uses unconfirmed funds; it may take longer");
            if (HasChangeKeys && outputs.Count > 1) transfer.Notes.Add("The change goes to a new address of your wallet");
            if (paidFee * 10 > amountRaw) transfer.Notes.Add("The fee is more than 10% of the amount");
            return transfer;
        }

        public override bool SupportsReplacement
        {
            get { return Sequence == 0xFFFFFFFD && !IsWatchOnly; }
        }

        public override async Task<PreparedTransfer> PrepareReplacementAsync(string txId, bool cancel, Asset asset, decimal amount, CancellationToken ct = default(CancellationToken))
        {
            if (!SupportsReplacement) throw new WalletException("This network does not support speeding up a transaction");
            var detail = await Provider.GetTxDetailAsync(txId, ct);
            if (detail == null) throw new WalletException("The network no longer has this transaction. Send it again instead");
            if (detail.Confirmed) throw new WalletException("This transaction is already confirmed");

            var keys = new Dictionary<string, byte[]>();
            byte[] freshScript;
            if (HasChangeKeys)
            {
                var scan = await ScanAsync(ct);
                foreach (var a in scan.UsedReceive.Concat(scan.UsedChange)) keys[a.Address] = a.Key;
                if (scan.NextChange == null) throw new WalletException("Too many used change addresses. Move your funds to a new account");
                freshScript = ScriptFor(scan.NextChange.Address);
            }
            else
            {
                keys[Address] = Key;
                freshScript = ScriptFor(Address);
            }

            var inputs = new List<Utxo>();
            foreach (var vin in detail.Inputs)
            {
                byte[] key;
                if (!keys.TryGetValue(vin.Address ?? "", out key)) throw new WalletException("This transaction was not created by this wallet, so it can't be sped up here");
                inputs.Add(new Utxo(vin.TxId, vin.Vout, vin.Value) { Key = key });
            }
            long totalIn = inputs.Sum(u => u.Value);
            long oldFee = totalIn - detail.Outputs.Sum(o => o.Value);
            var rate = Math.Max(await Provider.GetFeeRateAsync(FeeSpeed.Fast, ct), MinFeeRate);

            var outputs = new List<TxOutput>();
            long newFee;
            var notes = new List<string>();
            if (cancel)
            {
                var size = EstimateSize(inputs.Count, new[] { freshScript });
                newFee = Math.Max((long)Math.Ceiling(size * rate), Math.Max(oldFee + (long)Math.Ceiling(size) + 1, (long)Math.Ceiling(oldFee * 1.2m)));
                if (totalIn - newFee < DustLimit) throw new WalletException("There is not enough in this transaction to cover a higher fee");
                outputs.Add(new TxOutput(totalIn - newFee, freshScript));
                notes.Add("This cancels the payment: the funds go back to your own wallet");
            }
            else
            {
                var scripts = detail.Outputs.Select(o => o.Script).ToArray();
                var size = EstimateSize(inputs.Count, scripts);
                newFee = Math.Max((long)Math.Ceiling(size * rate), Math.Max(oldFee + (long)Math.Ceiling(size) + 1, (long)Math.Ceiling(oldFee * 1.2m)));
                long extra = newFee - oldFee;
                var rows = detail.Outputs.Select(o => new KeyValuePair<TxDetailOutput, bool>(o, keys.ContainsKey(o.Address ?? ""))).ToList();
                var change = rows.LastOrDefault(r => r.Value);
                if (change.Key != null)
                {
                    long left = change.Key.Value - extra;
                    foreach (var r in rows)
                    {
                        if (r.Key == change.Key)
                        {
                            if (left >= DustLimit) outputs.Add(new TxOutput(left, r.Key.Script));
                            else newFee = totalIn - detail.Outputs.Where(o => o != change.Key).Sum(o => o.Value);
                        }
                        else outputs.Add(new TxOutput(r.Key.Value, r.Key.Script));
                    }
                }
                else
                {
                    var biggest = rows.OrderByDescending(r => r.Key.Value).First();
                    long left = biggest.Key.Value - extra;
                    if (left < DustLimit) throw new WalletException("There is not enough in this transaction to cover a higher fee");
                    foreach (var r in rows) outputs.Add(new TxOutput(r.Key == biggest.Key ? left : r.Key.Value, r.Key.Script));
                    notes.Add("The extra fee comes out of the amount, so the recipient gets a little less");
                }
            }

            var signed = AddressType == UtxoAddressType.P2WPKH
                ? UtxoTransactionBuilder.SignP2wpkh(inputs, outputs, u => u.Key ?? Key, TxVersion, Sequence)
                : UtxoTransactionBuilder.SignP2pkh(inputs, outputs, u => u.Key ?? Key, TxVersion, Sequence);
            long paidFee = totalIn - outputs.Sum(o => o.Value);
            var transfer = new PreparedTransfer
            {
                Chain = this,
                Asset = cancel ? Native : (asset ?? Native),
                To = cancel ? Address : detail.Outputs.Where(o => !keys.ContainsKey(o.Address ?? "")).Select(o => o.Address).FirstOrDefault() ?? Address,
                Amount = cancel ? Units.FromRaw(outputs[0].Value, Native.Decimals) : amount,
                Fee = Units.FromRaw(paidFee, Native.Decimals),
                FeeAsset = Native,
                TxId = signed.TxId,
                Payload = Hex.Encode(signed.Raw),
            };
            transfer.Notes.Add("Replaces the pending transaction with the same payment and a higher fee");
            transfer.Notes.AddRange(notes);
            return transfer;
        }

        protected override Task<string> BroadcastPayloadAsync(PreparedTransfer transfer, CancellationToken ct)
        {
            return Provider.BroadcastAsync((string)transfer.Payload, ct);
        }

        private decimal EstimateSize(int inputs, byte[][] outputScripts)
        {
            decimal outputs = outputScripts.Sum(s => 8 + 1 + s.Length);
            decimal countBytes = inputs > 252 ? 2 : 0;
            return AddressType == UtxoAddressType.P2WPKH
                ? 10.75m + 68m * inputs + outputs + countBytes
                : 10m + 148m * inputs + outputs + countBytes;
        }
    }
}
