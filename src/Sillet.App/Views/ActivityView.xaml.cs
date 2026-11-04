using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Sillet.App.ViewModels;

namespace Sillet.App.Views
{
    public partial class ActivityView : UserControl
    {
        public ActivityView()
        {
            InitializeComponent();
        }

        private void Row_Click(object sender, MouseButtonEventArgs e)
        {
            var vm = DataContext as ActivityViewModel;
            var item = (sender as FrameworkElement) == null ? null : ((FrameworkElement)sender).DataContext as ActivityItem;
            if (vm != null && item != null) vm.OpenReceiptCommand.Execute(item);
        }

        private void Scrim_Click(object sender, MouseButtonEventArgs e)
        {
            var vm = DataContext as ActivityViewModel;
            if (vm != null) vm.IsReceiptOpen = false;
        }

        private void Card_Click(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
        }
    }
}
