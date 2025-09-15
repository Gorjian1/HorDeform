using System;
using System.Globalization;
using System.Windows.Data;

namespace Osadka.Converters
{
    public class RelaxedDoubleConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null) return string.Empty;
            if (value is double d) return d.ToString("0.###", CultureInfo.InvariantCulture);
           // if (value is double ? nd && nd.HasValue) return nd.Value.ToString("0.###", CultureInfo.InvariantCulture);
            return value.ToString();
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var s = (value?.ToString() ?? "").Trim();
            if (s.Length == 0) return (double?)null;

            s = s.Replace(',', '.');

            // Незавершённые состояния — не пихаем в источник, чтобы не откатывало текст
            if (s == "-" || s == "." || s == "-." || s.EndsWith(".", StringComparison.Ordinal))
                return Binding.DoNothing;

            return double.TryParse(s, NumberStyles.Float | NumberStyles.AllowThousands,
                                   CultureInfo.InvariantCulture, out var d)
                   ? (double?)d
                   : Binding.DoNothing;
        }
    }
}
