using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace AndroidMirror
{
    public sealed partial class Shell
    {
        void InitializeInputFocus()
        {
            window.PreviewMouseDown += (sender, e) => ClearFocusForBackground(e.OriginalSource as DependencyObject);
        }
        void ClearFocusForBackground(DependencyObject source)
        {
            if (dialogResult != null || source == null) return;
            for (var node = source; node != null && node != window;) {
                var element = node as UIElement;
                if (node is Hyperlink || ((element == null || element.IsEnabled) && (node is ButtonBase || node is TextBoxBase || node is PasswordBox || node is ComboBox || node is ComboBoxItem))) return;
                var content = node as FrameworkContentElement;
                node = content != null ? content.Parent : node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
            }
            Keyboard.ClearFocus(); FocusManager.SetFocusedElement(window, window); window.Focus();
        }
    }
}
