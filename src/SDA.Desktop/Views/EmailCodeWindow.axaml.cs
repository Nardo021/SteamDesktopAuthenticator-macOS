using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SDA.Desktop.Views
{
    public partial class EmailCodeWindow : Window
    {
        public EmailCodeWindow()
            : this(false)
        {
        }

        public EmailCodeWindow(bool previousCodeWasIncorrect)
        {
            InitializeComponent();
            if (previousCodeWasIncorrect)
            {
                PromptText.Text = "The code you provided was invalid. Enter the code sent to your email:";
            }
        }

        public string Code { get; private set; }

        public void Clear()
        {
            Code = null;
            CodeBox.Text = "";
        }

        private void OnSubmitClick(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(CodeBox.Text))
            {
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
