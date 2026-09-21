using Avalonia.Controls;
using Avalonia.Interactivity;
using System;

namespace SDA.Desktop.Views
{
    public partial class EncryptionPasswordWindow : Window
    {
        public EncryptionPasswordWindow()
        {
            InitializeComponent();
        }

        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            PasswordBox.Focus();
        }

        public string Password { get; private set; }

        public void SetError(string message)
        {
            ErrorText.Text = message ?? "";
        }

        private void OnUnlockClick(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(PasswordBox.Text))
            {
                ErrorText.Text = "Encryption password is required.";
                return;
            }

            Password = PasswordBox.Text;
            Close(true);
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            Close(false);
        }
    }
}
