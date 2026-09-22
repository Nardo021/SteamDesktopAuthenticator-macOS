using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SDA.Desktop.Views
{
    public partial class ConfirmWindow : Window
    {
        public ConfirmWindow()
            : this("Confirm", "", "Continue", "Cancel")
        {
        }

        public ConfirmWindow(string title, string message, string confirmText, string cancelText)
        {
            InitializeComponent();
            Title = title ?? "Confirm";
            MessageText.Text = message ?? "";
            ConfirmButton.Content = string.IsNullOrEmpty(confirmText) ? "Continue" : confirmText;
            CancelButton.Content = string.IsNullOrEmpty(cancelText) ? "Cancel" : cancelText;
        }

        private void OnConfirmClick(object sender, RoutedEventArgs e)
        {
            Close(true);
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            Close(false);
        }
    }
}
