using Avalonia.Controls;
using Avalonia.Interactivity;
using SDA.Desktop.Services;
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
            PasswordBox.Focus();
        }

        private void OnUnlockClick(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(PasswordBox.Text))
            {
                SetError(EncryptionManagementService.RequiredMessage);
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
