using System;
using System.Threading;
using System.Windows;
using Sillet.App.Services;

namespace Sillet.App.Views
{
    public partial class UpdateWindow : Window
    {
        private readonly CancellationTokenSource _cancel = new CancellationTokenSource();
        private bool _finished;

        public bool MayRetry { get; private set; } = true;

        public UpdateWindow()
        {
            InitializeComponent();
            Closing += (s, e) =>
            {
                if (!_finished) _cancel.Cancel();
            };
        }

        private System.Threading.Tasks.TaskCompletionSource<bool> _answer;

        private void OnInstall(object sender, RoutedEventArgs e)
        {
            if (_answer != null) _answer.TrySetResult(true);
        }

        private void OnLater(object sender, RoutedEventArgs e)
        {
            if (_answer != null) _answer.TrySetResult(false);
        }

        public async System.Threading.Tasks.Task<bool> RunAsync(UpdateInfo update)
        {
            TitleText.Text = Loc.F("Sillet {0} is available", update.Version);
            var signed = Sillet.App.Services.UpdateKey.IsConfigured;
            StatusText.Text = UpdateService.NeedsAdmin
                ? Loc.T(signed ? "Verified with the Sillet release key. Windows will ask for administrator permission because Sillet is installed in a protected folder" : "Windows will ask for administrator permission because Sillet is installed in a protected folder")
                : Loc.T(signed ? "Verified with the Sillet release key. Nothing is installed until you accept" : "Nothing is installed until you accept");
            if (!string.IsNullOrWhiteSpace(update.Notes))
            {
                NotesText.Text = update.Notes;
                NotesScroll.Visibility = Visibility.Visible;
            }
            InstallButton.Content = Loc.T("Install update");
            LaterButton.Content = Loc.T("Not now");
            InstallButton.Visibility = Visibility.Visible;
            LaterButton.Visibility = Visibility.Visible;
            _answer = new System.Threading.Tasks.TaskCompletionSource<bool>();
            Closing += (s, e) => { if (_answer != null) _answer.TrySetResult(false); };
            if (!await _answer.Task)
            {
                _finished = true;
                MayRetry = false;
                Close();
                return false;
            }
            InstallButton.Visibility = Visibility.Collapsed;
            LaterButton.Visibility = Visibility.Collapsed;
            Bar.Visibility = Visibility.Visible;
            TitleText.Text = Loc.T("Updating Sillet");
            StatusText.Text = Loc.F("Downloading Sillet {0}", update.Version);
            var file = await UpdateService.DownloadAsync(update, p => Dispatcher.BeginInvoke(new Action(() => Bar.Value = p)), _cancel.Token);
            if (_cancel.IsCancellationRequested)
            {
                UpdateService.DiscardDownload(file);
                return Fail(null);
            }
            if (file == null) return Fail(Loc.T("The update could not be downloaded or was not verified. Sillet will try again later"));
            StatusText.Text = UpdateService.NeedsAdmin ? Loc.T("Windows will ask for administrator permission to finish") : Loc.T("Finishing the update");
            Bar.IsIndeterminate = true;
            if (!UpdateService.StartReplace(update, file))
            {
                UpdateService.DiscardDownload(file);
                MayRetry = false;
                return Fail(Loc.T("Administrator permission was declined, so Sillet was not updated"));
            }
            _finished = true;
            return true;
        }

        private bool Fail(string message)
        {
            _finished = true;
            if (message == null)
            {
                Close();
                return false;
            }
            TitleText.Text = Loc.T("Update cancelled");
            StatusText.Text = message;
            Bar.IsIndeterminate = false;
            Bar.Value = 0;
            CloseButton.Content = Loc.T("Close");
            Bar.Visibility = Visibility.Collapsed;
            CloseButton.Visibility = Visibility.Visible;
            return false;
        }

        private void OnClose(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
