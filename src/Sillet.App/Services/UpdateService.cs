using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Sillet.Net;

namespace Sillet.App.Services
{
    public static class AppInfo
    {
        /// <summary>Release of the Windows, Linux and command line builds. build.ps1 publishes this one.</summary>
        public const string Version = "1.1.3";
        public const string DownloadUrl = "https://raton.fun/wallet/download";
        public const string DesktopVersionUrl = "https://raton.fun/wallet/version.json";
        public const string AndroidVersionUrl = "https://raton.fun/wallet/version-android.json";

        /// <summary>The version this build compares against. Android sets its own, so it can be updated without a new desktop release.</summary>
        public static string CurrentVersion { get; set; } = Version;

        /// <summary>Where the newest version is announced. Android reads its own file.</summary>
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
        }

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
        /// <summary>True when the desktop app took over: it closes, downloads and replaces itself, so no notification is needed.</summary>
        public bool Handled { get; }
    }

    public static class UpdateService
    {
        /// <summary>Raised on Windows when auto update is on and a new version was found. The app shows the download window and restarts itself.</summary>
        public static event Action<UpdateInfo> AutoUpdateRequested;

        private static int _autoStarted;

        /// <summary>Checks for a newer version. On Windows with auto update on it hands the version to the app, which installs it. Returns null when up to date or offline.</summary>
        public static async Task<UpdateResult> RunAsync(AppSettings settings, CancellationToken ct = default(CancellationToken))
        {
            var update = await CheckAsync(ct);
            if (update == null) return null;
            var handler = AutoUpdateRequested;
            var auto = settings.AutoUpdate && handler != null && CanSelfUpdate && !string.IsNullOrEmpty(update.ExeUrl) && !string.IsNullOrEmpty(update.Sha256);
            if (auto && System.Threading.Interlocked.CompareExchange(ref _autoStarted, 1, 0) == 0) handler(update);
            return new UpdateResult(update, auto);
        }

        /// <summary>Called by the app when an automatic update was cancelled, so the next check can offer it again.</summary>
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
                return new UpdateInfo(version, url, exeUrl, hash);
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
            return uri.Host == "raton.fun" || uri.Host.EndsWith(".raton.fun", StringComparison.Ordinal);
        }

        private const long MaxExeBytes = 250L * 1024 * 1024;

        /// <summary>The running Sillet.exe, or null when this is not a plain Windows exe we can swap.</summary>
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

        /// <summary>True when this is a plain Windows Sillet.exe. The swap itself runs elevated, so a protected folder is fine.</summary>
        public static bool CanSelfUpdate { get { return CurrentExe() != null; } }

        /// <summary>Removes leftovers of a previous update. Call once at startup.</summary>
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

        /// <summary>Downloads the new exe to a temp folder and checks it against the published SHA256. Returns the file, or null when anything fails.</summary>
        public static async Task<string> DownloadAsync(UpdateInfo update, Action<double> progress, CancellationToken ct = default(CancellationToken))
        {
            if (update == null || string.IsNullOrEmpty(update.ExeUrl) || string.IsNullOrEmpty(update.Sha256)) return null;
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

        /// <summary>
        /// Asks Windows for admin rights (UAC) and starts a small script that stops Sillet, puts the new exe where the old one was and opens it.
        /// Returns false when the prompt was declined or could not be shown, in which case nothing was changed and the app should keep running.
        /// </summary>
        public static bool StartReplace(string newExe)
        {
            var exe = CurrentExe();
            if (exe == null || string.IsNullOrEmpty(newExe) || !File.Exists(newExe)) return false;
            try
            {
                var script = Path.Combine(Path.GetDirectoryName(newExe), "Sillet-update.cmd");
                var pid = Process.GetCurrentProcess().Id;
                var lines = new[]
                {
                    "@echo off",
                    "set \"TARGET=" + exe + "\"",
                    "set \"NEWEXE=" + newExe + "\"",
                    "taskkill /F /PID " + pid + " >nul 2>&1",
                    "set /a TRIES=0",
                    ":retry",
                    "ping -n 2 127.0.0.1 >nul",
                    "copy /Y \"%NEWEXE%\" \"%TARGET%\" >nul 2>&1",
                    "if not errorlevel 1 goto done",
                    "set /a TRIES+=1",
                    "if %TRIES% LSS 30 goto retry",
                    "goto end",
                    ":done",
                    "del \"%NEWEXE%\" >nul 2>&1",
                    "start \"\" explorer.exe \"%TARGET%\"",
                    ":end",
                    "(goto) 2>nul & del \"%~f0\""
                };
                File.WriteAllText(script, string.Join("\r\n", lines) + "\r\n");
                var info = new ProcessStartInfo("cmd.exe", "/c \"\"" + script + "\" " + pid + "\"")
                {
                    Verb = "runas",
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
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

        /// <summary>Removes the downloaded file after a cancelled update.</summary>
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

        /// <summary>The path Sillet should relaunch from after an update.</summary>
        public static string RestartPath { get { return CurrentExe(); } }

        public static async Task<UpdateInfo> CheckAsync(CancellationToken ct = default(CancellationToken))
        {
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
