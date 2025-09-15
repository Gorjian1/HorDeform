// File: Views/PitConverters.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Osadka.Views
{
    internal static class C
    {
        public static double D(object v, double def = 0)
        {
            if (v == null) return def;
            if (v is double d) return d;
            if (v is float f) return f;
            if (v is decimal m) return (double)m;
            if (v is int i) return i;
            if (v is long l) return l;
            if (v is string s && double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var dv)) return dv;
            var nd = v as double?;
            return nd ?? def;
        }
        public static void ABT(double scale, double rotDeg, out double a, out double b)
        {
            var r = rotDeg * Math.PI / 180.0;
            a = scale * Math.Cos(r);
            b = scale * Math.Sin(r);
        }
    }

    // ===== Экранные координаты начала =====
    public sealed class ToScreenConverter : IMultiValueConverter
    {
        public object Convert(object[] v, Type tt, object p, CultureInfo c)
        {
            double x = C.D(v.ElementAtOrDefault(0)), y = C.D(v.ElementAtOrDefault(1));
            double s = C.D(v.ElementAtOrDefault(2), 1), tx = C.D(v.ElementAtOrDefault(3)), ty = C.D(v.ElementAtOrDefault(4));
            double deg = C.D(v.ElementAtOrDefault(5));
            C.ABT(s, deg, out var a, out var b);
            double u = a * x - b * y + tx;
            double w = b * x + a * y + ty;
            return (string)p == "x" ? u : w;
        }
        public object[] ConvertBack(object v, Type[] t, object p, CultureInfo c) => new object[] { Binding.DoNothing };
    }

    // ===== Взвешенная длина вектора (конец) =====
    // [row, scale, offX, offY, baseScale, rotDeg, maxV, settings, settings.Version] + 'x'|'y'
    public sealed class VectorEndWeightedConverter : IMultiValueConverter
    {
        public object Convert(object[] v, Type tt, object p, CultureInfo c)
        {
            var row = v.ElementAtOrDefault(0) as global::Osadka.ViewModels.PitPointRow;
            if (row == null) return 0.0;

            double s = C.D(v.ElementAtOrDefault(1), 1);
            double tx = C.D(v.ElementAtOrDefault(2));
            double ty = C.D(v.ElementAtOrDefault(3));
            double baseScale = C.D(v.ElementAtOrDefault(4), 1);
            double deg = C.D(v.ElementAtOrDefault(5));
            double maxV = C.D(v.ElementAtOrDefault(6));
            var settings = v.ElementAtOrDefault(7) as global::Osadka.ViewModels.VectorDisplaySettings;

            double vlen = Math.Sqrt(Math.Pow(row.Dx ?? 0, 2) + Math.Pow(row.Dy ?? 0, 2));
            double w = 1.0;
            if (settings != null && settings.UseRelativeWeight && maxV > 1e-9 && vlen > 0)
            {
                double norm = vlen / maxV;                   // 0..1
                double gamma = Math.Max(0.0, settings.WeightExponent);
                w = Math.Pow(norm, gamma);                   // плавная кривая
            }

            double k = baseScale * w;                        // итоговый множитель в мире

            // Пиксельные ограничения длины
            if (settings != null && vlen > 0)
            {
                double pxLen = vlen * k * s;                 // текущая длина в пикселях
                if (settings.MinArrowPx > 0 && pxLen < settings.MinArrowPx)
                    k = settings.MinArrowPx / (vlen * s);
                if (settings.MaxArrowPx > 0 && settings.MaxArrowPx >= settings.MinArrowPx && pxLen > settings.MaxArrowPx)
                    k = settings.MaxArrowPx / (vlen * s);
            }

            // Конец в мировых координатах
            double x2 = (row.X ?? 0) + (row.Dx ?? 0) * k;
            double y2 = (row.Y ?? 0) + (row.Dy ?? 0) * k;

            // Проекция на экран
            C.ABT(s, deg, out var a, out var b);
            double u = a * x2 - b * y2 + tx;
            double w2 = b * x2 + a * y2 + ty;
            return (string)p == "x" ? u : w2;
        }
        public object[] ConvertBack(object v, Type[] t, object p, CultureInfo c) => new object[] { Binding.DoNothing };
    }

    // ===== Наконечник треугольником (взвешенный) =====
    // [row, scale, offX, offY, baseScale, headPx, rotDeg, maxV, settings, settings.Version]
    public sealed class ArrowHeadPointsWeightedConverter : IMultiValueConverter
    {
        public object Convert(object[] v, Type tt, object p, CultureInfo c)
        {
            var row = v.ElementAtOrDefault(0) as global::Osadka.ViewModels.PitPointRow;
            if (row == null) return null;

            double s = C.D(v.ElementAtOrDefault(1), 1), tx = C.D(v.ElementAtOrDefault(2)), ty = C.D(v.ElementAtOrDefault(3));
            double baseScale = C.D(v.ElementAtOrDefault(4), 1);
            double headPx = C.D(v.ElementAtOrDefault(5), 12);
            double deg = C.D(v.ElementAtOrDefault(6));
            double maxV = C.D(v.ElementAtOrDefault(7));
            var settings = v.ElementAtOrDefault(8) as global::Osadka.ViewModels.VectorDisplaySettings;

            double vlen = Math.Sqrt(Math.Pow(row.Dx ?? 0, 2) + Math.Pow(row.Dy ?? 0, 2));
            double w = 1.0;
            if (settings != null && settings.UseRelativeWeight && maxV > 1e-9 && vlen > 0)
            {
                double norm = vlen / maxV;
                double gamma = Math.Max(0.0, settings.WeightExponent);
                w = Math.Pow(norm, gamma);
            }
            double k = baseScale * w;

            if (settings != null && vlen > 0)
            {
                double pxLen = vlen * k * s;
                if (settings.MinArrowPx > 0 && pxLen < settings.MinArrowPx)
                    k = settings.MinArrowPx / (vlen * s);
                if (settings.MaxArrowPx > 0 && settings.MaxArrowPx >= settings.MinArrowPx && pxLen > settings.MaxArrowPx)
                    k = settings.MaxArrowPx / (vlen * s);
            }

            double x1 = (row.X ?? 0), y1 = (row.Y ?? 0);
            double x2 = x1 + (row.Dx ?? 0) * k;
            double y2 = y1 + (row.Dy ?? 0) * k;

            C.ABT(s, deg, out var a, out var b);
            double u1 = a * x1 - b * y1 + tx, w1 = b * x1 + a * y1 + ty;
            double u2 = a * x2 - b * y2 + tx, w2 = b * x2 + a * y2 + ty;

            var vx = u2 - u1; var vy = w2 - w1;
            var len = Math.Sqrt(vx * vx + vy * vy);
            if (len < 1e-6) return new PointCollection { new Point(u2, w2) };

            var ux = vx / len; var uy = vy / len;
            var nx = -uy; var ny = ux;
            double head = Math.Min(headPx, len * 0.25);

            var p0 = new Point(u2, w2);
            var p1 = new Point(u2 - ux * head + nx * head * 0.4, w2 - uy * head + ny * head * 0.4);
            var p2 = new Point(u2 - ux * head - nx * head * 0.4, w2 - uy * head - ny * head * 0.4);
            return new PointCollection { p0, p1, p2 };
        }
        public object[] ConvertBack(object v, Type[] t, object p, CultureInfo c) => new object[] { Binding.DoNothing };
    }

    // ===== Цвет цикла из настроек =====
    public sealed class CycleColorFromSettingsConverter : IMultiValueConverter
    {
        private static readonly Brush[] Palette =
            { Brushes.Red, Brushes.Blue, Brushes.Green, Brushes.Orange, Brushes.Purple, Brushes.Teal };

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            int id = values != null && values.Length > 0 && values[0] is int i ? i : 0;
            var settings = values != null && values.Length > 1 ? values[1] as global::Osadka.ViewModels.VectorDisplaySettings : null;
            if (settings == null) return Palette[Math.Abs(id) % Palette.Length];
            if (settings.UseSingleColor) return new SolidColorBrush(settings.SingleColor);
            var style = settings.CycleStyles.FirstOrDefault(cs => cs.CycleId == id);
            if (style != null && style.Color != Colors.Transparent) return new SolidColorBrush(style.Color);
            return Palette[Math.Abs(id) % Palette.Length];
        }
        public object[] ConvertBack(object v, Type[] t, object p, CultureInfo c) => new object[] { Binding.DoNothing };
    }

    // ===== Штриховка (угол/шаг/прозрачность из настроек) =====
    // [cycleId, settings, settings.Version]
    public sealed class CycleHatchMultiConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            var settings = values != null && values.Length > 1 ? values[1] as global::Osadka.ViewModels.VectorDisplaySettings : null;
            double spacing = settings?.HatchSpacingPx ?? 12;
            double opacity = settings?.HatchOpacity ?? 0.6;
            double angle = settings?.HatchAngleDeg ?? 45;

            var stroke = Brushes.Gray;
            if (values != null && values.Length > 0 && values[0] is int id)
            {
                var pal = new[] { Colors.Red, Colors.Blue, Colors.Green, Colors.Orange, Colors.Purple, Colors.Teal };
                var c = pal[Math.Abs(id) % pal.Length];
                stroke = new SolidColorBrush(Color.FromArgb((byte)(opacity * 255), c.R, c.G, c.B));
            }
            var pen = new Pen(stroke, 1);

            var group = new GeometryGroup();
            int lines = 12;
            for (int i = -lines; i <= lines; i++)
            {
                double x = i * spacing;
                group.Children.Add(new LineGeometry(new Point(x, -1000), new Point(x, 1000)));
            }
            var drawing = new GeometryDrawing(null, pen, group);
            var brush = new DrawingBrush(drawing)
            {
                TileMode = TileMode.Tile,
                Viewport = new Rect(0, 0, spacing, spacing),
                ViewportUnits = BrushMappingMode.Absolute,
                Viewbox = new Rect(0, 0, spacing, spacing),
                ViewboxUnits = BrushMappingMode.Absolute,
                Opacity = settings?.FillOpacity ?? 0.3
            };
            brush.RelativeTransform = new RotateTransform(angle, 0.5, 0.5);
            return brush;
        }

        public object[] ConvertBack(object v, Type[] t, object p, CultureInfo c) => new object[] { Binding.DoNothing };
    }

    // ===== Простые конвертеры =====
    public sealed class InvertedBoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => (value is bool b && b) ? Visibility.Collapsed : Visibility.Visible;
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => Binding.DoNothing;
    }

    public sealed class EnumEqualsConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            try { return object.Equals(value, Enum.Parse(value.GetType(), (string)parameter)); }
            catch { return false; }
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b && b) try { return Enum.Parse(targetType, (string)parameter); } catch { }
            return Binding.DoNothing;
        }
    }

    public sealed class ColorToHexConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Color c) return $"#{c.R:X2}{c.G:X2}{c.B:X2}";
            return "#000000";
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string s)
            {
                s = s.Trim().TrimStart('#');
                if (s.Length == 6 &&
                    byte.TryParse(s.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r) &&
                    byte.TryParse(s.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g) &&
                    byte.TryParse(s.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
                    return Color.FromRgb(r, g, b);
            }
            return Colors.Black;
        }
    }
}
