using System.Collections.Generic;
using System.Linq;
using System;
using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Sillet.Core;
using Sillet.Crypto;
using Sillet.App.Services;

namespace Sillet.App.ViewModels
{
    public enum OnboardingStep
    {
        Welcome,
        ShowPhrase,
        VerifyPhrase,
        Import,
        Password,
    }

    internal static class SecureRandom
    {
        public static int Next(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive) throw new ArgumentOutOfRangeException(nameof(maxExclusive));
            uint range = (uint)(maxExclusive - minInclusive);
            uint limit = uint.MaxValue - uint.MaxValue % range;
            var buffer = new byte[4];
            using (var rng = RandomNumberGenerator.Create())
            {
                while (true)
                {
                    rng.GetBytes(buffer);
                    uint value = BitConverter.ToUInt32(buffer, 0);
                    if (value < limit) return minInclusive + (int)(value % range);
                }
            }
        }
    }

    public sealed class OnboardingViewModel : ObservableObject
    {
        private readonly MainViewModel _main;
        private OnboardingStep _step;
        private string _phrase = "";
        private bool _savedConfirmed;
        private string _verifyA = "";
        private string _verifyB = "";
        private string _importText = "";
        private string _error;
        private bool _isBusy;
        private bool _isImport;

        public OnboardingViewModel(MainViewModel main, bool restoring)
        {
            _main = main;
            IsRestoring = restoring;
            _step = restoring ? OnboardingStep.Import : OnboardingStep.Welcome;
            _isImport = restoring;
            Words = new ObservableCollection<WordItem>();
            Password = "";
            PasswordRepeat = "";

            CreateCommand = new RelayCommand(StartCreate);
            ImportCommand = new RelayCommand(() =>
            {
                IsImport = true;
                Go(OnboardingStep.Import);
            });
            ContinueFromPhraseCommand = new RelayCommand(() => Go(OnboardingStep.VerifyPhrase), () => SavedConfirmed);
            VerifyCommand = new RelayCommand(Verify);
            ImportNextCommand = new RelayCommand(ImportNext);
            FinishCommand = new AsyncCommand(FinishAsync);
            BackCommand = new RelayCommand(Back);
            CancelRestoreCommand = new RelayCommand(() => _main.ShowUnlock());
            RestoreBackupCommand = new RelayCommand(RestoreBackup);
            CancelAddCommand = new RelayCommand(() => _main.CancelAddWallet());
            WatchCommand = new RelayCommand(() => _main.StartWatchWallet());
        }

        public RelayCommand CancelAddCommand { get; }
        public RelayCommand WatchCommand { get; }
        public bool CanCancelAdd => _main.IsAddingWallet || (_main.Profiles.Items.Count > 0 && !_main.Wallet.HasSavedWallet);
        public string WelcomeTitle => _main.IsAddingWallet ? Loc.T("Add a wallet") : "sillet";

        public bool IsRestoring { get; }

        public OnboardingStep Step
        {
            get { return _step; }
            private set
            {
                if (Set(ref _step, value)) Raise(nameof(StepIndex), nameof(CanGoBack), nameof(CanCancelRestore));
            }
        }

        public int StepIndex => (int)Step;
        public bool CanGoBack => Step != OnboardingStep.Welcome && !(IsRestoring && Step == OnboardingStep.Import);
        public bool CanCancelRestore => IsRestoring && Step == OnboardingStep.Import;

        public bool IsImport
        {
            get { return _isImport; }
            private set { Set(ref _isImport, value); }
        }

        public ObservableCollection<WordItem> Words { get; }

        public bool SavedConfirmed
        {
            get { return _savedConfirmed; }
            set
            {
                if (Set(ref _savedConfirmed, value)) ContinueFromPhraseCommand.Refresh();
            }
        }

        public int VerifyIndexA { get; private set; }
        public int VerifyIndexB { get; private set; }

        public string VerifyA
        {
            get { return _verifyA; }
            set { Set(ref _verifyA, value); }
        }

        public string VerifyB
        {
            get { return _verifyB; }
            set { Set(ref _verifyB, value); }
        }

        public string ImportText
        {
            get { return _importText; }
            set { Set(ref _importText, value); }
        }

        private string _password = "";

        public string Password
        {
            get { return _password; }
            set
            {
                _password = value ?? "";
                Raise(nameof(StrengthScore), nameof(StrengthValue), nameof(StrengthText), nameof(HasStrength));
            }
        }

        public string PasswordRepeat { get; set; }
        public string Passphrase { get; set; } = "";
        public string PassphraseRepeat { get; set; } = "";
        public int StrengthScore => Sillet.App.Services.PasswordStrength.Score(_password);
        public double StrengthValue => Math.Max(0.08, StrengthScore / 4.0);
        public string StrengthText => Sillet.App.Services.PasswordStrength.Label(StrengthScore);
        public bool HasStrength => _password.Length > 0;

        public string Error
        {
            get { return _error; }
            private set
            {
                Set(ref _error, value);
                Raise(nameof(HasError));
            }
        }

        public bool HasError => !string.IsNullOrEmpty(Error);

        public bool IsBusy
        {
            get { return _isBusy; }
            private set { Set(ref _isBusy, value); }
        }

        public RelayCommand CreateCommand { get; }
        public RelayCommand ImportCommand { get; }
        public RelayCommand ContinueFromPhraseCommand { get; }
        public RelayCommand VerifyCommand { get; }
        public RelayCommand ImportNextCommand { get; }
        public AsyncCommand FinishCommand { get; }
        public RelayCommand BackCommand { get; }
        public RelayCommand CancelRestoreCommand { get; }
        public RelayCommand RestoreBackupCommand { get; }

        private void RestoreBackup()
        {
            var path = _main.Ui.PickOpenPath("Sillet backup|*.json|All files|*.*");
            if (path == null) return;
            try
            {
                _main.Wallet.ImportBackup(path);
                _main.ShowUnlock();
                _main.ShowToast(Loc.T("Backup restored. Unlock it with its password"));
            }
            catch (WalletException e)
            {
                Error = MainViewModel.Clean(e.Message);
            }
        }

        private void Go(OnboardingStep step)
        {
            Error = null;
            Step = step;
        }

        private void StartCreate()
        {
            IsImport = false;
            _phrase = WalletManager.GenerateMnemonic(12);
            Words.Clear();
            var words = _phrase.Split(' ');
            for (int i = 0; i < words.Length; i++) Words.Add(new WordItem(i + 1, words[i]));
            VerifyIndexA = SecureRandom.Next(1, 7);
            VerifyIndexB = SecureRandom.Next(7, 13);
            VerifyA = "";
            VerifyB = "";
            SavedConfirmed = false;
            Raise(nameof(VerifyIndexA), nameof(VerifyIndexB));
            Go(OnboardingStep.ShowPhrase);
        }

        private void Verify()
        {
            var words = _phrase.Split(' ');
            if (!VerifyA.Trim().Equals(words[VerifyIndexA - 1], StringComparison.OrdinalIgnoreCase) ||
                !VerifyB.Trim().Equals(words[VerifyIndexB - 1], StringComparison.OrdinalIgnoreCase))
            {
                Error = Loc.T("The words do not match");
                return;
            }
            Go(OnboardingStep.Password);
        }

        private void ImportNext()
        {
            var problem = Bip39.Validate(ImportText);
            if (problem != null)
            {
                Error = problem;
                return;
            }
            _phrase = Bip39.Normalize(ImportText);
            Go(OnboardingStep.Password);
        }

        private async Task FinishAsync()
        {
            if (Password.Length < 8)
            {
                Error = Loc.T("The password must have at least 8 characters");
                return;
            }
            if (!Sillet.App.Services.PasswordStrength.IsAcceptable(Password))
            {
                Error = Sillet.App.Services.PasswordStrength.Advice;
                return;
            }
            if (Password != PasswordRepeat)
            {
                Error = Loc.T("The passwords do not match");
                return;
            }
            if ((Passphrase ?? "") != (PassphraseRepeat ?? ""))
            {
                Error = Loc.T("The passphrases do not match");
                return;
            }
            Error = null;
            IsBusy = true;
            try
            {
                var phrase = _phrase;
                var password = Password;
                var passphrase = Passphrase ?? "";
                await Task.Run(() => _main.Wallet.Create(phrase, password, passphrase));
                Passphrase = "";
                PassphraseRepeat = "";
                _phrase = "";
                Words.Clear();
                _main.OnUnlocked();
                _main.ShowToast(IsImport ? Loc.T("Wallet imported") : Loc.T("Wallet created"));
            }
            catch (WalletException e)
            {
                Error = MainViewModel.Clean(e.Message);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void Back()
        {
            switch (Step)
            {
                case OnboardingStep.VerifyPhrase:
                    Go(OnboardingStep.ShowPhrase);
                    break;
                case OnboardingStep.Password:
                    Go(IsImport ? OnboardingStep.Import : OnboardingStep.VerifyPhrase);
                    break;
                default:
                    Go(OnboardingStep.Welcome);
                    break;
            }
        }
    }

    public sealed class UnlockViewModel : ObservableObject
    {
        private readonly MainViewModel _main;
        private string _error;
        private bool _isBusy;

        public UnlockViewModel(MainViewModel main)
        {
            _main = main;
            Password = "";
            UnlockCommand = new AsyncCommand(UnlockAsync);
            ForgotCommand = new AsyncCommand(ForgotAsync);
            HelloCommand = new AsyncCommand(HelloAsync);
            OpenWatchCommand = new AsyncCommand(OpenWatchAsync);
        }

        public string Password { get; set; }

        public string Error
        {
            get { return _error; }
            private set
            {
                Set(ref _error, value);
                Raise(nameof(HasError));
            }
        }

        public bool HasError => !string.IsNullOrEmpty(Error);

        public bool IsBusy
        {
            get { return _isBusy; }
            private set { Set(ref _isBusy, value); }
        }

        public AsyncCommand UnlockCommand { get; }
        public AsyncCommand ForgotCommand { get; }
        public AsyncCommand HelloCommand { get; }
        public AsyncCommand OpenWatchCommand { get; }
        public bool HasPasskey => _main.Passkey.IsSet && !IsWatch;
        public bool IsWatch => _main.ActiveProfile != null && _main.ActiveProfile.IsWatch;
        public bool IsSeed => !IsWatch;
        public bool HasManyProfiles => _main.HasManyProfiles;
        public IReadOnlyList<Sillet.Storage.Profile> Profiles => _main.ProfileList;
        public string ProfileName => _main.ActiveProfileName;
        public string ProfileKind => _main.ActiveProfile == null ? "" : _main.ActiveProfile.KindText;

        public Sillet.Storage.Profile SelectedProfile
        {
            get { return _main.ProfileList.FirstOrDefault(p => _main.ActiveProfile != null && p.Id == _main.ActiveProfile.Id); }
            set
            {
                if (value == null || (_main.ActiveProfile != null && value.Id == _main.ActiveProfile.Id)) return;
                _main.SwitchProfile(value);
            }
        }

        private async Task OpenWatchAsync()
        {
            Error = null;
            try
            {
                _main.OpenWatchActive();
            }
            catch (WalletException e)
            {
                Error = MainViewModel.Clean(e.Message);
            }
            await Task.CompletedTask;
        }
        public bool AutoHelloTried { get; set; }

        private void ForgetPasskey(string message)
        {
            _main.Passkey.Remove(_main.Ui);
            Raise(nameof(HasPasskey));
            Error = Loc.T(message);
        }

        private async Task HelloAsync()
        {
            if (!HasPasskey || IsBusy) return;
            Error = null;
            IsBusy = true;
            try
            {
                var password = await _main.Passkey.GetPasswordAsync(_main.Ui, Loc.T("Unlock Sillet"));
                try
                {
                    await Task.Run(() => _main.Wallet.Unlock(password));
                }
                catch (WalletException)
                {
                    ForgetPasskey(Loc.T("Your password changed since Windows Hello was set up. Unlock with your password and set it up again"));
                    return;
                }
                Password = "";
                _main.OnUnlocked();
            }
            catch (Sillet.App.Services.PasskeyException e)
            {
                if (e.Missing) ForgetPasskey(e.Message);
                else if (!e.Cancelled) Error = Loc.T(e.Message);
            }
            finally
            {
                IsBusy = false;
            }
        }

        private async Task ForgotAsync()
        {
            if (await _main.ConfirmAsync(Loc.T("Restore with recovery phrase"),
                Loc.T("The wallet saved on this computer will be replaced by the one from your phrase"), Loc.T("Continue")))
                _main.ShowOnboarding(true);
        }

        public string LastOpenedText => _main.LastOpenedText;
        public bool HasLastOpened => LastOpenedText.Length > 0;

        private async Task UnlockAsync()
        {
            if (Password.Length == 0) return;
            var wait = _main.UnlockWaitSeconds(DateTime.UtcNow);
            if (wait > 0)
            {
                Error = Loc.T("Too many wrong passwords. Try again in ") + wait + "s";
                return;
            }
            Error = null;
            IsBusy = true;
            try
            {
                var password = Password;
                await Task.Run(() => _main.Wallet.Unlock(password));
                Password = "";
                _main.OnUnlocked();
            }
            catch (WalletException e)
            {
                var message = MainViewModel.Clean(e.Message);
                var delay = _main.RegisterFailedUnlock(DateTime.UtcNow);
                Error = delay > 0 ? Loc.T(message) + Loc.F(". Try again in {0}s", delay) : Loc.T(message);
            }
            finally
            {
                IsBusy = false;
            }
        }
    }

    public sealed class WatchField : ObservableObject
    {
        private string _address = "";
        private bool _isInvalid;

        public WatchField(string group)
        {
            Group = group;
            Title = WalletManager.WatchGroupName(group);
            Hint = group == "evm" ? Loc.T("0x... works for Ethereum, BNB, Polygon, Arbitrum, Optimism, Base, Avalanche, Kaia, Linea, Blast and Gnosis") : Loc.F("Your {0} receive address", Title);
        }

        public string Group { get; }
        public string Title { get; }
        public string Hint { get; }

        public string Address
        {
            get { return _address; }
            set
            {
                Set(ref _address, (value ?? "").Trim());
                IsInvalid = false;
            }
        }

        public bool IsInvalid
        {
            get { return _isInvalid; }
            set { Set(ref _isInvalid, value); }
        }
    }

    public sealed class WatchWalletViewModel : ObservableObject
    {
        private readonly MainViewModel _main;
        private string _name;
        private string _error;

        public WatchWalletViewModel(MainViewModel main)
        {
            _main = main;
            _name = Loc.T("Hardware wallet");
            Fields = WalletManager.WatchGroups.Select(g => new WatchField(g)).ToList();
            SaveCommand = new RelayCommand(Save);
            CancelCommand = new RelayCommand(() => _main.CancelAddWallet());
            Sources = _main.Profiles.Items.Where(p => p.HasAddresses).ToList();
            ImportFileCommand = new RelayCommand(ImportFile);
            PasteCommand = new RelayCommand(p =>
            {
                var field = p as WatchField;
                var text = _main.Ui.ReadClipboard();
                if (field != null && !string.IsNullOrWhiteSpace(text)) field.Address = text.Trim();
            });
        }

        public IReadOnlyList<WatchField> Fields { get; }
        public IReadOnlyList<Sillet.Storage.Profile> Sources { get; }
        public bool HasSources => Sources.Count > 0;
        public RelayCommand ImportFileCommand { get; }
        private Sillet.Storage.Profile _source;

        public Sillet.Storage.Profile Source
        {
            get { return _source; }
            set
            {
                if (!Set(ref _source, value) || value == null) return;
                Fill(value.Name + (value.IsWatch ? "" : Loc.T(" (watch)")), value.Watch);
            }
        }

        private void Fill(string name, IDictionary<string, string> addresses)
        {
            Error = null;
            if (!string.IsNullOrWhiteSpace(name)) Name = name.Length > 32 ? name.Substring(0, 32) : name;
            foreach (var f in Fields)
            {
                string address;
                f.Address = addresses.TryGetValue(f.Group, out address) ? address : "";
            }
        }

        private void ImportFile()
        {
            var path = _main.Ui.PickOpenPath("Sillet watch-only file|*.json");
            if (path == null) return;
            string name;
            Dictionary<string, string> addresses;
            string text;
            try
            {
                text = System.IO.File.ReadAllText(path);
            }
            catch (Exception e) when (e is System.IO.IOException || e is UnauthorizedAccessException)
            {
                Error = Loc.T("Could not read that file");
                return;
            }
            if (!Sillet.Storage.WatchFile.TryParse(text, out name, out addresses))
            {
                Error = Loc.T("That file is not a Sillet watch-only file");
                return;
            }
            Fill(name, addresses);
        }
        public RelayCommand SaveCommand { get; }
        public RelayCommand CancelCommand { get; }
        public RelayCommand PasteCommand { get; }
        public bool CanCancel => _main.Profiles.Items.Count > 0;

        public string Name
        {
            get { return _name; }
            set { Set(ref _name, value ?? ""); }
        }

        public string Error
        {
            get { return _error; }
            private set
            {
                Set(ref _error, value);
                Raise(nameof(HasError));
            }
        }

        public bool HasError => !string.IsNullOrEmpty(Error);

        private void Save()
        {
            Error = null;
            var checker = new WalletManager(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sillet-watch-check.json"));
            var bad = new List<string>();
            foreach (var f in Fields)
            {
                f.IsInvalid = f.Address.Length > 0 && !checker.IsValidWatchAddress(f.Group, f.Address);
                if (f.IsInvalid) bad.Add(f.Title);
            }
            if (bad.Count > 0)
            {
                Error = Loc.F("Check the address for {0}", string.Join(", ", bad));
                return;
            }
            if (Fields.All(f => f.Address.Length == 0))
            {
                Error = Loc.T("Add at least one address");
                return;
            }
            try
            {
                _main.OpenWatchProfile(Name, Fields.Where(f => f.Address.Length > 0).ToDictionary(f => f.Group, f => f.Address));
            }
            catch (WalletException e)
            {
                Error = MainViewModel.Clean(e.Message);
            }
        }
    }
}
