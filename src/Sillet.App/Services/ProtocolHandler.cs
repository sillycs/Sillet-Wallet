// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
using System;
using Microsoft.Win32;

namespace Sillet.App.Services
{
    // Makes Windows open Sillet for sillet:// and wc: links. Only the current user's settings are touched, no admin rights needed
    public static class ProtocolHandler
    {
        public static void Register(string exePath)
        {
            if (string.IsNullOrEmpty(exePath)) return;
            foreach (var scheme in new[] { "sillet", "wc" })
            {
                try
                {
                    using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + scheme))
                    {
                        if (key == null) continue;
                        key.SetValue("", "URL:" + (scheme == "wc" ? "WalletConnect" : "Sillet"));
                        key.SetValue("URL Protocol", "");
                        using (var icon = key.CreateSubKey("DefaultIcon")) { if (icon != null) icon.SetValue("", "\"" + exePath + "\",0"); }
                        using (var command = key.CreateSubKey(@"shell\open\command")) { if (command != null) command.SetValue("", "\"" + exePath + "\" \"%1\""); }
                    }
                }
                catch (Exception e) when (e is UnauthorizedAccessException || e is System.Security.SecurityException || e is System.IO.IOException) { }
            }
        }
    }
}
