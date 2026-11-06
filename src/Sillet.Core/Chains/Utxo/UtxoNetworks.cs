using Sillet.Core;
using Sillet.Crypto;
using Sillet.Encoding;

namespace Sillet.Chains.Utxo
{
    public sealed class BitcoinChain : UtxoChain
    {
        private readonly IUtxoProvider _provider = new EsploraProvider("bitcoin", "https://mempool.space/api", "https://blockstream.info/api");

        public override string Id { get { return "bitcoin"; } }
        public override string Name { get { return "Bitcoin"; } }
        public override Asset Native { get; } = new Asset("BTC", "Bitcoin", 8);
        public override string DerivationPath { get { return "m/84'/0'/" + Account + "'/0/0"; } }
        protected override UtxoAddressType AddressType { get { return UtxoAddressType.P2WPKH; } }
        protected override string Bech32Hrp { get { return "bc"; } }
        protected override byte P2pkhVersion { get { return 0x00; } }
        protected override byte[] P2shVersions { get { return new byte[] { 0x05 }; } }
        protected override string ExplorerBase { get { return "https://mempool.space"; } }
        protected override IUtxoProvider Provider { get { return _provider; } }
    }

    public sealed class LitecoinChain : UtxoChain
    {
        private readonly IUtxoProvider _provider = new EsploraProvider("litecoin", "https://litecoinspace.org/api");

        public override string Id { get { return "litecoin"; } }
        public override string Name { get { return "Litecoin"; } }
        public override Asset Native { get; } = new Asset("LTC", "Litecoin", 8);
        public override string DerivationPath { get { return "m/84'/2'/" + Account + "'/0/0"; } }
        protected override UtxoAddressType AddressType { get { return UtxoAddressType.P2WPKH; } }
        protected override string Bech32Hrp { get { return "ltc"; } }
        protected override byte P2pkhVersion { get { return 0x30; } }
        protected override byte[] P2shVersions { get { return new byte[] { 0x32, 0x05 }; } }
        protected override string ExplorerBase { get { return "https://litecoinspace.org"; } }
        protected override IUtxoProvider Provider { get { return _provider; } }
    }

    public sealed class DogecoinChain : UtxoChain
    {
        private readonly IUtxoProvider _provider = new BlockCypherProvider("doge");

        public override string Id { get { return "dogecoin"; } }
        public override string Name { get { return "Dogecoin"; } }
        public override Asset Native { get; } = new Asset("DOGE", "Dogecoin", 8);
        public override string DerivationPath { get { return "m/44'/3'/" + Account + "'/0/0"; } }
        protected override UtxoAddressType AddressType { get { return UtxoAddressType.P2PKH; } }
        protected override string Bech32Hrp { get { return null; } }
        protected override byte P2pkhVersion { get { return 0x1e; } }
        protected override byte[] P2shVersions { get { return new byte[] { 0x16 }; } }
        protected override long DustLimit { get { return 1000000; } }
        protected override decimal MinFeeRate { get { return 1000m; } }
        protected override uint TxVersion { get { return 1; } }
        protected override uint Sequence { get { return 0xFFFFFFFF; } }
        protected override string ExplorerBase { get { return "https://dogechain.info"; } }
        protected override IUtxoProvider Provider { get { return _provider; } }
    }
}

namespace Sillet.Chains.Utxo
{
    public sealed class BitcoinCashChain : UtxoChain
    {
        private readonly IUtxoProvider _provider = new BlockbookProvider("bitcoincash", Normalize, "https://bch1.trezor.io/api/v2", "https://bch2.trezor.io/api/v2");

        private static string Normalize(string address)
        {
            int type;
            byte[] hash;
            return CashAddr.TryDecode(address, out type, out hash) ? CashAddr.Encode(type, hash, false) : (address ?? "");
        }

        public override string Id { get { return "bitcoincash"; } }
        public override string Name { get { return "Bitcoin Cash"; } }
        public override Asset Native { get; } = new Asset("BCH", "Bitcoin Cash", 8);
        public override string DerivationPath { get { return "m/44'/145'/" + Account + "'/0/0"; } }
        protected override UtxoAddressType AddressType { get { return UtxoAddressType.P2PKH; } }
        protected override string Bech32Hrp { get { return null; } }
        protected override byte P2pkhVersion { get { return 0x00; } }
        protected override byte[] P2shVersions { get { return new byte[] { 0x05 }; } }
        protected override uint TxVersion { get { return 1; } }
        protected override uint Sequence { get { return 0xFFFFFFFF; } }
        protected override bool UsesForkId { get { return true; } }
        protected override string ExplorerBase { get { return "https://blockchair.com/bitcoin-cash"; } }
        protected override IUtxoProvider Provider { get { return _provider; } }

        protected override string ComputeAddress(byte[] privateKey)
        {
            return CashAddr.Encode(0, Hashes.Hash160(Secp256k1.PublicKey(privateKey, true)));
        }

        public override string ExplorerTxUrl(string txId)
        {
            return ExplorerBase + "/transaction/" + txId;
        }

        public override byte[] ScriptFor(string address)
        {
            if (string.IsNullOrWhiteSpace(address)) return null;
            int type;
            byte[] hash;
            if (CashAddr.TryDecode(address, out type, out hash))
                return type == 0 ? UtxoTransactionBuilder.P2pkhScript(hash) : UtxoTransactionBuilder.P2shScript(hash);
            return base.ScriptFor(address);
        }
    }
}
