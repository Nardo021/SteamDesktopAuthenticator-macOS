using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SDA.Desktop.Views
{
    public partial class SmsCodeWindow : Window
    {
        public SmsCodeWindow()
            : this(null)
        {
        }

        public SmsCodeWindow(string prompt)
        {
            InitializeComponent();
            if (!string.IsNullOrEmpty(prompt))
            {
                PromptText.Text = prompt;
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
