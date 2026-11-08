// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Sillet.App.Services;
using Sillet.WalletConnect;

namespace Sillet.App.ViewModels
{
    public sealed class WcSessionRow
    {
        public WcSessionRow(WcSession session, RelayCommand disconnect)
        {
            Topic = session.Topic;
            Name = string.IsNullOrWhiteSpace(session.Peer.Name) ? Loc.T("An app") : session.Peer.Name;
            Host = session.Peer.Host;
            DisconnectCommand = disconnect;
        }

        public string Topic { get; }
        public string Name { get; }
        public string Host { get; }
        public RelayCommand DisconnectCommand { get; }
    }

    public sealed class WalletConnectViewModel : ObservableObject
    {
        private readonly MainViewModel _main;
        private readonly WalletConnectHub _hub;
        private readonly SynchronizationContext _ui;
        private string _link = "";
        private string _status = "";
        private bool _busy;

        public WalletConnectViewModel(MainViewModel main, WalletConnectHub hub)
        {
            _main = main;
            _hub = hub;
            _ui = SynchronizationContext.Current;
            Sessions = new ObservableCollection<WcSessionRow>();
            ConnectCommand = new AsyncCommand(ConnectAsync);
            PasteCommand = new AsyncCommand(PasteAsync);
            ScanImageCommand = new RelayCommand(ScanImage);
            DisconnectCommand = new RelayCommand(p => { var row = p as WcSessionRow; if (row != null) { var ignored = DisconnectAsync(row.Topic); } });
            hub.Service.SessionsChanged += OnSessionsChanged;
            Reload();
        }

        public ObservableCollection<WcSessionRow> Sessions { get; }
        public AsyncCommand ConnectCommand { get; }
        public AsyncCommand PasteCommand { get; }
        public RelayCommand ScanImageCommand { get; }
        public RelayCommand DisconnectCommand { get; }

        // The phone sets this to open the camera and return the text of the QR code
        public Func<Task<string>> ScanCamera { get; set; }

        // The phone reads its clipboard asynchronously, the desktop has it right away
        public Func<Task<string>> ReadClipboardAsync { get; set; }

        public bool IsReady => _hub.IsReady;
        public bool IsNotReady => !_hub.IsReady;
        public bool HasSessions => Sessions.Count > 0;
        public bool HasNoSessions => Sessions.Count == 0;
        public bool CanScanCamera => ScanCamera != null;

        public string LinkText
        {
            get { return _link; }
            set { Set(ref _link, value ?? ""); }
        }

        public string Status
        {
            get { return _status; }
            private set { if (Set(ref _status, value ?? "")) Raise(nameof(HasStatus)); }
        }

        public bool HasStatus => !string.IsNullOrEmpty(_status);

        public bool IsBusy
        {
            get { return _busy; }
            private set { Set(ref _busy, value); }
        }

        private void OnSessionsChanged()
        {
            if (_ui != null) _ui.Post(_ => Reload(), null);
            else Reload();
        }

        private void Reload()
        {
            Sessions.Clear();
            foreach (var s in _hub.Service.Sessions) Sessions.Add(new WcSessionRow(s, DisconnectCommand));
            Raise(nameof(HasSessions), nameof(HasNoSessions));
        }

        private async Task PasteAsync()
        {
            var read = ReadClipboardAsync;
            var text = read != null ? await read() : _main.Ui.ReadClipboard();
            if (string.IsNullOrWhiteSpace(text)) { _main.ShowToast(Loc.T("Nothing to paste"), true); return; }
            LinkText = text.Trim();
        }

        private void ScanImage()
        {
            string text = null;
            try { text = _main.Ui.ScanQrFromFile(); } catch (Exception) { }
            if (string.IsNullOrWhiteSpace(text)) { _main.ShowToast(Loc.T("No QR code found"), true); return; }
            LinkText = text.Trim();
        }

        public async Task ScanCameraAsync()
        {
            var scan = ScanCamera;
            if (scan == null) return;
            string text = null;
            try { text = await scan(); } catch (Exception) { }
            if (string.IsNullOrWhiteSpace(text)) return;
            LinkText = text.Trim();
            await ConnectAsync();
        }

        // Also used when a wc: link arrives from outside (a web page or another app opened Sillet with it)
        public async Task ConnectAsync()
        {
            var link = (LinkText ?? "").Trim();
            if (!WalletConnectService.IsWcLink(link)) throw new Sillet.Core.WalletException(Loc.T("That is not a WalletConnect link"));
            IsBusy = true;
            try
            {
                Status = Loc.T("Connecting… Approve the request that appears");
                await _hub.Service.PairAsync(link);
                LinkText = "";
                // the request to approve arrives a moment later
                await Task.Delay(4000);
                Status = "";
            }
            catch (Exception)
            {
                Status = "";
                throw;
            }
            finally { IsBusy = false; }
        }

        private async Task DisconnectAsync(string topic)
        {
            try { await _hub.Service.DisconnectAsync(topic); }
            catch (Exception) { }
            Reload();
        }
    }
}
