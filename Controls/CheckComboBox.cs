// File: Controls/CheckComboBox.cs
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace Osadka.Controls
{
    /// <summary>
    /// Комбобокс с поддержкой чекбоксов внутри ItemTemplate.
    /// Не закрывает выпадающий список при клике по чекбоксу и корректно обновляет биндинги.
    /// </summary>
    public class CheckComboBox : ComboBox
    {
        static CheckComboBox()
        {
            DefaultStyleKeyProperty.OverrideMetadata(typeof(CheckComboBox), new FrameworkPropertyMetadata(typeof(ComboBox)));
        }

        protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            if (IsDropDownOpen && e.OriginalSource is DependencyObject source)
            {
                var checkBox = FindAncestor<CheckBox>(source);
                if (checkBox != null)
                {
                    e.Handled = true;
                    ToggleCheckBox(checkBox);
                    return;
                }
            }

            base.OnPreviewMouseLeftButtonDown(e);
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            if (IsDropDownOpen && (e.Key == Key.Space || e.Key == Key.Enter))
            {
                if (ItemContainerGenerator.ContainerFromIndex(SelectedIndex) is DependencyObject container)
                {
                    var checkBox = FindDescendant<CheckBox>(container);
                    if (checkBox != null)
                    {
                        e.Handled = true;
                        ToggleCheckBox(checkBox);
                        return;
                    }
                }
            }

            base.OnPreviewKeyDown(e);
        }

        private static void ToggleCheckBox(CheckBox checkBox)
        {
            checkBox.Focus();
            bool newValue = !(checkBox.IsChecked ?? false);
            checkBox.IsChecked = newValue;
            checkBox.GetBindingExpression(ToggleButton.IsCheckedProperty)?.UpdateSource();
        }

        private static T? FindAncestor<T>(DependencyObject? start) where T : DependencyObject
        {
            var current = start;
            while (current != null)
            {
                if (current is T typed)
                    return typed;
                current = VisualTreeHelper.GetParent(current);
            }

            return null;
        }

        private static T? FindDescendant<T>(DependencyObject? start) where T : DependencyObject
        {
            if (start == null)
                return null;

            if (start is T match)
                return match;

            int count = VisualTreeHelper.GetChildrenCount(start);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(start, i);
                var result = FindDescendant<T>(child);
                if (result != null)
                    return result;
            }

            return null;
        }
    }
}
