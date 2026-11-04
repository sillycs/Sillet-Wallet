using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Sillet.App.ViewModels;

namespace Sillet.Mobile.UI.Views
{
    public partial class HomeView : UserControl
    {
        private Point _newsStart;

        public HomeView()
        {
            InitializeComponent();
            var card = this.FindControl<Border>("NewsCard");
            if (card == null) return;
            // handledEventsToo: the text and the buttons inside the card handle their own touches first
            card.AddHandler(PointerPressedEvent, News_Pressed, RoutingStrategies.Bubble, true);
            card.AddHandler(PointerReleasedEvent, News_Released, RoutingStrategies.Bubble, true);
        }

        /// <summary>Touching the news stops them from rotating, so a long post can be read and scrolled in peace.</summary>
        private void News_Pressed(object sender, PointerPressedEventArgs e)
        {
            _newsStart = e.GetPosition(this);
            var home = DataContext as HomeViewModel;
            if (home != null) home.PauseNews();
        }

        /// <summary>A sideways swipe on the news goes to the next or the previous one. Vertical drags still scroll the text.</summary>
        private void News_Released(object sender, PointerReleasedEventArgs e)
        {
            var home = DataContext as HomeViewModel;
            if (home == null || !home.HasManyNews) return;
            var end = e.GetPosition(this);
            var dx = end.X - _newsStart.X;
            var dy = end.Y - _newsStart.Y;
            if (System.Math.Abs(dx) < 60 || System.Math.Abs(dx) < System.Math.Abs(dy) * 1.5) return;
            var command = dx < 0 ? home.NextNewsCommand : home.PreviousNewsCommand;
            command.Execute(null);
        }
    }
}
