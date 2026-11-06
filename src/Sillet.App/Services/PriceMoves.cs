using System;
using System.Collections.Generic;
using System.Linq;

namespace Sillet.App.Services
{
    public static class PriceMoves
    {
        public const string Default = "bitcoin";

        public static List<string> Parse(string list)
        {
            var ids = (string.IsNullOrWhiteSpace(list) ? Default : list)
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim().ToLowerInvariant())
                .Where(x => x.Length > 0)
                .Distinct()
                .ToList();
            return ids;
        }

        public static string Join(IEnumerable<string> ids)
        {
            return string.Join(",", ids.Distinct());
        }

        public static void Check(NotificationStore store, IReadOnlyList<MarketCoin> coins, AppSettings settings, Action<string, string, string> report)
        {
            if (!settings.NotifyBitcoin || coins == null) return;
            foreach (var id in Parse(settings.PriceCoins))
            {
                var coin = coins.FirstOrDefault(c => c.Id == id);
                if (coin == null) continue;
                var change = store.DetectPriceMove(id, coin.Price, settings.Currency, settings.BitcoinMovePercent);
                if (!change.HasValue) continue;
                var up = change.Value >= 0;
                var name = string.IsNullOrEmpty(coin.Name) ? coin.Symbol.ToUpperInvariant() : coin.Name;
                report(up ? "price_up" : "price_down",
                    up ? Loc.F("{0} is up", name) : Loc.F("{0} is down", name),
                    Loc.F("{0} moved {1}% to {2}", name, (up ? "+" : "−") + Math.Abs(change.Value).ToString("0.0", Formatters.Display), Formatters.Fiat(coin.Price, settings.Currency)));
            }
        }
    }
}
