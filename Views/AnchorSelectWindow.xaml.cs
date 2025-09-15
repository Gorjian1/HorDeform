// File: Views/AnchorSelectWindow.xaml.cs
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using Osadka.ViewModels;

namespace Osadka.Views
{
    public partial class AnchorSelectWindow : Window
    {
        private readonly PitOffsetViewModel _vm;
        private List<PitPointRow> _all = new();

        public double SelectedWorldX { get; private set; }
        public double SelectedWorldY { get; private set; }

        public AnchorSelectWindow(PitOffsetViewModel vm)
        {
            InitializeComponent();
            _vm = vm;
            LoadCandidates();
        }

        private void LoadCandidates()
        {
            _all.Clear();
            foreach (var c in _vm.Cycles.OrderBy(c => c.Id))
                foreach (var r in _vm.GetRowsForCycle(c.Id))
                    _all.Add(r);
            GridPoints.ItemsSource = _all;
        }

        private void OnSearchChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            var s = SearchBox.Text?.Trim();
            if (string.IsNullOrEmpty(s))
            {
                GridPoints.ItemsSource = _all;
            }
            else if (int.TryParse(s, out var n))
            {
                GridPoints.ItemsSource = _all.Where(r => r.N == n).ToList();
            }
            else
            {
                GridPoints.ItemsSource = _all;
            }
        }

        private bool TryParseManual(out double x, out double y)
        {
            x = y = 0;
            if (ManualCheck.IsChecked == true)
            {
                if (!double.TryParse(ManualX.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out x)) return false;
                if (!double.TryParse(ManualY.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out y)) return false;
                return true;
            }
            return false;
        }

        private void OnOk(object sender, RoutedEventArgs e)
        {
            if (TryParseManual(out var x, out var y))
            {
                SelectedWorldX = x; SelectedWorldY = y;
                DialogResult = true; return;
            }

            if (GridPoints.SelectedItem is PitPointRow r && r.X.HasValue && r.Y.HasValue)
            {
                SelectedWorldX = r.X.Value; SelectedWorldY = r.Y.Value;
                DialogResult = true; return;
            }

            MessageBox.Show("Выберите строку или включите «ручной ввод X/Y».", "Выбор точки", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
