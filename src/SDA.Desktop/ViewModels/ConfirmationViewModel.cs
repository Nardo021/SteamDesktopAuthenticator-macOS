using SteamAuth;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;

namespace SDA.Desktop.ViewModels
{
    public sealed class ConfirmationViewModel : INotifyPropertyChanged
    {
        private byte[] _iconBytes;
        private bool _isBusy;
        private int _acting;

        public ConfirmationViewModel(Confirmation confirmation)
        {
            Confirmation = confirmation ?? new Confirmation();
            Headline = Confirmation.Headline ?? "";
            CreatorText = Confirmation.Creator.ToString();
            SummaryText = JoinSummary(Confirmation.Summary);
            AcceptLabel = string.IsNullOrEmpty(Confirmation.Accept) ? "Accept" : Confirmation.Accept;
            CancelLabel = string.IsNullOrEmpty(Confirmation.Cancel) ? "Cancel" : Confirmation.Cancel;
            IconUrl = Confirmation.Icon ?? "";
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public Confirmation Confirmation { get; }

        public string Headline { get; }

        public string CreatorText { get; }

        public string SummaryText { get; }

        public string AcceptLabel { get; }

        public string CancelLabel { get; }

        public string IconUrl { get; }

        public byte[] IconBytes
        {
            get { return _iconBytes; }
            set
            {
                if (ReferenceEquals(_iconBytes, value))
                {
                    return;
                }

                _iconBytes = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasIcon));
            }
        }

        public bool HasIcon
        {
            get { return _iconBytes != null && _iconBytes.Length > 0; }
        }

        public bool IsBusy
        {
            get { return _isBusy; }
            private set
            {
                if (_isBusy == value)
                {
                    return;
                }

                _isBusy = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanAct));
            }
        }

        public bool CanAct
        {
            get { return !_isBusy; }
        }

        public bool TryBeginAction()
        {
            if (Interlocked.Exchange(ref _acting, 1) == 1)
            {
                return false;
            }

            IsBusy = true;
            return true;
        }

        public void EndAction()
        {
            IsBusy = false;
            Interlocked.Exchange(ref _acting, 0);
        }

        private static string JoinSummary(List<string> summary)
        {
            if (summary == null || summary.Count == 0)
            {
                return "";
            }

            return string.Join("\n", summary);
        }

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null)
            {
                handler(this, new PropertyChangedEventArgs(propertyName));
            }
        }
    }
}
