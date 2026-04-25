using System;
using System.Globalization;
using Xamarin.Forms;

namespace Finder.Converters
{
    /// <summary>
    /// Flips a boolean value.
    /// Used in MainPage.xaml to show "Start" when service is NOT running
    /// and "Stop" when it IS running.
    /// </summary>
    public class InverseBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b && !b;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => value is bool b && !b;
    }
}