using Avalonia.Controls;
using Avalonia.Interactivity;
using SDA.Desktop.Services;
using SDA.Desktop.ViewModels;
using System;

namespace SDA.Desktop.Views
{
    public partial class EncryptionManagementWindow : Window
    {
        private readonly EncryptionManagementService _encryption;
        private readonly string _directory;
        private readonly EncryptionManagementViewModel _viewModel;

        public EncryptionManagementWindow()
            : this(new EncryptionManagementService(), null, EncryptionManagementKind.Setup)
        {
        }

        public EncryptionManagementWindow(EncryptionManagementService encryption, string directory, EncryptionManagementKind kind)
        {
            InitializeComponent();
            _encryption = encryption ?? new EncryptionManagementService();
            _directory = directory;
            _viewModel = new EncryptionManagementViewModel(kind);
            DataContext = _viewModel;
            Title = _viewModel.Title;
        }

        public EncryptionManagementViewModel ViewModel
        {
            get { return _viewModel; }
        }

        private void OnContinueClick(object sender, RoutedEventArgs e)
        {
            if (!_viewModel.CanSubmit)
            {
                return;
            }

            _viewModel.Busy = true;
            string current = CurrentBox == null ? null : CurrentBox.Text;
            string next = NewBox.Text;
            string confirm = ConfirmBox.Text;
            EncryptionChangeStatus? errorStatus = null;
            try
            {
                EncryptionChangeResult result = _encryption.Change(_directory, current, next, confirm);
                if (result.Succeeded || result.Status == EncryptionChangeStatus.Unchanged)
                {
                    _viewModel.ActivePassKey = result.ActivePassKey;
                    Close(true);
                    return;
                }

                _viewModel.StatusText = result.Message;
                errorStatus = result.Status;
            }
            catch (Exception)
            {
                _viewModel.StatusText = EncryptionManagementService.UnableToChangeMessage;
                errorStatus = EncryptionChangeStatus.Failed;
            }
            finally
            {
                if (CurrentBox != null)
                {
                    CurrentBox.Text = "";
                }

                NewBox.Text = "";
                ConfirmBox.Text = "";
                _viewModel.Busy = false;
            }

            if (errorStatus != null)
            {
                FocusEncryptionError(errorStatus.Value);
            }
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            Close(false);
        }

        private void FocusEncryptionError(EncryptionChangeStatus status)
        {
            switch (status)
            {
                case EncryptionChangeStatus.WrongCurrentKey:
                    if (CurrentBox != null)
                    {
                        CurrentBox.Focus();
                    }
                    break;
                case EncryptionChangeStatus.Mismatch:
                    ConfirmBox.Focus();
                    break;
                default:
                    NewBox.Focus();
                    break;
            }
        }
    }
}
