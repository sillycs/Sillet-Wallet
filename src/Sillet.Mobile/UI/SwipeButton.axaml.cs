// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Raton team (raton.fun)
using System;
using System.Windows.Input;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace Sillet.Mobile.UI
{
    public partial class SwipeButton : UserControl
    {
        public static readonly StyledProperty<ICommand> CommandProperty =
            AvaloniaProperty.Register<SwipeButton, ICommand>(nameof(Command));

        public static readonly StyledProperty<string> TextProperty =
            AvaloniaProperty.Register<SwipeButton, string>(nameof(Text), "Swipe to send");

        public static readonly StyledProperty<string> DoneTextProperty =
            AvaloniaProperty.Register<SwipeButton, string>(nameof(DoneText), "Sending...");

        private readonly TranslateTransform _move = new TranslateTransform();
        private bool _dragging;
        private double _grab;
        private double _offset;

        public SwipeButton()
        {
            InitializeComponent();
            Thumb.RenderTransform = _move;
            Hint.Text = Text;
            Thumb.PointerPressed += OnPressed;
            Thumb.PointerMoved += OnMoved;
            Thumb.PointerReleased += OnReleased;
            Thumb.PointerCaptureLost += (s, e) => Finish();
        }

        public ICommand Command
        {
            get { return GetValue(CommandProperty); }
            set { SetValue(CommandProperty, value); }
        }

        public string Text
        {
            get { return GetValue(TextProperty); }
            set { SetValue(TextProperty, value); }
        }

        public string DoneText
        {
            get { return GetValue(DoneTextProperty); }
            set { SetValue(DoneTextProperty, value); }
        }

        private double Max => Math.Max(0, Track.Bounds.Width - Thumb.Bounds.Width - 10);

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == TextProperty && !_dragging && Hint != null) Hint.Text = Text;
        }

        private bool CanRun()
        {
            var command = Command;
            return IsEffectivelyEnabled && command != null && command.CanExecute(null);
        }

        private void OnPressed(object sender, PointerPressedEventArgs e)
        {
            if (!CanRun()) return;
            _dragging = true;
            _move.Transitions = null;
            Fill.Transitions = null;
            _grab = e.GetPosition(Track).X - _offset;
            e.Pointer.Capture(Thumb);
            e.Handled = true;
        }

        private void OnMoved(object sender, PointerEventArgs e)
        {
            if (!_dragging) return;
            SetOffset(Math.Max(0, Math.Min(Max, (e.GetPosition(Track).X - _grab) * 1.3)));
            e.Handled = true;
        }

        private void OnReleased(object sender, PointerReleasedEventArgs e)
        {
            e.Pointer.Capture(null);
            Finish();
            e.Handled = true;
        }

        private void Finish()
        {
            if (!_dragging) return;
            _dragging = false;
            var max = Max;
            if (max > 0 && _offset >= max * 0.6 && CanRun())
            {
                SetOffset(max);
                Hint.Text = DoneText;
                Hint.Foreground = new SolidColorBrush(Color.Parse("#0B3D22"));
                Hint.Opacity = 1;
                Command.Execute(null);
                DispatcherTimer.RunOnce(Reset, TimeSpan.FromMilliseconds(1200));
            }
            else
            {
                Reset();
            }
        }

        private void Reset()
        {
            var duration = TimeSpan.FromMilliseconds(260);
            _move.Transitions = new Transitions
            {
                new DoubleTransition { Property = TranslateTransform.XProperty, Duration = duration, Easing = new CubicEaseOut() },
            };
            Fill.Transitions = new Transitions
            {
                new DoubleTransition { Property = Layoutable.WidthProperty, Duration = duration, Easing = new CubicEaseOut() },
            };
            SetOffset(0);
            Hint.Text = Text;
            Hint.ClearValue(TextBlock.ForegroundProperty);
        }

        private void SetOffset(double x)
        {
            _offset = x;
            _move.X = x;
            Fill.Width = x + Thumb.Bounds.Width + 8;
            var max = Max;
            Hint.Opacity = max <= 0 ? 1 : 1 - Math.Min(1, x / max * 1.4);
        }
    }
}
