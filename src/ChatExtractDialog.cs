using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace DesktopMemo
{
    public class ChatExtractDialog : Window
    {
        private readonly TrayAndAppController _controller;
        private readonly string _initialNoteId;

        private Border _rootCard;
        private TextBox _chatInputBox;
        private Button _extractBtn;
        private TextBlock _statusText;
        private StackPanel _previewListPanel;
        private ScrollViewer _previewScroller;
        private ComboBox _destCombo;
        private Button _confirmBtn;

        private List<AiExtractedTask> _extractedTasks = new List<AiExtractedTask>();
        private readonly List<PreviewRowBindings> _rowBindings = new List<PreviewRowBindings>();

        private class PreviewRowBindings
        {
            public AiExtractedTask Item;
            public CheckBox SelectedBox;
            public TextBox TitleBox;
            public ComboBox QuadBox;
            public TextBox DdlBox;
            public TextBox RemBox;
        }

        public ChatExtractDialog(TrayAndAppController controller, string initialNoteId)
        {
            _controller = controller;
            _initialNoteId = initialNoteId ?? "";

            Title = "AI 微信聊天 / 文本任务提取";
            Width = 650;
            Height = 640;
            MinWidth = 520;
            MinHeight = 480;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = true;

            BuildUi();
            Loaded += delegate
            {
                SpringAnimationHelper.AnimatePopIn(_rootCard, 300);
                _chatInputBox.Focus();
            };
        }

        private void BuildUi()
        {
            ThemePalette pal = ThemeService.GetPalette(_controller.Config.PanelTheme, 0.97);
            UiScaleMetrics scale = ThemeService.GetScaleMetrics(_controller.Config.UiScale, _controller.Config.PanelFontSize);
            GlassControlStyles.ApplyToResources(Resources, pal, scale);

            _rootCard = new Border
            {
                Margin = new Thickness(12),
                CornerRadius = new CornerRadius(14),
                Background = pal.PopupBg,
                BorderBrush = pal.Border,
                BorderThickness = new Thickness(1),
                Effect = new DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 24,
                    ShadowDepth = 4,
                    Opacity = 0.24
                }
            };

            var mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Header
            var header = new Border
            {
                Background = pal.HeaderBg,
                BorderBrush = pal.Border,
                BorderThickness = new Thickness(0, 0, 0, 1),
                CornerRadius = new CornerRadius(14, 14, 0, 0),
                Padding = new Thickness(18, 12, 14, 12)
            };
            header.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                if (e.ButtonState == MouseButtonState.Pressed) { try { DragMove(); } catch { } }
            };
            var hDock = new DockPanel();
            var hTitle = new TextBlock
            {
                Text = "💬 AI 智能识别微信聊天 / 通知提取任务",
                FontFamily = ThemeService.AppFontFamily,
                FontSize = scale.HeaderFontSize,
                FontWeight = FontWeights.Bold,
                Foreground = pal.TextPrimary,
                VerticalAlignment = VerticalAlignment.Center
            };
            DockPanel.SetDock(hTitle, Dock.Left);
            hDock.Children.Add(hTitle);

            var closeBtn = GlassControlStyles.CreateGlassButton("✕", Brushes.Transparent, pal.TextSecondary, null, 13, new Thickness(8, 2, 8, 2), 7);
            closeBtn.HorizontalAlignment = HorizontalAlignment.Right;
            closeBtn.Click += delegate { DialogResult = false; Close(); };
            DockPanel.SetDock(closeBtn, Dock.Right);
            hDock.Children.Add(closeBtn);
            header.Child = hDock;
            Grid.SetRow(header, 0);
            mainGrid.Children.Add(header);

            // Input area
            var inputStack = new StackPanel { Margin = new Thickness(18, 12, 18, 8) };
            inputStack.Children.Add(new TextBlock
            {
                Text = "粘贴微信群消息、作业通知或工作分工（AI 将自动拆解任务、推算 DDL 与四象限，确认后再写入）：",
                FontFamily = ThemeService.AppFontFamily,
                FontSize = scale.SmallFontSize + 0.5,
                Foreground = pal.TextSecondary,
                Margin = new Thickness(0, 0, 0, 6)
            });

            _chatInputBox = new TextBox
            {
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                Height = 112,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(10, 8, 10, 8),
                FontSize = scale.FontSize
            };
            inputStack.Children.Add(_chatInputBox);

            var actionRow = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
            _extractBtn = GlassControlStyles.CreateGlassButton("✨ AI 解析提取任务", pal.AccentBg, pal.AccentFg, null,
                scale.FontSize, scale.ButtonPadding, scale.CornerRadius);
            _extractBtn.FontWeight = FontWeights.SemiBold;
            _extractBtn.Click += OnExtractClicked;
            DockPanel.SetDock(_extractBtn, Dock.Left);
            actionRow.Children.Add(_extractBtn);

            _statusText = new TextBlock
            {
                FontFamily = ThemeService.AppFontFamily,
                FontSize = scale.SmallFontSize + 0.5,
                Foreground = pal.TextSecondary,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 0, 0),
                TextWrapping = TextWrapping.Wrap
            };
            actionRow.Children.Add(_statusText);
            inputStack.Children.Add(actionRow);

            Grid.SetRow(inputStack, 1);
            mainGrid.Children.Add(inputStack);

            // Preview list area
            var previewBorder = new Border
            {
                Margin = new Thickness(18, 4, 18, 10),
                CornerRadius = new CornerRadius(10),
                Background = pal.SurfaceBg,
                BorderBrush = pal.Border,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8)
            };
            _previewScroller = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            _previewListPanel = new StackPanel();
            _previewListPanel.Children.Add(new TextBlock
            {
                Text = "在上方粘贴聊天记录并点击「✨ AI 解析提取任务」，提取结果会显示在这里供你勾选和微调。",
                FontFamily = ThemeService.AppFontFamily,
                FontSize = scale.SmallFontSize + 1,
                Foreground = pal.TextSecondary,
                Margin = new Thickness(8, 22, 8, 8),
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = HorizontalAlignment.Center
            });
            _previewScroller.Content = _previewListPanel;
            previewBorder.Child = _previewScroller;
            Grid.SetRow(previewBorder, 2);
            mainGrid.Children.Add(previewBorder);

            // Footer
            var footer = new DockPanel { Margin = new Thickness(18, 0, 18, 16) };
            var leftFooter = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            leftFooter.Children.Add(new TextBlock
            {
                Text = "添加到：",
                FontFamily = ThemeService.AppFontFamily,
                FontSize = scale.FontSize,
                Foreground = pal.TextSecondary,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            });

            _destCombo = new ComboBox { Width = 200 };
            _destCombo.Items.Add(new ComboBoxItem { Content = "全局四象限与日程列表", Tag = "" });
            int selIdx = 0;
            for (int i = 0; i < _controller.Data.StickyNotes.Count; i++)
            {
                StickyNote sn = _controller.Data.StickyNotes[i];
                if (sn.FilterMode == "custom")
                {
                    _destCombo.Items.Add(new ComboBoxItem { Content = "独立便签：" + sn.Title, Tag = sn.Id });
                    if (!string.IsNullOrEmpty(_initialNoteId) && sn.Id == _initialNoteId)
                    {
                        selIdx = _destCombo.Items.Count - 1;
                    }
                }
            }
            _destCombo.SelectedIndex = selIdx;
            leftFooter.Children.Add(_destCombo);
            DockPanel.SetDock(leftFooter, Dock.Left);
            footer.Children.Add(leftFooter);

            var rightBtns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var cancelBtn = GlassControlStyles.CreateGlassButton("取消", pal.SurfaceBg, pal.TextPrimary, pal.Border,
                scale.FontSize, scale.ButtonPadding, scale.CornerRadius);
            cancelBtn.Margin = new Thickness(0, 0, 8, 0);
            cancelBtn.Click += delegate { DialogResult = false; Close(); };

            _confirmBtn = GlassControlStyles.CreateGlassButton("✓ 确认加入选中任务", pal.AccentBg, pal.AccentFg, null,
                scale.FontSize, scale.ButtonPadding, scale.CornerRadius);
            _confirmBtn.IsEnabled = false;
            _confirmBtn.Click += OnConfirmClicked;

            rightBtns.Children.Add(cancelBtn);
            rightBtns.Children.Add(_confirmBtn);
            footer.Children.Add(rightBtns);
            Grid.SetRow(footer, 3);
            mainGrid.Children.Add(footer);

            _rootCard.Child = mainGrid;
            Content = _rootCard;
        }

        private async void OnExtractClicked(object sender, RoutedEventArgs e)
        {
            string rawText = (_chatInputBox.Text ?? "").Trim();
            if (string.IsNullOrEmpty(rawText))
            {
                _statusText.Text = "请先粘贴聊天内容或通知文本。";
                _statusText.Foreground = ThemeService.BrushFromHex("#DC2626");
                return;
            }

            _extractBtn.IsEnabled = false;
            _extractBtn.Content = "⏳ 正在解析任务...";
            _statusText.Text = "正在调用 " + _controller.Config.AiModel + " 提取 DDL 与四象限...";
            _statusText.Foreground = ThemeService.BrushFromHex("#6D28D9");

            try
            {
                AiChatExtractOutcome outcome = await AiSortService.ExtractTasksFromChatAsync(_controller.Config, rawText, DateTime.Now);
                if (!outcome.Success)
                {
                    _statusText.Text = "⚠ " + outcome.ErrorMessage;
                    _statusText.Foreground = ThemeService.BrushFromHex("#DC2626");
                    return;
                }

                _extractedTasks = outcome.Tasks;
                RenderPreviewRows();
                _confirmBtn.IsEnabled = _extractedTasks.Count > 0;
                _statusText.Text = string.Format("✓ 已识别 {0} 条任务，请勾选确认或微调后加入。", _extractedTasks.Count);
                _statusText.Foreground = ThemeService.BrushFromHex("#059669");
            }
            catch (Exception ex)
            {
                _statusText.Text = "⚠ 解析异常：" + ex.Message;
                _statusText.Foreground = ThemeService.BrushFromHex("#DC2626");
            }
            finally
            {
                _extractBtn.IsEnabled = true;
                _extractBtn.Content = "✨ AI 解析提取任务";
            }
        }

        private void RenderPreviewRows()
        {
            ThemePalette pal = ThemeService.GetPalette(_controller.Config.PanelTheme, 0.97);
            UiScaleMetrics scale = ThemeService.GetScaleMetrics(_controller.Config.UiScale, _controller.Config.PanelFontSize);

            _previewListPanel.Children.Clear();
            _rowBindings.Clear();

            for (int i = 0; i < _extractedTasks.Count; i++)
            {
                AiExtractedTask item = _extractedTasks[i];

                var card = new Border
                {
                    Background = pal.CardBg,
                    BorderBrush = pal.Border,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(9),
                    Padding = new Thickness(10, 8, 10, 8),
                    Margin = new Thickness(0, 0, 0, 8)
                };

                var stack = new StackPanel();

                var topRow = new Grid();
                topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var cb = new CheckBox
                {
                    IsChecked = item.Selected,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 0)
                };
                Grid.SetColumn(cb, 0);
                topRow.Children.Add(cb);

                var titleBox = new TextBox
                {
                    Text = item.Title,
                    FontWeight = FontWeights.SemiBold,
                    Padding = new Thickness(7, 4, 7, 4)
                };
                Grid.SetColumn(titleBox, 1);
                topRow.Children.Add(titleBox);

                var quadBox = new ComboBox { Width = 126, Margin = new Thickness(8, 0, 0, 0) };
                quadBox.Items.Add(new ComboBoxItem { Content = "未分类", Tag = 0 });
                quadBox.Items.Add(new ComboBoxItem { Content = "Q1 重要紧急", Tag = 1 });
                quadBox.Items.Add(new ComboBoxItem { Content = "Q2 重要不紧急", Tag = 2 });
                quadBox.Items.Add(new ComboBoxItem { Content = "Q3 紧急不重要", Tag = 3 });
                quadBox.Items.Add(new ComboBoxItem { Content = "Q4 不重要不紧急", Tag = 4 });
                quadBox.SelectedIndex = MemoTask.NormalizeQuadrant(item.Quadrant);
                Grid.SetColumn(quadBox, 2);
                topRow.Children.Add(quadBox);
                stack.Children.Add(topRow);

                var metaRow = new WrapPanel { Margin = new Thickness(25, 6, 0, 0) };
                metaRow.Children.Add(new TextBlock
                {
                    Text = "DDL：",
                    FontFamily = ThemeService.AppFontFamily,
                    FontSize = scale.SmallFontSize,
                    Foreground = pal.TextSecondary,
                    VerticalAlignment = VerticalAlignment.Center
                });
                var ddlBox = new TextBox
                {
                    Text = item.DeadlineIso ?? "",
                    Width = 140,
                    FontSize = scale.SmallFontSize + 0.5,
                    Padding = new Thickness(6, 3, 6, 3),
                    Margin = new Thickness(0, 0, 10, 2),
                    ToolTip = "格式 yyyy-MM-dd HH:mm，留空表示无DDL"
                };
                metaRow.Children.Add(ddlBox);

                metaRow.Children.Add(new TextBlock
                {
                    Text = "提醒：",
                    FontFamily = ThemeService.AppFontFamily,
                    FontSize = scale.SmallFontSize,
                    Foreground = pal.TextSecondary,
                    VerticalAlignment = VerticalAlignment.Center
                });
                var remBox = new TextBox
                {
                    Text = item.RemindAtIso ?? "",
                    Width = 140,
                    FontSize = scale.SmallFontSize + 0.5,
                    Padding = new Thickness(6, 3, 6, 3),
                    Margin = new Thickness(0, 0, 8, 2),
                    ToolTip = "格式 yyyy-MM-dd HH:mm，留空不提醒"
                };
                metaRow.Children.Add(remBox);

                if (!string.IsNullOrWhiteSpace(item.Reason))
                {
                    metaRow.Children.Add(new Border
                    {
                        Background = pal.HoverBg,
                        CornerRadius = new CornerRadius(5),
                        Padding = new Thickness(6, 2, 6, 2),
                        VerticalAlignment = VerticalAlignment.Center,
                        Child = new TextBlock
                        {
                            Text = "AI: " + item.Reason,
                            FontFamily = ThemeService.AppFontFamily,
                            FontSize = scale.SmallFontSize,
                            Foreground = pal.TextSecondary
                        }
                    });
                }
                stack.Children.Add(metaRow);

                if (!string.IsNullOrWhiteSpace(item.Notes))
                {
                    stack.Children.Add(new TextBlock
                    {
                        Text = "备注：" + item.Notes,
                        FontFamily = ThemeService.AppFontFamily,
                        FontSize = scale.SmallFontSize,
                        Foreground = pal.TextSecondary,
                        Margin = new Thickness(25, 4, 0, 0),
                        TextWrapping = TextWrapping.Wrap
                    });
                }

                card.Child = stack;
                _previewListPanel.Children.Add(card);
                SpringAnimationHelper.AnimatePopIn(card, 240 + Math.Min(160, i * 45));

                _rowBindings.Add(new PreviewRowBindings
                {
                    Item = item,
                    SelectedBox = cb,
                    TitleBox = titleBox,
                    QuadBox = quadBox,
                    DdlBox = ddlBox,
                    RemBox = remBox
                });
            }
        }

        private void OnConfirmClicked(object sender, RoutedEventArgs e)
        {
            var selDest = _destCombo.SelectedItem as ComboBoxItem;
            string noteId = selDest != null && selDest.Tag != null ? selDest.Tag.ToString() : "";

            var toAdd = new List<MemoTask>();
            for (int i = 0; i < _rowBindings.Count; i++)
            {
                PreviewRowBindings row = _rowBindings[i];
                if (row.SelectedBox.IsChecked != true) continue;

                string title = (row.TitleBox.Text ?? "").Trim();
                if (string.IsNullOrEmpty(title)) continue;

                int q = 0;
                var qSel = row.QuadBox.SelectedItem as ComboBoxItem;
                if (qSel != null && qSel.Tag != null) int.TryParse(qSel.Tag.ToString(), out q);

                DateTime? ddl = MemoTask.ParseIsoDateTime(row.DdlBox.Text);
                DateTime? rem = MemoTask.ParseIsoDateTime(row.RemBox.Text);

                var task = new MemoTask
                {
                    Title = title,
                    Notes = row.Item.Notes ?? "",
                    Quadrant = MemoTask.NormalizeQuadrant(q),
                    DeadlineIso = MemoTask.FormatIso(ddl),
                    RemindAtIso = MemoTask.FormatIso(rem),
                    AiReason = row.Item.Reason ?? "",
                    NoteId = noteId
                };
                toAdd.Add(task);
            }

            if (toAdd.Count == 0)
            {
                MessageBox.Show("请至少勾选一条有效任务。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _controller.AddTasksBatch(toAdd);
            DialogResult = true;
            Close();
        }
    }
}
