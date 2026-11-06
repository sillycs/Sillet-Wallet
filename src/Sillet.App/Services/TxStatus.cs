using System;
using System.Threading;
using System.Threading.Tasks;
using Sillet.Core;

namespace Sillet.App.Services
{
    public static class TxStatus
    {
        public static string Sent(TxStage stage)
        {
            switch (stage)
            {
                case TxStage.Confirmed: return Loc.T("Confirmed");
                case TxStage.Final: return Loc.T("Sent");
                case TxStage.Failed: return Loc.T("Failed");
                default: return Loc.T("Pending");
            }
        }

        public static string Received(TxStage stage)
        {
            switch (stage)
            {
                case TxStage.Pending: return Loc.T("Pending");
                case TxStage.Final: return Loc.T("Confirmed");
                case TxStage.Failed: return Loc.T("Failed");
                default: return Loc.T("Received");
            }
        }

        public static string SentHint(TxStage stage)
        {
            switch (stage)
            {
                case TxStage.Confirmed: return Loc.T("It is in a block. A few more confirmations and it is final");
                case TxStage.Final: return Loc.T("Final on the network");
                case TxStage.Failed: return Loc.T("The network did not accept it");
                default: return Loc.T("Waiting for the network to include it");
            }
        }

        public static string ReceivedLine(TxStage stage, string from)
        {
            var line = Loc.F("Status: {0}", Received(stage));
            if (!string.IsNullOrEmpty(from)) line += "  \u00B7  " + Loc.F("From {0}", Formatters.ShortAddress(from));
            return line;
        }

        public static string ReceivedTitle(TxStage stage)
        {
            return stage == TxStage.Pending ? Loc.T("Payment incoming") : Loc.T("Payment received");
        }

        public static async Task<TxStage> StageOfAsync(IChain chain, string txId)
        {
            if (chain == null || string.IsNullOrEmpty(txId)) return TxStage.Unknown;
            try
            {
                using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8)))
                    return await chain.GetTxStageAsync(txId, timeout.Token);
            }
            catch (Exception e) when (e is OperationCanceledException || e is WalletException || e is InvalidOperationException)
            {
                return TxStage.Unknown;
            }
        }
    }
}
