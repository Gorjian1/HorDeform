// File: Views/VectorDisplaySettingsWindow.xaml.cs
using System.Windows;

namespace Osadka.Views
{
    public partial class VectorDisplaySettingsWindow : Window
    {
        public VectorDisplaySettingsWindow()
        {
            InitializeComponent();
        }
        private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
    }
}
