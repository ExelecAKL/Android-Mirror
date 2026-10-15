using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace AndroidMirror
{
    public sealed partial class Shell
    {
        TaskCompletionSource<bool> dialogResult;
        IInputElement dialogPreviousFocus;
        void InitializeDialogs()
        {
            Control<Button>("DialogPrimary").Click += delegate { CompleteDialog(true); };
            Control<Button>("DialogCancel").Click += delegate { CompleteDialog(false); };
        }
        Task<bool> ConfirmDialog(string title, string body, string accept, string cancel = "Cancel", string updateKind = null, string releaseNotes = null)
        {
            if (closed || dialogResult != null) return Task.FromResult(false);
            dialogPreviousFocus = Keyboard.FocusedElement;
            dialogResult = new TaskCompletionSource<bool>();
            Control<TextBlock>("DialogTitle").Text = title; Control<TextBlock>("DialogBody").Text = body;
            Control<Button>("DialogPrimary").Content = accept; Control<Button>("DialogCancel").Content = cancel;
            Control<Border>("DialogUpdateTag").Visibility = updateKind == null ? Visibility.Collapsed : Visibility.Visible;
            Control<TextBlock>("DialogUpdateKind").Text = updateKind ?? "";
            Control<StackPanel>("DialogChanges").Visibility = releaseNotes == null ? Visibility.Collapsed : Visibility.Visible;
            Control<TextBlock>("DialogReleaseSummary").Text = releaseNotes == null ? "" : ReleaseNotesSummary.Create(releaseNotes);
            Control<Grid>("AppContent").IsEnabled = false;
            Control<Grid>("DialogOverlay").Visibility = Visibility.Visible;
            var card = Control<Border>("DialogCard");
            if (MotionEnabled) {
                card.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
                var scale = (ScaleTransform)card.RenderTransform;
                var reveal = new DoubleAnimation(0.97, 1, TimeSpan.FromMilliseconds(180)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, reveal); scale.BeginAnimation(ScaleTransform.ScaleYProperty, reveal);
            }
            Control<Button>("DialogCancel").Focus();
            return dialogResult.Task;
        }
        void CompleteDialog(bool accepted)
        {
            if (dialogResult == null) return;
            var result = dialogResult; dialogResult = null;
            Control<Grid>("DialogOverlay").Visibility = Visibility.Collapsed;
            Control<Grid>("AppContent").IsEnabled = !updatingEngine;
            if (dialogPreviousFocus != null) Keyboard.Focus(dialogPreviousFocus);
            dialogPreviousFocus = null; result.TrySetResult(accepted);
        }
    }
}
