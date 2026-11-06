using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Android.App;
using Android.Appwidget;
using Android.Content;
using Android.Graphics;
using Android.Widget;
using Sillet.App.Services;
using Sillet.Core;
using Sillet.Storage;

namespace Sillet.Droid
{
    internal static class WidgetKit
    {
        public const string Next = "fun.raton.sillet.WIDGET_NEXT";
        public const string Refresh = "fun.raton.sillet.WIDGET_REFRESH";
        private const string Prefs = "sillet_widgets";

        public static ISharedPreferences Store(Context context)
        {
            return context.GetSharedPreferences(Prefs, FileCreationMode.Private);
        }

        public static string T(string key)
        {
            try
            {
                Loc.Enabled = true;
                Loc.Instance.Current = AppSettings.Load().Language;
                return Loc.T(key);
            }
            catch (Exception)
            {
                return key;
            }
        }

        public static PendingIntent Open(Context context, string target, int code)
        {
            var launch = context.PackageManager.GetLaunchIntentForPackage(context.PackageName);
            if (launch == null) return null;
            launch.SetFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop | ActivityFlags.NewTask);
            launch.PutExtra(AlertNotifier.TargetExtra, target);
            return PendingIntent.GetActivity(context, 7000 + code, launch, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);
        }

        public static PendingIntent Broadcast(Context context, Type receiver, string action, int code)
        {
            var intent = new Intent(context, receiver);
            intent.SetAction(action);
            return PendingIntent.GetBroadcast(context, 7100 + code, intent, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);
        }
    }

    [BroadcastReceiver(Label = "Sillet shortcuts", Exported = true)]
    [IntentFilter(new[] { "android.appwidget.action.APPWIDGET_UPDATE" })]
    [MetaData("android.appwidget.provider", Resource = "@xml/widget_actions")]
    public class ActionsWidget : AppWidgetProvider
    {
        public override void OnUpdate(Context context, AppWidgetManager appWidgetManager, int[] appWidgetIds)
        {
            foreach (var id in appWidgetIds)
            {
                var views = new RemoteViews(context.PackageName, Resource.Layout.widget_actions);
                views.SetOnClickPendingIntent(Resource.Id.w_send, WidgetKit.Open(context, "send", 1));
                views.SetOnClickPendingIntent(Resource.Id.w_receive, WidgetKit.Open(context, "receive", 2));
                views.SetOnClickPendingIntent(Resource.Id.w_market, WidgetKit.Open(context, "market", 3));
                views.SetOnClickPendingIntent(Resource.Id.w_activity, WidgetKit.Open(context, "activity", 4));
                views.SetTextViewText(Resource.Id.w_send_label, WidgetKit.T("Send"));
                views.SetTextViewText(Resource.Id.w_receive_label, WidgetKit.T("Receive"));
                views.SetTextViewText(Resource.Id.w_market_label, WidgetKit.T("Market"));
                views.SetTextViewText(Resource.Id.w_activity_label, WidgetKit.T("Activity"));
                appWidgetManager.UpdateAppWidget(id, views);
            }
        }
    }

    [BroadcastReceiver(Label = "Sillet market", Exported = true)]
    [IntentFilter(new[] { "android.appwidget.action.APPWIDGET_UPDATE" })]
    [MetaData("android.appwidget.provider", Resource = "@xml/widget_market")]
    public class MarketWidget : AppWidgetProvider
    {
        private static readonly string[] Symbols = { "BTC", "ETH", "SOL", "BNB" };
        private const string CacheKey = "market_rows";

        public override void OnReceive(Context context, Intent intent)
        {
            base.OnReceive(context, intent);
            var action = intent == null ? null : intent.Action;
            if (action != AppWidgetManager.ActionAppwidgetUpdate && action != WidgetKit.Refresh) return;
            var pending = GoAsync();
            Task.Run(async () =>
            {
                try
                {
                    var settings = AppSettings.Load();
                    var coins = await new MarketService().GetMarketsAsync(settings.Currency);
                    var info = Currencies.Find(settings.Currency);
                    var symbol = info != null ? info.Symbol : settings.Currency.ToUpperInvariant();
                    var lines = new List<string>();
                    foreach (var s in Symbols)
                    {
                        var coin = coins.FirstOrDefault(c => string.Equals(c.Symbol, s, StringComparison.OrdinalIgnoreCase));
                        if (coin == null) continue;
                        var format = coin.Price >= 1000m ? "N0" : coin.Price >= 1m ? "N2" : "N4";
                        var change = coin.Change24h.HasValue ? coin.Change24h.Value.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture) + "%" : "";
                        lines.Add(s + "|" + symbol + " " + coin.Price.ToString(format, CultureInfo.CurrentCulture) + "|" + change);
                    }
                    if (lines.Count > 0) WidgetKit.Store(context).Edit().PutString(CacheKey, string.Join(";", lines)).Apply();
                    Paint(context, AppWidgetManager.GetInstance(context), new ComponentName(context, Java.Lang.Class.FromType(typeof(MarketWidget))));
                }
                catch (Exception)
                {
                }
                finally
                {
                    pending.Finish();
                }
            });
        }

        public override void OnUpdate(Context context, AppWidgetManager appWidgetManager, int[] appWidgetIds)
        {
            Paint(context, appWidgetManager, new ComponentName(context, Java.Lang.Class.FromType(typeof(MarketWidget))));
        }

        private static void Paint(Context context, AppWidgetManager manager, ComponentName component)
        {
            var cached = WidgetKit.Store(context).GetString(CacheKey, "") ?? "";
            var rows = cached.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).Select(r => r.Split('|')).Where(p => p.Length == 3).ToList();
            var syms = new[] { Resource.Id.m_sym1, Resource.Id.m_sym2, Resource.Id.m_sym3, Resource.Id.m_sym4 };
            var prices = new[] { Resource.Id.m_price1, Resource.Id.m_price2, Resource.Id.m_price3, Resource.Id.m_price4 };
            var changes = new[] { Resource.Id.m_chg1, Resource.Id.m_chg2, Resource.Id.m_chg3, Resource.Id.m_chg4 };
            foreach (var id in manager.GetAppWidgetIds(component))
            {
                var views = new RemoteViews(context.PackageName, Resource.Layout.widget_market);
                views.SetTextViewText(Resource.Id.m_title, WidgetKit.T("Market"));
                for (int i = 0; i < 4; i++)
                {
                    if (i >= rows.Count)
                    {
                        views.SetTextViewText(syms[i], i == 0 ? "..." : "");
                        views.SetTextViewText(prices[i], "");
                        views.SetTextViewText(changes[i], "");
                        continue;
                    }
                    views.SetTextViewText(syms[i], rows[i][0]);
                    views.SetTextViewText(prices[i], rows[i][1]);
                    views.SetTextViewText(changes[i], rows[i][2]);
                    var down = rows[i][2].StartsWith("-");
                    views.SetTextColor(changes[i], down ? Color.ParseColor("#F87171") : Color.ParseColor("#34D399"));
                }
                views.SetOnClickPendingIntent(Resource.Id.m_root, WidgetKit.Open(context, "market", 5));
                views.SetOnClickPendingIntent(Resource.Id.m_title, WidgetKit.Broadcast(context, typeof(MarketWidget), WidgetKit.Refresh, 1));
                manager.UpdateAppWidget(id, views);
            }
        }
    }

    [BroadcastReceiver(Label = "Sillet address QR", Exported = true)]
    [IntentFilter(new[] { "android.appwidget.action.APPWIDGET_UPDATE" })]
    [MetaData("android.appwidget.provider", Resource = "@xml/widget_qr")]
    public class AddressWidget : AppWidgetProvider
    {
        private const string IndexKey = "qr_index";

        public override void OnReceive(Context context, Intent intent)
        {
            if (intent != null && intent.Action == WidgetKit.Next)
            {
                var store = WidgetKit.Store(context);
                store.Edit().PutInt(IndexKey, store.GetInt(IndexKey, 0) + 1).Apply();
                Paint(context, AppWidgetManager.GetInstance(context));
                return;
            }
            base.OnReceive(context, intent);
        }

        public override void OnUpdate(Context context, AppWidgetManager appWidgetManager, int[] appWidgetIds)
        {
            Paint(context, appWidgetManager);
        }

        private static string Short(string group)
        {
            return group == "evm" ? "Ethereum / EVM" : WalletManager.WatchGroupName(group);
        }

        private static List<KeyValuePair<string, string>> Addresses()
        {
            var result = new List<KeyValuePair<string, string>>();
            try
            {
                var store = new ProfileStore();
                var profile = store.Active;
                if (profile == null) return result;
                foreach (var group in WalletManager.WatchGroups)
                {
                    string address;
                    if (profile.Watch.TryGetValue(group, out address) && !string.IsNullOrEmpty(address)) result.Add(new KeyValuePair<string, string>(group, address));
                }
            }
            catch (Exception)
            {
            }
            return result;
        }

        public static Bitmap Qr(string text)
        {
            var matrix = new ZXing.QrCode.QRCodeWriter().encode(text, ZXing.BarcodeFormat.QR_CODE, 0, 0);
            int n = matrix.Width;
            int scale = Math.Max(2, 360 / n);
            int size = n * scale;
            var pixels = new int[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++) pixels[y * size + x] = matrix[x / scale, y / scale] ? unchecked((int)0xFF000000) : unchecked((int)0xFFFFFFFF);
            }
            var bitmap = Bitmap.CreateBitmap(size, size, Bitmap.Config.Argb8888);
            bitmap.SetPixels(pixels, 0, size, 0, 0, size, size);
            return bitmap;
        }

        private static void Paint(Context context, AppWidgetManager manager)
        {
            var component = new ComponentName(context, Java.Lang.Class.FromType(typeof(AddressWidget)));
            var addresses = Addresses();
            var index = addresses.Count == 0 ? 0 : ((WidgetKit.Store(context).GetInt(IndexKey, 0) % addresses.Count) + addresses.Count) % addresses.Count;
            foreach (var id in manager.GetAppWidgetIds(component))
            {
                var views = new RemoteViews(context.PackageName, Resource.Layout.widget_qr);
                if (addresses.Count == 0)
                {
                    views.SetTextViewText(Resource.Id.q_label, "Sillet");
                    views.SetTextViewText(Resource.Id.q_addr, WidgetKit.T("Open Sillet to set up your wallet"));
                    views.SetTextViewText(Resource.Id.q_next, "");
                }
                else
                {
                    var pick = addresses[index];
                    try { views.SetImageViewBitmap(Resource.Id.q_img, Qr(pick.Value)); }
                    catch (Exception) { }
                    views.SetTextViewText(Resource.Id.q_label, Short(pick.Key));
                    views.SetTextViewText(Resource.Id.q_addr, pick.Value);
                    views.SetTextViewText(Resource.Id.q_next, addresses.Count > 1 ? WidgetKit.T("Tap the QR for the next network") : "");
                    views.SetOnClickPendingIntent(Resource.Id.q_box, WidgetKit.Broadcast(context, typeof(AddressWidget), WidgetKit.Next, 2));
                }
                views.SetOnClickPendingIntent(Resource.Id.q_text, WidgetKit.Open(context, "receive", 6));
                manager.UpdateAppWidget(id, views);
            }
        }
    }
}
