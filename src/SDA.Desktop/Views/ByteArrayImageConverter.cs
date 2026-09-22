using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using System;
using System.Globalization;
using System.IO;

namespace SDA.Desktop.Views
{
    public sealed class ByteArrayImageConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            byte[] data = value as byte[];
            if (data == null || data.Length == 0)
            {
                return null;
            }

            try
            {
                using (MemoryStream stream = new MemoryStream(data))
                {
                    return new Bitmap(stream);
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return null;
        }
    }
}
