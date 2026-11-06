using System;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.OS;
using Android.Util;
using Android.Views;
using Android.Webkit;
using Android.Widget;
using Sillet.App.Services;
using AColor = Android.Graphics.Color;

namespace Sillet.Droid
{
    [Activity(Label = "Sillet", Theme = "@style/SilletTheme", Exported = false,
        ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode | ConfigChanges.KeyboardHidden | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize)]
    public class BrowserActivity : Activity
    {
        private WebView _web;
        private TextView _host;
        private TextView _mark;
        private ProgressBar _progress;
        private string _url;

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            _url = Intent == null ? null : Intent.GetStringExtra("url");
            if (string.IsNullOrEmpty(_url) || !(_url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || _url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)))
            {
                Finish();
                return;
            }
            Window.SetStatusBarColor(AColor.Black);
            Window.SetNavigationBarColor(AColor.Black);

            var root = new LinearLayout(this) { Orientation = Orientation.Vertical };
            root.SetBackgroundColor(AColor.Black);
            root.SetFitsSystemWindows(true);

            var bar = new LinearLayout(this) { Orientation = Orientation.Horizontal };
            bar.SetBackgroundColor(AColor.Black);
            bar.SetGravity(GravityFlags.CenterVertical);
            root.AddView(bar, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(52)));

            var close = Glyph("✕", 18);
            close.Click += (s, e) => Finish();
            bar.AddView(close, new LinearLayout.LayoutParams(Dp(52), ViewGroup.LayoutParams.MatchParent));

            var center = new LinearLayout(this) { Orientation = Orientation.Horizontal };
            center.SetGravity(GravityFlags.Center);
            _mark = new TextView(this) { TextSize = 13 };
            _mark.SetPadding(0, 0, Dp(6), 0);
            _host = new TextView(this) { TextSize = 15 };
            _host.SetTextColor(AColor.White);
            _host.SetSingleLine(true);
            _host.Ellipsize = Android.Text.TextUtils.TruncateAt.End;
            center.AddView(_mark, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent));
            center.AddView(_host, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent));
            bar.AddView(center, new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.MatchParent, 1f));

            var more = Glyph("⋮", 22);
            more.Click += (s, e) => ShowMenu(more);
            bar.AddView(more, new LinearLayout.LayoutParams(Dp(52), ViewGroup.LayoutParams.MatchParent));

            _progress = new ProgressBar(this, null, Android.Resource.Attribute.ProgressBarStyleHorizontal) { Max = 100 };
            _progress.ProgressTintList = Android.Content.Res.ColorStateList.ValueOf(AColor.White);
            root.AddView(_progress, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, Dp(2)));

            _web = new WebView(this);
            _web.SetBackgroundColor(AColor.Black);
            var settings = _web.Settings;
            settings.JavaScriptEnabled = true;
            settings.DomStorageEnabled = true;
            settings.AllowFileAccess = false;
            settings.AllowContentAccess = false;
            settings.MixedContentMode = MixedContentHandling.NeverAllow;
            settings.SetSupportMultipleWindows(false);
            _web.SetWebViewClient(new Client(this));
            _web.SetWebChromeClient(new Chrome(this));
            root.AddView(_web, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, 0, 1f));

            SetContentView(root);
            ShowAddress(_url);
            _web.LoadUrl(_url);
        }

        private int Dp(int value)
        {
            return (int)TypedValue.ApplyDimension(ComplexUnitType.Dip, value, Resources.DisplayMetrics);
        }

        private TextView Glyph(string text, int size)
        {
            var view = new TextView(this) { Text = text, TextSize = size, Gravity = GravityFlags.Center };
            view.SetTextColor(AColor.White);
            view.Clickable = true;
            return view;
        }

        private void ShowAddress(string url)
        {
            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri)) return;
            var host = uri.Host;
            if (host.StartsWith("www.", StringComparison.OrdinalIgnoreCase)) host = host.Substring(4);
            _host.Text = host;
            var secure = uri.Scheme == Uri.UriSchemeHttps;
            _mark.Text = secure ? "🔒" : "✕";
            _mark.SetTextColor(secure ? AColor.ParseColor("#3DD68C") : AColor.ParseColor("#F2555A"));
        }

        private void ShowMenu(View anchor)
        {
            var menu = new PopupMenu(this, anchor);
            menu.Menu.Add(0, 1, 0, Loc.T("Reload"));
            menu.Menu.Add(0, 2, 1, Loc.T("Open in browser"));
            menu.Menu.Add(0, 3, 2, Loc.T("Copy link"));
            menu.Menu.Add(0, 4, 3, Loc.T("Share link"));
            menu.MenuItemClick += (s, e) =>
            {
                var current = _web.Url ?? _url;
                switch (e.Item.ItemId)
                {
                    case 1: _web.Reload(); break;
                    case 2: Launch(new Intent(Intent.ActionView, Android.Net.Uri.Parse(current))); break;
                    case 3:
                        var clipboard = GetSystemService(ClipboardService) as ClipboardManager;
                        if (clipboard != null) clipboard.PrimaryClip = ClipData.NewPlainText("link", current);
                        Toast.MakeText(this, Loc.T("Link copied"), ToastLength.Short).Show();
                        break;
                    case 4:
                        var send = new Intent(Intent.ActionSend);
                        send.SetType("text/plain");
                        send.PutExtra(Intent.ExtraText, current);
                        Launch(Intent.CreateChooser(send, Loc.T("Share link")));
                        break;
                }
            };
            menu.Show();
        }

        private void Launch(Intent intent)
        {
            try
            {
                StartActivity(intent);
            }
            catch (ActivityNotFoundException)
            {
            }
        }

        public override void OnBackPressed()
        {
            if (_web != null && _web.CanGoBack()) _web.GoBack();
            else base.OnBackPressed();
        }

        protected override void OnDestroy()
        {
            if (_web != null)
            {
                _web.StopLoading();
                _web.Destroy();
                _web = null;
            }
            base.OnDestroy();
        }

        private sealed class Client : WebViewClient
        {
            private readonly BrowserActivity _owner;

            public Client(BrowserActivity owner)
            {
                _owner = owner;
            }

            public override bool ShouldOverrideUrlLoading(WebView view, IWebResourceRequest request)
            {
                var uri = request.Url;
                var scheme = uri == null ? null : uri.Scheme;
                if (scheme == "http" || scheme == "https") return false;
                if (scheme == "tel" || scheme == "mailto" || scheme == "sms")
                    _owner.Launch(new Intent(Intent.ActionView, uri));
                return true;
            }

            public override void OnPageStarted(WebView view, string url, Bitmap favicon)
            {
                base.OnPageStarted(view, url, favicon);
                _owner.ShowAddress(url);
            }
        }

        private sealed class Chrome : WebChromeClient
        {
            private readonly BrowserActivity _owner;

            public Chrome(BrowserActivity owner)
            {
                _owner = owner;
            }

            public override void OnProgressChanged(WebView view, int newProgress)
            {
                _owner._progress.Progress = newProgress;
                _owner._progress.Visibility = newProgress >= 100 ? ViewStates.Invisible : ViewStates.Visible;
            }
        }
    }
}
