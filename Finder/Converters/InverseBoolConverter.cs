using System;
using System.Globalization;
using Xamarin.Forms;

namespace Finder.Converters
{
    /// <summary>Flips a boolean — used to show/hide Start vs Stop button.</summary>
    public class InverseBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b && !b;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b && !b;
    }
}