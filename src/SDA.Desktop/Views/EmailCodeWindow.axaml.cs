using Avalonia.Controls;
using Avalonia.Interactivity;
using System;

namespace SDA.Desktop.Views
{
    public partial class EmailCodeWindow : Window
    {
        public const string PromptMessage = "Enter the code sent to your email.";
        public const string InvalidPromptMessage = "That code is incorrect. Enter the code sent to your email.";

        public EmailCodeWindow()
            : this(false)
        {
        }

        public EmailCodeWindow(bool previousCodeWasIncorrect)
        {
            InitializeComponent();
            if (previousCodeWasIncorrect)
            {
                PromptText.Text = InvalidPromptMessage;
            }
        }

        public string Code { get; private set; }

        protected override void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            CodeBox.Focus();
        }

        public void Clear()
        {
            Code = null;
            CodeBox.Text = "";
        }

        private void OnSubmitClick(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(CodeBox.Text))
            {
                ErrorText.Text = PromptMessage;
                CodeBox.Focus();
                return;
            }

            Code = CodeBox.Text;
            CodeBox.Text = "";
            Close(true);
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            Clear();
            Close(false);
        }
    }
}
