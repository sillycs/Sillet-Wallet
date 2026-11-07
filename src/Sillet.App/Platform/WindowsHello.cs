// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
using System;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Sillet.App.Services;

namespace Sillet.App.Platform
{
    public static class WindowsHello
    {
        private const string ProviderName = "Microsoft Passport Key Storage Provider";
        private const string KeyDomain = "Sillet";
        private const string KeySubDomain = "";
        private const string KeyLabel = "SilletWalletUnlock";
        private const string WindowHandleProperty = "HWND Handle";
        private const string UseContextProperty = "Use Context";
        private const string LengthProperty = "Length";
        private const string KeyUsageProperty = "Key Usage";
        private const string CacheTypeProperty = "NgcCacheType";
        private const string CacheTypePropertyOld = "NgcCacheTypeProperty";
        private const int AllowDecrypt = 0x1;
        private const int AllowSigning = 0x2;
        private const int CacheAuthMandatory = 0x1;
        private const int OverwriteKey = 0x80;
        private const int PadPkcs1 = 0x2;
        private const int UserCancelled = unchecked((int)0x80090036);
        private const int NoKey = unchecked((int)0x8009000D);
        private const int BadKeyset = unchecked((int)0x80090016);
        private const int NotFound = unchecked((int)0x80090011);

        [DllImport("ncrypt.dll", CharSet = CharSet.Unicode)]
        private static extern int NCryptOpenStorageProvider(out IntPtr provider, string providerName, int flags);

        [DllImport("ncrypt.dll", CharSet = CharSet.Unicode)]
        private static extern int NCryptOpenKey(IntPtr provider, out IntPtr key, string keyName, int legacyKeySpec, int flags);

        [DllImport("ncrypt.dll", CharSet = CharSet.Unicode)]
        private static extern int NCryptCreatePersistedKey(IntPtr provider, out IntPtr key, string algorithm, string keyName, int legacyKeySpec, int flags);

        [DllImport("ncrypt.dll", CharSet = CharSet.Unicode)]
        private static extern int NCryptSetProperty(IntPtr handle, string property, byte[] input, int inputLength, int flags);

        [DllImport("ncrypt.dll")]
        private static extern int NCryptFinalizeKey(IntPtr key, int flags);

        [DllImport("ncrypt.dll")]
        private static extern int NCryptEncrypt(IntPtr key, byte[] input, int inputLength, IntPtr padding, byte[] output, int outputLength, out int resultLength, int flags);

        [DllImport("ncrypt.dll")]
        private static extern int NCryptDecrypt(IntPtr key, byte[] input, int inputLength, IntPtr padding, byte[] output, int outputLength, out int resultLength, int flags);

        [DllImport("ncrypt.dll")]
        private static extern int NCryptDeleteKey(IntPtr key, int flags);

        [DllImport("ncrypt.dll")]
        private static extern int NCryptFreeObject(IntPtr handle);

        [DllImport("cryptngc.dll", CharSet = CharSet.Unicode)]
        private static extern int NgcGetDefaultDecryptionKeyName(string sid, int reserved1, int reserved2, out string keyName);

        private static string Sid => WindowsIdentity.GetCurrent().User.Value;
        private static string KeyName => Sid + "//" + KeyDomain + "/" + KeySubDomain + "/" + KeyLabel;

        public static string Unavailable()
        {
            IntPtr provider;
            try
            {
                if (NCryptOpenStorageProvider(out provider, ProviderName, 0) != 0) return "Windows Hello is not available on this computer";
                NCryptFreeObject(provider);
            }
            catch (Exception e) when (e is DllNotFoundException || e is EntryPointNotFoundException)
            {
                return "Windows Hello needs Windows 10 or newer";
            }
            try
            {
                string name;
                if (NgcGetDefaultDecryptionKeyName(Sid, 0, 0, out name) == 0 && string.IsNullOrEmpty(name))
                    return "Set up Windows Hello (PIN, face or fingerprint) in Windows Settings first";
            }
            catch (Exception e) when (e is DllNotFoundException || e is EntryPointNotFoundException)
            {
            }
            return null;
        }

        public static byte[] Create(IntPtr window, byte[] secret)
        {
            var provider = OpenProvider();
            try
            {
                IntPtr key;
                Check(NCryptCreatePersistedKey(provider, out key, "RSA", KeyName, 0, OverwriteKey));
                try
                {
                    SetInt(key, LengthProperty, 2048);
                    SetInt(key, KeyUsageProperty, AllowDecrypt | AllowSigning);
                    if (NCryptSetProperty(key, CacheTypeProperty, BitConverter.GetBytes(CacheAuthMandatory), 4, 0) != 0)
                        NCryptSetProperty(key, CacheTypePropertyOld, BitConverter.GetBytes(CacheAuthMandatory), 4, 0);
                    SetWindow(key, window, "Set up Windows Hello to unlock Sillet");
                    Check(NCryptFinalizeKey(key, 0));
                    int length;
                    Check(NCryptEncrypt(key, secret, secret.Length, IntPtr.Zero, null, 0, out length, PadPkcs1));
                    var output = new byte[length];
                    Check(NCryptEncrypt(key, secret, secret.Length, IntPtr.Zero, output, output.Length, out length, PadPkcs1));
                    return Trim(output, length);
                }
                finally
                {
                    NCryptFreeObject(key);
                }
            }
            finally
            {
                NCryptFreeObject(provider);
            }
        }

        public static byte[] Decrypt(IntPtr window, byte[] encrypted, string reason)
        {
            var provider = OpenProvider();
            try
            {
                IntPtr key;
                Check(NCryptOpenKey(provider, out key, KeyName, 0, 0));
                try
                {
                    SetWindow(key, window, reason);
                    int length;
                    Check(NCryptDecrypt(key, encrypted, encrypted.Length, IntPtr.Zero, null, 0, out length, PadPkcs1));
                    var output = new byte[length];
                    Check(NCryptDecrypt(key, encrypted, encrypted.Length, IntPtr.Zero, output, output.Length, out length, PadPkcs1));
                    return Trim(output, length);
                }
                finally
                {
                    NCryptFreeObject(key);
                }
            }
            finally
            {
                NCryptFreeObject(provider);
            }
        }

        public static void Delete()
        {
            IntPtr provider;
            try
            {
                if (NCryptOpenStorageProvider(out provider, ProviderName, 0) != 0) return;
            }
            catch (Exception e) when (e is DllNotFoundException || e is EntryPointNotFoundException)
            {
                return;
            }
            try
            {
                IntPtr key;
                if (NCryptOpenKey(provider, out key, KeyName, 0, 0) == 0 && NCryptDeleteKey(key, 0) != 0) NCryptFreeObject(key);
            }
            finally
            {
                NCryptFreeObject(provider);
            }
        }

        private static IntPtr OpenProvider()
        {
            IntPtr provider;
            try
            {
                Check(NCryptOpenStorageProvider(out provider, ProviderName, 0));
            }
            catch (Exception e) when (e is DllNotFoundException || e is EntryPointNotFoundException)
            {
                throw new PasskeyException("Windows Hello needs Windows 10 or newer", false);
            }
            return provider;
        }

        private static void SetInt(IntPtr key, string property, int value)
        {
            Check(NCryptSetProperty(key, property, BitConverter.GetBytes(value), 4, 0));
        }

        private static void SetWindow(IntPtr key, IntPtr window, string reason)
        {
            if (window != IntPtr.Zero)
            {
                var bytes = IntPtr.Size == 8 ? BitConverter.GetBytes(window.ToInt64()) : BitConverter.GetBytes(window.ToInt32());
                NCryptSetProperty(key, WindowHandleProperty, bytes, bytes.Length, 0);
            }
            var text = System.Text.Encoding.Unicode.GetBytes(reason + "\0");
            NCryptSetProperty(key, UseContextProperty, text, text.Length, 0);
        }

        private static byte[] Trim(byte[] data, int length)
        {
            if (length == data.Length) return data;
            var result = new byte[length];
            Buffer.BlockCopy(data, 0, result, 0, length);
            return result;
        }

        private static void Check(int status)
        {
            if (status == 0) return;
            if (status == UserCancelled) throw new PasskeyException("Windows Hello was canceled", true);
            if (status == NoKey || status == BadKeyset || status == NotFound)
                throw new PasskeyException("The Windows Hello key for Sillet is gone. Unlock with your password and set it up again", false, true);
            throw new PasskeyException("Windows Hello failed (0x" + status.ToString("X8") + ")", false);
        }
    }
}
