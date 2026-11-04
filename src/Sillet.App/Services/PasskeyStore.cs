using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Sillet.Net;
using Sillet.Storage;

namespace Sillet.App.Services
{
    public sealed class PasskeyException : Exception
    {
        public PasskeyException(string message, bool cancelled, bool missing = false) : base(message)
        {
            Cancelled = cancelled;
            Missing = missing;
        }

        public bool Cancelled { get; }
        public bool Missing { get; }
    }

    public interface IPasskeyHost
    {
        string PasskeyUnavailable();
        Task<byte[]> PasskeyCreateAsync(byte[] secret);
        Task<byte[]> PasskeyDecryptAsync(byte[] encrypted, string reason);
        void PasskeyDelete();
        byte[] ProtectLocal(byte[] data);
        byte[] UnprotectLocal(byte[] data);
    }

    public sealed class PasskeyStore
    {
        private readonly string _path;
        private string _encrypted;
        private string _sealed;

        public PasskeyStore(string path = null)
        {
            _path = path ?? Path.Combine(AppSettings.DataFolder, "passkey.json");
            Load();
        }

        public bool IsSet => !string.IsNullOrEmpty(_encrypted) && !string.IsNullOrEmpty(_sealed);

        public async Task SetupAsync(IPasskeyHost host, string password)
        {
            var reason = host.PasskeyUnavailable();
            if (reason != null) throw new PasskeyException(reason, false);
            var secret = new byte[32];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(secret);
            try
            {
                var encrypted = await host.PasskeyCreateAsync(secret);
                var sealedPassword = host.ProtectLocal(Storage.Vault.Seal(secret, System.Text.Encoding.UTF8.GetBytes(password)));
                _encrypted = Convert.ToBase64String(encrypted);
                _sealed = Convert.ToBase64String(sealedPassword);
                Save();
            }
            finally
            {
                Array.Clear(secret, 0, secret.Length);
            }
        }

        public async Task<string> GetPasswordAsync(IPasskeyHost host, string reason)
        {
            if (!IsSet) throw new PasskeyException(Loc.T("Windows Hello is not set up"), false, true);
            byte[] secret = null;
            byte[] plain = null;
            try
            {
                secret = await host.PasskeyDecryptAsync(Convert.FromBase64String(_encrypted), reason);
                plain = Storage.Vault.Open(secret, host.UnprotectLocal(Convert.FromBase64String(_sealed)));
                return System.Text.Encoding.UTF8.GetString(plain);
            }
            catch (Exception e) when (e is FormatException || e is CryptographicException || e is Core.WalletException)
            {
                throw new PasskeyException(Loc.T("The saved Windows Hello data can't be read. Unlock with your password and set it up again"), false, true);
            }
            finally
            {
                if (secret != null) Array.Clear(secret, 0, secret.Length);
                if (plain != null) Array.Clear(plain, 0, plain.Length);
            }
        }

        public void Remove(IPasskeyHost host)
        {
            _encrypted = null;
            _sealed = null;
            try
            {
                if (File.Exists(_path)) File.Delete(_path);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
            }
            try
            {
                var folder = Path.GetDirectoryName(_path);
                if (!Directory.Exists(folder) || Directory.GetFiles(folder, "passkey*.json").Length == 0) host.PasskeyDelete();
            }
            catch (Exception e) when (e is PasskeyException || e is IOException || e is UnauthorizedAccessException)
            {
            }
        }

        public static string PathFor(string profileId)
        {
            return Path.Combine(AppSettings.DataFolder, profileId == null || profileId == ProfileStore.MainId ? "passkey.json" : "passkey-" + profileId + ".json");
        }

        private void Load()
        {
            var json = JsonFileStore.Read(_path);
            if (json == null) return;
            _encrypted = JsonFileStore.Text(json, "Encrypted");
            _sealed = JsonFileStore.Text(json, "Sealed");
        }

        private void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path));
                var temp = _path + ".tmp";
                File.WriteAllText(temp, Json.Serialize(new { Version = 1, Encrypted = _encrypted, Sealed = _sealed, CreatedUtc = DateTime.UtcNow.ToString("o", Formatters.Display) }));
                if (File.Exists(_path)) File.Replace(temp, _path, null);
                else File.Move(temp, _path);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                _encrypted = null;
                _sealed = null;
                throw new PasskeyException(Loc.F("Could not save the Windows Hello data: {0}", e.Message.TrimEnd('.')), false);
            }
        }
    }
}
