// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Hardware;
using Android.OS;
using Android.Provider;
using Android.Util;
using Android.Views;
using Android.Widget;
using Sillet.App.Services;
using ZXing.Common;
using AColor = Android.Graphics.Color;
using Camera = Android.Hardware.Camera;

namespace Sillet.Droid
{
    [Activity(Label = "Sillet", Theme = "@style/SilletTheme", ScreenOrientation = ScreenOrientation.Portrait, Exported = false,
        ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode | ConfigChanges.KeyboardHidden)]
    public class ScanActivity : Activity, ISurfaceHolderCallback, Camera.IPreviewCallback
    {
        private const int PermissionRequest = 41;
        private const int PickRequest = 42;

        private SurfaceView _surface;
        private FrameLayout _host;
        private Camera _camera;
        private Camera.Size _previewSize;
        private bool _surfaceReady;
        private bool _flash;
        private bool _busy;
        private bool _done;
        private bool _pickOnly;
        private LinearLayout _denied;
        private TextView _hint;
        private ImageView _flashButton;
        private ZXing.BarcodeReaderGeneric _reader;
        private int _frames;

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            _pickOnly = Intent != null && Intent.GetBooleanExtra("pick", false);
            _reader = new ZXing.BarcodeReaderGeneric
            {
                AutoRotate = true,
                Options = new DecodingOptions
                {
                    TryHarder = true,
                    TryInverted = true,
                    PossibleFormats = new List<ZXing.BarcodeFormat> { ZXing.BarcodeFormat.QR_CODE }
                }
            };
            if (_pickOnly)
            {
                SetContentView(new FrameLayout(this));
                PickImage();
                return;
            }
            BuildUi();
        }

        private int Dp(float value)
        {
            return (int)Math.Round(TypedValue.ApplyDimension(ComplexUnitType.Dip, value, Resources.DisplayMetrics));
        }

        private ImageView IconButton(int resource, string description)
        {
            var button = new ImageView(this) { ContentDescription = description, Clickable = true, Focusable = true };
            button.SetImageResource(resource);
            button.SetColorFilter(AColor.White);
            button.SetScaleType(ImageView.ScaleType.Center);
            var pad = Dp(17);
            button.SetPadding(pad, pad, pad, pad);
            button.Background = Pill(unchecked((int)0x99000000), 30);
            return button;
        }

        private GradientDrawable Pill(int argb, float radiusDp)
        {
            var drawable = new GradientDrawable();
            drawable.SetColor(argb);
            drawable.SetCornerRadius(Dp(radiusDp));
            return drawable;
        }

        private TextView Chip(string text)
        {
            var view = new TextView(this) { Text = text, Gravity = GravityFlags.Center };
            view.SetTextColor(AColor.White);
            view.SetTextSize(ComplexUnitType.Sp, 14);
            view.SetTypeface(Typeface.DefaultBold, TypefaceStyle.Normal);
            view.SetPadding(Dp(18), Dp(12), Dp(18), Dp(12));
            view.Background = Pill(unchecked((int)0x99000000), 24);
            view.Clickable = true;
            return view;
        }

        private void BuildUi()
        {
            var root = new FrameLayout(this);
            root.SetBackgroundColor(AColor.Black);

            _host = new FrameLayout(this);
            _surface = new SurfaceView(this);
            _surface.Holder.AddCallback(this);
            _host.AddView(_surface, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent, GravityFlags.Center));
            root.AddView(_host, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
            _host.Click += (s, e) =>
            {
                try { if (_camera != null) _camera.AutoFocus(null); } catch (Java.Lang.Exception) { }
            };
            root.AddView(new Finder(this), new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));

            var overlay = new FrameLayout(this);
            overlay.SetFitsSystemWindows(true);

            var close = new TextView(this) { Text = "✕", Gravity = GravityFlags.Center };
            close.SetTextColor(AColor.White);
            close.SetTextSize(ComplexUnitType.Sp, 18);
            close.Background = Pill(unchecked((int)0x99000000), 24);
            close.Click += (s, e) => Cancel();
            var closeParams = new FrameLayout.LayoutParams(Dp(48), Dp(48), GravityFlags.Top | GravityFlags.Start);
            closeParams.SetMargins(Dp(16), Dp(12), 0, 0);
            overlay.AddView(close, closeParams);

            var title = new TextView(this) { Text = Loc.T("Scan QR code"), Gravity = GravityFlags.Center };
            title.SetTextColor(AColor.White);
            title.SetTextSize(ComplexUnitType.Sp, 17);
            title.SetTypeface(Typeface.DefaultBold, TypefaceStyle.Normal);
            var titleParams = new FrameLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, Dp(48), GravityFlags.Top | GravityFlags.CenterHorizontal);
            titleParams.SetMargins(0, Dp(12), 0, 0);
            overlay.AddView(title, titleParams);

            _hint = new TextView(this) { Text = Loc.T("Point the camera at a QR code"), Gravity = GravityFlags.Center };
            _hint.SetTextColor(AColor.White);
            _hint.SetTextSize(ComplexUnitType.Sp, 14);
            _hint.SetPadding(Dp(32), 0, Dp(32), 0);
            var hintParams = new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent, GravityFlags.Bottom);
            hintParams.SetMargins(0, 0, 0, Dp(120));
            overlay.AddView(_hint, hintParams);

            var bar = new LinearLayout(this) { Orientation = Orientation.Horizontal };
            bar.SetGravity(GravityFlags.Center);
            _flashButton = IconButton(Resource.Drawable.ic_flash_off, Loc.T("Flash"));
            _flashButton.Click += (s, e) => ToggleFlash();
            var gallery = IconButton(Resource.Drawable.ic_image, Loc.T("From image"));
            gallery.Click += (s, e) => PickImage();
            var chipParams = new LinearLayout.LayoutParams(Dp(60), Dp(60));
            chipParams.SetMargins(Dp(12), 0, Dp(12), 0);
            bar.AddView(_flashButton, chipParams);
            bar.AddView(gallery, new LinearLayout.LayoutParams(chipParams));
            var barParams = new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent, GravityFlags.Bottom);
            barParams.SetMargins(0, 0, 0, Dp(32));
            overlay.AddView(bar, barParams);

            _denied = new LinearLayout(this) { Orientation = Orientation.Vertical, Visibility = ViewStates.Gone };
            _denied.SetGravity(GravityFlags.Center);
            _denied.SetPadding(Dp(32), 0, Dp(32), 0);
            var deniedText = new TextView(this) { Text = Loc.T("Allow the camera to scan QR codes"), Gravity = GravityFlags.Center };
            deniedText.SetTextColor(AColor.White);
            deniedText.SetTextSize(ComplexUnitType.Sp, 16);
            var allow = Chip(Loc.T("Allow camera"));
            allow.Background = Pill(unchecked((int)0xFFFFFFFF), 24);
            allow.SetTextColor(AColor.Black);
            allow.Click += (s, e) => AskCamera(true);
            _denied.AddView(deniedText, new LinearLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent));
            var allowParams = new LinearLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent);
            allowParams.SetMargins(0, Dp(18), 0, 0);
            _denied.AddView(allow, allowParams);
            overlay.AddView(_denied, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent, GravityFlags.Center));

            root.AddView(overlay, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
            SetContentView(root);
            Window.SetStatusBarColor(AColor.Black);
            Window.SetNavigationBarColor(AColor.Black);
            AskCamera(false);
        }

        private bool HasCamera => CheckSelfPermission(Android.Manifest.Permission.Camera) == Permission.Granted;

        private void AskCamera(bool fromButton)
        {
            if (HasCamera)
            {
                ShowDenied(false);
                StartCamera();
                return;
            }
            if (fromButton && !ShouldShowRequestPermissionRationale(Android.Manifest.Permission.Camera) && GetSharedPreferences("sillet", FileCreationMode.Private).GetBoolean("asked_camera", false))
            {
                var settings = new Intent(Settings.ActionApplicationDetailsSettings, Android.Net.Uri.FromParts("package", PackageName, null));
                StartActivity(settings);
                return;
            }
            GetSharedPreferences("sillet", FileCreationMode.Private).Edit().PutBoolean("asked_camera", true).Apply();
            RequestPermissions(new[] { Android.Manifest.Permission.Camera }, PermissionRequest);
        }

        public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
        {
            base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
            if (requestCode != PermissionRequest) return;
            var granted = grantResults != null && grantResults.Length > 0 && grantResults[0] == Permission.Granted;
            ShowDenied(!granted);
            if (granted) StartCamera();
        }

        private void ShowDenied(bool denied)
        {
            if (_denied == null) return;
            _denied.Visibility = denied ? ViewStates.Visible : ViewStates.Gone;
            _hint.Visibility = denied ? ViewStates.Gone : ViewStates.Visible;
        }

        protected override void OnResume()
        {
            base.OnResume();
            if (!_pickOnly && HasCamera) ShowDenied(false);
            StartCamera();
        }

        protected override void OnPause()
        {
            StopCamera();
            base.OnPause();
        }

        public void SurfaceCreated(ISurfaceHolder holder)
        {
            _surfaceReady = true;
            StartCamera();
        }

        public void SurfaceChanged(ISurfaceHolder holder, Format format, int width, int height)
        {
        }

        public void SurfaceDestroyed(ISurfaceHolder holder)
        {
            _surfaceReady = false;
            StopCamera();
        }

        private void StartCamera()
        {
            if (_pickOnly || _camera != null || !_surfaceReady || !HasCamera || _done) return;
            try
            {
                var id = 0;
                var info = new Camera.CameraInfo();
                for (int i = 0; i < Camera.NumberOfCameras; i++)
                {
                    Camera.GetCameraInfo(i, info);
                    if (info.Facing == CameraFacing.Back)
                    {
                        id = i;
                        break;
                    }
                }
                Camera.GetCameraInfo(id, info);
                _camera = Camera.Open(id);
                var parameters = _camera.GetParameters();
                _previewSize = BestSize(parameters.SupportedPreviewSizes);
                parameters.SetPreviewSize(_previewSize.Width, _previewSize.Height);
                parameters.PreviewFormat = ImageFormatType.Nv21;
                var modes = parameters.SupportedFocusModes;
                if (modes != null && modes.Contains(Camera.Parameters.FocusModeContinuousPicture)) parameters.FocusMode = Camera.Parameters.FocusModeContinuousPicture;
                try
                {
                    if (parameters.MinExposureCompensation < 0) parameters.ExposureCompensation = parameters.MinExposureCompensation / 3;
                }
                catch (Java.Lang.Exception)
                {
                }
                _camera.SetParameters(parameters);
                var degrees = 0;
                switch (WindowManager.DefaultDisplay.Rotation)
                {
                    case SurfaceOrientation.Rotation90: degrees = 90; break;
                    case SurfaceOrientation.Rotation180: degrees = 180; break;
                    case SurfaceOrientation.Rotation270: degrees = 270; break;
                }
                _camera.SetDisplayOrientation((info.Orientation - degrees + 360) % 360);
                _camera.SetPreviewDisplay(_surface.Holder);
                _camera.SetPreviewCallback(this);
                _camera.StartPreview();
                FitPreview(info.Orientation - degrees);
                _flash = false;
                UpdateFlashLabel();
            }
            catch (Java.Lang.Exception)
            {
                StopCamera();
                Toast.MakeText(this, Loc.T("Could not open the camera"), ToastLength.Short).Show();
            }
        }

        private static Camera.Size BestSize(IList<Camera.Size> sizes)
        {
            var target = 1920 * 1080;
            return sizes.OrderBy(s => Math.Abs(s.Width * s.Height - target)).First();
        }

        private void FitPreview(int rotation)
        {
            var size = _previewSize;
            if (size == null) return;
            _host.Post(() =>
            {
                if (_host.Width == 0 || _host.Height == 0) return;
                var turned = ((rotation % 180) + 180) % 180 == 90;
                double visibleW = turned ? size.Height : size.Width;
                double visibleH = turned ? size.Width : size.Height;
                var scale = Math.Max(_host.Width / visibleW, _host.Height / visibleH);
                _surface.LayoutParameters = new FrameLayout.LayoutParams((int)(visibleW * scale), (int)(visibleH * scale), GravityFlags.Center);
            });
        }

        private void StopCamera()
        {
            var camera = _camera;
            _camera = null;
            if (camera == null) return;
            try
            {
                camera.SetPreviewCallback(null);
                camera.StopPreview();
            }
            catch (Java.Lang.Exception)
            {
            }
            try
            {
                camera.Release();
            }
            catch (Java.Lang.Exception)
            {
            }
        }

        private void ToggleFlash()
        {
            var camera = _camera;
            if (camera == null) return;
            try
            {
                var parameters = camera.GetParameters();
                var modes = parameters.SupportedFlashModes;
                if (modes == null || !modes.Contains(Camera.Parameters.FlashModeTorch)) return;
                _flash = !_flash;
                parameters.FlashMode = _flash ? Camera.Parameters.FlashModeTorch : Camera.Parameters.FlashModeOff;
                camera.SetParameters(parameters);
                UpdateFlashLabel();
            }
            catch (Java.Lang.Exception)
            {
            }
        }

        private void UpdateFlashLabel()
        {
            if (_flashButton == null) return;
            _flashButton.Background = Pill(_flash ? unchecked((int)0xFFFFFFFF) : unchecked((int)0x99000000), 30);
            _flashButton.SetImageResource(_flash ? Resource.Drawable.ic_flash_on : Resource.Drawable.ic_flash_off);
            _flashButton.SetColorFilter(_flash ? AColor.Black : AColor.White);
        }

        public void OnPreviewFrame(byte[] data, Camera camera)
        {
            if (_busy || _done || data == null || _previewSize == null) return;
            _busy = true;
            var width = _previewSize.Width;
            var height = _previewSize.Height;
            var attempt = _frames++ % 3;
            Task.Run(() =>
            {
                try
                {
                    ZXing.PlanarYUVLuminanceSource source;
                    if (attempt == 0) source = new ZXing.PlanarYUVLuminanceSource(data, width, height, 0, 0, width, height, false);
                    else
                    {
                        var side = (int)(Math.Min(width, height) * 0.75);
                        source = new ZXing.PlanarYUVLuminanceSource(data, width, height, (width - side) / 2, (height - side) / 2, side, side, false);
                    }
                    var text = attempt == 2 ? DecodeFlat(source) : _reader.Decode(source)?.Text;
                    if (!string.IsNullOrEmpty(text)) Finish(text);
                }
                catch (Exception)
                {
                }
                finally
                {
                    _busy = false;
                }
            });
        }

        private static string DecodeFlat(ZXing.LuminanceSource source)
        {
            var hints = new Dictionary<ZXing.DecodeHintType, object> { { ZXing.DecodeHintType.TRY_HARDER, true } };
            var result = new ZXing.QrCode.QRCodeReader().decode(new ZXing.BinaryBitmap(new GlobalHistogramBinarizer(source)), hints);
            return result == null ? null : result.Text;
        }

        private void Finish(string text)
        {
            RunOnUiThread(() =>
            {
                if (_done) return;
                _done = true;
                StopCamera();
                var data = new Intent();
                data.PutExtra("text", text);
                SetResult(Result.Ok, data);
                Finish();
            });
        }

        private void Cancel()
        {
            _done = true;
            SetResult(Result.Canceled);
            Finish();
        }

        private void PickImage()
        {
            try
            {
                var intent = new Intent(Intent.ActionOpenDocument);
                intent.AddCategory(Intent.CategoryOpenable);
                intent.SetType("image/*");
                StartActivityForResult(intent, PickRequest);
            }
            catch (ActivityNotFoundException)
            {
                Toast.MakeText(this, Loc.T("Scanner unavailable"), ToastLength.Short).Show();
                if (_pickOnly) Cancel();
            }
        }

        protected override void OnActivityResult(int requestCode, Result resultCode, Intent data)
        {
            base.OnActivityResult(requestCode, resultCode, data);
            if (requestCode != PickRequest) return;
            if (resultCode != Result.Ok || data == null || data.Data == null)
            {
                if (_pickOnly) Cancel();
                return;
            }
            var uri = data.Data;
            Task.Run(() =>
            {
                var text = DecodeImage(uri);
                RunOnUiThread(() =>
                {
                    if (!string.IsNullOrEmpty(text)) Finish(text);
                    else
                    {
                        Toast.MakeText(this, Loc.T("No QR code found in that image"), ToastLength.Long).Show();
                        if (_pickOnly) Cancel();
                    }
                });
            });
        }

        private string DecodeImage(Android.Net.Uri uri)
        {
            foreach (var maxSide in new[] { 2400, 1400, 900 })
            {
                try
                {
                    int sample = 1;
                    var bounds = new BitmapFactory.Options { InJustDecodeBounds = true };
                    using (var stream = ContentResolver.OpenInputStream(uri))
                        BitmapFactory.DecodeStream(stream, null, bounds);
                    while (Math.Max(bounds.OutWidth, bounds.OutHeight) / sample > maxSide) sample *= 2;
                    var options = new BitmapFactory.Options { InSampleSize = sample };
                    using (var stream = ContentResolver.OpenInputStream(uri))
                    using (var bitmap = BitmapFactory.DecodeStream(stream, null, options))
                    {
                        if (bitmap == null) continue;
                        var width = bitmap.Width;
                        var height = bitmap.Height;
                        var pixels = new int[width * height];
                        bitmap.GetPixels(pixels, 0, width, 0, 0, width, height);
                        var bytes = new byte[pixels.Length * 4];
                        Buffer.BlockCopy(pixels, 0, bytes, 0, bytes.Length);
                        var result = _reader.Decode(new ZXing.RGBLuminanceSource(bytes, width, height, ZXing.RGBLuminanceSource.BitmapFormat.BGRA32));
                        if (result != null && !string.IsNullOrEmpty(result.Text)) return result.Text;
                    }
                }
                catch (Exception)
                {
                }
            }
            return null;
        }

        private sealed class Finder : View
        {
            private readonly Paint _dim = new Paint { AntiAlias = true };
            private readonly Paint _corner = new Paint { AntiAlias = true };
            private readonly Path _mask = new Path();

            public Finder(Context context) : base(context)
            {
                _dim.Color = new AColor(0, 0, 0, 150);
                _corner.Color = AColor.White;
                _corner.SetStyle(Paint.Style.Stroke);
                _corner.StrokeCap = Paint.Cap.Round;
            }

            protected override void OnDraw(Canvas canvas)
            {
                base.OnDraw(canvas);
                var density = Resources.DisplayMetrics.Density;
                var side = Math.Min(Width, Height) * 0.66f;
                var left = (Width - side) / 2f;
                var top = (Height - side) / 2f - 40 * density;
                var rect = new RectF(left, top, left + side, top + side);
                var radius = 28 * density;
                _mask.Reset();
                _mask.AddRect(0, 0, Width, Height, Path.Direction.Cw);
                _mask.AddRoundRect(rect, radius, radius, Path.Direction.Ccw);
                _mask.SetFillType(Path.FillType.EvenOdd);
                canvas.DrawPath(_mask, _dim);

                _corner.StrokeWidth = 4 * density;
                var arm = 34 * density;
                var corners = new Path();
                corners.MoveTo(rect.Left, rect.Top + arm);
                corners.LineTo(rect.Left, rect.Top + radius);
                corners.QuadTo(rect.Left, rect.Top, rect.Left + radius, rect.Top);
                corners.LineTo(rect.Left + arm, rect.Top);
                corners.MoveTo(rect.Right - arm, rect.Top);
                corners.LineTo(rect.Right - radius, rect.Top);
                corners.QuadTo(rect.Right, rect.Top, rect.Right, rect.Top + radius);
                corners.LineTo(rect.Right, rect.Top + arm);
                corners.MoveTo(rect.Right, rect.Bottom - arm);
                corners.LineTo(rect.Right, rect.Bottom - radius);
                corners.QuadTo(rect.Right, rect.Bottom, rect.Right - radius, rect.Bottom);
                corners.LineTo(rect.Right - arm, rect.Bottom);
                corners.MoveTo(rect.Left + arm, rect.Bottom);
                corners.LineTo(rect.Left + radius, rect.Bottom);
                corners.QuadTo(rect.Left, rect.Bottom, rect.Left, rect.Bottom - radius);
                corners.LineTo(rect.Left, rect.Bottom - arm);
                canvas.DrawPath(corners, _corner);
            }
        }
    }
}
