using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SDA.Desktop.Views
{
    public partial class DeactivateConfirmCodeWindow : Window
    {
        public DeactivateConfirmCodeWindow()
            : this("", "")
        {
        }

        public DeactivateConfirmCodeWindow(string accountName, string confirmationCode)
        {
            InitializeComponent();
            PromptText.Text = "Removing Steam Guard from " + (accountName ?? "") + ".";
            CodeDisplay.Text = confirmationCode ?? "";
        }

        public string EnteredCode { get; private set; }

        private void OnConfirmClick(object sender, RoutedEventArgs e)
        {
            EnteredCode = CodeBox.Text ?? "";
            CodeBox.Text = "";
            Close(true);
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            EnteredCode = null;
            CodeBox.Text = "";
            Close(false);
        }
    }
}
