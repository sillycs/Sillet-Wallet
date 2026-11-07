// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
using System.Windows;
using System.Windows.Controls;

namespace Sillet.App.Controls
{
    public static class Field
    {
        public static readonly DependencyProperty HolderProperty = DependencyProperty.RegisterAttached(
            "Holder", typeof(string), typeof(Field), new PropertyMetadata(null, OnHolderChanged));

        private static readonly DependencyPropertyKey HasPasswordKey = DependencyProperty.RegisterAttachedReadOnly(
            "HasPassword", typeof(bool), typeof(Field), new PropertyMetadata(false));

        public static readonly DependencyProperty HasPasswordProperty = HasPasswordKey.DependencyProperty;

        public static string GetHolder(DependencyObject d) { return (string)d.GetValue(HolderProperty); }
        public static void SetHolder(DependencyObject d, string value) { d.SetValue(HolderProperty, value); }
        public static bool GetHasPassword(DependencyObject d) { return (bool)d.GetValue(HasPasswordProperty); }

        private static void OnHolderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var box = d as PasswordBox;
            if (box == null) return;
            box.PasswordChanged -= OnPasswordChanged;
            box.PasswordChanged += OnPasswordChanged;
            Update(box);
        }

        private static void OnPasswordChanged(object sender, RoutedEventArgs e)
        {
            Update((PasswordBox)sender);
        }

        private static void Update(PasswordBox box)
        {
            box.SetValue(HasPasswordKey, box.Password.Length > 0);
        }
    }
}
