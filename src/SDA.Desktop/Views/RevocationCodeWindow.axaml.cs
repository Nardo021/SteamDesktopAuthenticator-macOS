using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SDA.Desktop.Views
{
    public partial class RevocationCodeWindow : Window
    {
        private readonly bool _confirm;
        private readonly string _expected;

        public RevocationCodeWindow()
            : this("", false, null)
        {
        }

        public RevocationCodeWindow(string revocationCode, bool confirm, string expected)
        {
            InitializeComponent();
            _confirm = confirm;
            _expected = expected ?? "";
            CodeBox.Text = revocationCode ?? "";
            if (confirm)
            {
                Title = "Confirm Revocation Code";
                PromptText.Text = "Confirm Revocation Code";
                ConfirmPrompt.IsVisible = true;
                ConfirmBox.IsVisible = true;
                CancelButton.IsVisible = true;
                ContinueButton.Content = "Confirm";
            }
            else
            {
                PromptText.Text = "IMPORTANT. Save this revocation code somewhere safe:";
            }
        }

        public string EnteredCode { get; private set; }

        private void OnContinueClick(object sender, RoutedEventArgs e)
        {
            if (!_confirm)
            {
                Close(true);
                return;
            }

            EnteredCode = ConfirmBox.Text ?? "";
            ConfirmBox.Text = "";
            Close(true);
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            EnteredCode = null;
            ConfirmBox.Text = "";
            Close(false);
        }
    }
}
