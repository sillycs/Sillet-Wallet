// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Sillet.Net;

namespace Sillet.App.Services
{
    public static class AppInfo
    {
        public const string Version = "1.1.8";
        public const string DownloadUrl = "https://github.com/sillycs/Sillet-Wallet/releases/latest";
        public const string DesktopVersionUrl = "https://github.com/sillycs/Sillet-Wallet/releases/latest/download/version.json";
        public const string AndroidVersionUrl = "https://github.com/sillycs/Sillet-Wallet/releases/latest/download/version-android.json";

        public static string CurrentVersion { get; set; } = Version;

        public static string VersionUrl { get; set; } = DesktopVersionUrl;
    }

    public sealed class UpdateInfo
    {
        public UpdateInfo(string version, string url, string exeUrl = null, string sha256 = null)
        {
            Version = version;
            Url = url;
            ExeUrl = exeUrl;
            Sha256 = sha256;
            Notes = "";
        }

        public string Notes { get; set; }
        public string Signature { get; set; }
        public bool Verified { get; set; }

        public string Version { get; }
        public string Url { get; }
        public string ExeUrl { get; }
        public string Sha256 { get; }
    }

    public sealed class UpdateResult
    {
        public UpdateResult(UpdateInfo update, bool handled)
        {
            Update = update;
            Handled = handled;
        }

        public UpdateInfo Update { get; }
        public bool Handled { get; }
    }

    public static class UpdateService
    {
        public static event Action<UpdateInfo> AutoUpdateRequested;

        private static int _autoStarted;

        public static async Task<UpdateResult> RunAsync(AppSettings settings, CancellationToken ct = default(CancellationToken))
        {
            var update = await CheckAsync(ct);
            if (update == null) return null;
            var handler = AutoUpdateRequested;
            var auto = settings.AutoUpdate && handler != null && CanSelfUpdate && update.Verified && !string.IsNullOrEmpty(update.ExeUrl) && !string.IsNullOrEmpty(update.Sha256);
            if (auto && System.Threading.Interlocked.CompareExchange(ref _autoStarted, 1, 0) == 0) handler(update);
            return new UpdateResult(update, auto);
        }

        public static void AutoUpdateFinished()
        {
            System.Threading.Interlocked.Exchange(ref _autoStarted, 0);
        }

        public static bool IsNewer(string latest, string current)
        {
            Version a, b;
            if (!System.Version.TryParse((latest ?? "").Trim().TrimStart('v', 'V'), out a) || !System.Version.TryParse((current ?? "").Trim(), out b)) return false;
            return a > b;
        }

        public static UpdateInfo Parse(string json, string current)
        {
            try
            {
                var root = JsonValue.Parse(json);
                JsonValue v, u;
                if (!root.TryGet("version", out v) || !v.IsString) return null;
                var version = v.AsString();
                if (!IsNewer(version, current)) return null;
                var url = root.TryGet("url", out u) && u.IsString && u.AsString().StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? u.AsString() : AppInfo.DownloadUrl;
                JsonValue e, h;
                var exeUrl = root.TryGet("exe", out e) && e.IsString && IsTrustedUrl(e.AsString()) ? e.AsString() : null;
                var hash = root.TryGet("sha256", out h) && h.IsString ? h.AsString().Trim().ToLowerInvariant() : null;
                if (hash != null && (hash.Length != 64 || hash.Trim("0123456789abcdef".ToCharArray()).Length != 0)) hash = null;
                JsonValue n, s;
                var notes = root.TryGet("notes", out n) && n.IsString ? n.AsString() : "";
                if (notes.Length > 1500) notes = notes.Substring(0, 1500);
                var signature = root.TryGet("sig", out s) && s.IsString ? s.AsString().Trim() : null;
                var verified = exeUrl != null && hash != null && (UpdateKey.IsConfigured ? UpdateKey.Verify(version, hash, notes, signature) : true);
                return new UpdateInfo(version, url, verified ? exeUrl : null, verified ? hash : null) { Notes = verified ? notes : "", Signature = verified ? signature : null, Verified = verified };
            }
            catch (Exception e) when (e is FormatException || e is InvalidOperationException || e is System.Collections.Generic.KeyNotFoundException)
            {
                return null;
            }
        }

        private static bool IsTrustedUrl(string url)
        {
            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri) || uri.Scheme != Uri.UriSchemeHttps) return false;
            return uri.Host == "github.com" && uri.AbsolutePath.StartsWith("/sillycs/Sillet-Wallet/releases/download/", StringComparison.Ordinal);
        }

        private const long MaxExeBytes = 250L * 1024 * 1024;

        private static string CurrentExe()
        {
            try
            {
                if (Environment.OSVersion.Platform != PlatformID.Win32NT) return null;
                var path = Process.GetCurrentProcess().MainModule.FileName;
                var name = Path.GetFileName(path) ?? "";
                if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || name.Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase)) return null;
                return File.Exists(path) ? path : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static bool CanSelfUpdate { get { return CurrentExe() != null; } }

        public static void CleanupOld()
        {
            try
            {
                var exe = CurrentExe();
                if (exe == null) return;
                var old = exe + ".old";
                if (File.Exists(old)) File.Delete(old);
                var tmp = Path.Combine(Path.GetDirectoryName(exe), "Sillet.update.tmp");
                if (File.Exists(tmp)) File.Delete(tmp);
                var dir = Path.Combine(Path.GetTempPath(), "SilletUpdate");
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
            catch (Exception)
            {
            }
        }

        public static async Task<string> DownloadAsync(UpdateInfo update, Action<double> progress, CancellationToken ct = default(CancellationToken))
        {
            if (update == null || !update.Verified || string.IsNullOrEmpty(update.ExeUrl) || string.IsNullOrEmpty(update.Sha256)) return null;
            if (!UpdateKey.Verify(update.Version, update.Sha256, update.Notes, update.Signature)) return null;
            string tmp = null;
            try
            {
                var dir = Path.Combine(Path.GetTempPath(), "SilletUpdate");
                Directory.CreateDirectory(dir);
                tmp = Path.Combine(dir, "Sillet.new.exe");
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                using (var http = new HttpClient(Sillet.Net.Http.NewHandler()) { Timeout = TimeSpan.FromMinutes(10) })
                {
                    http.DefaultRequestHeaders.UserAgent.ParseAdd("Sillet/" + AppInfo.CurrentVersion);
                    using (var response = await http.GetAsync(update.ExeUrl, HttpCompletionOption.ResponseHeadersRead, ct))
                    {
                        if (!response.IsSuccessStatusCode) return null;
                        var length = response.Content.Headers.ContentLength;
                        if (length.HasValue && length.Value > MaxExeBytes) return null;
                        using (var source = await response.Content.ReadAsStreamAsync())
                        using (var target = File.Create(tmp))
                        {
                            var buffer = new byte[81920];
                            long total = 0;
                            int read;
                            while ((read = await source.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
                            {
                                total += read;
                                if (total > MaxExeBytes) { target.Close(); Discard(tmp); return null; }
                                target.Write(buffer, 0, read);
                                if (progress != null && length.HasValue && length.Value > 0) progress((double)total / length.Value);
                            }
                        }
                    }
                }
                if (new FileInfo(tmp).Length < 100 * 1024 || !LooksLikeExe(tmp) || !string.Equals(HashOf(tmp), update.Sha256, StringComparison.Ordinal)) { Discard(tmp); return null; }
                if (progress != null) progress(1);
                return tmp;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is HttpRequestException || e is TaskCanceledException || e is OperationCanceledException)
            {
                if (tmp != null) Discard(tmp);
                return null;
            }
        }

        private static bool IsWritableFolder(string folder)
        {
            try
            {
                var probe = Path.Combine(folder, ".sillet-write-test-" + Guid.NewGuid().ToString("N"));
                using (File.Create(probe, 1, FileOptions.DeleteOnClose)) { }
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static bool NeedsAdmin
        {
            get
            {
                var exe = CurrentExe();
                return exe == null || !IsWritableFolder(Path.GetDirectoryName(exe));
            }
        }

        public static bool StartReplace(UpdateInfo update, string newExe)
        {
            var exe = CurrentExe();
            if (exe == null || update == null || !update.Verified || string.IsNullOrEmpty(newExe) || !File.Exists(newExe)) return false;
            try
            {
                var pid = Process.GetCurrentProcess().Id;
                var args = string.Join(" ", new[]
                {
                    "--apply-update", Quote(newExe), Quote(update.Version), update.Sha256, Quote(update.Notes ?? ""), update.Signature, pid.ToString()
                });
                var info = new ProcessStartInfo(exe, args) { UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden };
                if (NeedsAdmin) info.Verb = "runas";
                Process.Start(info);
                return true;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return false;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is InvalidOperationException)
            {
                return false;
            }
        }

        private static string Quote(string value)
        {
            return "\"" + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(value)) + "\"";
        }

        private static string Unquote(string value)
        {
            return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(value));
        }

        public static bool ApplyFromArguments(string[] args)
        {
            if (args == null || args.Length < 8 || args[0] != "--apply-update") return false;
            try
            {
                var newExe = Unquote(args[1]);
                var version = Unquote(args[2]);
                var sha = args[3];
                var notes = Unquote(args[4]);
                var signature = args[5];
                int pid = int.Parse(args[6]);
                var target = CurrentExe();
                if (target == null || !UpdateKey.Verify(version, sha, notes, signature) || !File.Exists(newExe)) return true;
                try
                {
                    var parent = Process.GetProcessById(pid);
                    if (!parent.WaitForExit(20000)) parent.Kill();
                }
                catch (ArgumentException)
                {
                }
                var bytes = File.ReadAllBytes(newExe);
                if (bytes.Length < 100 * 1024 || bytes[0] != 'M' || bytes[1] != 'Z') return true;
                string actual;
                using (var hasher = SHA256.Create()) actual = Sillet.Encoding.Hex.Encode(hasher.ComputeHash(bytes));
                if (!string.Equals(actual, sha, StringComparison.Ordinal)) return true;
                var staged = target + ".new";
                File.WriteAllBytes(staged, bytes);
                var old = target + ".old";
                if (File.Exists(old)) File.Delete(old);
                File.Move(target, old);
                File.Move(staged, target);
                try { File.Delete(newExe); } catch (IOException) { }
                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is FormatException || e is InvalidOperationException || e is System.ComponentModel.Win32Exception)
            {
            }
            return true;
        }

        public static void DiscardDownload(string path)
        {
            Discard(path);
        }

        private static bool Discard(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch (Exception) { }
            return false;
        }

        private static bool LooksLikeExe(string path)
        {
            using (var stream = File.OpenRead(path))
                return stream.ReadByte() == 'M' && stream.ReadByte() == 'Z';
        }

        private static string HashOf(string path)
        {
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(path))
            {
                var hash = sha.ComputeHash(stream);
                var chars = new char[hash.Length * 2];
                for (int i = 0; i < hash.Length; i++)
                {
                    chars[i * 2] = "0123456789abcdef"[hash[i] >> 4];
                    chars[i * 2 + 1] = "0123456789abcdef"[hash[i] & 15];
                }
                return new string(chars);
            }
        }

        public static string RestartPath { get { return CurrentExe(); } }

        public static async Task<UpdateInfo> CheckAsync(CancellationToken ct = default(CancellationToken))
        {
            if (!AppSettings.UpdateChecksAllowed) return null;
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                using (var http = new HttpClient(Sillet.Net.Http.NewHandler()) { Timeout = TimeSpan.FromSeconds(10) })
                {
                    http.DefaultRequestHeaders.UserAgent.ParseAdd("Sillet/" + AppInfo.CurrentVersion);
                    http.DefaultRequestHeaders.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue { NoCache = true };
                    var json = await http.GetStringAsync(AppInfo.VersionUrl + "?t=" + DateTime.UtcNow.Ticks);
                    return Parse(json, AppInfo.CurrentVersion);
                }
            }
            catch (Exception e) when (e is HttpRequestException || e is TaskCanceledException || e is OperationCanceledException)
            {
                return null;
            }
        }
    }
}
