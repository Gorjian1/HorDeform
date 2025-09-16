// File: Views/PitOffsetPage.xaml.cs
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Osadka.ViewModels;

namespace Osadka.Views
{
    public partial class PitOffsetPage : UserControl
    {
        public PitOffsetPage()
        {
            InitializeComponent();

            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
            Loaded += OnLoadedHook;
            Unloaded += OnUnloadedHook;
        }

        private void OnLoaded(object? sender, RoutedEventArgs e)
        {
            if (DataContext is PitOffsetViewModel vm)
            {
                // клики по подложке
                CanvasHost.MouseLeftButtonDown += OnCanvasClick;
            }
        }

        private void OnUnloaded(object? sender, RoutedEventArgs e)
        {
            CanvasHost.MouseLeftButtonDown -= OnCanvasClick;
        }

        private void OnCanvasClick(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is not PitOffsetViewModel vm) return;
            if (!vm.IsBuildMode) return;

            var p = e.GetPosition(CanvasHost); // экранные пиксели (после поворота контейнера)

            // Откроем окно выбора точки (или ручной ввод)
            var dlg = new AnchorSelectWindow(vm) { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() == true)
            {
                // Получили мировые координаты X/Y
                vm.AddAnchor(p.X, p.Y, dlg.SelectedWorldX, dlg.SelectedWorldY);
            }
        }

        // Экспорт PNG (вызывается командой из VM)
        private void Vm_OnExportRequested(object? sender, EventArgs e)
        {
            var dlg = new SaveFileDialog { Filter = "PNG|*.png", FileName = "pit-canvas.png" };
            if (dlg.ShowDialog() != true) return;

            var rtb = new RenderTargetBitmap(
                (int)Math.Max(1, CanvasHost.ActualWidth),
                (int)Math.Max(1, CanvasHost.ActualHeight),
                96, 96, PixelFormats.Pbgra32);

            rtb.Render(CanvasHost);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));
            using var fs = System.IO.File.Create(dlg.FileName);
            encoder.Save(fs);

            MessageBox.Show("Изображение сохранено.", "Экспорт PNG",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void OnVectorSettings(object? sender, EventArgs e)
        {
            if (DataContext is not PitOffsetViewModel vm) return;
            var win = new VectorDisplaySettingsWindow
            {
                Owner = Window.GetWindow(this),
                DataContext = vm
            };
            win.ShowDialog();
        }

        private void OnLoadedHook(object? sender, EventArgs e)
        {
            if (DataContext is PitOffsetViewModel vm)
            {
                vm.OnExportRequested += Vm_OnExportRequested;
                vm.OnOpenVectorSettingsRequested += OnVectorSettings;
            }
        }



        private void OnUnloadedHook(object? sender, EventArgs e)
        {
            if (DataContext is PitOffsetViewModel vm)
            {
                vm.OnExportRequested -= Vm_OnExportRequested;
                vm.OnOpenVectorSettingsRequested -= OnVectorSettings;
            }
        }
    }
}
