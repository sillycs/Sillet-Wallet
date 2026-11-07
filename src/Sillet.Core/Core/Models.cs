// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
using System;
using System.Collections.Generic;
using System.Numerics;

namespace Sillet.Core
{
    public enum FeeSpeed
    {
        Slow,
        Normal,
        Fast,
    }

    public enum TxStage
    {
        Unknown,
        Pending,
        Confirmed,
        Final,
        Failed,
    }

    public sealed class IncomingTransfer
    {
        public IncomingTransfer(string txId, string from, decimal amount)
        {
            TxId = txId;
            From = from ?? "";
            Amount = amount;
        }

        public string TxId { get; }
        public string From { get; }
        public decimal Amount { get; }
    }

    public sealed class Asset
    {
        public Asset(string symbol, string name, int decimals, string contract = null)
        {
            Symbol = symbol;
            Name = name;
            Decimals = decimals;
            Contract = contract;
        }

        public string Symbol { get; }
        public string Name { get; }
        public int Decimals { get; }
        public string Contract { get; }
        public bool IsCustom { get; set; }

        public bool IsNative
        {
            get { return Contract == null; }
        }

        public override string ToString()
        {
            return Symbol;
        }
    }

    public sealed class AssetBalance
    {
        public AssetBalance(Asset asset, decimal amount, BigInteger raw)
        {
            Asset = asset;
            Amount = amount;
            Raw = raw;
        }

        public Asset Asset { get; }
        public decimal Amount { get; }
        public BigInteger Raw { get; }

        public override string ToString()
        {
            return Amount + " " + Asset.Symbol;
        }
    }

    public sealed class TransferResult
    {
        public TransferResult(string txId, string explorerUrl)
        {
            TxId = txId;
            ExplorerUrl = explorerUrl;
        }

        public string TxId { get; }
        public string ExplorerUrl { get; }
    }

    public sealed class PreparedTransfer
    {
        public PreparedTransfer()
        {
            Notes = new List<string>();
        }

        public IChain Chain { get; internal set; }
        public Asset Asset { get; internal set; }
        public string To { get; internal set; }
        public decimal Amount { get; internal set; }
        public decimal Fee { get; internal set; }
        public Asset FeeAsset { get; internal set; }
        public string TxId { get; internal set; }
        public List<string> Notes { get; }

        internal object Payload { get; set; }
        internal bool Broadcasted { get; set; }

        public override string ToString()
        {
            var total = Asset == FeeAsset ? " (total " + (Amount + Fee) + " " + Asset.Symbol + ")" : "";
            return "Send " + Amount + " " + Asset.Symbol + " via " + Chain.Name + " to " + To +
                   Environment.NewLine + "Fee: ~" + Fee + " " + FeeAsset.Symbol + total;
        }
    }

    public sealed class WalletException : Exception
    {
        public WalletException(string message, Exception inner = null) : base(message, inner)
        {
        }
    }
}
