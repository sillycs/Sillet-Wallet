using System;
using System.Threading;
using System.Windows;
using Sillet.App.Services;

namespace Sillet.App.Views
{
    /// <summary>Shown while Sillet downloads a new version of itself. It asks for admin permission, then Sillet closes and the script swaps the exe.</summary>
    public partial class UpdateWindow : Window
    {
        private readonly CancellationTokenSource _cancel = new CancellationTokenSource();
        private bool _finished;

        /// <summary>False when the user declined admin permission, so the same version is not offered again until Sillet restarts.</summary>
        public bool MayRetry { get; private set; } = true;

        public UpdateWindow()
        {
            InitializeComponent();
            Closing += (s, e) =>
            {
                if (!_finished) _cancel.Cancel();
            };
        }

        /// <summary>Downloads, verifies and starts the replacement. Returns true when the swap script runs and Sillet must exit.</summary>
        public async System.Threading.Tasks.Task<bool> RunAsync(UpdateInfo update)
        {
            TitleText.Text = Loc.T("Updating Sillet");
            StatusText.Text = Loc.F("Downloading Sillet {0}", update.Version);
            var file = await UpdateService.DownloadAsync(update, p => Dispatcher.BeginInvoke(new Action(() => Bar.Value = p)), _cancel.Token);
            if (_cancel.IsCancellationRequested)
            {
                UpdateService.DiscardDownload(file);
                return Fail(null);
            }
            if (file == null) return Fail(Loc.T("The update could not be downloaded or was not verified. Sillet will try again later"));
            StatusText.Text = Loc.T("Windows will ask for administrator permission to finish");
            Bar.IsIndeterminate = true;
            if (!UpdateService.StartReplace(file))
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
            CloseButton.Visibility = Visibility.Visible;
            return false;
        }

        private void OnClose(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
