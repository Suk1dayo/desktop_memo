using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace DesktopMemo
{
    public class UndoToastWindow : Window
    {
        private readonly DispatcherTimer _autoCloseTimer;
        private bool _closed;

        public event Action UndoClicked;

        public UndoToastWindow(string deletedTitle, string themeKey)
            : this(BuildDeletedLabel(deletedTitle), themeKey, true)
        {
        }

        private static string BuildDeletedLabel(string deletedTitle)
        {
            string rawTitle = string.IsNullOrWhiteSpace(deletedTitle) ? "未命名日程" : deletedTitle.Trim();
            string shortTitle = rawTitle.Length > 16 ? rawTitle.Substring(0, 16) + "…" : rawTitle;
            return "已删除「" + shortTitle + "」";
        }

        public UndoToastWindow(string displayText, string themeKey, bool rawMessage)
        {
            Title = "撤回操作";
            Width = 360;
            SizeToContent = SizeToContent.Height;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            ResizeMode = ResizeMode.NoResize;

            ThemePalette pal = ThemeService.GetPalette(themeKey, 0.98);

            var root = new Border();
            root.Margin = new Thickness(10);
            root.Padding = new Thickness(14, 10, 12, 10);
            root.CornerRadius = new CornerRadius(12);
            root.Background = pal.PopupBg;
            root.BorderBrush = pal.Border;
            root.BorderThickness = new Thickness(1);
            root.Effect = new DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 18,
                ShadowDepth = 4,
                Opacity = 0.20
            };

            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var label = new TextBlock();
            label.Text = rawMessage ? (displayText ?? "") : BuildDeletedLabel(displayText);
            label.FontFamily = ThemeService.AppFontFamily;
            label.FontSize = 13;
            label.FontWeight = FontWeights.Medium;
            label.Foreground = pal.TextPrimary;
            label.VerticalAlignment = VerticalAlignment.Center;
            label.TextTrimming = TextTrimming.CharacterEllipsis;
            label.Margin = new Thickness(0, 0, 10, 0);
            Grid.SetColumn(label, 0);
            row.Children.Add(label);

            var undoBtn = GlassControlStyles.CreateGlassButton("↶ 撤回",
                pal.AccentBg, pal.AccentFg, null, 12, new Thickness(10, 4, 10, 4), 7);
            undoBtn.VerticalAlignment = VerticalAlignment.Center;
            undoBtn.Margin = new Thickness(0, 0, 6, 0);
            undoBtn.Click += delegate
            {
                StopTimer();
                Action handler = UndoClicked;
                SafeClose();
                if (handler != null) handler();
            };
            Grid.SetColumn(undoBtn, 1);
            row.Children.Add(undoBtn);

            var closeBtn = GlassControlStyles.CreateGlassButton("✕",
                Brushes.Transparent, pal.TextSecondary, null, 12, new Thickness(5, 2, 5, 2), 6);
            closeBtn.VerticalAlignment = VerticalAlignment.Center;
            closeBtn.Click += delegate
            {
                StopTimer();
                SafeClose();
            };
            Grid.SetColumn(closeBtn, 2);
            row.Children.Add(closeBtn);

            root.Child = row;
            Content = root;

            _autoCloseTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
            _autoCloseTimer.Tick += delegate
            {
                StopTimer();
                SafeClose();
            };

            Loaded += delegate
            {
                PositionBottomRight();
                SpringAnimationHelper.AnimatePopIn(root, 240);
                if (!_closed) _autoCloseTimer.Start();
            };

            Closed += delegate
            {
                _closed = true;
                StopTimer();
            };
        }

        public void PositionBottomRight()
        {
            Rect work = SystemParameters.WorkArea;
            UpdateLayout();
            double w = ActualWidth > 20 ? ActualWidth : (Width > 0 ? Width : 360);
            double h = ActualHeight > 20 ? ActualHeight : 64;
            Left = work.Right - w - 16;
            Top = work.Bottom - h - 16;
        }

        private void StopTimer()
        {
            if (_autoCloseTimer != null) _autoCloseTimer.Stop();
        }

        private void SafeClose()
        {
            if (_closed) return;
            _closed = true;
            try { Close(); } catch { }
        }
    }
}
