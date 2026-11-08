// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Sillet.App.ViewModels;
using Sillet.Storage;
using Sillet.WalletConnect;

namespace Sillet.App.Services
{
    // Keeps the WalletConnect connection for the whole run of the app and asks the person before anything is signed
    public sealed class WalletConnectHub : IWcPrompts
    {
        private readonly MainViewModel _main;
        private readonly SynchronizationContext _ui;
        private int _started;

        public WalletConnectHub(MainViewModel main)
        {
            _main = main;
            _ui = SynchronizationContext.Current;
            var folder = Path.GetDirectoryName(Vault.DefaultPath);
            Service = new WalletConnectService(WalletConnectConfig.ProjectId, Path.Combine(folder, "walletconnect.json"));
            Bridge = new WcWalletBridge(() => main.Wallet, this, Service);
        }

        public WalletConnectService Service { get; }
        public WcWalletBridge Bridge { get; }

        public bool IsReady
        {
            get { return WalletConnectService.IsConfigured(WalletConnectConfig.ProjectId); }
        }

        // Reconnects the apps that were connected before, so their requests can arrive
        public async Task StartAsync()
        {
            if (!IsReady || Interlocked.Exchange(ref _started, 1) == 1) return;
            try { await Service.StartAsync(); }
            catch (Exception) { _started = 0; }
        }

        // The dialogs belong to the screen, so they are always opened from the screen's own thread
        private Task<bool> OnUi(Func<Task<bool>> ask)
        {
            if (_ui == null || SynchronizationContext.Current == _ui) return ask();
            var done = new TaskCompletionSource<bool>();
            _ui.Post(async _ =>
            {
                try { done.TrySetResult(await ask()); }
                catch (Exception) { done.TrySetResult(false); }
            }, null);
            return done.Task;
        }

        private static string Short(string address)
        {
            return address != null && address.Length > 14 ? address.Substring(0, 8) + "…" + address.Substring(address.Length - 6) : address;
        }

        private static string Who(WcPeer peer)
        {
            var name = string.IsNullOrWhiteSpace(peer.Name) ? Loc.T("An app") : peer.Name;
            return string.IsNullOrEmpty(peer.Host) ? name : name + " (" + peer.Host + ")";
        }

        public Task<bool> ConfirmConnectAsync(WcConnectPrompt p)
        {
            var sb = new StringBuilder();
            sb.AppendLine(Loc.F("{0} wants to connect to your wallet", Who(p.Peer)));
            sb.AppendLine();
            if (p.Details.Count > 1)
            {
                sb.AppendLine(Loc.T("It will see these addresses:"));
                foreach (var line in p.Details) sb.AppendLine(line);
            }
            else sb.AppendLine(Loc.F("It will see your address {0} on: {1}", Short(p.Account), string.Join(", ", p.Networks)));
            sb.AppendLine(Loc.T("It can never move your money without asking you each time"));
            foreach (var w in p.Warnings) { sb.AppendLine(); sb.Append(Loc.T(w)); }
            var text = sb.ToString().TrimEnd();
            return OnUi(() => _main.ConfirmAsync(Loc.T("Connect to an app"), text, Loc.T("Connect")));
        }

        public Task<bool> ConfirmMessageAsync(WcMessagePrompt p)
        {
            var sb = new StringBuilder();
            sb.AppendLine(Loc.F("{0} asks you to sign this on {1}", Who(p.Peer), p.Network));
            sb.AppendLine();
            sb.Append(p.Text.Length > 1200 ? p.Text.Substring(0, 1200) + "…" : p.Text);
            foreach (var w in p.Warnings) { sb.AppendLine(); sb.AppendLine(); sb.Append(Loc.T(w)); }
            var text = sb.ToString();
            return OnUi(() => _main.ConfirmAsync(Loc.T(p.IsTypedData ? "Sign data" : "Sign message"), text, Loc.T("Sign"), p.Warnings.Count > 0));
        }

        public Task<bool> ConfirmTransactionAsync(WcTransactionPrompt p)
        {
            var sb = new StringBuilder();
            sb.AppendLine(Loc.F("{0} wants to send from your wallet on {1}", Who(p.Peer), p.Network));
            sb.AppendLine();
            sb.AppendLine(Loc.F("To: {0}", p.To));
            sb.AppendLine(Loc.F("Amount: {0}", p.Amount));
            sb.Append(Loc.F("Fee: {0}", p.Fee));
            if (!string.IsNullOrEmpty(p.Data)) { sb.AppendLine(); sb.Append(p.Data); }
            foreach (var w in p.Warnings) { sb.AppendLine(); sb.AppendLine(); sb.Append(Loc.T(w)); }
            var text = sb.ToString();
            return OnUi(() => _main.ConfirmAsync(Loc.T("Confirm transaction"), text, Loc.T("Send"), p.Warnings.Count > 0));
        }
    }
}
