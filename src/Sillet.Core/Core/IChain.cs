// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Sillet.Core
{
    public interface IChain
    {
        string Id { get; }
        string Name { get; }
        Asset Native { get; }
        IReadOnlyList<Asset> Assets { get; }
        Task<IncomingTransfer> FindIncomingAsync(Asset asset, decimal amount, CancellationToken ct = default(CancellationToken));
        decimal? PaymentWatchBalance(Asset asset);
        bool SupportsCustomTokens { get; }
        Task<Asset> LookupTokenAsync(string contract, CancellationToken ct = default(CancellationToken));
        void AddCustomAsset(Asset asset);
        void RemoveCustomAsset(Asset asset);
        string DerivationPath { get; }
        string Address { get; }
        string ReceiveAddress { get; }
        System.Collections.Generic.IEnumerable<string> KnownAddresses { get; }

        bool IsValidAddress(string address);

        Task<AssetBalance> GetBalanceAsync(Asset asset, CancellationToken ct = default(CancellationToken));

        Task<PreparedTransfer> PrepareTransferAsync(Asset asset, string to, decimal amount, bool sendMax = false, CancellationToken ct = default(CancellationToken));

        Task<TransferResult> BroadcastAsync(PreparedTransfer transfer, CancellationToken ct = default(CancellationToken));

        bool SupportsFeeSpeed { get; }
        FeeSpeed FeeSpeed { get; set; }

        bool SupportsReplacement { get; }
        Task<PreparedTransfer> PrepareReplacementAsync(string txId, bool cancel, Asset asset, decimal amount, CancellationToken ct = default(CancellationToken));

        Task<TxStage> GetTxStageAsync(string txId, CancellationToken ct = default(CancellationToken));

        string ExplorerAddressUrl(string address);
        string ExplorerTxUrl(string txId);
    }
}
