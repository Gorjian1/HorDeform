// File: Views/PitConverters.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Osadka.ViewModels;

namespace Osadka.Views
{
    internal static class PitColorHelper
    {
        private static readonly Color[] Palette =
        {
            Colors.Red, Colors.Blue, Colors.Green, Colors.Orange, Colors.Purple, Colors.Teal
        };

        public static Color ResolveColor(int id, VectorDisplaySettings? settings)
        {
            if (settings == null) return Palette[Math.Abs(id) % Palette.Length];
            if (settings.UseSingleColor) return settings.SingleColor;
            var style = settings.CycleStyles.FirstOrDefault(cs => cs.CycleId == id);
            if (style != null && style.Color != Colors.Transparent) return style.Color;
            return Palette[Math.Abs(id) % Palette.Length];
        }

        public static SolidColorBrush CreateFrozenBrush(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
    }

    // ===== Цвет цикла из настроек =====
    public sealed class CycleColorFromSettingsConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            int id = values != null && values.Length > 0 && values[0] is int i ? i : 0;
            var settings = values != null && values.Length > 1 ? values[1] as VectorDisplaySettings : null;
            var color = PitColorHelper.ResolveColor(id, settings);
            return PitColorHelper.CreateFrozenBrush(color);
        }
        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => new object[] { Binding.DoNothing };
    }

    // ===== Штриховка (угол/шаг/прозрачность из настроек) =====
    // [cycleId, settings, settings.Version]
    public sealed class CycleHatchMultiConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            int id = values != null && values.Length > 0 && values[0] is int i ? i : 0;
            var settings = values != null && values.Length > 1 ? values[1] as VectorDisplaySettings : null;

            double spacing = Math.Max(2.0, settings?.HatchSpacingPx ?? 12.0);
            double fillOpacity = Math.Clamp(settings?.FillOpacity ?? 0.3, 0.0, 1.0);
            double hatchOpacity = Math.Clamp(settings?.HatchOpacity ?? 0.6, 0.0, 1.0);
            double angle = settings?.HatchAngleDeg ?? 45.0;

            var baseColor = PitColorHelper.ResolveColor(id, settings);
            var fillColor = Color.FromArgb((byte)(fillOpacity * 255), baseColor.R, baseColor.G, baseColor.B);
            var hatchColor = Color.FromArgb((byte)(hatchOpacity * 255), baseColor.R, baseColor.G, baseColor.B);

            var fillBrush = new SolidColorBrush(fillColor);
            fillBrush.Freeze();
            var hatchBrush = new SolidColorBrush(hatchColor);
            hatchBrush.Freeze();
            var pen = new Pen(hatchBrush, 1);
            pen.Freeze();

            var cell = new Rect(0, 0, spacing, spacing);
            var background = new GeometryDrawing(fillBrush, null, new RectangleGeometry(cell));
            background.Freeze();

            var geometryGroup = new GeometryGroup();
            for (int k = -1; k <= 1; k++)
            {
                double offset = k * spacing;
                var line = new LineGeometry(new Point(offset, spacing), new Point(offset + spacing, 0));
                line.Freeze();
                geometryGroup.Children.Add(line);
            }
            geometryGroup.Freeze();

            var hatchDrawing = new GeometryDrawing(null, pen, geometryGroup);
            hatchDrawing.Freeze();

            var drawingGroup = new DrawingGroup();
            drawingGroup.Children.Add(background);
            drawingGroup.Children.Add(hatchDrawing);
            drawingGroup.Freeze();

            var brush = new DrawingBrush(drawingGroup)
            {
                TileMode = TileMode.Tile,
                Viewport = cell,
                ViewportUnits = BrushMappingMode.Absolute,
                Viewbox = cell,
                ViewboxUnits = BrushMappingMode.Absolute,
                Stretch = Stretch.None
            };
            brush.RelativeTransform = new RotateTransform(angle, 0.5, 0.5);
            brush.Freeze();
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
