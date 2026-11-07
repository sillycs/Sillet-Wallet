// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows;
using System.Windows.Controls;
using Sillet.App.Services;
using Sillet.App.ViewModels;

namespace Sillet.App.Views
{
    public partial class SettingsView : UserControl
    {
        private SettingsViewModel _subscribed;

        public SettingsView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
            Loaded += OnLoaded;
        }

        private sealed class Group
        {
            public string Title;
            public UIElement Header;
            public readonly List<UIElement> Items = new List<UIElement>();
        }

        private readonly List<Group> _groups = new List<Group>();
        private readonly HashSet<UIElement> _hidden = new HashSet<UIElement>();
        private readonly Dictionary<UIElement, BindingBase> _bindings = new Dictionary<UIElement, BindingBase>();
        private bool _built;

        private static string HeaderTitle(UIElement e)
        {
            var panel = e as StackPanel;
            if (panel == null || panel.Children.Count == 0) return null;
            if (panel.Orientation == Orientation.Horizontal)
            {
                if (panel.Margin.Top < 20 || panel.Children.Count != 2) return null;
                var label = panel.Children[1] as TextBlock;
                return panel.Children[0] is TextBlock && label != null ? label.Text : null;
            }
            return HeaderTitle(panel.Children[0]);
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (_built) return;
            var scroll = Content as ScrollViewer;
            var root = scroll == null ? null : scroll.Content as StackPanel;
            if (root == null) return;
            _built = true;
            Group current = null;
            foreach (UIElement child in root.Children)
            {
                if (child is FrameworkElement && ((FrameworkElement)child).Uid == "fixed") continue;
                var title = HeaderTitle(child);
                if (title != null)
                {
                    var sp = (StackPanel)child;
                    var separate = sp.Orientation == Orientation.Horizontal;
                    if (current == null || current.Title != title || !separate)
                    {
                        current = new Group { Title = title };
                        _groups.Add(current);
                    }
                    if (separate) current.Header = child; else current.Items.Add(child);
                    continue;
                }
                if (current == null) continue;
                current.Items.Add(child);
            }
        }

        private void Search_Changed(object sender, TextChangedEventArgs e)
        {
            ApplyFilter();
        }

        private void Search_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape) return;
            SearchBox.Clear();
            e.Handled = true;
        }

        private void SetShown(UIElement element, bool show)
        {
            if (show)
            {
                if (!_hidden.Remove(element)) return;
                BindingBase binding;
                if (_bindings.TryGetValue(element, out binding) && binding != null) BindingOperations.SetBinding(element, VisibilityProperty, binding);
                else element.Visibility = Visibility.Visible;
                return;
            }
            if (!_hidden.Add(element)) return;
            if (!_bindings.ContainsKey(element)) _bindings[element] = BindingOperations.GetBindingBase(element, VisibilityProperty);
            element.Visibility = Visibility.Collapsed;
        }

        private static void Collect(DependencyObject node, StringBuilder text)
        {
            var block = node as TextBlock;
            if (block != null && !string.IsNullOrEmpty(block.Text)) text.Append(block.Text).Append(' ');
            var content = node as ContentControl;
            if (content != null && content.Content is string) text.Append((string)content.Content).Append(' ');
            var element = node as FrameworkElement;
            if (element != null)
            {
                if (element.Tag is string) text.Append((string)element.Tag).Append(' ');
                if (element.ToolTip is string) text.Append((string)element.ToolTip).Append(' ');
            }
            foreach (var child in LogicalTreeHelper.GetChildren(node))
            {
                var dep = child as DependencyObject;
                if (dep != null) Collect(dep, text);
            }
        }

        private void ApplyFilter()
        {
            if (!_built) return;
            var query = (SearchBox.Text ?? "").Trim();
            foreach (var group in _groups)
            {
                var inCategory = true;
                var titleHit = query.Length > 0 && group.Title.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0;
                foreach (var item in group.Items)
                {
                    var show = inCategory;
                    if (show && query.Length > 0 && !titleHit)
                    {
                        var text = new StringBuilder();
                        Collect(item, text);
                        show = text.ToString().IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0;
                    }
                    SetShown(item, show);
                }
            }
            var anyVisible = false;
            foreach (var group in _groups)
            {
                var visible = false;
                foreach (var item in group.Items) if (item.Visibility == Visibility.Visible && !_hidden.Contains(item)) visible = true;
                if (group.Header != null) SetShown(group.Header, visible);
                if (visible) anyVisible = true;
            }
            NoResults.Visibility = anyVisible ? Visibility.Collapsed : Visibility.Visible;
        }

        private SettingsViewModel Vm => DataContext as SettingsViewModel;

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (_subscribed != null) _subscribed.PropertyChanged -= OnVmPropertyChanged;
            _subscribed = Vm;
            if (_subscribed != null) _subscribed.PropertyChanged += OnVmPropertyChanged;
        }

        private void OnVmPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SettingsViewModel.PasskeyFieldsVersion)) PasskeyBox.Clear();
            if (e.PropertyName != nameof(SettingsViewModel.PasswordFieldsVersion)) return;
            CurrentPasswordBox.Clear();
            NewPasswordBox.Clear();
        }

        private void RevealBox_Changed(object sender, RoutedEventArgs e)
        {
            var vm = Vm;
            if (vm != null) vm.RevealPassword = RevealBox.Password;
        }

        private void PasskeyBox_Changed(object sender, RoutedEventArgs e)
        {
            var vm = Vm;
            if (vm != null) vm.PasskeyPassword = PasskeyBox.Password;
        }

        private void DeleteBox_Changed(object sender, RoutedEventArgs e)
        {
            var vm = Vm;
            if (vm != null) vm.DeletePassword = DeleteBox.Password;
        }

        private void CurrentPassword_Changed(object sender, RoutedEventArgs e)
        {
            var vm = Vm;
            if (vm != null) vm.CurrentPassword = CurrentPasswordBox.Password;
        }

        private void NewPassword_Changed(object sender, RoutedEventArgs e)
        {
            var vm = Vm;
            if (vm != null) vm.NewPassword = NewPasswordBox.Password;
        }
    }
}
