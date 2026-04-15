using System.Globalization;
using System.Windows.Data;

namespace StreamPlayer.Converters;

public class MsToTimeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is long ms && ms >= 0)
        {
            var t = TimeSpan.FromMilliseconds(ms);
            return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");
        }
        return "0:00";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
