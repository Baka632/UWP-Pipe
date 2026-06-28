using System;
using UwpPipe.Common.Messages;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Data;

namespace UwpPipe.NetNative.Converters;

public partial class MessageTypeToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is MessageType type)
        {
            bool isShow = type switch
            {
                MessageType.Inbound => true,
                _ => false
            };

            if (parameter?.ToString()?.ToUpperInvariant() == "TRUE")
            {
                isShow = !isShow;
            }

            return isShow switch
            {
                true => Visibility.Visible,
                false => Visibility.Collapsed
            };
        }

        return DependencyProperty.UnsetValue;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
