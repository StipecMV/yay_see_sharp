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
                ShowInTaskbar = false;
                Hide();
            }

            if(state == WindowState.Normal)
            {
                ShowInTaskbar = true;
                this.BringIntoView();
                Activate();
                Focus();
            }
            base.HandleWindowStateChanged(state);
        }
    }
}