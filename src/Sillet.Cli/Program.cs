// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Sillet.Core;
using Sillet.Crypto;

namespace Sillet.Cli
{
    internal static class Program
    {
        public const string Version = "1.1.7";

        private static async Task<int> Main(string[] args)
        {
            Console.OutputEncoding = new UTF8Encoding(false);
            try
            {
                if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                {
                    WindowsProtection.Use();
                    Sillet.Storage.Vault.LockToDevice = CliSettings.LockVaultToDevice();
                }
            }
            catch (Exception e) when (e is PlatformNotSupportedException || e is TypeLoadException || e is DllNotFoundException)
            {
            }
            var profiles = new Sillet.Storage.ProfileStore();
            var app = new ConsoleApp(profiles);
            try
            {
                return await app.RunAsync(args);
            }
            catch (WalletException e)
            {
                Ui.Error(e.Message);
                return 1;
            }
            catch (Exception e) when (e is HttpRequestException || e is TaskCanceledException || e is FormatException)
            {
                Ui.Error("No connection to the network. Check your internet");
                return 1;
            }
        }
    }

    internal static class CliSettings
    {
        public static bool LockVaultToDevice()
        {
            try
            {
                var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify);
                var path = System.IO.Path.Combine(root, "Sillet", "settings.json");
                if (!System.IO.File.Exists(path)) return true;
                var json = Sillet.Net.JsonValue.Parse(Sillet.Storage.LocalData.ReadText(path));
                Sillet.Net.JsonValue value;
                return !json.TryGet("LockVaultToDevice", out value) || value.IsTrue;
            }
            catch (Exception e) when (e is System.IO.IOException || e is UnauthorizedAccessException || e is FormatException || e is InvalidOperationException)
            {
                return true;
            }
        }
    }

    internal static class Prices
    {
        private static readonly Dictionary<string, string> Ids = new Dictionary<string, string>
        {
            { "BTC", "bitcoin" }, { "ETH", "ethereum" }, { "USDT", "tether" }, { "USDC", "usd-coin" }, { "SOL", "solana" }, { "BNB", "binancecoin" },
            { "TRX", "tron" }, { "POL", "polygon-ecosystem-token" }, { "AVAX", "avalanche-2" }, { "LTC", "litecoin" }, { "DOGE", "dogecoin" }, { "KAIA", "kaia" },
        };

        public static async Task<Dictionary<string, decimal[]>> GetAsync(IEnumerable<string> symbols)
        {
            var result = new Dictionary<string, decimal[]>();
            var wanted = symbols.Distinct().Where(Ids.ContainsKey).ToList();
            if (wanted.Count == 0) return result;
            using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) })
            {
                http.DefaultRequestHeaders.UserAgent.ParseAdd("sillet-cli/" + Program.Version);
                var url = "https://api.coingecko.com/api/v3/simple/price?vs_currencies=usd&include_24hr_change=true&ids=" + string.Join(",", wanted.Select(w => Ids[w]));
                var json = Sillet.Net.JsonValue.Parse(await http.GetStringAsync(url));
                foreach (var symbol in wanted)
                {
                    Sillet.Net.JsonValue coin, price, change;
                    if (!json.TryGet(Ids[symbol], out coin) || !coin.TryGet("usd", out price)) continue;
                    decimal p, c = 0;
                    if (!price.TryDecimal(out p)) continue;
                    if (coin.TryGet("usd_24h_change", out change)) change.TryDecimal(out c);
                    result[symbol] = new[] { p, c };
                }
            }
            return result;
        }
    }

    internal sealed class ConsoleApp
    {
        private readonly Sillet.Storage.ProfileStore _profiles;
        private WalletManager _wallet;

        public ConsoleApp(Sillet.Storage.ProfileStore profiles)
        {
            _profiles = profiles;
            _wallet = new WalletManager(_profiles.VaultPath(_profiles.Active));
        }

        private Sillet.Storage.Profile Active => _profiles.Active;

        public async Task<int> RunAsync(string[] args)
        {
            var list = args.ToList();
            var walletIndex = list.FindIndex(a => a == "--wallet" || a == "-w");
            if (walletIndex >= 0 && walletIndex + 1 < list.Count)
            {
                var name = list[walletIndex + 1];
                var match = _profiles.Items.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (match == null) { Ui.Error("No wallet named \"" + name + "\". Use: sillet-cli wallets"); return 1; }
                Use(match);
                list.RemoveRange(walletIndex, 2);
            }
            var command = list.Count > 0 ? list[0].ToLowerInvariant() : "";
            switch (command)
            {
                case "":
                    await InteractiveAsync();
                    return 0;
                case "help": case "-h": case "--help": case "/?":
                    Help();
                    return 0;
                case "version": case "--version":
                    Console.WriteLine("sillet-cli " + Program.Version);
                    return 0;
                case "wallets":
                    ListWallets();
                    return 0;
                case "balance": case "portfolio":
                    if (!Open()) return 1;
                    await PortfolioAsync();
                    return 0;
                case "address": case "receive":
                    if (!Open()) return 1;
                    Receive(list.Count > 1 ? list[1] : null);
                    return 0;
                case "prices":
                    await PricesAsync();
                    return 0;
                default:
                    Ui.Error("Unknown command \"" + command + "\"");
                    Help();
                    return 1;
            }
        }

        private static void Help()
        {
            Ui.Banner();
            Console.WriteLine("  Usage: sillet-cli [command] [--wallet <name>]");
            Console.WriteLine();
            Ui.Row("(none)", "Interactive menu");
            Ui.Row("balance", "Balances on every network, with US$ value");
            Ui.Row("address [network]", "Receive addresses, for example: address tron");
            Ui.Row("prices", "Prices of the supported coins");
            Ui.Row("wallets", "Your wallets on this computer");
            Ui.Row("version", "Show the version");
            Console.WriteLine();
        }

        private void Use(Sillet.Storage.Profile profile)
        {
            _wallet.Lock();
            _profiles.SetActive(profile);
            _wallet = new WalletManager(_profiles.VaultPath(profile));
        }

        private void ListWallets()
        {
            Ui.Banner();
            if (_profiles.Items.Count == 0) { Console.WriteLine("  No wallets yet. Run sillet-cli to create one"); return; }
            foreach (var p in _profiles.Items)
            {
                var active = Active != null && Active.Id == p.Id;
                Ui.Color(active ? ConsoleColor.Green : ConsoleColor.Gray, "  " + (active ? "> " : "  ") + p.Name.PadRight(24));
                Ui.Color(ConsoleColor.DarkGray, p.KindText + Environment.NewLine);
            }
        }

        private bool Open()
        {
            if (_wallet.IsUnlocked) return true;
            if (Active != null && Active.IsWatch)
            {
                _wallet.OpenWatch(Active.Watch);
                return true;
            }
            if (!_wallet.HasSavedWallet)
            {
                Ui.Error("There is no wallet on this computer. Run sillet-cli without a command to create one");
                return false;
            }
            return Unlock();
        }

        private async Task InteractiveAsync()
        {
            Ui.Banner();
            if (_profiles.Items.Count > 1) ChooseWallet();
            if (Active == null || (!Active.IsWatch && !_wallet.HasSavedWallet))
            {
                if (!Setup()) return;
            }
            else if (!Open())
            {
                return;
            }

            while (_wallet.IsUnlocked)
            {
                Console.WriteLine();
                Ui.Color(ConsoleColor.Cyan, "  " + (Active == null ? "Wallet" : Active.Name));
                Ui.Color(ConsoleColor.DarkGray, _wallet.IsWatchOnly ? "  (hardware wallet)" + Environment.NewLine : Environment.NewLine);
                Ui.Menu("1", "Portfolio");
                Ui.Menu("2", "Receive");
                if (!_wallet.IsWatchOnly) Ui.Menu("3", "Send");
                Ui.Menu("4", "Prices");
                if (_profiles.Items.Count > 1) Ui.Menu("5", "Switch wallet");
                if (!_wallet.IsWatchOnly)
                {
                    Ui.Menu("6", "Export encrypted backup");
                    Ui.Menu("7", "Change password");
                    Ui.Menu("8", "Show recovery phrase");
                    Ui.Menu("9", "Delete this wallet from this computer");
                }
                Ui.Menu("0", "Exit");
                try
                {
                    switch (Ui.Ask("Choose an option"))
                    {
                        case "1": await PortfolioAsync(); break;
                        case "2": Receive(null); break;
                        case "3": if (!_wallet.IsWatchOnly) await SendAsync(); break;
                        case "4": await PricesAsync(); break;
                        case "5":
                            if (_profiles.Items.Count > 1 && ChooseWallet() && !Open()) return;
                            break;
                        case "6": if (!_wallet.IsWatchOnly) ExportBackup(); break;
                        case "7": if (!_wallet.IsWatchOnly) ChangePassword(); break;
                        case "8": if (!_wallet.IsWatchOnly) RevealPhrase(); break;
                        case "9": if (!_wallet.IsWatchOnly && DeleteWallet()) return; break;
                        case "0": _wallet.Lock(); return;
                    }
                }
                catch (WalletException e) { Ui.Error(e.Message); }
                catch (HttpRequestException) { Ui.Error("No connection to the network. Check your internet"); }
                catch (TaskCanceledException) { Ui.Error("The network took too long to respond. Try again"); }
            }
        }

        private bool ChooseWallet()
        {
            var choice = Ui.Choose("Which wallet?", _profiles.Items, p => p.Name + "  (" + p.KindText + ")");
            if (choice == null) return false;
            Use(choice);
            return true;
        }

        private bool Setup()
        {
            Console.WriteLine("  There is no wallet on this computer");
            Console.WriteLine();
            Ui.Menu("1", "Create a new wallet");
            Ui.Menu("2", "Import with my recovery phrase");
            Ui.Menu("0", "Exit");
            string phrase;
            switch (Ui.Ask("Choose an option"))
            {
                case "1":
                    phrase = WalletManager.GenerateMnemonic(12);
                    var words = phrase.Split(' ');
                    Console.WriteLine();
                    Console.WriteLine("  Your recovery phrase (write it on paper, in order, and show it to no one):");
                    Console.WriteLine();
                    Ui.Words(words);
                    Console.WriteLine();
                    Ui.Color(ConsoleColor.Yellow, "  Whoever has these words controls your funds. If you lose them, nobody can recover them" + Environment.NewLine);
                    Ui.Ask("Press Enter once you have written it down");
                    Console.Clear();
                    Ui.Banner();
                    foreach (int n in new[] { 3, 9 })
                    {
                        while (!Ui.Ask("To check, type word #" + n).Trim().Equals(words[n - 1], StringComparison.OrdinalIgnoreCase))
                            Ui.Error("It does not match. Check your paper");
                    }
                    break;
                case "2":
                    while (true)
                    {
                        phrase = Ui.AskSecret("Type your recovery phrase (12 or 24 words separated by spaces)");
                        var problem = Bip39.Validate(phrase);
                        if (problem == null) break;
                        Ui.Error(problem);
                    }
                    break;
                default:
                    return false;
            }

            while (true)
            {
                var p1 = Ui.AskSecret("Create a password for this computer (at least 8 characters)");
                if (p1.Length < 8) { Ui.Error("Too short"); continue; }
                if (Ui.AskSecret("Repeat it") != p1) { Ui.Error("They do not match"); continue; }
                var pass1 = Ui.AskSecret("Optional passphrase (25th word), leave empty for none");
                if (pass1.Length > 0 && Ui.AskSecret("Repeat the passphrase") != pass1) { Ui.Error("They do not match"); continue; }
                Ui.Spin("Encrypting", () => _wallet.Create(phrase, p1, pass1));
                _profiles.EnsureMain();
                Ui.Ok("  Wallet saved and encrypted");
                return true;
            }
        }

        private bool Unlock()
        {
            for (int attempt = 0; attempt < 5; attempt++)
            {
                var password = Ui.AskSecret("Password for " + (Active == null ? "your wallet" : Active.Name));
                try
                {
                    Ui.Spin("Unlocking", () => _wallet.Unlock(password));
                    Ui.Ok("  Unlocked");
                    return true;
                }
                catch (WalletException e)
                {
                    Ui.Error(e.Message);
                    if (e.Message.StartsWith("This wallet file is locked")) return false;
                    if (attempt >= 2) System.Threading.Thread.Sleep(1000 * (attempt - 1) * 2);
                }
            }
            return false;
        }

        private async Task PortfolioAsync()
        {
            Console.WriteLine();
            IReadOnlyList<PortfolioEntry> entries = null;
            await Ui.SpinAsync("Checking all networks", async () => entries = await _wallet.GetPortfolioAsync());
            var withFunds = entries.Where(e => e.Balance != null && e.Balance.Raw.Sign > 0).ToList();
            Dictionary<string, decimal[]> prices;
            try { prices = await Prices.GetAsync(withFunds.Select(e => e.Asset.Symbol)); }
            catch (Exception e) when (e is HttpRequestException || e is TaskCanceledException || e is FormatException) { prices = new Dictionary<string, decimal[]>(); }

            Console.WriteLine();
            if (withFunds.Count == 0) Console.WriteLine("  No funds yet. Use \"Receive\" to see your addresses");
            decimal total = 0;
            foreach (var e in withFunds.OrderByDescending(x => prices.ContainsKey(x.Asset.Symbol) ? x.Balance.Amount * prices[x.Asset.Symbol][0] : 0))
            {
                decimal[] price;
                var usd = prices.TryGetValue(e.Asset.Symbol, out price) ? e.Balance.Amount * price[0] : (decimal?)null;
                if (usd.HasValue) total += usd.Value;
                Ui.Color(ConsoleColor.White, "  " + e.Asset.Symbol.PadRight(6));
                Console.Write(Ui.Amount(e.Balance.Amount).PadLeft(20));
                Ui.Color(ConsoleColor.DarkGray, ("   " + e.Chain.Name).PadRight(16));
                Ui.Color(ConsoleColor.Gray, usd.HasValue ? ("US$" + usd.Value.ToString("N2", CultureInfo.InvariantCulture)).PadLeft(16) : "");
                Console.WriteLine();
            }
            if (total > 0)
            {
                Console.WriteLine("  " + new string('-', 58));
                Ui.Color(ConsoleColor.White, "  Total".PadRight(42));
                Ui.Ok(("US$" + total.ToString("N2", CultureInfo.InvariantCulture)).PadLeft(16));
            }

            var failed = entries.Where(e => e.Error != null).Select(e => e.Chain.Name).Distinct().ToList();
            if (failed.Count > 0)
            {
                Console.WriteLine();
                Ui.Color(ConsoleColor.DarkYellow, "  Could not check: " + string.Join(", ", failed) + " (try again later)" + Environment.NewLine);
            }
        }

        private async Task PricesAsync()
        {
            Dictionary<string, decimal[]> prices = null;
            await Ui.SpinAsync("Getting prices", async () => prices = await Prices.GetAsync(new[] { "BTC", "ETH", "SOL", "BNB", "TRX", "LTC", "DOGE", "AVAX", "POL", "KAIA", "USDT", "USDC" }));
            Console.WriteLine();
            foreach (var p in prices)
            {
                Ui.Color(ConsoleColor.White, "  " + p.Key.PadRight(6));
                Console.Write(("US$" + p.Value[0].ToString(p.Value[0] < 10 ? "N4" : "N2", CultureInfo.InvariantCulture)).PadLeft(18));
                var change = p.Value[1];
                Ui.Color(change >= 0 ? ConsoleColor.Green : ConsoleColor.Red, ((change >= 0 ? "+" : "") + change.ToString("0.00", CultureInfo.InvariantCulture) + "%").PadLeft(12) + Environment.NewLine);
            }
            Ui.Color(ConsoleColor.DarkGray, Environment.NewLine + "  24h change, data from CoinGecko" + Environment.NewLine);
        }

        private void Receive(string filter)
        {
            Console.WriteLine();
            var chains = _wallet.Chains.Where(c => filter == null || c.Id.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0
                || c.Name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0 || c.Native.Symbol.Equals(filter, StringComparison.OrdinalIgnoreCase)).ToList();
            if (chains.Count == 0) { Ui.Error("No network matches \"" + filter + "\""); return; }
            foreach (var chain in chains)
            {
                var tokens = chain.Assets.Where(a => !a.IsNative).Select(a => a.Symbol).ToList();
                var extra = tokens.Count > 0 ? ", " + string.Join(", ", tokens) : "";
                Ui.Color(ConsoleColor.Cyan, "  " + chain.Name);
                Ui.Color(ConsoleColor.DarkGray, "  " + chain.Native.Symbol + extra + Environment.NewLine);
                Console.WriteLine("    " + chain.Address);
            }
            Console.WriteLine();
            Ui.Color(ConsoleColor.Yellow, "  The sender must choose the SAME network. USDT on TRON only arrives at your TRON address" + Environment.NewLine);
        }

        private async Task SendAsync()
        {
            var chain = Ui.Choose("Which network do you want to send on?", _wallet.Chains, c => c.Name + " (" + string.Join(", ", c.Assets.Select(a => a.Symbol)) + ")");
            if (chain == null) return;
            var asset = chain.Assets.Count == 1 ? chain.Native : Ui.Choose("Which coin?", chain.Assets, a => a.Symbol + ", " + a.Name);
            if (asset == null) return;

            AssetBalance balance = null;
            await Ui.SpinAsync("Checking balance", async () => balance = await chain.GetBalanceAsync(asset));
            Console.WriteLine("  Available: " + balance);

            string to;
            while (true)
            {
                to = Ui.Ask("Recipient address on " + chain.Name).Trim();
                if (to.Length == 0) return;
                if (to == chain.Address) { Ui.Error("That is your own address"); continue; }
                if (chain.IsValidAddress(to)) break;
                Ui.Error("Invalid address for " + chain.Name);
            }

            decimal amount = 0;
            bool max = false;
            while (true)
            {
                var text = Ui.Ask("Amount in " + asset.Symbol + " (or \"max\" to send everything)").Trim().Replace(',', '.');
                if (text.Equals("max", StringComparison.OrdinalIgnoreCase)) { max = true; break; }
                if (decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out amount) && amount > 0) break;
                Ui.Error("Invalid amount");
            }

            PreparedTransfer transfer = null;
            await Ui.SpinAsync("Preparing and signing", async () => transfer = await chain.PrepareTransferAsync(asset, to, amount, max));

            Console.WriteLine();
            Ui.Row("Network", chain.Name);
            Ui.Row("Amount", Ui.Amount(transfer.Amount) + " " + asset.Symbol);
            Ui.Row("To", to);
            Ui.Row("Fee", "~" + Ui.Amount(transfer.Fee) + " " + transfer.FeeAsset.Symbol);
            foreach (var note in transfer.Notes) Ui.Color(ConsoleColor.Yellow, "    - " + note + Environment.NewLine);
            Ui.Color(ConsoleColor.DarkGray, "    This cannot be undone" + Environment.NewLine);

            var tail = to.Length >= 4 ? to.Substring(to.Length - 4) : to;
            if (!Ui.Ask("Type the last 4 characters of the address to send").Trim().Equals(tail, StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("  Cancelled. Nothing was sent");
                return;
            }
            TransferResult result = null;
            await Ui.SpinAsync("Broadcasting", async () => result = await chain.BroadcastAsync(transfer));
            Ui.Ok(Environment.NewLine + "  Sent. Id: " + result.TxId);
            Console.WriteLine("  Track it here: " + result.ExplorerUrl);
        }

        private void ExportBackup()
        {
            var name = "sillet-backup-" + DateTime.Now.ToString("yyyy-MM-dd") + ".json";
            var path = Ui.Ask("Save to (Enter for " + name + " in Documents)").Trim().Trim('"');
            if (path.Length == 0) path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), name);
            _wallet.ExportBackup(path);
            Ui.Ok("  Encrypted backup saved to " + path);
            Console.WriteLine("  It opens with your current password, on any computer");
        }

        private void ChangePassword()
        {
            var current = Ui.AskSecret("Current password");
            var next = Ui.AskSecret("New password (at least 8 characters)");
            if (Ui.AskSecret("Repeat the new password") != next) { Ui.Error("They do not match"); return; }
            Ui.Spin("Encrypting", () => _wallet.ChangePassword(current, next));
            Ui.Ok("  Password changed");
        }

        private void RevealPhrase()
        {
            string phrase = null;
            var password = Ui.AskSecret("Confirm your password");
            Ui.Spin("Decrypting", () => phrase = _wallet.RevealMnemonic(password));
            Console.WriteLine();
            Ui.Color(ConsoleColor.Yellow, "  Do not share it with anyone. No legitimate service will ever ask for it" + Environment.NewLine);
            Console.WriteLine();
            Ui.Words(phrase.Split(' '));
            Ui.Ask(Environment.NewLine + "Press Enter to hide it");
            Console.Clear();
            Ui.Banner();
        }

        private bool DeleteWallet()
        {
            Ui.Color(ConsoleColor.Red, "  This deletes the wallet from this computer. You can only recover it with your recovery phrase" + Environment.NewLine);
            if (Ui.Ask("Type \"delete\" to confirm") != "delete") return false;
            var password = Ui.AskSecret("Password");
            Ui.Spin("Checking", () => _wallet.DeleteSavedWallet(password));
            if (Active != null) _profiles.Remove(Active);
            Ui.Ok("  Wallet deleted from this computer");
            return true;
        }
    }

    internal static class Ui
    {
        public static void Banner()
        {
            Console.WriteLine();
            Color(ConsoleColor.White, "  sillet");
            Color(ConsoleColor.DarkGray, "  cli " + Program.Version + "  ·  raton.fun/wallet" + Environment.NewLine);
            Console.WriteLine();
        }

        public static void Color(ConsoleColor color, string text)
        {
            Console.ForegroundColor = color;
            Console.Write(text);
            Console.ResetColor();
        }

        public static void Menu(string key, string label)
        {
            Color(ConsoleColor.DarkCyan, "   " + key + "  ");
            Console.WriteLine(label);
        }

        public static void Row(string label, string value)
        {
            Color(ConsoleColor.DarkGray, "    " + label.PadRight(20));
            Console.WriteLine(value);
        }

        public static string Amount(decimal value)
        {
            return value.ToString("0.########", CultureInfo.InvariantCulture);
        }

        public static void Words(string[] words)
        {
            for (int i = 0; i < words.Length; i++)
            {
                Color(ConsoleColor.DarkGray, "  " + (i + 1).ToString().PadLeft(2) + ". ");
                Color(ConsoleColor.White, words[i].PadRight(12));
                if ((i + 1) % 3 == 0) Console.WriteLine();
            }
        }

        public static void Spin(string label, Action work)
        {
            SpinAsync(label, () => Task.Run(work)).GetAwaiter().GetResult();
        }

        public static async Task SpinAsync(string label, Func<Task> work)
        {
            var frames = new[] { "|", "/", "-", "\\" };
            var task = work();
            var interactive = !Console.IsOutputRedirected;
            int i = 0;
            while (!task.IsCompleted)
            {
                if (interactive)
                {
                    Console.Write("\r  " + frames[i++ % frames.Length] + " " + label + "...");
                }
                await Task.WhenAny(task, Task.Delay(90));
            }
            if (interactive) Console.Write("\r" + new string(' ', label.Length + 10) + "\r");
            await task;
        }

        public static T Choose<T>(string question, IReadOnlyList<T> options, Func<T, string> label) where T : class
        {
            Console.WriteLine();
            Console.WriteLine("  " + question);
            for (int i = 0; i < options.Count; i++) Menu((i + 1).ToString().PadLeft(2), label(options[i]));
            Menu(" 0", "Back");
            while (true)
            {
                var text = Ask("Number");
                if (text == "0" || text.Length == 0) return null;
                int n;
                if (int.TryParse(text, out n) && n >= 1 && n <= options.Count) return options[n - 1];
                Error("Invalid option");
            }
        }

        public static string Ask(string prompt)
        {
            Color(ConsoleColor.Gray, "  " + prompt + ": ");
            return Console.ReadLine() ?? "";
        }

        public static string AskSecret(string prompt)
        {
            Color(ConsoleColor.Gray, "  " + prompt + ": ");
            if (Console.IsInputRedirected) return Console.ReadLine() ?? "";
            var sb = new StringBuilder();
            while (true)
            {
                var key = Console.ReadKey(true);
                if (key.Key == ConsoleKey.Enter) break;
                if (key.Key == ConsoleKey.Backspace)
                {
                    if (sb.Length > 0)
                    {
                        sb.Length--;
                        Console.Write("\b \b");
                    }
                    continue;
                }
                if (!char.IsControl(key.KeyChar))
                {
                    sb.Append(key.KeyChar);
                    Console.Write('*');
                }
            }
            Console.WriteLine();
            return sb.ToString();
        }

        public static void Error(string message)
        {
            Color(ConsoleColor.Red, "  Error: " + message + Environment.NewLine);
        }

        public static void Ok(string message)
        {
            Color(ConsoleColor.Green, message + Environment.NewLine);
        }
    }
}
