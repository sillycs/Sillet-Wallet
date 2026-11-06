using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace Sillet.Core
{
    public abstract class ChainBase : IChain
    {
        private string _address;
        private byte[] _privateKey;

        public abstract string Id { get; }
        public abstract string Name { get; }
        public abstract Asset Native { get; }
        public abstract string DerivationPath { get; }

        private readonly List<Asset> _custom = new List<Asset>();

        protected virtual IReadOnlyList<Asset> BuiltIn
        {
            get { return new[] { Native }; }
        }

        public IReadOnlyList<Asset> Assets
        {
            get { return _custom.Count == 0 ? BuiltIn : BuiltIn.Concat(_custom).ToList(); }
        }

        public virtual Task<IncomingTransfer> FindIncomingAsync(Asset asset, decimal amount, CancellationToken ct = default(CancellationToken))
        {
            return Task.FromResult<IncomingTransfer>(null);
        }

        public virtual string ReceiveAddress
        {
            get { return Address; }
        }

        public virtual System.Collections.Generic.IEnumerable<string> KnownAddresses
        {
            get { return new[] { Address }; }
        }

        public virtual decimal? PaymentWatchBalance(Asset asset)
        {
            return null;
        }

        public virtual Task<TxStage> GetTxStageAsync(string txId, CancellationToken ct = default(CancellationToken))
        {
            return Task.FromResult(TxStage.Unknown);
        }

        public virtual bool SupportsCustomTokens
        {
            get { return false; }
        }

        public virtual Task<Asset> LookupTokenAsync(string contract, CancellationToken ct = default(CancellationToken))
        {
            throw new WalletException(Name + " does not support custom tokens");
        }

        public bool HasToken(string contract)
        {
            return Assets.Any(a => a.Contract != null && string.Equals(a.Contract, contract, StringComparison.OrdinalIgnoreCase));
        }

        public void AddCustomAsset(Asset asset)
        {
            if (asset == null || asset.IsNative) throw new WalletException("Not a token");
            if (HasToken(asset.Contract)) throw new WalletException("This token is already in your list");
            asset.IsCustom = true;
            _custom.Add(asset);
        }

        public void RemoveCustomAsset(Asset asset)
        {
            _custom.RemoveAll(a => a.IsCustom && string.Equals(a.Contract, asset.Contract, StringComparison.OrdinalIgnoreCase));
        }

        public virtual bool SupportsReplacement { get { return false; } }

        public virtual Task<PreparedTransfer> PrepareReplacementAsync(string txId, bool cancel, Asset asset, decimal amount, CancellationToken ct = default(CancellationToken))
        {
            throw new WalletException("This network does not support speeding up a transaction");
        }

        public virtual bool SupportsFeeSpeed
        {
            get { return false; }
        }

        public FeeSpeed FeeSpeed { get; set; } = FeeSpeed.Normal;

        public int Account { get; set; }

        public bool IsLoaded
        {
            get { return _privateKey != null; }
        }

        public string Address
        {
            get
            {
                if (_address == null) throw new WalletException("The wallet is locked");
                return _address;
            }
        }

        public bool HasAddress
        {
            get { return _address != null; }
        }

        public bool IsWatchOnly
        {
            get { return _address != null && _privateKey == null; }
        }

        public Func<Task<Sillet.Hardware.ILedgerTransport>> LedgerConnect { get; set; }

        public virtual bool SupportsDeviceSigning { get { return false; } }

        public bool CanSignOnDevice { get { return SupportsDeviceSigning && IsWatchOnly && LedgerConnect != null; } }

        protected async Task<Sillet.Hardware.ILedgerTransport> OpenLedgerAsync()
        {
            var connect = LedgerConnect;
            var device = connect == null ? null : await connect();
            if (device == null) throw new WalletException("Connect your Ledger with a cable and try again");
            return device;
        }

        public void LoadWatch(string address)
        {
            Unload();
            if (string.IsNullOrWhiteSpace(address) || !IsValidAddress(address.Trim())) throw new WalletException("Invalid " + Name + " address");
            _address = address.Trim();
        }

        protected byte[] Key
        {
            get
            {
                if (IsWatchOnly) throw new WalletException("This network can't be signed with your hardware wallet here yet. Send from your hardware wallet app");
                if (_privateKey == null) throw new WalletException("The wallet is locked");
                return _privateKey;
            }
        }

        public virtual void Load(byte[] seed)
        {
            Unload();
            _privateKey = DeriveKey(seed);
            _address = ComputeAddress(_privateKey);
        }

        public virtual void Unload()
        {
            if (_privateKey != null) Array.Clear(_privateKey, 0, _privateKey.Length);
            _privateKey = null;
            _address = null;
        }

        protected abstract byte[] DeriveKey(byte[] seed);
        protected abstract string ComputeAddress(byte[] privateKey);

        public abstract bool IsValidAddress(string address);
        public abstract Task<AssetBalance> GetBalanceAsync(Asset asset, CancellationToken ct = default(CancellationToken));
        public abstract Task<PreparedTransfer> PrepareTransferAsync(Asset asset, string to, decimal amount, bool sendMax = false, CancellationToken ct = default(CancellationToken));
        protected abstract Task<string> BroadcastPayloadAsync(PreparedTransfer transfer, CancellationToken ct);
        public abstract string ExplorerAddressUrl(string address);
        public abstract string ExplorerTxUrl(string txId);

        public async Task<TransferResult> BroadcastAsync(PreparedTransfer transfer, CancellationToken ct = default(CancellationToken))
        {
            if (!ReferenceEquals(transfer.Chain, this)) throw new WalletException("This transfer belongs to another network");
            if (transfer.Broadcasted) throw new WalletException("This transfer was already broadcast");
            transfer.Broadcasted = true;
            var txId = await BroadcastPayloadAsync(transfer, ct).ConfigureAwait(false);
            return new TransferResult(txId, ExplorerTxUrl(txId));
        }

        protected AssetBalance Balance(Asset asset, BigInteger raw)
        {
            return new AssetBalance(asset, Units.FromRaw(raw, asset.Decimals), raw);
        }

        protected BigInteger CheckTransfer(Asset asset, string to, decimal amount, bool sendMax)
        {
            if (!Assets.Contains(asset)) throw new WalletException(asset.Symbol + " is not available on " + Name);
            if (!IsValidAddress(to)) throw new WalletException("The recipient address is not valid for " + Name);
            if (KnownAddresses.Any(a => string.Equals(to, a, StringComparison.OrdinalIgnoreCase))) throw new WalletException("You cannot send to your own address");
            if (sendMax) return BigInteger.Zero;
            var raw = Units.ToRaw(amount, asset.Decimals);
            if (raw <= 0) throw new WalletException("The amount must be greater than 0");
            return raw;
        }

        protected static WalletException Insufficient(Asset asset, BigInteger needed, BigInteger have)
        {
            return new WalletException("Insufficient " + asset.Symbol + ": you need " + Units.FromRaw(needed, asset.Decimals) +
                                       " and you have " + Units.FromRaw(have, asset.Decimals));
        }

        public override string ToString()
        {
            return Name;
        }
    }
}
