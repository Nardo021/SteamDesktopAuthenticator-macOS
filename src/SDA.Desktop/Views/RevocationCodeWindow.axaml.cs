using Avalonia.Controls;
using Avalonia.Interactivity;
using SDA.Desktop.Services;
using System;

namespace SDA.Desktop.Views
{
    public partial class RevocationCodeWindow : Window
    {
        private readonly bool _confirm;

        public RevocationCodeWindow()
            : this("", false)
        {
        }

        public RevocationCodeWindow(string revocationCode, bool confirm)
        {
            InitializeComponent();
            _confirm = confirm;
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
                PromptText.Text = AuthenticatorEnrollmentService.RevocationSavePrompt;
            }
        }

        public string EnteredCode { get; private set; }

        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            if (_confirm)
            {
                ConfirmBox.Focus();
            }
        }

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
