using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace RE.WinUI.Converters
{
    /// <summary>
    /// Chuyển đổi giá trị boolean sang Microsoft.UI.Xaml.Visibility trong tầng UI/XAML,
    /// giữ ViewModel hoàn toàn độc lập và không chứa UI types.
    /// </summary>
    public sealed class BooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is bool b && b)
            {
                return Visibility.Visible;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            if (value is Visibility v)
            {
                return v == Visibility.Visible;
            }
            return false;
        }
    }
}
