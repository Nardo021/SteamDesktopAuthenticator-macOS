using Avalonia.Controls;
using Avalonia.Interactivity;
using SDA.Desktop.ViewModels;
using System;

namespace SDA.Desktop.Views
{
    public partial class ConfirmationPopupWindow : Window
    {
        private readonly ConfirmationPopupViewModel _viewModel;

        public ConfirmationPopupWindow()
            : this(null)
        {
        }

        public ConfirmationPopupWindow(ConfirmationPopupViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            DataContext = _viewModel;
        }

        private async void OnAcceptClick(object sender, RoutedEventArgs e)
        {
            if (_viewModel == null)
            {
                return;
            }

            try
            {
                await _viewModel.AcceptAsync();
            }
            catch (Exception)
            {
            }
        }

        private async void OnDenyClick(object sender, RoutedEventArgs e)
        {
            if (_viewModel == null)
            {
                return;
            }

            try
            {
                await _viewModel.DenyAsync();
            }
            catch (Exception)
            {
            }
        }
    }
}
