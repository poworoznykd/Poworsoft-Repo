using System;
using System.Globalization;
using CollectIQ.Domain.Enums;
using Microsoft.Maui.Controls;

namespace CollectIQ.Converters
{
    public class MediumColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is PasswordStrength s)
                return (s >= PasswordStrength.Medium) ? Colors.Gold : Colors.Transparent;

            return Colors.Transparent;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
