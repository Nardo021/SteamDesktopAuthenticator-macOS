using Avalonia.Controls;
using Avalonia.Interactivity;
using SDA.Desktop.Services;
using SDA.Desktop.ViewModels;
using SteamAuth;
using System;
using System.Threading.Tasks;

namespace SDA.Desktop.Views
{
    public partial class ImportAccountWindow : Window
    {
        private readonly AccountImportService _importer;
        private readonly IMaFilePicker _picker;
        private readonly ISteamLoginService _login;
        private readonly string _destinationDirectory;
        private readonly ImportAccountWindowViewModel _viewModel;

        public ImportAccountWindow()
            : this(null, null, null, null)
        {
        }

        public ImportAccountWindow(
            AccountImportService importer,
            IMaFilePicker picker,
            ISteamLoginService login,
            string destinationDirectory)
        {
            InitializeComponent();
            _importer = importer ?? new AccountImportService();
            _picker = picker;
            _login = login ?? new SteamLoginService();
            _destinationDirectory = destinationDirectory;
            _viewModel = new ImportAccountWindowViewModel();
            DataContext = _viewModel;
        }

        public ImportAccountWindowViewModel ViewModel
        {
            get { return _viewModel; }
        }

        private async void OnBrowseClick(object sender, RoutedEventArgs e)
        {
            if (_picker == null)
            {
                return;
            }

            string path = await _picker.PickAsync();
            if (!string.IsNullOrEmpty(path))
            {
                _viewModel.SourcePath = path;
            }
        }

        private async void OnImportClick(object sender, RoutedEventArgs e)
        {
            if (!_viewModel.CanImport)
            {
                return;
            }

            _viewModel.Busy = true;
            string sourceKey = SourcePasskeyBox.Text;
            try
            {
                if (_importer.IsDestinationEncrypted(_destinationDirectory))
                {
                    _viewModel.StatusText = AccountImportService.DestinationEncryptedMessage;
                    return;
                }

                AccountImportResult result = await _importer.ImportAsync(
                    _viewModel.SourcePath,
                    sourceKey,
                    _destinationDirectory,
                    RequestImportLoginAsync,
                    ConfirmReplace);
                if (result.Succeeded)
                {
                    _viewModel.ImportedAccount = result.Account;
                    _viewModel.SuccessMessage = result.Message;
                    Close(true);
                    return;
                }

                if (result.Status != AccountImportStatus.Cancelled)
                {
                    _viewModel.StatusText = result.Message;
                }
            }
            catch (Exception)
            {
                _viewModel.StatusText = AccountImportService.InvalidMaFileMessage;
            }
            finally
            {
                sourceKey = null;
                SourcePasskeyBox.Text = "";
                _viewModel.Busy = false;
            }
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            Close(false);
        }

        private async Task<SessionData> RequestImportLoginAsync(SteamGuardAccount account)
        {
            LoginWindow window = LoginWindow.ForImport(account, _login);
            bool succeeded = await window.ShowDialog<bool>(this);
            SessionData session = succeeded ? window.ResultSession : null;
            return session;
        }

        private async Task<bool> ConfirmReplace(ulong steamId)
        {
            ConfirmWindow window = new ConfirmWindow(
                "Import Account",
                "An account with this SteamID already exists in the current Manifest and will be replaced. Continue?",
                "Replace",
                "Cancel");
            return await window.ShowDialog<bool>(this);
        }
    }
}
