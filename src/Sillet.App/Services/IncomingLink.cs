// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
using System;
using System.IO;

namespace Sillet.App.Services
{
    // When a link opens a second copy of Sillet, the copy that is already running gets the link through a small file
    public static class IncomingLink
    {
        private static string PathOf { get { return Path.Combine(AppSettings.DataFolder, "incoming-link.txt"); } }

        public static void Write(string link)
        {
            try
            {
                Directory.CreateDirectory(AppSettings.DataFolder);
                File.WriteAllText(PathOf, link ?? "");
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
        }

        public static string Take()
        {
            try
            {
                if (!File.Exists(PathOf)) return null;
                var text = File.ReadAllText(PathOf);
                File.Delete(PathOf);
                return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { return null; }
        }
    }
}
