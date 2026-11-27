using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace DesktopMemo
{
    public class StickyNoteWindow : Window
    {
        private StickyNote _note;
        private readonly TrayAndAppController _controller;

        private bool _isInitializing = true;
        private bool _isAdjustingZ = false;
        private bool _suppressTextSync = false;
        private string _appliedThemeKey;
        private string _appliedControlStyleKey;
        private string _appliedPinMode;
        private bool _isImeComposing = false;
        private int _timetableDayOffset = 0;

        public bool IsImeComposing { get { return _isImeComposing; } }

        private Border _outerCard;
        private Border _headerBar;
        private TextBox _titleEditBox;
        private Border _modeBadgeBorder;
        private TextBlock _modeBadge;
        private Button _chatExtractBtn;
        private Button _pinBtn;
        private Button _lockBtn;
        private Button _settingsBtn;
        private Button _hideBtn;
        private Popup _settingsPopup;
        private Grid _bodyGrid;
        private TextBox _scratchpadBox;
        private Border _scratchpadContainer;
        private TextBlock _scratchpadPlaceholder;
        private StackPanel _tasksPanel;
        private TextBox _quickTaskBox;
        private ComboBox _quickDdlBox;
        private Button _quickAddBtn;
        private TextBlock _emptyHint;

        public string NoteId { get { return _note.Id; } }

        public StickyNoteWindow(StickyNote note, TrayAndAppController controller)
        {
            _note = note;
            _controller = controller;

            Title = string.IsNullOrWhiteSpace(note.Title) ? "桌面便签" : note.Title;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.CanResize;
            ShowInTaskbar = false;
            MinWidth = 240;
            MinHeight = 180;

            double left = note.Left;
            double top = note.Top;
            double width = note.Width >= 240 ? note.Width : 320;
            double height = note.Height >= 180 ? note.Height : 360;
            Win32Native.ClampWindowToScreens(ref left, ref top, width, height);

            Left = left;
            Top = top;
            Width = width;
            Height = height;

            BuildUi();
            ApplyThemeAndContent();

            SourceInitialized += OnSourceInitialized;
            LocationChanged += OnGeometryChanged;
            SizeChanged += OnGeometryChanged;
            PreviewMouseWheel += OnPreviewMouseWheelZoom;
            TextCompositionManager.AddPreviewTextInputStartHandler(this, delegate { _isImeComposing = true; });
            TextCompositionManager.AddPreviewTextInputUpdateHandler(this, delegate(object s, TextCompositionEventArgs e)
            {
                _isImeComposing = (e.TextComposition != null && !string.IsNullOrEmpty(e.TextComposition.CompositionText));
            });
            TextCompositionManager.AddPreviewTextInputHandler(this, delegate { _isImeComposing = false; });
            Deactivated += delegate { _isImeComposing = false; };
            PreviewKeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.Key == Key.Escape) _isImeComposing = false;
            };
            _isInitializing = false;
        }

        private void OnSourceInitialized(object sender, EventArgs e)
        {
            HwndSource src = PresentationSource.FromVisual(this) as HwndSource;
            if (src != null)
            {
                src.AddHook(WndProcHook);
            }
            _appliedPinMode = null;
            ApplyPinModeNow();
        }

        private IntPtr WndProcHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            return Win32Native.HandleDesktopAndResizeMessages(
                this, hwnd, msg, wParam, lParam,
                _note.PinMode, _note.Locked, false, _isAdjustingZ, 8.0, ref handled);
        }

        public void RebindNote(StickyNote note)
        {
            if (note != null) _note = note;
        }

        public void ApplyPinModeNow()
        {
            Win32Native.ApplyPinMode(this, _note.PinMode, ref _isAdjustingZ);
            _appliedPinMode = _note.PinMode;
            UpdatePinAndLockButtons();
        }

        public void EnsurePinModeApplied()
        {
            if (!string.Equals(_appliedPinMode, _note.PinMode, StringComparison.Ordinal))
            {
                ApplyPinModeNow();
            }
            else
            {
                UpdatePinAndLockButtons();
            }
        }

        private void OnGeometryChanged(object sender, EventArgs e)
        {
            if (_isInitializing) return;
            _note.Left = Left;
            _note.Top = Top;
            if (ActualWidth >= 240 && ActualHeight >= 160)
            {
                _note.Width = Width;
                _note.Height = Height;
            }
            _controller.ScheduleSave();
        }

        private void OnPreviewMouseWheelZoom(object sender, MouseWheelEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                double delta = e.Delta > 0 ? 1 : -1;
                double next = Math.Max(10, Math.Min(22, _note.FontSize + delta));
                if (Math.Abs(next - _note.FontSize) > 0.1)
                {
                    _note.FontSize = next;
                    ApplyThemeAndContent();
                    _controller.ScheduleSave();
                }
                e.Handled = true;
            }
        }

        private void BuildUi()
        {
            _outerCard = new Border
            {
                Margin = new Thickness(0),
                CornerRadius = new CornerRadius(13),
                BorderThickness = new Thickness(1),
                Effect = new DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 15,
                    ShadowDepth = 2,
                    Opacity = 0.14
                }
            };

            var rootDock = new DockPanel();

            _headerBar = new Border
            {
                CornerRadius = new CornerRadius(13, 13, 0, 0),
                Padding = new Thickness(12, 8, 8, 8),
                BorderThickness = new Thickness(0, 0, 0, 1)
            };
            _headerBar.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                if (e.Source == _titleEditBox) return;
                if (e.ButtonState == MouseButtonState.Pressed && !_note.Locked)
                {
                    try { DragMove(); } catch { }
                }
            };

            var headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _titleEditBox = new TextBox
            {
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center,
                Padding = new Thickness(2, 1, 4, 1)
            };
            _titleEditBox.TextChanged += delegate
            {
                if (_suppressTextSync) return;
                _note.Title = _titleEditBox.Text;
                Title = string.IsNullOrWhiteSpace(_note.Title) ? "桌面便签" : _note.Title;
                _controller.ScheduleSave();
            };
            Grid.SetColumn(_titleEditBox, 0);
            headerGrid.Children.Add(_titleEditBox);

            _modeBadgeBorder = new Border
            {
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(6, 2, 6, 2),
                Margin = new Thickness(4, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            _modeBadge = new TextBlock
            {
                FontFamily = ThemeService.AppFontFamily,
                FontSize = 10.5,
                FontWeight = FontWeights.SemiBold
            };
            _modeBadgeBorder.Child = _modeBadge;
            Grid.SetColumn(_modeBadgeBorder, 1);
            headerGrid.Children.Add(_modeBadgeBorder);

            var btnRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

            _chatExtractBtn = MakeHeaderButton("💬", "AI 识别微信聊天 / 通知文本并提取任务");
            _chatExtractBtn.Click += delegate
            {
                string noteTarget = _note.FilterMode == "custom" ? _note.Id : "";
                _controller.OpenChatExtractDialog(noteTarget, this);
            };

            _pinBtn = MakeHeaderButton("📌", "切换：贴在桌面 / 置顶悬浮 / 普通窗口");
            _pinBtn.Click += delegate
            {
                _note.PinMode = Win32Native.CyclePinMode(_note.PinMode);
                ApplyPinModeNow();
                _controller.ScheduleSave();
            };

            _lockBtn = MakeHeaderButton("🔓", "锁定/解锁位置与大小");
            _lockBtn.Click += delegate
            {
                _note.Locked = !_note.Locked;
                UpdatePinAndLockButtons();
                _controller.ScheduleSave();
            };

            _settingsBtn = MakeHeaderButton("⚙", "配色、透明度、显示随手记区与设置");
            _settingsBtn.Click += delegate { OpenSettingsPopup(); };

            _hideBtn = MakeHeaderButton("—", "隐藏此便签（可在主面板「独立便利贴」或系统托盘重新显示）");
            _hideBtn.Click += delegate { _controller.SetStickyNoteVisible(_note.Id, false); };

            btnRow.Children.Add(_chatExtractBtn);
            btnRow.Children.Add(_pinBtn);
            btnRow.Children.Add(_lockBtn);
            btnRow.Children.Add(_settingsBtn);
            btnRow.Children.Add(_hideBtn);
            Grid.SetColumn(btnRow, 2);
            headerGrid.Children.Add(btnRow);

            _headerBar.Child = headerGrid;
            DockPanel.SetDock(_headerBar, Dock.Top);
            rootDock.Children.Add(_headerBar);

            _bodyGrid = new Grid { Margin = new Thickness(11, 9, 11, 10) };
            _bodyGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // optional scratchpad
            _bodyGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // task checklist
            _bodyGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // quick add bar

            // Optional flat scratchpad
            _scratchpadContainer = new Border
            {
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(4, 2, 4, 6),
                Margin = new Thickness(0, 0, 0, 8)
            };
            var scratchGrid = new Grid();
            _scratchpadPlaceholder = new TextBlock
            {
                Text = "随手记文本区（可在 ⚙ 中随时关闭）...",
                FontFamily = ThemeService.AppFontFamily,
                Margin = new Thickness(4, 3, 0, 0),
                IsHitTestVisible = false,
                Opacity = 0.6
            };
            _scratchpadBox = new TextBox
            {
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                MinHeight = 52,
                MaxHeight = 130,
                Padding = new Thickness(2),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            _scratchpadBox.TextChanged += delegate
            {
                _scratchpadPlaceholder.Visibility = string.IsNullOrEmpty(_scratchpadBox.Text) ? Visibility.Visible : Visibility.Collapsed;
                if (_suppressTextSync) return;
                _note.Content = _scratchpadBox.Text;
                _controller.ScheduleSave();
            };
            scratchGrid.Children.Add(_scratchpadPlaceholder);
            scratchGrid.Children.Add(_scratchpadBox);
            _scratchpadContainer.Child = scratchGrid;
            Grid.SetRow(_scratchpadContainer, 0);
            _bodyGrid.Children.Add(_scratchpadContainer);

            // Checklist
            var scroller = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            var listHost = new StackPanel();
            _tasksPanel = new StackPanel();
            _emptyHint = new TextBlock
            {
                Text = "暂无待办，在下方输入按 Enter 添加，或点右上角 💬 用 AI 提取聊天任务",
                FontFamily = ThemeService.AppFontFamily,
                Margin = new Thickness(4, 12, 4, 8),
                TextWrapping = TextWrapping.Wrap
            };
            listHost.Children.Add(_tasksPanel);
            listHost.Children.Add(_emptyHint);
            scroller.Content = listHost;
            Grid.SetRow(scroller, 1);
            _bodyGrid.Children.Add(scroller);

            // Bottom quick add
            var quickRow = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            quickRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            quickRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            quickRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _quickTaskBox = new TextBox
            {
                Padding = new Thickness(8, 5, 8, 5),
                ToolTip = "输入待办内容后按 Enter 添加"
            };
            _quickTaskBox.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.Key == Key.Enter)
                {
                    AddQuickTaskFromNote();
                    e.Handled = true;
                }
            };
            Grid.SetColumn(_quickTaskBox, 0);
            quickRow.Children.Add(_quickTaskBox);

            _quickDdlBox = new ComboBox
            {
                Width = 74,
                Margin = new Thickness(5, 0, 5, 0),
                ToolTip = "快速设置截止时间"
            };
            _quickDdlBox.Items.Add("无DDL");
            _quickDdlBox.Items.Add("1小时");
            _quickDdlBox.Items.Add("今晚");
            _quickDdlBox.Items.Add("明天");
            _quickDdlBox.SelectedIndex = 0;
            Grid.SetColumn(_quickDdlBox, 1);
            quickRow.Children.Add(_quickDdlBox);

            _quickAddBtn = GlassControlStyles.CreateGlassButton("+",
                ThemeService.BrushFromHex("#2563EB"), Brushes.White, null, 13, new Thickness(10, 3, 10, 3), 8);
            _quickAddBtn.ToolTip = "添加到列表";
            _quickAddBtn.Click += delegate { AddQuickTaskFromNote(); };
            Grid.SetColumn(_quickAddBtn, 2);
            quickRow.Children.Add(_quickAddBtn);

            Grid.SetRow(quickRow, 2);
            _bodyGrid.Children.Add(quickRow);

            rootDock.Children.Add(_bodyGrid);
            _outerCard.Child = rootDock;
            Content = new Border
            {
                Margin = new Thickness(5),
                Padding = new Thickness(3),
                CornerRadius = new CornerRadius(15),
                Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)),
                Child = _outerCard
            };
        }

        private void AddQuickTaskFromNote()
        {
            string text = (_quickTaskBox.Text ?? "").Trim();
            if (string.IsNullOrEmpty(text)) return;

            var task = new MemoTask();
            task.Title = text;
            task.Quadrant = ResolveDefaultQuadrant(); // 0 (unclassified) for custom sticky note!
            task.NoteId = _note.FilterMode == "custom" ? _note.Id : "";

            int ddlIdx = _quickDdlBox.SelectedIndex;
            if (ddlIdx == 1) task.DeadlineIso = MemoTask.FormatIso(DateTime.Now.AddHours(1));
            else if (ddlIdx == 2) task.DeadlineIso = MemoTask.FormatIso(DateTime.Today.AddHours(23).AddMinutes(59));
            else if (ddlIdx == 3) task.DeadlineIso = MemoTask.FormatIso(DateTime.Today.AddDays(1).AddHours(23).AddMinutes(59));

            _quickTaskBox.Text = "";
            _quickDdlBox.SelectedIndex = 0;
            _controller.AddTask(task);
            _quickTaskBox.Focus();
        }

        private int ResolveDefaultQuadrant()
        {
            switch (_note.FilterMode)
            {
                case "q1": return 1;
                case "q2": return 2;
                case "q3": return 3;
                case "q4": return 4;
                default: return 0; // Bug 4 fix: custom/all stays Unclassified until AI or user sets it!
            }
        }

        private void UpdatePinAndLockButtons()
        {
            _pinBtn.Content = _note.PinMode == "topmost" ? "📍" : (_note.PinMode == "normal" ? "🗔" : "📌");
            _pinBtn.ToolTip = "当前：" + Win32Native.GetPinModeLabel(_note.PinMode) + "（点击切换）";
            _lockBtn.Content = _note.Locked ? "🔒" : "🔓";
            _lockBtn.ToolTip = _note.Locked ? "已锁定位置与大小（点击解锁）" : "未锁定（点击锁定防止误拖）";
        }

        public void ApplyThemeAndContent()
        {
            ThemePalette pal = ThemeService.GetPalette(_note.Theme, _note.Opacity);
            double effectiveFont = _note.FontSize;
            UiScaleMetrics scale = ThemeService.GetScaleMetrics(_controller.Config.UiScale, effectiveFont);

            string themeKey = ThemeService.NormalizeThemeKey(_note.Theme);
            string controlStyleKey = themeKey + "|"
                + (_controller.Config.UiScale ?? "standard") + "|"
                + Math.Round(effectiveFont, 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (!_isImeComposing)
            {
                bool themeChanged = !string.Equals(_appliedThemeKey, themeKey, StringComparison.Ordinal);
                bool controlStyleChanged = !string.Equals(_appliedControlStyleKey, controlStyleKey, StringComparison.Ordinal);
                if (themeChanged || controlStyleChanged)
                {
                    GlassControlStyles.ApplyToResources(Resources, pal, scale, themeChanged);
                    _appliedThemeKey = themeKey;
                    _appliedControlStyleKey = controlStyleKey;
                }
            }

            _outerCard.Background = pal.CardBg;
            _outerCard.BorderBrush = pal.Border;
            _headerBar.Background = pal.HeaderBg;
            _headerBar.BorderBrush = pal.Border;

            _suppressTextSync = true;
            if (!_titleEditBox.IsKeyboardFocused)
            {
                _titleEditBox.Text = _note.Title ?? "";
            }
            _titleEditBox.Foreground = pal.TextPrimary;
            _titleEditBox.FontSize = scale.FontSize + 0.5;
            if (!_scratchpadBox.IsKeyboardFocused)
            {
                _scratchpadBox.Text = _note.Content ?? "";
            }
            _scratchpadBox.Foreground = pal.TextPrimary;
            _scratchpadBox.FontSize = scale.FontSize;
            _scratchpadPlaceholder.FontSize = scale.SmallFontSize + 0.5;
            _scratchpadPlaceholder.Foreground = pal.TextSecondary;
            _scratchpadPlaceholder.Visibility = string.IsNullOrEmpty(_scratchpadBox.Text) ? Visibility.Visible : Visibility.Collapsed;
            _suppressTextSync = false;

            _scratchpadContainer.Background = Brushes.Transparent;
            _scratchpadContainer.BorderBrush = pal.Border;
            // Feature 4: User controls whether scratchpad appears!
            _scratchpadContainer.Visibility = (_note.FilterMode == "custom" && _note.ShowScratchpad)
                ? Visibility.Visible
                : Visibility.Collapsed;

            if (_note.FilterMode == "timetable")
            {
                int curW = _controller.Data != null && _controller.Data.Timetable != null
                    ? _controller.Data.Timetable.GetWeekNumberForDate(DateTime.Now)
                    : 0;
                _modeBadge.Text = curW >= 1 ? string.Format("第{0}周课表", curW) : "今日课表";
            }
            else
            {
                _modeBadge.Text = StickyNote.GetFilterModeLabel(_note.FilterMode);
            }
            _modeBadge.Foreground = pal.AccentFg;
            _modeBadgeBorder.Background = pal.AccentBg;

            _quickTaskBox.FontSize = scale.FontSize;
            _quickAddBtn.Background = pal.AccentBg;
            _quickAddBtn.Foreground = pal.AccentFg;
            _quickAddBtn.Padding = scale.SmallButtonPadding;

            _emptyHint.FontSize = scale.SmallFontSize + 0.5;
            _emptyHint.Foreground = pal.TextSecondary;

            _chatExtractBtn.Foreground = pal.TextPrimary;
            _pinBtn.Foreground = pal.TextPrimary;
            _lockBtn.Foreground = pal.TextPrimary;
            _settingsBtn.Foreground = pal.TextPrimary;
            if (_hideBtn != null) _hideBtn.Foreground = pal.TextPrimary;

            UpdatePinAndLockButtons();
            RefreshTasksOnly();
        }

        public void RefreshTasksOnly()
        {
            ThemePalette pal = ThemeService.GetPalette(_note.Theme, _note.Opacity);
            UiScaleMetrics scale = ThemeService.GetScaleMetrics(_controller.Config.UiScale, _note.FontSize);
            _tasksPanel.Children.Clear();

            DateTime now = DateTime.Now;
            int courseCount = 0;
            if (_note.FilterMode == "timetable")
            {
                courseCount = RenderTimetableCoursesInSticky(pal, scale, now);
            }

            List<MemoTask> list = GetFilteredSortedTasks();
            _emptyHint.Visibility = (list.Count == 0 && courseCount == 0 && _note.FilterMode != "timetable")
                ? Visibility.Visible
                : Visibility.Collapsed;

            for (int i = 0; i < list.Count; i++)
            {
                MemoTask task = list[i];
                _tasksPanel.Children.Add(BuildTaskRow(task, pal, scale, now));
            }
        }

        private int RenderTimetableCoursesInSticky(ThemePalette pal, UiScaleMetrics scale, DateTime now)
        {
            TimetableData tt = _controller.Data != null ? _controller.Data.Timetable : null;
            DateTime targetDate = now.Date.AddDays(_timetableDayOffset);
            int weekNum = tt != null ? tt.GetWeekNumberForDate(targetDate) : 0;
            int isoDay = TimetableData.ToIsoWeekday(targetDate.DayOfWeek);

            var topBar = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            var infoLbl = new TextBlock
            {
                Text = weekNum >= 1
                    ? string.Format("第{0}周 · {1} ({2})", weekNum, CourseItem.GetWeekdayName(isoDay), targetDate.ToString("MM/dd"))
                    : string.Format("{0} ({1})", CourseItem.GetWeekdayName(isoDay), targetDate.ToString("MM/dd")),
                FontFamily = ThemeService.AppFontFamily,
                FontSize = scale.SmallFontSize + 0.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = pal.TextPrimary,
                VerticalAlignment = VerticalAlignment.Center
            };
            DockPanel.SetDock(infoLbl, Dock.Left);
            topBar.Children.Add(infoLbl);

            var switchRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var todayBtn = GlassControlStyles.CreateGlassButton("今",
                _timetableDayOffset == 0 ? pal.AccentBg : pal.SurfaceBg,
                _timetableDayOffset == 0 ? pal.AccentFg : pal.TextSecondary,
                _timetableDayOffset == 0 ? null : pal.Border,
                scale.SmallFontSize, new Thickness(6, 1, 6, 1), 5);
            todayBtn.Margin = new Thickness(0, 0, 3, 0);
            todayBtn.Click += delegate { _timetableDayOffset = 0; RefreshTasksOnly(); };

            var tmrBtn = GlassControlStyles.CreateGlassButton("明",
                _timetableDayOffset == 1 ? pal.AccentBg : pal.SurfaceBg,
                _timetableDayOffset == 1 ? pal.AccentFg : pal.TextSecondary,
                _timetableDayOffset == 1 ? null : pal.Border,
                scale.SmallFontSize, new Thickness(6, 1, 6, 1), 5);
            tmrBtn.Click += delegate { _timetableDayOffset = 1; RefreshTasksOnly(); };

            switchRow.Children.Add(todayBtn);
            switchRow.Children.Add(tmrBtn);
            topBar.Children.Add(switchRow);
            _tasksPanel.Children.Add(topBar);

            List<CourseItem> courses = tt != null ? tt.GetCoursesForDate(targetDate) : new List<CourseItem>();
            if (courses.Count == 0)
            {
                _tasksPanel.Children.Add(new Border
                {
                    Background = pal.SurfaceBg,
                    BorderBrush = pal.Border,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(scale.CornerRadius),
                    Padding = scale.CardPadding,
                    Margin = new Thickness(0, 0, 0, 5),
                    Child = new TextBlock
                    {
                        Text = tt == null || tt.Courses.Count == 0
                            ? "尚未导入课表，请在设置中接入 MIS 课程表。"
                            : (_timetableDayOffset == 0 ? "今天没有排课 🎉" : "明天没有排课 🎉"),
                        FontFamily = ThemeService.AppFontFamily,
                        FontSize = scale.SmallFontSize + 0.5,
                        Foreground = pal.TextSecondary
                    }
                });
                return 0;
            }

            string nowHm = now.ToString("HH:mm");
            for (int i = 0; i < courses.Count; i++)
            {
                CourseItem c = courses[i];
                string st = tt != null ? tt.GetCourseStartTime(c) : c.GetEffectiveStartTime();
                string et = tt != null ? tt.GetCourseEndTime(c) : c.GetEffectiveEndTime();
                string stateTag = "";
                string stateBg = "";
                string stateFg = "";
                if (_timetableDayOffset == 0)
                {
                    if (string.Compare(nowHm, st, StringComparison.Ordinal) >= 0 && string.Compare(nowHm, et, StringComparison.Ordinal) <= 0)
                    {
                        stateTag = "上课中";
                        stateBg = "#DCFCE7";
                        stateFg = "#15803D";
                    }
                    else if (string.Compare(nowHm, et, StringComparison.Ordinal) > 0)
                    {
                        stateTag = "已结束";
                        stateBg = pal.IsDark ? "#252A36" : "#F1F5F9";
                        stateFg = pal.TextSecondaryHex;
                    }
                }

                var cCard = new Border
                {
                    Background = pal.SurfaceBg,
                    BorderBrush = pal.Border,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(scale.CornerRadius),
                    Padding = scale.CardPadding,
                    Margin = new Thickness(0, 0, 0, 5)
                };

                var dock = new DockPanel();
                var stripe = new Border
                {
                    Width = 4,
                    CornerRadius = new CornerRadius(2),
                    Background = ThemeService.BrushFromHex(string.IsNullOrEmpty(c.ColorHex) ? "#3B82F6" : c.ColorHex),
                    Margin = new Thickness(0, 1, 8, 1)
                };
                DockPanel.SetDock(stripe, Dock.Left);
                dock.Children.Add(stripe);

                CourseItem capCourse = c;
                string capEt = et;
                var addCourseTaskBtn = GlassControlStyles.CreateGlassButton("+",
                    pal.HoverBg, pal.TextSecondary, null, 12, new Thickness(5, 1, 5, 1), 5);
                addCourseTaskBtn.ToolTip = "为此课程新建待办/作业DDL";
                addCourseTaskBtn.VerticalAlignment = VerticalAlignment.Center;
                addCourseTaskBtn.Click += delegate
                {
                    var draft = new MemoTask
                    {
                        Title = capCourse.Name + " ",
                        Notes = string.IsNullOrWhiteSpace(capCourse.Location) ? "" : ("教室：" + capCourse.Location),
                        Quadrant = 2,
                        DeadlineIso = targetDate.ToString("yyyy-MM-dd") + " " + capEt
                    };
                    _controller.OpenNewTaskDialog(draft, this);
                };
                DockPanel.SetDock(addCourseTaskBtn, Dock.Right);
                dock.Children.Add(addCourseTaskBtn);

                var cInfo = new StackPanel();
                var topLine = new WrapPanel();
                topLine.Children.Add(new TextBlock
                {
                    Text = st + "-" + et,
                    FontFamily = ThemeService.AppFontFamily,
                    FontSize = scale.SmallFontSize,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = pal.AccentBg,
                    Margin = new Thickness(0, 0, 6, 0),
                    VerticalAlignment = VerticalAlignment.Center
                });
                topLine.Children.Add(new TextBlock
                {
                    Text = c.Name,
                    FontFamily = ThemeService.AppFontFamily,
                    FontSize = scale.FontSize,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = pal.TextPrimary,
                    VerticalAlignment = VerticalAlignment.Center
                });
                if (!string.IsNullOrEmpty(stateTag))
                {
                    topLine.Children.Add(new Border
                    {
                        Background = ThemeService.BrushFromHex(stateBg),
                        CornerRadius = new CornerRadius(4),
                        Padding = new Thickness(4, 0, 4, 0),
                        Margin = new Thickness(6, 0, 0, 0),
                        Child = new TextBlock
                        {
                            Text = stateTag,
                            FontFamily = ThemeService.AppFontFamily,
                            FontSize = Math.Max(9.5, scale.SmallFontSize - 1),
                            Foreground = ThemeService.BrushFromHex(stateFg)
                        }
                    });
                }
                cInfo.Children.Add(topLine);

                string metaStr = c.GetSectionLabel();
                if (!string.IsNullOrWhiteSpace(c.Location)) metaStr += " · " + c.Location;
                if (!string.IsNullOrWhiteSpace(c.Teacher)) metaStr += " · " + c.Teacher;
                cInfo.Children.Add(new TextBlock
                {
                    Text = metaStr,
                    FontFamily = ThemeService.AppFontFamily,
                    FontSize = scale.SmallFontSize,
                    Foreground = pal.TextSecondary,
                    Margin = new Thickness(0, 2, 0, 0)
                });

                dock.Children.Add(cInfo);
                cCard.Child = dock;
                _tasksPanel.Children.Add(cCard);
            }
            return courses.Count;
        }

        private List<MemoTask> GetFilteredSortedTasks()
        {
            AppData data = _controller.Data;
            IEnumerable<MemoTask> query = data.Tasks;

            switch (_note.FilterMode)
            {
                case "all":
                    query = query.Where(t => !t.Completed);
                    break;
                case "q1":
                    query = query.Where(t => !t.Completed && t.Quadrant == 1);
                    break;
                case "q2":
                    query = query.Where(t => !t.Completed && t.Quadrant == 2);
                    break;
                case "q3":
                    query = query.Where(t => !t.Completed && t.Quadrant == 3);
                    break;
                case "q4":
                    query = query.Where(t => !t.Completed && t.Quadrant == 4);
                    break;
                default:
                    query = query.Where(t => t.NoteId == _note.Id);
                    break;
            }

            return _controller.SortTaskList(query.ToList());
        }

        private UIElement BuildTaskRow(MemoTask task, ThemePalette pal, UiScaleMetrics scale, DateTime now)
        {
            QuadrantVisual qv = ThemeService.GetQuadrantVisual(task.Quadrant, pal.IsDark);
            DeadlineDisplayInfo ddl = task.GetDeadlineDisplay(now);

            var rowCard = new Border
            {
                Background = pal.SurfaceBg,
                BorderBrush = pal.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(scale.CornerRadius),
                Padding = scale.CardPadding,
                Margin = new Thickness(0, 0, 0, 5)
            };

            var rowGrid = new Grid();
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var cb = new CheckBox
            {
                IsChecked = task.Completed,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 2, 7, 0)
            };
            cb.Click += delegate
            {
                _controller.ToggleTaskCompleted(task.Id, cb.IsChecked == true);
            };
            Grid.SetColumn(cb, 0);
            rowGrid.Children.Add(cb);

            var textStack = new StackPanel();
            var titleLine = new DockPanel();
            var qDot = new Border
            {
                Width = 8,
                Height = 8,
                CornerRadius = new CornerRadius(4),
                Background = ThemeService.BrushFromHex(qv.AccentHex),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0),
                ToolTip = qv.Title
            };
            DockPanel.SetDock(qDot, Dock.Left);
            titleLine.Children.Add(qDot);

            var titleTb = new TextBlock
            {
                Text = task.Title,
                FontFamily = ThemeService.AppFontFamily,
                FontSize = scale.FontSize,
                Foreground = task.Completed ? pal.TextSecondary : pal.TextPrimary,
                TextWrapping = TextWrapping.Wrap,
                TextDecorations = task.Completed ? TextDecorations.Strikethrough : null
            };
            titleLine.Children.Add(titleTb);
            textStack.Children.Add(titleLine);

            if (!string.IsNullOrEmpty(ddl.Text) || !string.IsNullOrEmpty(task.RemindAtIso) || task.AiScore > 0 || task.Quadrant == 0 || (task.AttachmentFiles != null && task.AttachmentFiles.Count > 0))
            {
                var metaWrap = new WrapPanel { Margin = new Thickness(14, 3, 0, 0) };
                if (task.Quadrant == 0)
                {
                    metaWrap.Children.Add(new Border
                    {
                        Background = ThemeService.BrushFromHex(qv.BadgeBgHex),
                        CornerRadius = new CornerRadius(4),
                        Padding = new Thickness(5, 1, 5, 1),
                        Margin = new Thickness(0, 0, 4, 2),
                        Child = new TextBlock
                        {
                            Text = "未分类",
                            FontSize = scale.SmallFontSize - 0.5,
                            FontFamily = ThemeService.AppFontFamily,
                            Foreground = ThemeService.BrushFromHex(qv.BadgeFgHex)
                        }
                    });
                }
                if (!string.IsNullOrEmpty(ddl.Text))
                {
                    metaWrap.Children.Add(new Border
                    {
                        Background = ThemeService.BrushFromHex(ddl.BgHex),
                        CornerRadius = new CornerRadius(4),
                        Padding = new Thickness(5, 1, 5, 1),
                        Margin = new Thickness(0, 0, 4, 2),
                        Child = new TextBlock
                        {
                            Text = ddl.Text,
                            FontSize = scale.SmallFontSize - 0.5,
                            FontFamily = ThemeService.AppFontFamily,
                            FontWeight = FontWeights.SemiBold,
                            Foreground = ThemeService.BrushFromHex(ddl.FgHex)
                        }
                    });
                }
                if (!string.IsNullOrEmpty(task.RemindAtIso) && !task.Reminded && !task.Completed)
                {
                    metaWrap.Children.Add(new Border
                    {
                        Background = pal.HoverBg,
                        CornerRadius = new CornerRadius(4),
                        Padding = new Thickness(4, 1, 4, 1),
                        Margin = new Thickness(0, 0, 4, 2),
                        Child = new TextBlock
                        {
                            Text = "⏰ " + task.RemindAtIso.Substring(5),
                            FontSize = scale.SmallFontSize - 0.5,
                            FontFamily = ThemeService.AppFontFamily,
                            Foreground = pal.TextSecondary
                        }
                    });
                }
                if (task.AiScore > 0)
                {
                    metaWrap.Children.Add(new Border
                    {
                        Background = ThemeService.BrushFromHex(pal.IsDark ? "#312E81" : "#EDE9FE"),
                        CornerRadius = new CornerRadius(4),
                        Padding = new Thickness(4, 1, 4, 1),
                        Margin = new Thickness(0, 0, 4, 2),
                        ToolTip = task.AiReason,
                        Child = new TextBlock
                        {
                            Text = "AI " + task.AiScore + "分",
                            FontSize = scale.SmallFontSize - 0.5,
                            FontFamily = ThemeService.AppFontFamily,
                            Foreground = ThemeService.BrushFromHex(pal.IsDark ? "#C4B5FD" : "#6D28D9")
                        }
                    });
                }
                if (task.AttachmentFiles != null && task.AttachmentFiles.Count > 0)
                {
                    var attPaths = new List<string>();
                    for (int a = 0; a < task.AttachmentFiles.Count; a++)
                    {
                        string p = _controller.Storage.GetAttachmentPath(task.AttachmentFiles[a]);
                        if (!string.IsNullOrEmpty(p)) attPaths.Add(p);
                    }
                    var attPill = new Border
                    {
                        Background = pal.HoverBg,
                        BorderBrush = pal.Border,
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(4),
                        Padding = new Thickness(5, 1, 5, 1),
                        Margin = new Thickness(0, 0, 4, 2),
                        Cursor = Cursors.Hand,
                        ToolTip = "点击预览图片附件",
                        Child = new TextBlock
                        {
                            Text = "🖼 " + task.AttachmentFiles.Count,
                            FontSize = scale.SmallFontSize - 0.5,
                            FontFamily = ThemeService.AppFontFamily,
                            Foreground = pal.TextPrimary
                        }
                    };
                    attPill.MouseLeftButtonUp += delegate
                    {
                        ImageAttachmentHelper.ShowImagePreviewDialog(attPaths, 0, this, _note.Theme);
                    };
                    metaWrap.Children.Add(attPill);
                }
                textStack.Children.Add(metaWrap);
            }

            textStack.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                if (e.ClickCount == 2) _controller.OpenEditTaskDialog(task, this);
            };
            Grid.SetColumn(textStack, 1);
            rowGrid.Children.Add(textStack);

            var actionStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
            var editBtn = GlassControlStyles.CreateGlassButton("✎", Brushes.Transparent, pal.TextSecondary, null, 11.5, new Thickness(4, 1, 4, 1), 6);
            editBtn.ToolTip = "编辑日程与DDL";
            editBtn.Click += delegate { _controller.OpenEditTaskDialog(task, this); };

            var delBtn = GlassControlStyles.CreateGlassButton("×", Brushes.Transparent, pal.TextSecondary, null, 13, new Thickness(4, 0, 4, 0), 6);
            delBtn.ToolTip = "删除";
            delBtn.Click += delegate { _controller.RequestDeleteTask(task.Id, this); };

            actionStack.Children.Add(editBtn);
            actionStack.Children.Add(delBtn);
            Grid.SetColumn(actionStack, 2);
            rowGrid.Children.Add(actionStack);

            rowCard.Child = rowGrid;
            return rowCard;
        }

        private void OpenSettingsPopup()
        {
            if (_settingsPopup == null)
            {
                _settingsPopup = new Popup
                {
                    StaysOpen = false,
                    PlacementTarget = _settingsBtn,
                    Placement = PlacementMode.Bottom,
                    AllowsTransparency = true
                };
            }

            ThemePalette pal = ThemeService.GetPalette(_note.Theme, 0.96);
            UiScaleMetrics scale = ThemeService.GetScaleMetrics(_controller.Config.UiScale, _note.FontSize);

            var card = new Border
            {
                Width = 264,
                Background = pal.PopupBg,
                BorderBrush = pal.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(13),
                Margin = new Thickness(6),
                Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 18, ShadowDepth = 3, Opacity = 0.16 }
            };
            GlassControlStyles.ApplyToResources(card.Resources, pal, scale);

            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = "便签外观主题",
                FontFamily = ThemeService.AppFontFamily,
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Foreground = pal.TextPrimary,
                Margin = new Thickness(0, 0, 0, 6)
            });

            var swatchPanel = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
            for (int i = 0; i < ThemeService.ThemeKeys.Length; i++)
            {
                string key = ThemeService.ThemeKeys[i];
                ThemePalette tp = ThemeService.GetPalette(key, 1.0);
                var sw = new Button
                {
                    Width = 30,
                    Height = 30,
                    Margin = new Thickness(0, 0, 6, 4),
                    Background = ThemeService.BrushFromHex(tp.SwatchHex),
                    BorderBrush = key == ThemeService.NormalizeThemeKey(_note.Theme) ? pal.AccentBg : pal.Border,
                    BorderThickness = new Thickness(key == ThemeService.NormalizeThemeKey(_note.Theme) ? 2.2 : 1),
                    ToolTip = tp.DisplayName,
                    Cursor = Cursors.Hand
                };
                sw.Click += delegate
                {
                    _note.Theme = key;
                    ApplyThemeAndContent();
                    _controller.ScheduleSave();
                    _settingsPopup.IsOpen = false;
                };
                swatchPanel.Children.Add(sw);
            }
            stack.Children.Add(swatchPanel);

            var glassSliderLbl = new TextBlock
            {
                Text = string.Format("窗口不透明度 ({0}%)", (int)Math.Round(_note.Opacity * 100)),
                FontFamily = ThemeService.AppFontFamily,
                FontSize = 12,
                Foreground = pal.TextSecondary
            };
            stack.Children.Add(glassSliderLbl);
            var opSlider = new Slider
            {
                Minimum = 0.60,
                Maximum = 1.00,
                Value = _note.Opacity,
                Margin = new Thickness(0, 2, 0, 10)
            };
            bool draggingOp = false;
            opSlider.AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler(delegate
            {
                draggingOp = true;
            }));
            opSlider.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler(delegate
            {
                draggingOp = false;
                RefreshTasksOnly();
            }));
            opSlider.ValueChanged += delegate
            {
                _note.Opacity = opSlider.Value;
                glassSliderLbl.Text = string.Format("窗口不透明度 ({0}%)", (int)Math.Round(_note.Opacity * 100));
                ThemePalette livePal = ThemeService.GetPalette(_note.Theme, _note.Opacity);
                _outerCard.Background = livePal.CardBg;
                _headerBar.Background = livePal.HeaderBg;
                _controller.ScheduleSave();
                if (!draggingOp)
                {
                    RefreshTasksOnly();
                }
            };
            stack.Children.Add(opSlider);

            if (_note.FilterMode == "custom")
            {
                var scratchToggle = new CheckBox
                {
                    Content = "显示上方随手记文字区",
                    IsChecked = _note.ShowScratchpad,
                    Margin = new Thickness(0, 0, 0, 10)
                };
                scratchToggle.Click += delegate
                {
                    _note.ShowScratchpad = scratchToggle.IsChecked == true;
                    ApplyThemeAndContent();
                    _controller.ScheduleSave();
                };
                stack.Children.Add(scratchToggle);
            }

            stack.Children.Add(new TextBlock
            {
                Text = "便签内容来源",
                FontFamily = ThemeService.AppFontFamily,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = pal.TextSecondary,
                Margin = new Thickness(0, 0, 0, 4)
            });
            var modeBox = new ComboBox { Margin = new Thickness(0, 0, 0, 10) };
            string[] modes = new string[] { "custom", "all", "timetable", "q1", "q2", "q3", "q4" };
            for (int i = 0; i < modes.Length; i++)
            {
                var item = new ComboBoxItem { Content = StickyNote.GetFilterModeLabel(modes[i]), Tag = modes[i] };
                modeBox.Items.Add(item);
                if (_note.FilterMode == modes[i]) modeBox.SelectedIndex = i;
            }
            modeBox.SelectionChanged += delegate
            {
                var sel = modeBox.SelectedItem as ComboBoxItem;
                if (sel != null && sel.Tag != null)
                {
                    _note.FilterMode = sel.Tag.ToString();
                    ApplyThemeAndContent();
                    _controller.ScheduleSave();
                }
            };
            stack.Children.Add(modeBox);

            var fontRow = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            fontRow.Children.Add(new TextBlock
            {
                Text = "便签字号：" + (int)_note.FontSize,
                FontFamily = ThemeService.AppFontFamily,
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = pal.TextSecondary
            });
            var fontBtns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var fMinus = GlassControlStyles.CreateGlassButton("A-", pal.SurfaceBg, pal.TextPrimary, pal.Border, 11.5, new Thickness(8, 2, 8, 2), 6);
            fMinus.Margin = new Thickness(0, 0, 4, 0);
            fMinus.Click += delegate { _note.FontSize = Math.Max(10, _note.FontSize - 1); ApplyThemeAndContent(); _controller.ScheduleSave(); };
            var fPlus = GlassControlStyles.CreateGlassButton("A+", pal.SurfaceBg, pal.TextPrimary, pal.Border, 11.5, new Thickness(8, 2, 8, 2), 6);
            fPlus.Click += delegate { _note.FontSize = Math.Min(22, _note.FontSize + 1); ApplyThemeAndContent(); _controller.ScheduleSave(); };
            fontBtns.Children.Add(fMinus);
            fontBtns.Children.Add(fPlus);
            fontRow.Children.Add(fontBtns);
            stack.Children.Add(fontRow);

            var dashBtn = GlassControlStyles.CreateGlassButton("打开四象限与AI主面板", pal.AccentBg, pal.AccentFg, null, 12, new Thickness(8, 5, 8, 5), 8);
            dashBtn.Margin = new Thickness(0, 0, 0, 6);
            dashBtn.Click += delegate { _settingsPopup.IsOpen = false; _controller.ShowDashboardWindow(); };
            stack.Children.Add(dashBtn);

            var delBtn = GlassControlStyles.CreateGlassButton("删除此便签",
                ThemeService.BrushFromHex("#FEE2E2"), ThemeService.BrushFromHex("#DC2626"), ThemeService.BrushFromHex("#FCA5A5"),
                12, new Thickness(8, 5, 8, 5), 8);
            delBtn.Click += delegate
            {
                _settingsPopup.IsOpen = false;
                if (MessageBox.Show("确定删除便签「" + _note.Title + "」吗？（关联到此便签的待办也会清理）", "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    _controller.DeleteStickyNote(_note.Id);
                }
            };
            stack.Children.Add(delBtn);

            card.Child = stack;
            _settingsPopup.Child = card;
            _settingsPopup.IsOpen = true;
            SpringAnimationHelper.AnimatePopIn(card, 220);
        }

        private static Button MakeHeaderButton(string text, string tooltip)
        {
            var b = GlassControlStyles.CreateGlassButton(text, Brushes.Transparent, Brushes.Black, null, 12.5, new Thickness(5, 2, 5, 2), 6);
            b.ToolTip = tooltip;
            return b;
        }
    }
}
