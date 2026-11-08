// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Sillet.App.ViewModels;

namespace Sillet.Mobile.UI.Views
{
    public partial class WalletConnectView : UserControl
    {
        public WalletConnectView()
        {
            InitializeComponent();
            DataContextChanged += (s, e) => Hook();
        }

        // The phone reads the clipboard and opens the camera, so the screen gives those to the view model
        private void Hook()
        {
            var vm = DataContext as WalletConnectViewModel;
            if (vm == null) return;
            vm.ReadClipboardAsync = async () =>
            {
                var top = TopLevel.GetTopLevel(this);
                return top == null || top.Clipboard == null ? null : await top.Clipboard.GetTextAsync();
            };
            var scan = MobilePlatform.ScanQr;
            if (scan != null) vm.ScanCamera = () => scan(false);
        }

        private async void Scan_Click(object sender, RoutedEventArgs e)
        {
            var vm = DataContext as WalletConnectViewModel;
            if (vm == null) return;
            if (vm.ScanCamera == null) { Hook(); }
            if (vm.ScanCamera == null) return;
            await vm.ScanCameraAsync();
        }
    }
}
