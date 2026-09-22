using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SDA.Desktop.Views
{
    public partial class MessageWindow : Window
    {
        public MessageWindow()
            : this("", "")
        {
        }

        public MessageWindow(string title, string message)
        {
            InitializeComponent();
            Title = string.IsNullOrEmpty(title) ? "Steam Desktop Authenticator" : title;
            MessageText.Text = message ?? "";
        }

        private void OnOkClick(object sender, RoutedEventArgs e)
        {
            Close(true);
        }
    }
}
