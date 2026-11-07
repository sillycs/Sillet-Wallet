// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Sillet.App.ViewModels;

namespace Sillet.App.Views
{
    public partial class OnboardingView : UserControl
    {
        public OnboardingView()
        {
            InitializeComponent();
        }

        private OnboardingViewModel Vm => DataContext as OnboardingViewModel;

        private void Password1_Changed(object sender, RoutedEventArgs e)
        {
            var vm = Vm;
            if (vm != null) vm.Password = PasswordBox1.Password;
        }

        private void Passphrase1_Changed(object sender, RoutedEventArgs e)
        {
            var vm = Vm;
            if (vm != null) vm.Passphrase = PassphraseBox1.Password;
        }

        private void Passphrase2_Changed(object sender, RoutedEventArgs e)
        {
            var vm = Vm;
            if (vm != null) vm.PassphraseRepeat = PassphraseBox2.Password;
        }

        private void Password2_Changed(object sender, RoutedEventArgs e)
        {
            var vm = Vm;
            if (vm != null) vm.PasswordRepeat = PasswordBox2.Password;
        }

        private void Password2_KeyDown(object sender, KeyEventArgs e)
        {
            var vm = Vm;
            if (e.Key == Key.Enter && vm != null && vm.FinishCommand.CanExecute(null)) vm.FinishCommand.Execute(null);
        }
    }
}
