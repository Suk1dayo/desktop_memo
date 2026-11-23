using System;
using System.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace DesktopMemo
{
    public class ReminderToastWindow : Window
    {
        public string TaskId { get; private set; }

        public event Action<string> CompleteClicked;
        public event Action<string, int> SnoozeClicked;
        public event Action<string> Dismissed;

        public ReminderToastWindow(MemoTask task, bool playSound)
        {
            TaskId = task.Id;
            Title = "日程提醒";
            Width = 370;
            SizeToContent = SizeToContent.Height;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            ResizeMode = ResizeMode.NoResize;

            ThemePalette pal = ThemeService.GetPalette("claude_warm", 0.96);
            QuadrantVisual qv = ThemeService.GetQuadrantVisual(task.Quadrant, false);
            DeadlineDisplayInfo ddlInfo = task.GetDeadlineDisplay(DateTime.Now);

            var root = new Border();
            root.Margin = new Thickness(10);
            root.CornerRadius = new CornerRadius(14);
            root.Background = pal.PopupBg;
            root.BorderBrush = pal.Border;
            root.BorderThickness = new Thickness(1);
            root.Effect = new DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 20,
                ShadowDepth = 4,
                Opacity = 0.22
            };

            var outerGrid = new Grid();
            outerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) });
            outerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var stripe = new Border();
            stripe.Background = ThemeService.BrushFromHex(qv.AccentHex);
            stripe.CornerRadius = new CornerRadius(12, 0, 0, 12);
            Grid.SetColumn(stripe, 0);
            outerGrid.Children.Add(stripe);

            var body = new StackPanel();
            body.Margin = new Thickness(14, 12, 14, 12);
            Grid.SetColumn(body, 1);

            var header = new DockPanel();
            var badge = new Border();
            badge.Background = ThemeService.BrushFromHex(qv.BadgeBgHex);
            badge.CornerRadius = new CornerRadius(6);
            badge.Padding = new Thickness(7, 2, 7, 2);
            badge.Child = new TextBlock
            {
                Text = "⏰ 日程提醒 · " + qv.ShortTitle,
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                FontFamily = ThemeService.AppFontFamily,
                Foreground = ThemeService.BrushFromHex(qv.BadgeFgHex)
            };
            DockPanel.SetDock(badge, Dock.Left);
            header.Children.Add(badge);

            var closeBtn = GlassControlStyles.CreateGlassButton("✕", Brushes.Transparent,
                pal.TextSecondary, null, 12, new Thickness(6, 1, 6, 1), 6);
            closeBtn.HorizontalAlignment = HorizontalAlignment.Right;
            closeBtn.Click += delegate
            {
                if (Dismissed != null) Dismissed(TaskId);
                Close();
            };
            DockPanel.SetDock(closeBtn, Dock.Right);
            header.Children.Add(closeBtn);
            body.Children.Add(header);

            var titleBlock = new TextBlock();
            titleBlock.Text = string.IsNullOrWhiteSpace(task.Title) ? "（未命名日程）" : task.Title;
            titleBlock.FontFamily = ThemeService.AppFontFamily;
            titleBlock.FontSize = 14.5;
            titleBlock.FontWeight = FontWeights.Bold;
            titleBlock.Foreground = pal.TextPrimary;
            titleBlock.TextWrapping = TextWrapping.Wrap;
            titleBlock.Margin = new Thickness(0, 8, 0, 4);
            body.Children.Add(titleBlock);

            if (!string.IsNullOrWhiteSpace(task.Notes))
            {
                var notesBlock = new TextBlock();
                notesBlock.Text = task.Notes.Trim();
                notesBlock.FontFamily = ThemeService.AppFontFamily;
                notesBlock.FontSize = 12;
                notesBlock.Foreground = pal.TextSecondary;
                notesBlock.TextWrapping = TextWrapping.Wrap;
                notesBlock.MaxHeight = 48;
                notesBlock.TextTrimming = TextTrimming.CharacterEllipsis;
                notesBlock.Margin = new Thickness(0, 0, 0, 6);
                body.Children.Add(notesBlock);
            }

            if (!string.IsNullOrEmpty(ddlInfo.Text))
            {
                var ddlChip = new Border();
                ddlChip.Background = ThemeService.BrushFromHex(ddlInfo.BgHex);
                ddlChip.CornerRadius = new CornerRadius(5);
                ddlChip.Padding = new Thickness(7, 3, 7, 3);
                ddlChip.HorizontalAlignment = HorizontalAlignment.Left;
                ddlChip.Margin = new Thickness(0, 2, 0, 8);
                ddlChip.Child = new TextBlock
                {
                    Text = "DDL：" + ddlInfo.Text,
                    FontFamily = ThemeService.AppFontFamily,
                    FontSize = 11.5,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = ThemeService.BrushFromHex(ddlInfo.FgHex)
                };
                body.Children.Add(ddlChip);
            }

            var actions = new StackPanel();
            actions.Orientation = Orientation.Horizontal;
            actions.HorizontalAlignment = HorizontalAlignment.Right;
            actions.Margin = new Thickness(0, 4, 0, 0);

            var doneBtn = GlassControlStyles.CreateGlassButton("✓ 标记完成",
                ThemeService.BrushFromHex("#10B981"), Brushes.White, null, 12, new Thickness(10, 5, 10, 5), 7);
            doneBtn.Margin = new Thickness(0, 0, 6, 0);
            doneBtn.Click += delegate
            {
                if (CompleteClicked != null) CompleteClicked(TaskId);
                Close();
            };

            var snoozeBtn = GlassControlStyles.CreateGlassButton("10分钟后再提醒",
                pal.HoverBg, pal.TextPrimary, pal.Border, 12, new Thickness(9, 5, 9, 5), 7);
            snoozeBtn.Margin = new Thickness(0, 0, 6, 0);
            snoozeBtn.Click += delegate
            {
                if (SnoozeClicked != null) SnoozeClicked(TaskId, 10);
                Close();
            };

            var okBtn = GlassControlStyles.CreateGlassButton("知道了",
                pal.AccentBg, pal.AccentFg, null, 12, new Thickness(10, 5, 10, 5), 7);
            okBtn.Click += delegate
            {
                if (Dismissed != null) Dismissed(TaskId);
                Close();
            };

            actions.Children.Add(doneBtn);
            actions.Children.Add(snoozeBtn);
            actions.Children.Add(okBtn);
            body.Children.Add(actions);

            outerGrid.Children.Add(body);
            root.Child = outerGrid;
            Content = root;

            Loaded += delegate { SpringAnimationHelper.AnimatePopIn(root, 260); };

            if (playSound)
            {
                try { SystemSounds.Exclamation.Play(); } catch { }
            }
        }
    }
}
