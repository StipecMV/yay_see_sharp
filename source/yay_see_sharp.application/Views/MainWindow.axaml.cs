using Avalonia.Controls;

namespace yay_see_sharp.application.Views
{
    public partial class MainWindow : Window
    {
        private WindowIcon myIcon;

        public MainWindow()
        {
            InitializeComponent();
        }

        protected override void HandleWindowStateChanged(WindowState state)
        {
            if (state == WindowState.Minimized)
            {
                myIcon = Icon;
                Icon = null;
                ShowInTaskbar = false;
                Hide();
            }

            if(state == WindowState.Normal)
            {
                Icon = myIcon;
                ShowInTaskbar = true;
                this.BringIntoView();
                Activate();
                Focus();
            }
            base.HandleWindowStateChanged(state);
        }
    }
}