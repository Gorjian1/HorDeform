// File: Views/VectorDisplaySettingsWindow.xaml.cs
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Osadka.ViewModels;
using DrawingColor = System.Drawing.Color;
using WF = System.Windows.Forms;

namespace Osadka.Views
{
    public partial class VectorDisplaySettingsWindow : Window
    {
        public VectorDisplaySettingsWindow()
        {
            InitializeComponent();
        }

        private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

        private void OnSelectSingleColorClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is not PitOffsetViewModel { VectorSettings: { } settings }) return;
            var current = settings.SingleColor;
            if (TryPickColor(current, out var selected))
                settings.SingleColor = selected;
        }

        private void OnSelectCycleColorClick(object sender, RoutedEventArgs e)
        {
            if (sender is not Button { DataContext: CycleStyle style }) return;
            var current = style.Color;
            if (TryPickColor(current, out var selected))
                style.Color = selected;
        }

        private bool TryPickColor(Color initialColor, out Color selectedColor)
        {
            using var dialog = new WF.ColorDialog
            {
                AllowFullOpen = true,
                AnyColor = true,
                FullOpen = true,
                Color = DrawingColor.FromArgb(initialColor.A, initialColor.R, initialColor.G, initialColor.B)
            };

            var ownerHandle = new WindowInteropHelper(this).Handle;
            if (dialog.ShowDialog(new Win32Window(ownerHandle)) == WF.DialogResult.OK)
            {
                var color = dialog.Color;
                selectedColor = Color.FromArgb(color.A, color.R, color.G, color.B);
                return true;
            }

            selectedColor = initialColor;
            return false;
        }

        private sealed class Win32Window : WF.IWin32Window
        {
            public Win32Window(IntPtr handle) => Handle = handle;
            public IntPtr Handle { get; }
        }
    }
}
