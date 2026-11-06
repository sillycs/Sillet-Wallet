using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Sillet.Core;
using Sillet.Storage;

namespace Sillet.App.Services
{
    public sealed class PaymentWatcher
    {
        public static readonly PaymentWatcher Instance = new PaymentWatcher();

        private readonly object _gate = new object();
        private NotificationStore _notifications;
        private HistoryStore _history;
        private CancellationTokenSource _cts;
        private DateTime _lastAppPollUtc = DateTime.MinValue;
        private DateTime _lastExtrasUtc = DateTime.MinValue;
        private NewsService _news;
        private MarketService _market;
        private AlertStore _alerts;

        public static TimeSpan ExtrasInterval = TimeSpan.FromMinutes(5);

        public static TimeSpan Interval = TimeSpan.FromSeconds(45);

        public Func<bool> IsAppActive { get; set; }
        public Action<string, string, string> Notifier { get; set; }
        public event Action Received;

        public NotificationStore Notifications
        {
            get
            {
                lock (_gate) return _notifications ?? (_notifications = new NotificationStore());
            }
        }

        public HistoryStore History
        {
            get
            {
                lock (_gate) return _history ?? (_history = new HistoryStore());
            }
        }

        public bool IsRunning
        {
            get
            {
                lock (_gate) return _cts != null;
            }
        }

        public void NoteAppPoll()
        {
            _lastAppPollUtc = DateTime.UtcNow;
        }

        public void Start()
        {
            CancellationTokenSource cts;
            lock (_gate)
            {
                if (_cts != null) return;
                cts = _cts = new CancellationTokenSource();
            }
            Loc.Enabled = true;
            Loc.Instance.Current = AppSettings.Load().Language;
            var ignored = Task.Run(() => LoopAsync(cts.Token));
        }

        public void Stop()
        {
            lock (_gate)
            {
                if (_cts == null) return;
                _cts.Cancel();
                _cts = null;
            }
        }

        private async Task LoopAsync(CancellationToken ct)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
                while (!ct.IsCancellationRequested)
                {
                    if (DateTime.UtcNow - _lastAppPollUtc > TimeSpan.FromSeconds(30)) await PollOnceAsync(ct);
                    await Task.Delay(Interval, ct);
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        private DateTime _lastUpdateUtc = DateTime.MinValue;
        private static readonly TimeSpan UpdateInterval = TimeSpan.FromHours(6);
        private static readonly TimeSpan AutoUpdateInterval = TimeSpan.FromMinutes(20);

        private async Task PollUpdateAsync(AppSettings settings, CancellationToken ct)
        {
            if (DateTime.UtcNow - _lastUpdateUtc < (settings.AutoUpdate && UpdateService.CanSelfUpdate ? AutoUpdateInterval : UpdateInterval)) return;
            _lastUpdateUtc = DateTime.UtcNow;
            var result = await UpdateService.RunAsync(settings, ct);
            if (result == null || result.Handled || settings.UpdateNotified == result.Update.Version) return;
            settings.UpdateNotified = result.Update.Version;
            settings.Save();
            if (!settings.NotificationsOn) return;
            var title = Loc.T("Update available");
            var message = Loc.F("Sillet {0} is ready to download", result.Update.Version);
            var target = result.Update.Url;
            Notifications.Add("update", title, message, target, DateTime.UtcNow);
            var notifier = Notifier;
            if (settings.PushNotifications && notifier != null) notifier(title, message, "update");
        }

        private async Task PollExtrasAsync(AppSettings settings, CancellationToken ct)
        {
            if (!settings.NotificationsOn) return;
            if (DateTime.UtcNow - _lastExtrasUtc < ExtrasInterval) return;
            _lastExtrasUtc = DateTime.UtcNow;
            var notifier = Notifier;

            if (settings.NotifyNews)
            {
                try
                {
                    _news = _news ?? new NewsService();
                    var fresh = Notifications.DetectNews(await _news.GetAsync(ct));
                    foreach (var n in fresh.Skip(Math.Max(0, fresh.Count - 3)))
                    {
                        var title = n.HasTitle ? n.Title : Loc.F("News from {0}", n.Author);
                        Notifications.Add("news", title, n.Description, n.HasUrl ? n.Url : "home", DateTime.UtcNow);
                        if (settings.PushNotifications && notifier != null) notifier(title, n.Description, "news");
                    }
                }
                catch (Exception e) when (!(e is OperationCanceledException) && (e is System.Net.Http.HttpRequestException || e is System.IO.IOException || e is InvalidOperationException || e is FormatException))
                {
                }
            }

            {
                try
                {
                    _market = _market ?? new MarketService();
                    var coins = await _market.GetMarketsAsync(settings.Currency, false, ct);
                    if (_market.IsStale || coins == null) return;
                    PriceMoves.Check(Notifications, coins, settings, (kind, title, message) =>
                    {
                        Notifications.Add(kind, title, message, "market", DateTime.UtcNow);
                        if (settings.PushNotifications && notifier != null) notifier(title, message, kind);
                    });
                    _alerts = _alerts ?? new AlertStore();
                    foreach (var alert in _alerts.Check(coins, settings.Currency))
                    {
                        var title = Loc.T("Price alert");
                        Notifications.Add("alert", title, alert.Message, "market", DateTime.UtcNow);
                        if (settings.PushNotifications && notifier != null) notifier(title, alert.Message, "alert");
                    }
                }
                catch (Exception e) when (!(e is OperationCanceledException) && (e is System.Net.Http.HttpRequestException || e is System.IO.IOException || e is InvalidOperationException || e is FormatException))
                {
                }
            }
            var handler = Received;
            if (handler != null) handler();
        }

        public async Task PollOnceAsync(CancellationToken ct)
        {
            AppSettings settings;
            ProfileStore profiles;
            try
            {
                settings = AppSettings.Load();
                settings.ApplyNetwork();
                profiles = new ProfileStore();
            }
            catch (Exception e) when (e is System.IO.IOException || e is UnauthorizedAccessException || e is FormatException || e is InvalidOperationException)
            {
                return;
            }
            try
            {
                await PollUpdateAsync(settings, ct);
                await PollExtrasAsync(settings, ct);
            }
            catch (Exception e) when (!(e is OperationCanceledException) && (e is System.IO.IOException || e is UnauthorizedAccessException || e is InvalidOperationException))
            {
            }
            foreach (var profile in profiles.Items.ToList())
            {
                if (ct.IsCancellationRequested) return;
                if (!profile.HasAddresses) continue;
                try
                {
                    var wallet = new WalletManager();
                    wallet.OpenWatch(profile.Watch);
                    var portfolio = await wallet.GetPortfolioAsync(ct);
                    var readings = portfolio
                        .Where(p => p.Balance != null && p.Error == null && p.Chain.Address != null)
                        .Select(p => new BalanceReading(p.Chain.Id + ":" + p.Chain.Address + ":" + p.Asset.Symbol, p.Asset.Symbol, p.Chain.Name, p.Balance.Amount))
                        .ToList();
                    var received = Notifications.DetectPayments(readings);
                    if (received.Count == 0) continue;
                    var chains = wallet.Chains.ToDictionary(c => c.Id, c => c);
                    var details = new Dictionary<string, KeyValuePair<TxStage, string>>();
                    foreach (var r in received)
                    {
                        var rc = chains.ContainsKey(r.ChainId) ? chains[r.ChainId] : null;
                        var ra = rc == null ? null : rc.Assets.FirstOrDefault(a => a.Symbol == r.Symbol);
                        var entry = await MakeIncomingAsync(r, rc, ra);
                        History.Add(entry);
                        details[r.Key] = new KeyValuePair<TxStage, string>(await TxStatus.StageOfAsync(rc, entry.TxId), entry.From);
                    }
                    Announce(received, profile.Name, settings, details);
                    var handler = Received;
                    if (handler != null) handler();
                }
                catch (Exception e) when (!(e is OperationCanceledException) && (e is WalletException || e is System.Net.Http.HttpRequestException || e is System.IO.IOException || e is InvalidOperationException))
                {
                }
            }
        }

        private void Announce(List<BalanceReading> received, string walletName, AppSettings settings, Dictionary<string, KeyValuePair<TxStage, string>> details)
        {
            if (!settings.NotificationsOn || !settings.NotifyPayments) return;
            foreach (var r in received.Take(5))
            {
                var amount = settings.HideBalances ? r.Symbol : Formatters.Crypto(r.Balance) + " " + r.Symbol;
                KeyValuePair<TxStage, string> info;
                if (!details.TryGetValue(r.Key, out info)) info = new KeyValuePair<TxStage, string>(TxStage.Unknown, "");
                var message = Loc.F("{0} arrived on {1}", amount, r.Network) + "\n" + TxStatus.ReceivedLine(info.Key, info.Value);
                var title = TxStatus.ReceivedTitle(info.Key);
                Notifications.Add("payment", title, message, "activity", DateTime.UtcNow);
                var active = IsAppActive;
                var notifier = Notifier;
                if (settings.PushNotifications && notifier != null) notifier(title, message, "payment");
            }
        }

        public static async Task<HistoryEntry> MakeIncomingAsync(BalanceReading r, IChain chain, Asset asset = null)
        {
            if (chain != null && asset != null)
            {
                try
                {
                    using (var timeout = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(10)))
                    {
                        var found = await chain.FindIncomingAsync(asset, r.Balance, timeout.Token);
                        if (found != null && !string.IsNullOrEmpty(found.TxId))
                            return new HistoryEntry(DateTime.UtcNow, r.ChainId, r.Network, r.Symbol, r.Balance, 0m, r.Symbol, found.From, r.Address, found.TxId, chain.ExplorerTxUrl(found.TxId), true);
                    }
                }
                catch (Exception e) when (e is OperationCanceledException || e is WalletException || e is ArgumentException || e is FormatException || e is InvalidOperationException)
                {
                }
            }
            return MakeIncoming(r, chain);
        }

        public static HistoryEntry MakeIncoming(BalanceReading r, IChain chain)
        {
            string url = "";
            if (chain != null)
            {
                try
                {
                    url = chain.ExplorerAddressUrl(r.Address) ?? "";
                }
                catch (Exception e) when (e is ArgumentException || e is FormatException || e is InvalidOperationException)
                {
                }
            }
            return new HistoryEntry(DateTime.UtcNow, r.ChainId, r.Network, r.Symbol, r.Balance, 0m, r.Symbol, "", r.Address, "", url, true);
        }
    }
}
