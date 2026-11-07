// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Sillet.App.ViewModels;

namespace Sillet.App.Views
{
    public partial class UnlockView : UserControl
    {
        public UnlockView()
        {
            InitializeComponent();
        }

        private UnlockViewModel Vm => DataContext as UnlockViewModel;

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            PasswordInput.Focus();
            var vm = Vm;
            var window = Window.GetWindow(this);
            if (vm == null || !vm.HasPasskey || vm.AutoHelloTried || window == null || !window.IsActive) return;
            vm.AutoHelloTried = true;
            Dispatcher.BeginInvoke(new System.Action(() =>
            {
                if (vm.HelloCommand.CanExecute(null)) vm.HelloCommand.Execute(null);
            }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }

        private void Password_Changed(object sender, RoutedEventArgs e)
        {
            var vm = Vm;
            if (vm != null) vm.Password = PasswordInput.Password;
        }

        private void Password_KeyDown(object sender, KeyEventArgs e)
        {
            var vm = Vm;
            if (e.Key == Key.Enter && vm != null && vm.UnlockCommand.CanExecute(null)) vm.UnlockCommand.Execute(null);
        }
    }
}
