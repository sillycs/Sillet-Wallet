// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Sillet.App.ViewModels;

namespace Sillet.Mobile.UI.Views
{
    public partial class UnlockView : UserControl
    {
        public UnlockView()
        {
            InitializeComponent();
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            DispatcherTimer.RunOnce(TryHello, TimeSpan.FromMilliseconds(450));
        }

        private void TryHello()
        {
            var vm = DataContext as UnlockViewModel;
            if (vm == null || !vm.HasPasskey || vm.AutoHelloTried) return;
            vm.AutoHelloTried = true;
            if (vm.HelloCommand.CanExecute(null)) vm.HelloCommand.Execute(null);
        }
    }
}
