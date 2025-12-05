using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Layout;
using Avalonia.Media;
using System;

namespace Sup
{
    public class HorizontalAlignmentConverter : IValueConverter
    {
        public static HorizontalAlignmentConverter Instance { get; } = new HorizontalAlignmentConverter();

        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is bool isOwnMessage)
            {
                return isOwnMessage ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            }
            return HorizontalAlignment.Left;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    public class MessageBackgroundConverter : IValueConverter
    {
        public static MessageBackgroundConverter Instance { get; } = new MessageBackgroundConverter();

        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            var app = Avalonia.Application.Current;
            var brushes = app?.Resources;
            var primaryBrush = brushes?["PrimaryBrush"] as IBrush ?? new SolidColorBrush(Color.FromRgb(99,102,241));
            var messageAltBrush = brushes?["SurfaceAltBrush"] as IBrush ?? new SolidColorBrush(Color.FromRgb(15,23,42));

            if (value is bool isOwnMessage)
            {
                // Теперь свое сообщение темное, чужое яркое
                return isOwnMessage ? messageAltBrush : primaryBrush;
            }
            return messageAltBrush;
        }
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    public class MessageTextColorConverter : IValueConverter
    {
        public static MessageTextColorConverter Instance { get; } = new MessageTextColorConverter();
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            var app = Avalonia.Application.Current;
            var brushes = app?.Resources;
            var textBrush = brushes?["TextPrimaryBrush"] as IBrush ?? Brushes.White;
            if (value is bool isOwnMessage)
            {
                // В нашем сообщении пусть будет светло-серый, в чужом всегда белый
                return isOwnMessage ? textBrush : Brushes.White;
            }
            return Brushes.White;
        }
        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}