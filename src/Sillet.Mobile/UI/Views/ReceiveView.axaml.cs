using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using QRCoder;
using Sillet.App.Services;
using Sillet.App.ViewModels;

namespace Sillet.Mobile.UI.Views
{
    public partial class ReceiveView : UserControl
    {
        public ReceiveView()
        {
            InitializeComponent();
        }

        private ReceiveViewModel Vm => DataContext as ReceiveViewModel;

        private void ShareAddress_Click(object sender, RoutedEventArgs e)
        {
            var vm = Vm;
            if (vm == null || string.IsNullOrEmpty(vm.Address)) return;
            var text = vm.Address;
            if (MobilePlatform.ShareText == null)
            {
                vm.CopyCommand.Execute(null);
                return;
            }
            MobilePlatform.ShareText(Loc.T("Share address"), text);
        }

        private void ShareQr_Click(object sender, RoutedEventArgs e)
        {
            var vm = Vm;
            if (vm == null || string.IsNullOrEmpty(vm.Address)) return;
            if (MobilePlatform.ShareImage == null)
            {
                vm.CopyCommand.Execute(null);
                return;
            }
            byte[] png;
            try
            {
                using (var generator = new QRCodeGenerator())
                using (var data = generator.CreateQrCode(vm.Address, QRCodeGenerator.ECCLevel.M))
                {
                    png = new PngByteQRCode(data).GetGraphic(16);
                }
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
            {
                return;
            }
            var symbol = vm.SelectedNetwork == null ? "address" : vm.SelectedNetwork.Symbol.ToLowerInvariant();
            MobilePlatform.ShareImage(Loc.T("Share QR"), "sillet-" + symbol + "-qr.png", png);
        }
    }
}
