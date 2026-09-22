using Avalonia.Controls;
using Avalonia.Interactivity;
using SDA.Desktop.Services;

namespace SDA.Desktop.Views
{
    public partial class DeactivateChoiceWindow : Window
    {
        public DeactivateChoiceWindow()
            : this("")
        {
        }

        public DeactivateChoiceWindow(string accountName)
        {
            InitializeComponent();
            Title = string.IsNullOrEmpty(accountName) ? "Deactivate Authenticator" : "Deactivate Authenticator: " + accountName;
            PromptText.Text = string.IsNullOrEmpty(accountName)
                ? "How should Steam Guard be deactivated?"
                : "How should Steam Guard be deactivated for " + accountName + "?";
        }

        public int Scheme { get; private set; }

        private void OnRemoveCompletelyClick(object sender, RoutedEventArgs e)
        {
            Scheme = AuthenticatorDeactivationService.RemoveCompletelyScheme;
            Close(true);
        }

        private void OnEmailClick(object sender, RoutedEventArgs e)
        {
            Scheme = AuthenticatorDeactivationService.EmailScheme;
            Close(true);
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            Scheme = 0;
            Close(false);
        }
    }
}
