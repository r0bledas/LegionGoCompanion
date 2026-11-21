using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace HandheldCompanion.Views
{
    /// <summary>
    /// Tracks genuine physical mouse hover on Buttons while suppressing synthetic
    /// mouse hover left behind after Windows touch/stylus taps.
    /// </summary>
    public static class TouchHoverHelper
    {
        private static long _lastTouchTicks;
        private static WeakReference<Button>? _activeHovered;

        public static readonly DependencyProperty IsHoveredProperty =
            DependencyProperty.RegisterAttached(
                "IsHovered",
                typeof(bool),
                typeof(TouchHoverHelper),
                new PropertyMetadata(false));

        public static bool GetIsHovered(DependencyObject obj) => (bool)obj.GetValue(IsHoveredProperty);
        public static void SetIsHovered(DependencyObject obj, bool value) => obj.SetValue(IsHoveredProperty, value);

        public static readonly DependencyProperty TrackHoverProperty =
            DependencyProperty.RegisterAttached(
                "TrackHover",
                typeof(bool),
                typeof(TouchHoverHelper),
                new PropertyMetadata(false, OnTrackHoverChanged));

        public static bool GetTrackHover(DependencyObject obj) => (bool)obj.GetValue(TrackHoverProperty);
        public static void SetTrackHover(DependencyObject obj, bool value) => obj.SetValue(TrackHoverProperty, value);

        public static void ClearActiveHover()
        {
            if (_activeHovered != null && _activeHovered.TryGetTarget(out var prev))
            {
                SetIsHovered(prev, false);
            }
            _activeHovered = null;
        }

        private static void OnTrackHoverChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not Button btn)
                return;

            if ((bool)e.NewValue)
            {
                btn.MouseEnter += OnMouseEnterOrMove;
                btn.MouseMove += OnMouseEnterOrMove;
                btn.MouseLeave += OnMouseLeave;
                btn.PreviewTouchDown += OnTouchOrStylus;
                btn.PreviewTouchUp += OnTouchOrStylus;
                btn.PreviewStylusDown += OnTouchOrStylus;
                btn.PreviewStylusUp += OnTouchOrStylus;
                btn.IsVisibleChanged += OnVisibleChanged;
                btn.Click += OnButtonClick;
            }
            else
            {
                btn.MouseEnter -= OnMouseEnterOrMove;
                btn.MouseMove -= OnMouseEnterOrMove;
                btn.MouseLeave -= OnMouseLeave;
                btn.PreviewTouchDown -= OnTouchOrStylus;
                btn.PreviewTouchUp -= OnTouchOrStylus;
                btn.PreviewStylusDown -= OnTouchOrStylus;
                btn.PreviewStylusUp -= OnTouchOrStylus;
                btn.IsVisibleChanged -= OnVisibleChanged;
                btn.Click -= OnButtonClick;
                SetIsHovered(btn, false);
            }
        }

        private static void OnTouchOrStylus(object sender, InputEventArgs e)
        {
            System.Threading.Interlocked.Exchange(ref _lastTouchTicks, DateTime.UtcNow.Ticks);
            ClearActiveHover();
            if (sender is Button btn)
            {
                SetIsHovered(btn, false);
            }
        }

        private static void OnButtonClick(object sender, RoutedEventArgs e)
        {
            long elapsedTicks = DateTime.UtcNow.Ticks - System.Threading.Interlocked.Read(ref _lastTouchTicks);
            if (elapsedTicks < TimeSpan.TicksPerMillisecond * 500 && sender is Button btn)
            {
                SetIsHovered(btn, false);
                ClearActiveHover();
            }
        }

        private static void OnMouseEnterOrMove(object sender, MouseEventArgs e)
        {
            if (sender is not Button btn)
                return;

            long elapsedTicks = DateTime.UtcNow.Ticks - System.Threading.Interlocked.Read(ref _lastTouchTicks);
            if (e.StylusDevice != null || elapsedTicks < TimeSpan.TicksPerMillisecond * 450)
            {
                SetIsHovered(btn, false);
                return;
            }

            if (_activeHovered != null && _activeHovered.TryGetTarget(out var prev) && !ReferenceEquals(prev, btn))
            {
                SetIsHovered(prev, false);
            }

            _activeHovered = new WeakReference<Button>(btn);
            SetIsHovered(btn, true);
        }

        private static void OnMouseLeave(object sender, MouseEventArgs e)
        {
            if (sender is Button btn)
            {
                SetIsHovered(btn, false);
                if (_activeHovered != null && _activeHovered.TryGetTarget(out var prev) && ReferenceEquals(prev, btn))
                {
                    _activeHovered = null;
                }
            }
        }

        private static void OnVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (sender is Button btn && !btn.IsVisible)
            {
                SetIsHovered(btn, false);
            }
        }
    }
}
