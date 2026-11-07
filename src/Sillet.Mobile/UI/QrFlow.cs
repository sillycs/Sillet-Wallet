// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
using System;
using System.Linq;
using System.Threading.Tasks;
using Sillet.App.Services;
using Sillet.App.ViewModels;

namespace Sillet.Mobile.UI
{
    internal static class QrFlow
    {
        public static async Task Run(MainViewModel main, SendViewModel send, bool fromImage)
        {
            var scan = MobilePlatform.ScanQr;
            if (scan == null)
            {
                if (main != null) main.ShowToast(Loc.T("Scanner unavailable"), true);
                return;
            }
            string text;
            try
            {
                text = await scan(fromImage);
            }
            catch (Exception)
            {
                text = null;
            }
            if (string.IsNullOrWhiteSpace(text)) return;

            if (main != null) main.Navigate("send");
            if (send.IsDoneStep) send.NewCommand.Execute(null);
            else if (send.IsReviewStep) send.BackCommand.Execute(null);

            var request = PaymentUri.Parse(text);
            if (request != null) Preselect(send, request.Address, Scheme(text));
            send.ApplyScan(text, false);
        }

        private static void Preselect(SendViewModel send, string address, string scheme)
        {
            var current = send.SelectedAsset;
            if (current != null && current.Chain.IsValidAddress(address)) return;
            var matches = send.Assets.Where(a => a.Chain.IsValidAddress(address)).ToList();
            if (matches.Count == 0) return;
            var best = matches
                .OrderByDescending(a => scheme != null && (string.Equals(a.Chain.Id, scheme, StringComparison.OrdinalIgnoreCase) || string.Equals(a.Chain.Name, scheme, StringComparison.OrdinalIgnoreCase)))
                .ThenByDescending(a => a.Balance > 0)
                .ThenByDescending(a => a.Asset.IsNative)
                .First();
            send.SelectedAsset = best;
        }

        private static string Scheme(string text)
        {
            var trimmed = text.Trim();
            var colon = trimmed.IndexOf(':');
            if (colon <= 0 || colon > 20) return null;
            var scheme = trimmed.Substring(0, colon);
            return scheme.All(char.IsLetter) ? scheme : null;
        }
    }
}
