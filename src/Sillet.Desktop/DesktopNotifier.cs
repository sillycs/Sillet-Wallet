// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using Sillet.App.Services;

namespace Sillet.Desktop
{
    internal static class DesktopNotifier
    {
        public static void Show(string title, string message, string kind)
        {
            try
            {
                if (OperatingSystem.IsLinux())
                {
                    Run("notify-send", "--app-name=Sillet", "--icon=sillet", "--urgency=" + (kind == "payment" || kind == "security" ? "critical" : "normal"), "--category=" + (kind == "payment" ? "transfer.complete" : "network"), title ?? "Sillet", message ?? "");
                }
                else if (OperatingSystem.IsMacOS())
                {
                    var script = "display notification \"" + Escape(message) + "\" with title \"" + Escape(title) + "\"";
                    Run("osascript", "-e", script);
                }
            }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception || e is InvalidOperationException || e is IOException)
            {
            }
        }

        private static string Escape(string text)
        {
            return (text ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static void Run(string file, params string[] args)
        {
            var info = new ProcessStartInfo(file)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            };
            foreach (var a in args) info.ArgumentList.Add(a);
            using (var p = Process.Start(info))
            {
                if (p != null && !p.WaitForExit(4000)) p.Kill();
            }
        }

        public static void SetAutoStart(bool on)
        {
            if (!OperatingSystem.IsLinux()) return;
            try
            {
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                var folder = Path.Combine(Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") ?? Path.Combine(home, ".config"), "autostart");
                var file = Path.Combine(folder, "sillet.desktop");
                if (!on)
                {
                    if (File.Exists(file)) File.Delete(file);
                    return;
                }
                var exe = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exe)) return;
                Directory.CreateDirectory(folder);
                var sb = new StringBuilder();
                sb.AppendLine("[Desktop Entry]");
                sb.AppendLine("Type=Application");
                sb.AppendLine("Name=Sillet");
                sb.AppendLine("Comment=Watches your wallets for incoming payments");
                sb.AppendLine("Exec=\"" + exe + "\" --background");
                sb.AppendLine("Icon=sillet");
                sb.AppendLine("Terminal=false");
                sb.AppendLine("X-GNOME-Autostart-enabled=true");
                File.WriteAllText(file, sb.ToString());
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
            }
        }
    }
}
