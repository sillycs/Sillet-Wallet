using Avalonia.Controls;
using Avalonia.Interactivity;
using Sillet.App.Services;
using Sillet.App.ViewModels;

namespace Sillet.Mobile.UI.Views
{
    public partial class DonateView : UserControl
    {
        public DonateView()
        {
            InitializeComponent();
        }

        private void Share_Click(object sender, RoutedEventArgs e)
        {
            var vm = DataContext as DonateViewModel;
            if (vm == null || string.IsNullOrEmpty(vm.Address)) return;
            if (MobilePlatform.ShareText == null)
            {
                vm.CopyCommand.Execute(null);
                return;
            }
            MobilePlatform.ShareText(Loc.T("Share address"), vm.Address);
        }
    }
}
