using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Sillet.App.ViewModels;

namespace Sillet.App.Views
{
    public partial class MarketView : UserControl
    {
        private MarketViewModel _subscribed;

        public MarketView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (_subscribed != null) _subscribed.FocusSearchRequested -= FocusSearch;
            _subscribed = DataContext as MarketViewModel;
            if (_subscribed != null) _subscribed.FocusSearchRequested += FocusSearch;
            if (_subscribed != null && _subscribed.ConsumeSearchFocus()) FocusSearch();
        }

        private void FocusSearch()
        {
            if (_subscribed != null) _subscribed.ConsumeSearchFocus();
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                SearchBox.Focus();
                SearchBox.SelectAll();
            }));
        }
    }
}
