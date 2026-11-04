using System;
using System.Collections.Generic;
using System.ComponentModel;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Sillet.App.ViewModels;
using Sillet.Mobile.UI;

namespace Sillet.Mobile.UI.Views
{
    public partial class SendView : UserControl
    {
        private SendViewModel _subscribed;

        public SendView()
        {
            InitializeComponent();
        }

        private SendViewModel Vm => DataContext as SendViewModel;

        protected override void OnDataContextChanged(System.EventArgs e)
        {
            base.OnDataContextChanged(e);
            if (_subscribed != null) _subscribed.PropertyChanged -= OnVmChanged;
            _subscribed = Vm;
            if (_subscribed != null) _subscribed.PropertyChanged += OnVmChanged;
            if (_subscribed != null && _subscribed.IsDoneStep) PlayDone(true);
        }

        private void PlayDone(bool on)
        {
            var panel = this.FindControl<Border>("DonePanel");
            if (panel == null) return;
            var targets = new List<Control>();
            foreach (var node in panel.GetLogicalDescendants())
            {
                var control = node as Control;
                if (control != null && (control.Classes.Contains("rise") || control.Classes.Contains("pop") || control.Classes.Contains("ringpulse"))) targets.Add(control);
            }
            if (!on)
            {
                foreach (var control in targets) control.Classes.Set("in", false);
                return;
            }
            foreach (var control in targets) control.Classes.Set("in", false);
            _playToken++;
            var token = _playToken;
            if (!_watching)
            {
                _watching = true;
                panel.PropertyChanged += (s, a) =>
                {
                    if (a.Property.Name == "IsEffectivelyVisible" && panel.IsEffectivelyVisible && Vm != null && Vm.IsDoneStep) PlayDone(true);
                };
            }
            if (!panel.IsEffectivelyVisible) return;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            timer.Tick += (s, a) =>
            {
                timer.Stop();
                if (token != _playToken) return;
                foreach (var control in targets) control.Classes.Set("in", true);
            };
            timer.Start();
        }

        private int _playToken;
        private bool _watching;

        private void OnVmChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SendViewModel.IsDoneStep) && Vm != null) PlayDone(Vm.IsDoneStep);
            if (e.PropertyName == nameof(SendViewModel.ConfirmPasswordVersion) || e.PropertyName == nameof(SendViewModel.IsReviewStep))
            {
                var box = this.FindControl<TextBox>("ConfirmBox");
                if (box != null && !string.IsNullOrEmpty(box.Text)) box.Text = "";
            }
        }

        private void ConfirmBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var vm = Vm;
            var box = sender as TextBox;
            if (vm != null && box != null) vm.ConfirmPassword = box.Text ?? "";
        }

        private async void Scan_Click(object sender, RoutedEventArgs e)
        {
            var vm = Vm;
            if (vm != null) await QrFlow.Run(null, vm, false);
        }

        private async void ScanImage_Click(object sender, RoutedEventArgs e)
        {
            var vm = Vm;
            if (vm != null) await QrFlow.Run(null, vm, true);
        }

        private async void Paste_Click(object sender, RoutedEventArgs e)
        {
            var vm = Vm;
            var top = TopLevel.GetTopLevel(this);
            if (vm == null || top == null || top.Clipboard == null) return;
            var text = await top.Clipboard.GetTextAsync();
            if (!string.IsNullOrWhiteSpace(text)) vm.ToAddress = text.Trim();
        }
    }
}
