// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;
using Avalonia.LogicalTree;
using Sillet.App.Services;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Sillet.App.ViewModels;

namespace Sillet.Mobile.UI.Views
{
    public partial class SettingsView : UserControl
    {
        private SettingsViewModel _subscribed;

        public SettingsView()
        {
            InitializeComponent();
            AttachedToVisualTree += (s, e) => BuildFilter();
        }

        private sealed class Entry
        {
            public Panel Wrap;
            public Control Inner;
        }

        private sealed class Group
        {
            public string Title;
            public Panel Header;
            public readonly List<Entry> Items = new List<Entry>();
        }

        private readonly List<Group> _groups = new List<Group>();
        private bool _built;

        private static string HeaderTitle(Control element)
        {
            var panel = element as StackPanel;
            if (panel == null || panel.Children.Count == 0) return null;
            if (panel.Orientation == Avalonia.Layout.Orientation.Horizontal)
            {
                if (panel.Children.Count != 2) return null;
                var label = panel.Children[1] as TextBlock;
                return label != null && label.Classes.Contains("section") ? label.Text : null;
            }
            return HeaderTitle(panel.Children[0]);
        }

        private void BuildFilter()
        {
            if (_built) return;
            var scroll = Content as ScrollViewer;
            var root = scroll == null ? null : scroll.Content as StackPanel;
            if (root == null) return;
            _built = true;
            Group current = null;
            foreach (var child in new List<Control>(root.Children))
            {
                if (child.Tag as string == "fixed") continue;
                var title = HeaderTitle(child);
                var index = root.Children.IndexOf(child);
                var wrap = new Panel();
                root.Children.RemoveAt(index);
                wrap.Children.Add(child);
                root.Children.Insert(index, wrap);
                if (title != null)
                {
                    var separate = ((StackPanel)child).Orientation == Avalonia.Layout.Orientation.Horizontal;
                    if (current == null || current.Title != title || !separate)
                    {
                        current = new Group { Title = title };
                        _groups.Add(current);
                    }
                    if (separate) { current.Header = wrap; continue; }
                }
                if (current != null) current.Items.Add(new Entry { Wrap = wrap, Inner = child });
            }
        }

        private void Search_Changed(object sender, TextChangedEventArgs e)
        {
            ApplyFilter();
        }

        private static void Collect(ILogical node, StringBuilder text)
        {
            var block = node as TextBlock;
            if (block != null && !string.IsNullOrEmpty(block.Text)) text.Append(block.Text).Append(' ');
            var content = node as ContentControl;
            if (content != null && content.Content is string) text.Append((string)content.Content).Append(' ');
            var control = node as Control;
            if (control != null && control.Tag is string) text.Append((string)control.Tag).Append(' ');
            foreach (var child in node.LogicalChildren) Collect(child, text);
        }

        private void ApplyFilter()
        {
            if (!_built) return;
            var box = this.FindControl<TextBox>("SearchBox");
            var query = ((box == null ? "" : box.Text) ?? "").Trim();
            var anyVisible = false;
            foreach (var group in _groups)
            {
                var inCategory = true;
                var titleHit = query.Length > 0 && group.Title.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0;
                var visible = false;
                foreach (var item in group.Items)
                {
                    var show = inCategory;
                    if (show && query.Length > 0 && !titleHit)
                    {
                        var text = new StringBuilder();
                        Collect(item.Inner, text);
                        show = text.ToString().IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0;
                    }
                    item.Wrap.IsVisible = show;
                    if (show && item.Inner.IsVisible) visible = true;
                }
                if (group.Header != null) group.Header.IsVisible = visible;
                if (visible) anyVisible = true;
            }
            var none = this.FindControl<TextBlock>("NoResults");
            if (none != null) none.IsVisible = !anyVisible;
        }

        private SettingsViewModel Vm => DataContext as SettingsViewModel;

        protected override void OnDataContextChanged(System.EventArgs e)
        {
            base.OnDataContextChanged(e);
            if (_subscribed != null) _subscribed.PropertyChanged -= OnVmChanged;
            _subscribed = Vm;
            if (_subscribed != null) _subscribed.PropertyChanged += OnVmChanged;
        }

        private void OnVmChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SettingsViewModel.PasskeyFieldsVersion))
            {
                var box = this.FindControl<TextBox>("PasskeyPasswordBox");
                if (box != null) box.Text = "";
                return;
            }
            if (e.PropertyName != nameof(SettingsViewModel.PasswordFieldsVersion)) return;
            var current = this.FindControl<TextBox>("CurrentPasswordBox");
            var next = this.FindControl<TextBox>("NewPasswordBox");
            if (current != null) current.Text = "";
            if (next != null) next.Text = "";
        }

        private void CurrentPassword_Changed(object sender, TextChangedEventArgs e)
        {
            var vm = Vm;
            var box = sender as TextBox;
            if (vm != null && box != null) vm.CurrentPassword = box.Text ?? "";
        }

        private void NewPassword_Changed(object sender, TextChangedEventArgs e)
        {
            var vm = Vm;
            var box = sender as TextBox;
            if (vm != null && box != null) vm.NewPassword = box.Text ?? "";
        }

        private void PasskeyPassword_Changed(object sender, TextChangedEventArgs e)
        {
            var vm = Vm;
            var box = sender as TextBox;
            if (vm != null && box != null) vm.PasskeyPassword = box.Text ?? "";
        }

        private void Donate_Click(object sender, RoutedEventArgs e)
        {
            var main = this.FindAncestorOfType<MainView>();
            var vm = main == null ? null : main.DataContext as MainViewModel;
            if (vm != null) vm.DonateCommand.Execute(null);
        }
    }
}
