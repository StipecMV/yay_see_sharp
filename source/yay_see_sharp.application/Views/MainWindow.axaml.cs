using Avalonia.Controls;

namespace yay_see_sharp.application.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        protected override void HandleWindowStateChanged(WindowState state)
        {
            if (state == WindowState.Minimized)
            {
                Hide();
            }
            base.HandleWindowStateChanged(state);
        }
    }
}