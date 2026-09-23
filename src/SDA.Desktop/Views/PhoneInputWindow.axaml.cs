using Avalonia.Controls;
using Avalonia.Interactivity;
using SDA.Desktop.Services;
using System;

namespace SDA.Desktop.Views
{
    public partial class PhoneInputWindow : Window
    {
        public PhoneInputWindow()
        {
            InitializeComponent();
        }

        public string PhoneNumber { get; private set; }

        public string CountryCode { get; private set; }

        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            PhoneBox.Focus();
        }

        private void OnContinueClick(object sender, RoutedEventArgs e)
        {
            PhoneValidationResult result = AuthenticatorEnrollmentService.ValidatePhone(PhoneBox.Text, CountryBox.Text);
            if (!result.Valid)
            {
                ErrorText.Text = result.Error;
                if (result.Field == PhoneValidationField.Country)
                {
                    CountryBox.Focus();
                }
                else
                {
                    PhoneBox.Focus();
                }

                return;
            }

            PhoneNumber = result.PhoneNumber;
            CountryCode = result.CountryCode;
            CountryBox.Text = result.CountryCode;
            Close(true);
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            PhoneNumber = null;
            CountryCode = null;
            Close(false);
        }
    }
}
