using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Sillet.Core;
using Sillet.Crypto;
using Sillet.Encoding;
using Sillet.Net;

namespace Sillet.Chains.Evm
{
    public abstract class EvmChain : ChainBase
    {
        private static readonly BigInteger Gwei = 1000000000;
        private static readonly byte[] BalanceOf = EvmAbi.Selector("balanceOf(address)");
        private static readonly byte[] TransferSelector = EvmAbi.Selector("transfer(address,uint256)");
        private static readonly byte[] DecimalsSelector = EvmAbi.Selector("decimals()");
        private const string GasPriceOracle = "0x420000000000000000000000000000000000000F";

        private JsonRpcClient _rpc;

        public abstract long ChainId { get; }
        protected abstract string[] RpcUrls { get; }
        protected abstract string ExplorerBase { get; }

        protected virtual bool SupportsEip1559
        {
            get { return true; }
        }

        protected virtual bool IsOpStack
        {
            get { return false; }
        }

        protected virtual BigInteger MinPriorityFee
        {
            get { return BigInteger.Zero; }
        }

        public override string DerivationPath
        {
            get { return "m/44'/60'/0'/0/" + Account; }
        }

        protected JsonRpcClient Rpc
        {
            get { return _rpc ?? (_rpc = new JsonRpcClient(Id, RpcUrls)); }
        }

        protected override byte[] DeriveKey(byte[] seed)
        {
            return HdDerivation.DeriveSecp256k1(seed, DerivationPath);
        }

        protected override string ComputeAddress(byte[] privateKey)
        {
            return EvmAddress.FromPublicKey(Secp256k1.PublicKey(privateKey, false));
        }

        public override bool IsValidAddress(string address)
        {
            return EvmAddress.IsValid(address);
        }

        private long LogWindowBlocks
        {
            get
            {
                switch (ChainId)
                {
                    case 1: return 150;
                    case 42161: return 4000;
                    case 56: return 500;
                    case 8217: return 1200;
                    default: return 600;
                }
            }
        }

        public override async Task<IncomingTransfer> FindIncomingAsync(Asset asset, decimal amount, CancellationToken ct = default(CancellationToken))
        {
            if (asset.IsNative) return null;
            try
            {
                var raw = Units.ToRaw(amount, asset.Decimals);
                var head = (long)EvmAbi.DecodeUint((await Rpc.CallAsync("eth_blockNumber", new object[0], ct)).AsString());
                var from = EvmAbi.Quantity(Math.Max(head - LogWindowBlocks, 0));
                var topic = "0x" + Hex.Encode(Hashes.Keccak256(System.Text.Encoding.ASCII.GetBytes("Transfer(address,address,uint256)")));
                var toTopic = "0x" + Hex.Encode(EvmAbi.Address(Address));
                var logs = await Rpc.CallAsync("eth_getLogs", new object[] { new { fromBlock = from, toBlock = "latest", address = asset.Contract, topics = new object[] { topic, null, toTopic } } }, ct);
                foreach (var log in logs.Items.Reverse())
                {
                    if (EvmAbi.DecodeUint(log["data"].AsString()) != raw) continue;
                    var sender = EvmAddress.ToChecksum(log["topics"][1].AsString().Substring(26));
                    return new IncomingTransfer(log["transactionHash"].AsString(), sender, amount);
                }
            }
            catch (Exception e) when (e is WalletException || e is RpcException || e is KeyNotFoundException || e is InvalidOperationException || e is FormatException || e is System.Net.Http.HttpRequestException)
            {
            }
            return null;
        }

        public override bool SupportsCustomTokens
        {
            get { return true; }
        }

        public override async Task<Asset> LookupTokenAsync(string contract, CancellationToken ct = default(CancellationToken))
        {
            contract = (contract ?? "").Trim();
            if (!EvmAddress.IsValid(contract)) throw new WalletException("That is not a valid contract address for " + Name);
            contract = EvmAddress.ToChecksum(contract);
            if (HasToken(contract)) throw new WalletException("This token is already in your list");
            var code = await Rpc.CallAsync("eth_getCode", new object[] { contract, "latest" }, ct);
            var codeText = code.AsString();
            if (codeText == "0x" || codeText.Length <= 2) throw new WalletException("There is no contract at that address on " + Name);
            BigInteger decimals;
            try
            {
                decimals = EvmAbi.DecodeUint(await CallContractAsync(contract, DecimalsSelector, ct));
            }
            catch (Exception e) when (e is RpcException || e is FormatException || e is ArgumentException)
            {
                throw new WalletException("That contract is not a standard token (ERC-20)");
            }
            if (decimals > 36) throw new WalletException("That contract is not a standard token (ERC-20)");
            string symbol = "", name = "";
            try
            {
                symbol = EvmAbi.DecodeString(await CallContractAsync(contract, EvmAbi.Selector("symbol()"), ct));
                name = EvmAbi.DecodeString(await CallContractAsync(contract, EvmAbi.Selector("name()"), ct));
            }
            catch (Exception e) when (e is RpcException || e is FormatException || e is ArgumentException)
            {
            }
            if (symbol.Length == 0) symbol = "TOKEN";
            if (symbol.Length > 12) symbol = symbol.Substring(0, 12);
            if (name.Length == 0) name = symbol;
            if (name.Length > 40) name = name.Substring(0, 40);
            return new Asset(symbol, name, (int)decimals, contract) { IsCustom = true };
        }

        public override string ExplorerAddressUrl(string address)
        {
            return ExplorerBase + "/address/" + address;
        }

        public override string ExplorerTxUrl(string txId)
        {
            return ExplorerBase + "/tx/" + txId;
        }

        protected static Asset Token(string symbol, string name, int decimals, string contract)
        {
            if (!EvmAddress.IsValid(contract)) throw new ArgumentException("Invalid contract for " + symbol + ": " + contract);
            return new Asset(symbol, name, decimals, contract);
        }

        private const long EvmFinalDepth = 12;

        public override async Task<TxStage> GetTxStageAsync(string txId, CancellationToken ct = default(CancellationToken))
        {
            try
            {
                var receipt = await Rpc.CallAsync("eth_getTransactionReceipt", new object[] { txId }, ct);
                if (receipt == null || receipt.IsNull) return TxStage.Pending;
                JsonValue status;
                if (receipt.TryGet("status", out status) && !status.IsNull && status.AsString() == "0x0") return TxStage.Failed;
                JsonValue number;
                if (!receipt.TryGet("blockNumber", out number) || number.IsNull) return TxStage.Pending;
                var block = (long)EvmAbi.DecodeUint(number.AsString());
                var head = (long)EvmAbi.DecodeUint((await Rpc.CallAsync("eth_blockNumber", new object[0], ct)).AsString());
                return head - block + 1 >= EvmFinalDepth ? TxStage.Final : TxStage.Confirmed;
            }
            catch (Exception e) when (e is WalletException || e is RpcException || e is KeyNotFoundException || e is InvalidOperationException || e is FormatException || e is System.Net.Http.HttpRequestException)
            {
                return TxStage.Unknown;
            }
        }

        public override async Task<AssetBalance> GetBalanceAsync(Asset asset, CancellationToken ct = default(CancellationToken))
        {
            var raw = asset.IsNative ? await NativeBalanceAsync(ct) : await TokenBalanceAsync(asset, ct);
            return Balance(asset, raw);
        }

        private async Task<BigInteger> NativeBalanceAsync(CancellationToken ct)
        {
            var result = await Rpc.CallAsync("eth_getBalance", new object[] { Address, "latest" }, ct);
            return EvmAbi.DecodeUint(result.AsString());
        }

        private async Task<BigInteger> TokenBalanceAsync(Asset token, CancellationToken ct)
        {
            return EvmAbi.DecodeUint(await CallContractAsync(token.Contract, Bytes.Concat(BalanceOf, EvmAbi.Address(Address)), ct));
        }

        private async Task<string> CallContractAsync(string contract, byte[] data, CancellationToken ct)
        {
            var call = new { to = contract, data = "0x" + Hex.Encode(data) };
            var result = await Rpc.CallAsync("eth_call", new object[] { call, "latest" }, ct);
            return result.AsString();
        }

        public override async Task<PreparedTransfer> PrepareTransferAsync(Asset asset, string to, decimal amount, bool sendMax = false, CancellationToken ct = default(CancellationToken))
        {
            var raw = CheckTransfer(asset, to, amount, sendMax);
            var nativeBalanceTask = NativeBalanceAsync(ct);
            var nonceTask = Rpc.CallAsync("eth_getTransactionCount", new object[] { Address, "pending" }, ct);
            var feesTask = GetGasPricesAsync(ct);

            byte[] txTo;
            BigInteger value;
            byte[] data;
            if (asset.IsNative)
            {
                txTo = EvmAddress.ToBytes(to);
                value = raw;
                data = Bytes.Empty;
            }
            else
            {
                var onChainDecimals = EvmAbi.DecodeUint(await CallContractAsync(asset.Contract, DecimalsSelector, ct));
                if (onChainDecimals != asset.Decimals)
                    throw new WalletException("The " + asset.Symbol + " contract does not match what was expected. Send cancelled for safety");
                var tokenBalance = await TokenBalanceAsync(asset, ct);
                if (sendMax) raw = tokenBalance;
                if (raw <= 0) throw new WalletException("You have no " + asset.Symbol + " on " + Name);
                if (tokenBalance < raw) throw Insufficient(asset, raw, tokenBalance);
                txTo = EvmAddress.ToBytes(asset.Contract);
                value = 0;
                data = Bytes.Concat(TransferSelector, EvmAbi.Address(to), EvmAbi.Uint(raw));
            }

            var estimateCall = new
            {
                from = Address,
                to = "0x" + Hex.Encode(txTo),
                value = EvmAbi.Quantity(sendMax && asset.IsNative ? BigInteger.Zero : value),
                data = "0x" + Hex.Encode(data),
            };
            BigInteger estimated;
            try
            {
                var result = await Rpc.CallAsync("eth_estimateGas", new object[] { estimateCall }, ct);
                estimated = EvmAbi.DecodeUint(result.AsString());
            }
            catch (RpcException e)
            {
                throw new WalletException("The network rejected the transfer simulation: " + e.Message);
            }
            var gasLimit = estimated == 21000 ? estimated : estimated * 12 / 10 + 1;

            var nonce = EvmAbi.DecodeUint((await nonceTask).AsString());
            var fees = await feesTask;
            var maxPerGas = fees.Item1;
            var expectedPerGas = fees.Item2;
            var tip = fees.Item3;
            var nativeBalance = await nativeBalanceTask;

            Func<BigInteger, EvmTransaction> build = v => SupportsEip1559
                ? new EvmTransaction { ChainId = ChainId, Nonce = nonce, GasLimit = gasLimit, To = txTo, Value = v, Data = data, MaxFeePerGas = maxPerGas, MaxPriorityFeePerGas = tip }
                : new EvmTransaction { ChainId = ChainId, Nonce = nonce, GasLimit = gasLimit, To = txTo, Value = v, Data = data, GasPrice = maxPerGas };

            var l1Fee = IsOpStack ? await L1FeeAsync(build(value).SigningPayload().Length + 68, ct) : BigInteger.Zero;
            var worstCaseFee = gasLimit * maxPerGas + l1Fee * 5 / 4;
            var expectedFee = estimated * expectedPerGas + l1Fee;

            var notes = new List<string>();
            if (asset.IsNative)
            {
                if (sendMax)
                {
                    value = nativeBalance - worstCaseFee;
                    if (value <= 0) throw new WalletException("Your " + Native.Symbol + " balance does not even cover the fee");
                    notes.Add("A margin is reserved for the fee; whatever is left stays in your wallet");
                }
                else if (value + worstCaseFee > nativeBalance)
                {
                    throw Insufficient(Native, value + worstCaseFee, nativeBalance);
                }
            }
            else if (worstCaseFee > nativeBalance)
            {
                throw new WalletException("To move " + asset.Symbol + " you need " + Native.Symbol + " for the fee: ~" +
                                          Units.FromRaw(worstCaseFee, Native.Decimals) + " " + Native.Symbol + " and you have " +
                                          Units.FromRaw(nativeBalance, Native.Decimals));
            }

            var signed = build(value).Sign(Key);
            var transfer = new PreparedTransfer
            {
                Chain = this,
                Asset = asset,
                To = to,
                Amount = Units.FromRaw(asset.IsNative ? value : raw, asset.Decimals),
                Fee = Units.FromRaw(expectedFee, Native.Decimals),
                FeeAsset = Native,
                TxId = "0x" + Hex.Encode(Hashes.Keccak256(signed)),
                Payload = signed,
            };
            transfer.Notes.AddRange(notes);
            return transfer;
        }

        protected override async Task<string> BroadcastPayloadAsync(PreparedTransfer transfer, CancellationToken ct)
        {
            var signed = (byte[])transfer.Payload;
            try
            {
                var result = await Rpc.CallAsync("eth_sendRawTransaction", new object[] { "0x" + Hex.Encode(signed) }, ct);
                return result.AsString();
            }
            catch (RpcException e) when (e.Message.IndexOf("already known", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return transfer.TxId;
            }
            catch (RpcException e)
            {
                throw new WalletException("The network rejected the transaction: " + e.Message);
            }
        }

        public override bool SupportsFeeSpeed
        {
            get { return true; }
        }

        private BigInteger ScaleBySpeed(BigInteger value)
        {
            if (FeeSpeed == FeeSpeed.Fast) return value * 3 / 2;
            if (FeeSpeed == FeeSpeed.Slow) return value * 9 / 10;
            return value;
        }

        private async Task<Tuple<BigInteger, BigInteger, BigInteger>> GetGasPricesAsync(CancellationToken ct)
        {
            var gasPrice = EvmAbi.DecodeUint((await Rpc.CallAsync("eth_gasPrice", new object[0], ct)).AsString());
            if (!SupportsEip1559)
            {
                var legacy = FeeSpeed == FeeSpeed.Slow ? gasPrice : ScaleBySpeed(gasPrice);
                return Tuple.Create(legacy, legacy, BigInteger.Zero);
            }

            var block = await Rpc.CallAsync("eth_getBlockByNumber", new object[] { "latest", false }, ct);
            JsonValue baseFeeJson;
            if (!block.TryGet("baseFeePerGas", out baseFeeJson)) return Tuple.Create(ScaleBySpeed(gasPrice), ScaleBySpeed(gasPrice), BigInteger.Zero);
            var baseFee = EvmAbi.DecodeUint(baseFeeJson.AsString());

            BigInteger tip;
            try
            {
                tip = EvmAbi.DecodeUint((await Rpc.CallAsync("eth_maxPriorityFeePerGas", new object[0], ct)).AsString());
            }
            catch (RpcException)
            {
                tip = BigInteger.Max(gasPrice - baseFee, Gwei / 100);
            }
            if (FeeSpeed == FeeSpeed.Fast) tip = tip * 2;
            else if (FeeSpeed == FeeSpeed.Slow) tip = tip / 2;
            tip = BigInteger.Max(tip, MinPriorityFee);
            var headroom = FeeSpeed == FeeSpeed.Slow ? baseFee * 5 / 4 : FeeSpeed == FeeSpeed.Fast ? baseFee * 3 : baseFee * 2;
            return Tuple.Create(headroom + tip, baseFee + tip, tip);
        }

        private async Task<BigInteger> L1FeeAsync(int txSize, CancellationToken ct)
        {
            try
            {
                var data = Bytes.Concat(EvmAbi.Selector("getL1FeeUpperBound(uint256)"), EvmAbi.Uint(txSize));
                return EvmAbi.DecodeUint(await CallContractAsync(GasPriceOracle, data, ct));
            }
            catch (RpcException)
            {
                var data = Bytes.Concat(EvmAbi.Selector("getL1Fee(bytes)"), EvmAbi.Bytes(new byte[txSize]));
                return EvmAbi.DecodeUint(await CallContractAsync(GasPriceOracle, data, ct));
            }
        }
    }
}
