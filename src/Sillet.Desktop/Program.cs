using System;
using System.Security.Cryptography;
using Avalonia;
using System.Diagnostics;
using System.IO;
using Sillet.App.Services;
using Sillet.Mobile;
using Sillet.Storage;

namespace Sillet.Desktop
{
    internal static class Program
    {
        private static readonly byte[] VaultEntropy = System.Text.Encoding.UTF8.GetBytes("Sillet.Vault.Device.v1|7c1f0e4b9a2d");
        private static readonly byte[] DataEntropy = System.Text.Encoding.UTF8.GetBytes("Sillet.Local.Data.v1|3e88d1a05b6f");

        [STAThread]
        public static void Main(string[] args)
        {
            if (OperatingSystem.IsWindows())
            {
                Vault.LocalProtect = d => ProtectedData.Protect(d, VaultEntropy, DataProtectionScope.CurrentUser);
                Vault.LocalUnprotect = d => ProtectedData.Unprotect(d, VaultEntropy, DataProtectionScope.CurrentUser);
                LocalData.Protect = d => ProtectedData.Protect(d, DataEntropy, DataProtectionScope.CurrentUser);
                LocalData.Unprotect = d => ProtectedData.Unprotect(d, DataEntropy, DataProtectionScope.CurrentUser);
                Vault.LockToDevice = Sillet.App.Services.AppSettings.Load().LockVaultToDevice;
            }
            FileStream instance = TakeInstanceLock();
            if (instance == null) return;
            MobilePlatform.StartHidden = Array.IndexOf(args, "--background") >= 0;
            MobilePlatform.Notify = DesktopNotifier.Show;
            MobilePlatform.SetBackground = DesktopNotifier.SetAutoStart;
            if (AppSettings.Load().RunInBackground) DesktopNotifier.SetAutoStart(true);
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            GC.KeepAlive(instance);
        }

        private static FileStream TakeInstanceLock()
        {
            try
            {
                Directory.CreateDirectory(AppSettings.DataFolder);
                return new FileStream(Path.Combine(AppSettings.DataFolder, "instance.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return new FileStream(Path.GetTempFileName(), FileMode.Create, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
            }
        }

        public static AppBuilder BuildAvaloniaApp()
        {
            return AppBuilder.Configure<Sillet.Mobile.App>().UsePlatformDetect().WithInterFont().LogToTrace();
        }
    }
}
