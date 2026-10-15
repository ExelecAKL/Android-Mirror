using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace AndroidMirror
{
    public sealed partial class Shell
    {
        static readonly DependencyProperty PopupMotionHook = DependencyProperty.RegisterAttached("PopupMotionHook", typeof(bool), typeof(Shell), new PropertyMetadata(false));
        void InitializeMotion()
        {
            // A short vertical reveal and easing matches the Windows 11 menu reference.
            EventManager.RegisterClassHandler(typeof(ComboBox), FrameworkElement.LoadedEvent, new RoutedEventHandler((sender, e) => {
                var combo = sender as ComboBox;
                if (combo == null || (bool)combo.GetValue(PopupMotionHook)) return;
                combo.SetValue(PopupMotionHook, true);
                combo.DropDownOpened += delegate {
                if (!MotionEnabled || closed) return;
                combo.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => {
                    var popup = combo.Template.FindName("PART_Popup", combo) as Popup;
                    var surface = popup == null ? null : popup.Child as FrameworkElement;
                    if (surface == null || !popup.IsOpen) return;
                    var transform = new TranslateTransform(); surface.RenderTransform = transform;
                    transform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-8, 0, TimeSpan.FromMilliseconds(180)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                    surface.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0.25, 1, TimeSpan.FromMilliseconds(140)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                }));
                };
            }));
            window.AddHandler(Expander.ExpandedEvent, new RoutedEventHandler((sender, e) => {
                if (!MotionEnabled) return;
                var expander = e.OriginalSource as Expander;
                if (expander == null) return;
                expander.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => {
                    var content = expander.Content as FrameworkElement;
                    if (content == null || !expander.IsExpanded) return;
                    content.RenderTransform = new TranslateTransform(); AnimateIn(content, 180, 4);
                }));
            }));
        }
    }
}
