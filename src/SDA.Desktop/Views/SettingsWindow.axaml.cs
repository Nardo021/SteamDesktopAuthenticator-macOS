using Avalonia.Controls;
using Avalonia.Interactivity;
using SDA.Desktop.ViewModels;
using System.Threading.Tasks;

namespace SDA.Desktop.Views
{
    public partial class SettingsWindow : Window
    {
        private readonly SettingsWindowViewModel _viewModel;

        public SettingsWindow()
            : this(new SettingsWindowViewModel(new Services.ManifestSettingsService(), null))
        {
        }

        public SettingsWindow(SettingsWindowViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel ?? new SettingsWindowViewModel(new Services.ManifestSettingsService(), null);
            DataContext = _viewModel;
            _viewModel.Load();
            IntervalBox.Value = _viewModel.PeriodicCheckingInterval;
        }

        public SettingsWindowViewModel ViewModel
        {
            get { return _viewModel; }
        }

        private async void OnMarketClick(object sender, RoutedEventArgs e)
        {
            await HandleAutoConfirmAsync(MarketBox, _viewModel.AutoConfirmMarketTransactions, _viewModel.SetAutoConfirmMarketTransactions);
        }

        private async void OnTradesClick(object sender, RoutedEventArgs e)
        {
            await HandleAutoConfirmAsync(TradesBox, _viewModel.AutoConfirmTrades, _viewModel.SetAutoConfirmTrades);
        }

        private async Task HandleAutoConfirmAsync(CheckBox box, bool previous, System.Action<bool> apply)
        {
            bool desired = box.IsChecked == true;
            if (desired && _viewModel.NeedsAutoConfirmWarning(true, previous))
            {
                ConfirmWindow confirm = new ConfirmWindow(
                    "Auto-confirm",
                    SettingsWindowViewModel.AutoConfirmWarning,
                    "Enable auto-confirm",
                    "Cancel");
                if (!await confirm.ShowDialog<bool>(this))
                {
                    box.IsChecked = false;
                    apply(false);
                    return;
                }
            }

            apply(desired);
        }

        private void OnSaveClick(object sender, RoutedEventArgs e)
        {
            _viewModel.PeriodicChecking = PeriodicCheck.IsChecked == true;
            if (IntervalBox.Value != null)
            {
                _viewModel.PeriodicCheckingInterval = (int)IntervalBox.Value.Value;
            }

            _viewModel.CheckAllAccounts = CheckAllBox.IsChecked == true;
            Services.SettingsSaveResult result = _viewModel.Save();
            if (result.Succeeded)
            {
                Close(true);
            }
        }
    }
}
