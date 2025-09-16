// File: Views/VectorDisplaySettingsWindow.xaml.cs
using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Osadka.ViewModels;
using WinForms = System.Windows.Forms;

namespace Osadka.Views
{
    public partial class VectorDisplaySettingsWindow : Window
    {
        public VectorDisplaySettingsWindow()
        {
            InitializeComponent();
        }
        private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

        private void OnPickSingleColorClick(object sender, RoutedEventArgs e)
        {
            if (DataContext is not PitOffsetViewModel vm) return;
            var selected = PickColor(vm.VectorSettings.SingleColor);
            if (selected.HasValue)
                vm.VectorSettings.SingleColor = selected.Value;
        }

        private void OnPickCycleColorClick(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not CycleStyle style) return;
            var selected = PickColor(style.Color);
            if (selected.HasValue)
                style.Color = selected.Value;
        }

        private Color? PickColor(Color initial)
        {
            var dialog = new WinForms.ColorDialog
            {
                AllowFullOpen = true,
                FullOpen = true,
                AnyColor = true,
                Color = System.Drawing.Color.FromArgb(initial.A, initial.R, initial.G, initial.B)
            };

            var helper = new WindowInteropHelper(this);
            var owner = new Win32Window(helper.Handle);
            return dialog.ShowDialog(owner) == WinForms.DialogResult.OK
                ? Color.FromArgb(255, dialog.Color.R, dialog.Color.G, dialog.Color.B)
                : (Color?)null;
        }

        private sealed class Win32Window : WinForms.IWin32Window
        {
            public Win32Window(IntPtr handle) => Handle = handle;
            public IntPtr Handle { get; }
        }
    }
}
