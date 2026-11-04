using Avalonia.Controls;
using Sillet.App.Services;

namespace Sillet.Mobile.UI.Views
{
    public partial class TradeView : UserControl
    {
        public TradeView()
        {
            InitializeComponent();
            Refresh();
            Loc.Changed += Refresh;
        }

        private void Refresh()
        {
            var ramp = this.FindControl<TextBlock>("RampNote");
            if (ramp != null) ramp.Text = Loc.T("Payments happen on the provider's site. Sillet never sees your card or bank details");
            var swap = this.FindControl<TextBlock>("SwapNote");
            if (swap != null) swap.Text = Loc.T("Exchanged by ") + SwapService.Provider + Loc.T(", no account needed  ·  ");
        }
    }
}
