using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Sillet.App.ViewModels;

namespace Sillet.App.Views
{
    public partial class SendView : UserControl
    {
        private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff" };

        public SendView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            var old = e.OldValue as SendViewModel;
            if (old != null) old.PropertyChanged -= OnVmPropertyChanged;
            var vm = e.NewValue as SendViewModel;
            if (vm != null) vm.PropertyChanged += OnVmPropertyChanged;
            ConfirmBox.Clear();
        }

        private void OnVmPropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SendViewModel.ConfirmPasswordVersion) || e.PropertyName == nameof(SendViewModel.IsReviewStep))
            {
                if (ConfirmBox.Password.Length > 0) ConfirmBox.Clear();
            }
        }

        private void ConfirmBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            var vm = Vm;
            if (vm != null) vm.ConfirmPassword = ConfirmBox.Password;
        }

        private SendViewModel Vm => DataContext as SendViewModel;

        private static string DroppedImage(DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return null;
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files == null) return null;
            return files.FirstOrDefault(f => ImageExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()));
        }

        private void SetHighlight(bool on)
        {
            if (on) DropZone.SetResourceReference(Border.BorderBrushProperty, "BorderStrongBrush");
            else DropZone.SetResourceReference(Border.BorderBrushProperty, "BorderSubtleBrush");
            DropHint.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        }

        private void DropZone_DragEnter(object sender, DragEventArgs e)
        {
            var ok = DroppedImage(e) != null && Vm != null && Vm.IsFormStep;
            e.Effects = ok ? DragDropEffects.Copy : DragDropEffects.None;
            SetHighlight(ok);
            e.Handled = true;
        }

        private void DropZone_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = DroppedImage(e) != null && Vm != null && Vm.IsFormStep ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void DropZone_DragLeave(object sender, DragEventArgs e)
        {
            SetHighlight(false);
        }

        private void DropZone_Drop(object sender, DragEventArgs e)
        {
            SetHighlight(false);
            var vm = Vm;
            var file = DroppedImage(e);
            if (vm == null || file == null || !vm.IsFormStep) return;
            vm.ApplyScan(QrReader.FromFile(file), false);
            e.Handled = true;
        }
    }
}
