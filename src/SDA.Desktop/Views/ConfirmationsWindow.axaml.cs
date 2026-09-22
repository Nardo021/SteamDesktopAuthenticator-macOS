using Avalonia.Controls;
using Avalonia.Interactivity;
using SDA.Desktop.Services;
using SDA.Desktop.ViewModels;
using SteamAuth;
using System;
using System.Threading.Tasks;

namespace SDA.Desktop.Views
{
    public partial class ConfirmationsWindow : Window
    {
        private readonly ConfirmationsWindowViewModel _viewModel;

        public ConfirmationsWindow()
            : this(null, null, null)
        {
        }

        public ConfirmationsWindow(
            SteamGuardAccount account,
            ConfirmationService loader,
            Func<SteamGuardAccount, SessionData, Task<string>> persist,
            IConfirmationIconLoader icons = null)
        {
            InitializeComponent();
            _viewModel = new ConfirmationsWindowViewModel(account, loader, persist, icons ?? new ConfirmationIconLoader());
            DataContext = _viewModel;
        }

        protected override async void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            await _viewModel.RefreshAsync();
        }

        protected override void OnClosed(EventArgs e)
        {
            _viewModel.Close();
            base.OnClosed(e);
        }

        private async void OnRefreshClick(object sender, RoutedEventArgs e)
        {
            await _viewModel.RefreshAsync();
        }

        private async void OnAcceptClick(object sender, RoutedEventArgs e)
        {
            ConfirmationViewModel item = ItemFrom(sender);
            if (item != null)
            {
                await _viewModel.AcceptAsync(item);
            }
        }

        private async void OnCancelClick(object sender, RoutedEventArgs e)
        {
            ConfirmationViewModel item = ItemFrom(sender);
            if (item != null)
            {
                await _viewModel.DenyAsync(item);
            }
        }

        private static ConfirmationViewModel ItemFrom(object sender)
        {
            Button button = sender as Button;
            return button == null ? null : button.DataContext as ConfirmationViewModel;
        }
    }
}
