// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
using Avalonia.Controls;
using Avalonia.Interactivity;
using Sillet.App.ViewModels;

namespace Sillet.Mobile.UI.Views
{
    public partial class MarketView : UserControl
    {
        public MarketView()
        {
            InitializeComponent();
        }

        private void Coin_Click(object sender, RoutedEventArgs e)
        {
            var vm = DataContext as MarketViewModel;
            var coin = (sender as Control)?.DataContext as CoinItem;
            if (vm == null || coin == null) return;
            vm.SelectedCoin = coin;
            ShowDetail(true);
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            ShowDetail(false);
        }

        private void ShowDetail(bool detail)
        {
            var list = this.FindControl<Grid>("ListPanel");
            var panel = this.FindControl<ScrollViewer>("DetailPanel");
            if (list != null) list.IsVisible = !detail;
            if (panel != null)
            {
                panel.IsVisible = detail;
                if (detail) panel.Offset = new Avalonia.Vector(0, 0);
            }
        }
    }
}
