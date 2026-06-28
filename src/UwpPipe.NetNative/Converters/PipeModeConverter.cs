using System;
using UwpPipe.Common;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Data;

namespace UwpPipe.NetNative.Converters;

internal partial class PipeModeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value switch
        {
            PipeMode.Client => "客户端",
            PipeMode.Server => "服务器",
            _ => DependencyProperty.UnsetValue
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        throw new NotImplementedException();
    }
}
