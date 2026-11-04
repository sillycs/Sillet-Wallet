using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Sillet.Chains.Evm;
using Sillet.Chains.Solana;
using Sillet.Chains.Tron;
using Sillet.Chains.Utxo;
using Sillet.Core;
using Sillet.Crypto;
using Sillet.Storage;

namespace Sillet
{
    public sealed class PortfolioEntry
    {
        public PortfolioEntry(IChain chain, Asset asset, AssetBalance balance, string error)
        {
            Chain = chain;
            Asset = asset;
            Balance = balance;
            Error = error;
        }

        public IChain Chain { get; }
        public Asset Asset { get; }
        public AssetBalance Balance { get; }
        public string Error { get; }
    }

    public sealed class WalletManager
    {
        private readonly List<ChainBase> _chains;
        private readonly string _vaultPath;
        private string _mnemonic;
        private string _passphrase = "";
        private int _account;
        private const char PassphraseSeparator = '\u001F';
        private bool _watchOnly;

        public static readonly string[] WatchGroups = { "bitcoin", "evm", "solana", "tron", "litecoin", "dogecoin" };

        public WalletManager(string vaultPath = null)
        {
            _vaultPath = vaultPath ?? Vault.DefaultPath;
            _chains = new List<ChainBase>
            {
                new BitcoinChain(),
                new EthereumChain(),
                new TronChain(),
                new SolanaChain(),
                new BnbChain(),
                new PolygonChain(),
                new ArbitrumChain(),
                new OptimismChain(),
                new BaseChain(),
                new AvalancheChain(),
                new LitecoinChain(),
                new DogecoinChain(),
                new KaiaChain(),
                new LineaChain(),
                new BlastChain(),
                new GnosisChain(),
            };
        }

        public IReadOnlyList<IChain> Chains
        {
            get { return _watchOnly ? (IReadOnlyList<IChain>)_chains.Where(c => c.HasAddress).ToList() : _chains; }
        }

        public bool IsWatchOnly
        {
            get { return _watchOnly; }
        }

        public static string WatchGroupOf(IChain chain)
        {
            return chain is EvmChain ? "evm" : chain.Id;
        }

        public Dictionary<string, string> PublicAddresses()
        {
            var map = new Dictionary<string, string>();
            foreach (var chain in _chains.Where(c => c.HasAddress))
            {
                var group = WatchGroupOf(chain);
                if (!map.ContainsKey(group)) map[group] = chain.Address;
            }
            return map;
        }

        public static string WatchGroupName(string group)
        {
            switch (group)
            {
                case "bitcoin": return "Bitcoin";
                case "evm": return "Ethereum and EVM networks";
                case "solana": return "Solana";
                case "tron": return "TRON";
                case "litecoin": return "Litecoin";
                case "dogecoin": return "Dogecoin";
                default: return group;
            }
        }

        public bool IsValidWatchAddress(string group, string address)
        {
            if (string.IsNullOrWhiteSpace(address)) return false;
            var chain = group == "evm" ? _chains.OfType<EvmChain>().FirstOrDefault() : _chains.FirstOrDefault(c => c.Id == group);
            return chain != null && chain.IsValidAddress(address.Trim());
        }

        public void OpenWatch(IDictionary<string, string> groups)
        {
            Lock();
            foreach (var chain in _chains)
            {
                var group = chain is EvmChain ? "evm" : chain.Id;
                string address;
                if (groups.TryGetValue(group, out address) && !string.IsNullOrWhiteSpace(address)) chain.LoadWatch(address);
            }
            if (!_chains.Any(c => c.HasAddress)) throw new WalletException("Add at least one address");
            _watchOnly = true;
        }

        public bool HasSavedWallet
        {
            get { return Vault.Exists(_vaultPath); }
        }

        public bool IsUnlocked
        {
            get { return _mnemonic != null || _watchOnly; }
        }

        public static string GenerateMnemonic(int words = 12)
        {
            return Bip39.Generate(words);
        }

        public const int MaxAccounts = 50;

        public int Account
        {
            get { return _account; }
        }

        public bool HasPassphrase
        {
            get { return !string.IsNullOrEmpty(_passphrase); }
        }

        public static string Pack(string mnemonic, string passphrase)
        {
            return string.IsNullOrEmpty(passphrase) ? mnemonic : mnemonic + PassphraseSeparator + passphrase;
        }

        public static void Unpack(string blob, out string mnemonic, out string passphrase)
        {
            var at = blob.IndexOf(PassphraseSeparator);
            mnemonic = at < 0 ? blob : blob.Substring(0, at);
            passphrase = at < 0 ? "" : blob.Substring(at + 1);
        }

        public void Create(string mnemonic, string password, string passphrase = null)
        {
            mnemonic = Bip39.Normalize(mnemonic);
            var problem = Bip39.Validate(mnemonic);
            if (problem != null) throw new WalletException(problem);
            if (passphrase != null && passphrase.IndexOf(PassphraseSeparator) >= 0) throw new WalletException("The passphrase contains an invalid character");
            Vault.Save(Pack(mnemonic, passphrase), password, _vaultPath);
            LoadKeys(Pack(mnemonic, passphrase));
        }

        public void SetAccount(int account)
        {
            if (account < 0 || account >= MaxAccounts) throw new WalletException("Accounts go from 0 to " + (MaxAccounts - 1));
            if (_watchOnly) return;
            if (_mnemonic == null) throw new WalletException("The wallet is locked");
            _account = account;
            DeriveAll();
        }

        public void Unlock(string password)
        {
            var mnemonic = Vault.Load(password, _vaultPath);
            LoadKeys(mnemonic);
            try
            {
                if (Vault.NeedsUpgrade(_vaultPath)) Vault.Save(mnemonic, password, _vaultPath);
            }
            catch (Exception e) when (e is System.IO.IOException || e is UnauthorizedAccessException || e is System.Security.Cryptography.CryptographicException)
            {
            }
        }

        public void Lock()
        {
            foreach (var chain in _chains) chain.Unload();
            _mnemonic = null;
            _passphrase = "";
            _watchOnly = false;
        }

        public string RevealMnemonic(string password)
        {
            string mnemonic, passphrase;
            Unpack(Vault.Load(password, _vaultPath), out mnemonic, out passphrase);
            return mnemonic;
        }

        public bool VaultHasPassphrase(string password)
        {
            string mnemonic, passphrase;
            Unpack(Vault.Load(password, _vaultPath), out mnemonic, out passphrase);
            return passphrase.Length > 0;
        }

        public void ChangePassword(string currentPassword, string newPassword)
        {
            if (newPassword == null || newPassword.Length < 8) throw new WalletException("The new password must have at least 8 characters");
            if (newPassword == currentPassword) throw new WalletException("The new password is the same as the current one");
            var mnemonic = Vault.Load(currentPassword, _vaultPath);
            Vault.Save(mnemonic, newPassword, _vaultPath);
        }

        public void ExportBackup(string targetPath)
        {
            if (!Vault.Exists(_vaultPath)) throw new WalletException("There is no saved wallet to back up");
            System.IO.File.WriteAllText(targetPath, Vault.ReadInner(_vaultPath));
        }

        public void ImportBackup(string sourcePath)
        {
            string json;
            try
            {
                json = System.IO.File.ReadAllText(sourcePath);
            }
            catch (Exception e) when (e is System.IO.IOException || e is UnauthorizedAccessException)
            {
                throw new WalletException("Could not read the backup file");
            }
            if (!IsBackup(json)) throw new WalletException("That file is not a Sillet backup");
            Lock();
            Vault.WriteFile(_vaultPath, json);
        }

        public static bool IsBackup(string json)
        {
            try
            {
                var root = Sillet.Net.JsonValue.Parse(json);
                return root.IsObject && root.Has("Ciphertext") && root.Has("Salt") && root.Has("Nonce") && root.Has("Tag");
            }
            catch (Exception e) when (e is FormatException || e is InvalidOperationException)
            {
                return false;
            }
        }

        public bool IsFileLockedToDevice => Vault.IsLockedToDevice(_vaultPath);

        public void ApplyFileProtection()
        {
            Vault.ApplyDeviceLock(_vaultPath);
        }

        public void DeleteSavedWallet(string password)
        {
            Vault.Load(password, _vaultPath);
            Lock();
            Vault.Delete(_vaultPath);
        }

        public IChain Get(string id)
        {
            var chain = _chains.FirstOrDefault(c => c.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (chain == null) throw new WalletException("There is no network \"" + id + "\".");
            return chain;
        }

        public async Task<IReadOnlyList<PortfolioEntry>> GetPortfolioAsync(CancellationToken ct = default(CancellationToken))
        {
            if (!IsUnlocked) throw new WalletException("The wallet is locked");
            var tasks = new List<Task<PortfolioEntry>>();
            foreach (var chain in _chains)
                foreach (var asset in chain.Assets)
                    tasks.Add(BalanceEntryAsync(chain, asset, ct));
            return await Task.WhenAll(tasks);
        }

        private static async Task<PortfolioEntry> BalanceEntryAsync(IChain chain, Asset asset, CancellationToken ct)
        {
            try
            {
                return new PortfolioEntry(chain, asset, await chain.GetBalanceAsync(asset, ct), null);
            }
            catch (Exception e) when (!(e is OperationCanceledException) || !ct.IsCancellationRequested)
            {
                return new PortfolioEntry(chain, asset, null, e is WalletException ? e.Message : "No connection to the network");
            }
        }

        private void LoadKeys(string blob)
        {
            var account = _account;
            Lock();
            string mnemonic, passphrase;
            Unpack(blob, out mnemonic, out passphrase);
            _mnemonic = mnemonic;
            _passphrase = passphrase;
            _account = account;
            try
            {
                DeriveAll();
            }
            catch
            {
                Lock();
                throw;
            }
        }

        private void DeriveAll()
        {
            var seed = Bip39.ToSeed(_mnemonic, _passphrase);
            try
            {
                foreach (var chain in _chains)
                {
                    chain.Account = _account;
                    chain.Load(seed);
                }
            }
            finally
            {
                Array.Clear(seed, 0, seed.Length);
            }
        }
    }
}
