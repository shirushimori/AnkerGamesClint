using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace AnkerGamesClient.Converters;

/// <summary>
/// Converts a local file path string to a BitmapImage.
/// Returns null if the path is empty or the file doesn't exist,
/// which lets the XAML fallback placeholder show.
/// </summary>
[ValueConversion(typeof(string), typeof(BitmapImage))]
public class BannerPathConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrWhiteSpace(path))
            return null;

        if (!File.Exists(path))
            return null;

        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(path, UriKind.Absolute);
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => null;
}
