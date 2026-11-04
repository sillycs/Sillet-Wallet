using System;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Hardware.Biometrics;
using Android.OS;
using Android.Security.Keystore;
using Java.Lang;
using Java.Security;
using Javax.Crypto;
using Javax.Crypto.Spec;
using Sillet.App.Services;

namespace Sillet.Droid
{
    public sealed class Biometrics
    {
        private const string KeyAlias = "sillet_passkey";
        private const int BiometricStrong = 15;
        private const int IvLength = 12;
        private readonly Activity _activity;

        public Biometrics(Activity activity)
        {
            _activity = activity;
        }

        public string Unavailable()
        {
            if (!OperatingSystem.IsAndroidVersionAtLeast(30)) return Loc.T("Biometric unlock needs Android 11 or newer");
            try
            {
                var manager = _activity.GetSystemService(Context.BiometricService) as BiometricManager;
                if (manager == null) return Loc.T("This phone has no biometric unlock that Sillet can use");
                var status = (int)manager.CanAuthenticate(BiometricStrong);
                if (status == 0) return null;
                if (status == 11) return Loc.T("Add a fingerprint or face in your phone settings first");
                return Loc.T("This phone has no biometric unlock that Sillet can use");
            }
            catch (Java.Lang.Exception)
            {
                return Loc.T("This phone has no biometric unlock that Sillet can use");
            }
        }

        public Task<byte[]> CreateAsync(byte[] secret)
        {
            var reason = Unavailable();
            if (reason != null) throw new PasskeyException(reason, false);
            Cipher cipher;
            try
            {
                DeleteKey();
                var key = CreateKey();
                cipher = Cipher.GetInstance("AES/GCM/NoPadding");
                cipher.Init(CipherMode.EncryptMode, key);
            }
            catch (Java.Lang.Exception e)
            {
                throw new PasskeyException(e.Message ?? Loc.T("Biometric check failed"), false);
            }
            var copy = (byte[])secret.Clone();
            return Authenticate(cipher, Loc.T("Set up biometric unlock"), c =>
            {
                var body = c.DoFinal(copy);
                Array.Clear(copy, 0, copy.Length);
                var iv = c.GetIV();
                var result = new byte[iv.Length + body.Length];
                Buffer.BlockCopy(iv, 0, result, 0, iv.Length);
                Buffer.BlockCopy(body, 0, result, iv.Length, body.Length);
                return result;
            });
        }

        public Task<byte[]> DecryptAsync(byte[] encrypted, string title)
        {
            if (encrypted == null || encrypted.Length <= IvLength) throw new PasskeyException(Loc.T("The saved Windows Hello data can't be read. Unlock with your password and set it up again"), false, true);
            Cipher cipher;
            try
            {
                var key = LoadKey();
                if (key == null) throw new PasskeyException(Loc.T("Windows Hello is not set up"), false, true);
                var iv = new byte[IvLength];
                Buffer.BlockCopy(encrypted, 0, iv, 0, IvLength);
                cipher = Cipher.GetInstance("AES/GCM/NoPadding");
                cipher.Init(CipherMode.DecryptMode, key, new GCMParameterSpec(128, iv));
            }
            catch (KeyPermanentlyInvalidatedException)
            {
                DeleteKey();
                throw new PasskeyException(Loc.T("The saved Windows Hello data can't be read. Unlock with your password and set it up again"), false, true);
            }
            catch (Java.Lang.Exception e)
            {
                throw new PasskeyException(e.Message ?? Loc.T("Biometric check failed"), false);
            }
            var body = new byte[encrypted.Length - IvLength];
            Buffer.BlockCopy(encrypted, IvLength, body, 0, body.Length);
            return Authenticate(cipher, title, c => c.DoFinal(body));
        }

        public void DeleteKey()
        {
            try
            {
                var store = KeyStore.GetInstance("AndroidKeyStore");
                store.Load(null);
                if (store.ContainsAlias(KeyAlias)) store.DeleteEntry(KeyAlias);
            }
            catch (Java.Lang.Exception)
            {
            }
        }

        private static IKey LoadKey()
        {
            var store = KeyStore.GetInstance("AndroidKeyStore");
            store.Load(null);
            return store.ContainsAlias(KeyAlias) ? store.GetKey(KeyAlias, null) : null;
        }

        private static IKey CreateKey()
        {
            var generator = KeyGenerator.GetInstance(KeyProperties.KeyAlgorithmAes, "AndroidKeyStore");
            var spec = new KeyGenParameterSpec.Builder(KeyAlias, KeyStorePurpose.Encrypt | KeyStorePurpose.Decrypt)
                .SetBlockModes(KeyProperties.BlockModeGcm)
                .SetEncryptionPaddings(KeyProperties.EncryptionPaddingNone)
                .SetKeySize(256)
                .SetUserAuthenticationRequired(true)
                .SetInvalidatedByBiometricEnrollment(true)
                .Build();
            generator.Init(spec);
            return generator.GenerateKey();
        }

        private Task<byte[]> Authenticate(Cipher cipher, string title, Func<Cipher, byte[]> work)
        {
            var done = new TaskCompletionSource<byte[]>();
            _activity.RunOnUiThread(() =>
            {
                try
                {
                    var executor = _activity.MainExecutor;
                    var prompt = new BiometricPrompt.Builder(_activity)
                        .SetTitle(string.IsNullOrEmpty(title) ? "Sillet" : title)
                        .SetSubtitle("Sillet Wallet")
                        .SetNegativeButton(Loc.T("Cancel"), executor, new CancelListener(done))
                        .Build();
                    prompt.Authenticate(new BiometricPrompt.CryptoObject(cipher), new CancellationSignal(), executor, new Callback(done, work));
                }
                catch (Java.Lang.Exception e)
                {
                    done.TrySetException(new PasskeyException(e.Message ?? Loc.T("Biometric check failed"), false));
                }
            });
            return done.Task;
        }

        private sealed class CancelListener : Java.Lang.Object, IDialogInterfaceOnClickListener
        {
            private readonly TaskCompletionSource<byte[]> _done;

            public CancelListener(TaskCompletionSource<byte[]> done)
            {
                _done = done;
            }

            public void OnClick(IDialogInterface dialog, int which)
            {
                _done.TrySetException(new PasskeyException(Loc.T("Canceled"), true));
            }
        }

        private sealed class Callback : BiometricPrompt.AuthenticationCallback
        {
            private readonly TaskCompletionSource<byte[]> _done;
            private readonly Func<Cipher, byte[]> _work;

            public Callback(TaskCompletionSource<byte[]> done, Func<Cipher, byte[]> work)
            {
                _done = done;
                _work = work;
            }

            public override void OnAuthenticationSucceeded(BiometricPrompt.AuthenticationResult result)
            {
                try
                {
                    var cipher = result.CryptoObject == null ? null : result.CryptoObject.Cipher;
                    if (cipher == null) throw new PasskeyException(Loc.T("Biometric check failed"), false);
                    _done.TrySetResult(_work(cipher));
                }
                catch (PasskeyException e)
                {
                    _done.TrySetException(e);
                }
                catch (Java.Lang.Exception)
                {
                    _done.TrySetException(new PasskeyException(Loc.T("The saved Windows Hello data can't be read. Unlock with your password and set it up again"), false, true));
                }
            }

            public override void OnAuthenticationError(BiometricErrorCode errorCode, ICharSequence errString)
            {
                var code = (int)errorCode;
                var cancelled = code == 5 || code == 10 || code == 13;
                var text = errString == null ? Loc.T("Biometric check failed") : errString.ToString();
                _done.TrySetException(new PasskeyException(cancelled ? Loc.T("Canceled") : text, cancelled));
            }
        }
    }
}
