// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Sillet.App.Services
{
    public static class IconCache
    {
        private static readonly HttpClient Http = new HttpClient(Sillet.Net.Http.NewHandler()) { Timeout = TimeSpan.FromSeconds(20) };
        private static readonly SemaphoreSlim Gate = new SemaphoreSlim(4);

        private static string Folder
        {
            get { return Path.Combine(AppSettings.DataFolder, "icons"); }
        }

        public static string TryGet(string coinId)
        {
            var path = PathFor(coinId);
            return File.Exists(path) ? path : null;
        }

        public static async Task<string> GetAsync(string coinId, string url)
        {
            var existing = TryGet(coinId);
            if (existing != null) return existing;
            if (string.IsNullOrEmpty(url) || !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) return null;

            await Gate.WaitAsync();
            try
            {
                var again = TryGet(coinId);
                if (again != null) return again;
                var bytes = await Http.GetByteArrayAsync(url);
                if (!IsSupportedImage(bytes)) return null;
                Directory.CreateDirectory(Folder);
                var path = PathFor(coinId);
                var temp = path + ".tmp";
                File.WriteAllBytes(temp, bytes);
                if (File.Exists(path)) File.Delete(path);
                File.Move(temp, path);
                return path;
            }
            catch (Exception e) when (e is HttpRequestException || e is TaskCanceledException || e is IOException || e is UnauthorizedAccessException)
            {
                return null;
            }
            finally
            {
                Gate.Release();
            }
        }

        private static string PathFor(string coinId)
        {
            var safe = string.Concat(coinId.Where(c => char.IsLetterOrDigit(c) || c == '-'));
            return Path.Combine(Folder, safe + ".png");
        }

        internal static bool IsSupportedImage(byte[] b)
        {
            return b.Length > 8 && ((b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47) || (b[0] == 0xFF && b[1] == 0xD8));
        }
    }
}
