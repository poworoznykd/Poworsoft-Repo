using System;
using System.Globalization;
using CollectIQ.Domain.Enums;
using Microsoft.Maui.Controls;

namespace CollectIQ.Converters
{
    public class StrengthToLabelConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is PasswordStrength s)
            {
                return s switch
                {
                    PasswordStrength.None => "Password Strength: None",
                    PasswordStrength.Weak => "Password Strength: Weak",
                    PasswordStrength.Medium => "Password Strength: Medium",
                    PasswordStrength.Strong => "Password Strength: Strong",
                    _ => ""
                };
            }
            return "";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
