using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace DesktopMemo
{
    public class DashboardWindow : Window
    {
        private readonly TrayAndAppController _controller;

        private bool _isInitializing = true;
        private bool _isAdjustingZ = false;
        private bool _suppressSettingEvents = false;
        private int _activeTabIndex = 0; // 0=Quadrants, 1=Schedule, 2=Stickies, 3=Settings

        private Border _outerCard;
        private Border _headerBar;
        private TextBlock _headerTitleText;
        private TextBlock _headerSummaryText;
        private Border _modeBoxBorder;
        private Button _modePanelBtn;
        private Button _modeStickyBtn;
        private Button _modeBothBtn;
        private Button _pinBtn;
        private Button _lockBtn;
        private Button _hideBtn;
        private Grid _mainContentGrid;

        // Floating iOS Glass Dock Navigation
        private Border _dockContainer;
        private Border _dockIndicatorPill;
        private readonly Button[] _navTabButtons = new Button[4];

        private Border _quickAddBar;
        private TextBox _quickTitleBox;
        private ComboBox _quickQuadBox;
        private ComboBox _quickDdlBox;
        private Button _qaAddBtn;
        private Button _qaDetailBtn;
        private Button _chatExtractBtn;
        private Button _aiSortBtn;
        private Button _undoAiBtn;
        private TextBlock _aiStatusBanner;

        // Views
        private Grid _quadrantsView;
        private readonly StackPanel[] _quadListPanels = new StackPanel[4];
        private readonly TextBlock[] _quadCountLabels = new TextBlock[4];
        private readonly Button[] _quadPinBtns = new Button[4];
        private readonly Button[] _quadAddBtns = new Button[4];

        private Grid _scheduleView;
        private TextBox _searchBox;
        private ComboBox _filterCombo;
        private ComboBox _sortCombo;
        private Button _clearDoneBtn;
        private StackPanel _scheduleListPanel;

        private Grid _stickiesView;
        private StackPanel _stickiesListPanel;

        private ScrollViewer _settingsView;
        private readonly List<Border> _settingsCards = new List<Border>();
        private readonly List<TextBlock> _settingsCardTitles = new List<TextBlock>();
        private readonly List<TextBlock> _settingsLabels = new List<TextBlock>();

        private ComboBox _aiPresetCombo;
        private TextBox _aiBaseUrlBox;
        private PasswordBox _aiKeyPasswordBox;
        private TextBox _aiKeyPlainBox;
        private bool _showPlainKey = false;
        private TextBox _aiModelBox;
        private TextBox _aiCustomPromptBox;
        private TextBlock _aiTestResultText;

        private CheckBox _autoStartBox;
        private TextBlock _autoStartStatusText;
        private CheckBox _hotkeysBox;
        private CheckBox _soundBox;
        private ComboBox _panelThemeCombo;
        private ComboBox _uiScaleCombo;
        private Slider _panelFontSlider;
        private TextBlock _panelFontText;
        private Slider _panelOpacitySlider;
        private TextBlock _panelOpacityText;

        private TextBox _serverPortBox;
        private TextBox _serverPassBox;
        private CheckBox _remoteSyncBox;
        private TextBox _remoteUrlBox;
        private TextBox _remotePassBox;
        private TextBlock _syncStatusText;

        public DashboardWindow(TrayAndAppController controller)
        {
            _controller = controller;
            AppConfig cfg = _controller.Config;

            Title = "桌面备忘录 · 智能四象限";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            MinWidth = 560;
            MinHeight = 420;

            double left = cfg.PanelLeft;
            double top = cfg.PanelTop;
            double width = cfg.PanelWidth >= 560 ? cfg.PanelWidth : 890;
            double height = cfg.PanelHeight >= 420 ? cfg.PanelHeight : 630;
            Win32Native.ClampWindowToScreens(ref left, ref top, width, height);

            Left = left;
            Top = top;
            Width = width;
            Height = height;

            BuildUi();
            ApplyThemeAndRefreshAll();

            SourceInitialized += OnSourceInitialized;
            Loaded += delegate { UpdateDockIndicator(false); };
            LocationChanged += OnGeometryChanged;
            SizeChanged += delegate(object s, SizeChangedEventArgs e)
            {
                OnGeometryChanged(s, e);
                UpdateDockIndicator(false);
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
            ApplyPinModeNow();
        }

        private IntPtr WndProcHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            AppConfig cfg = _controller.Config;
            return Win32Native.HandleDesktopAndResizeMessages(
                this, hwnd, msg, wParam, lParam,
                cfg.PanelPinMode, cfg.PanelLocked, false, _isAdjustingZ, ref handled);
        }

        public void ApplyPinModeNow()
        {
            Win32Native.ApplyPinMode(this, _controller.Config.PanelPinMode, ref _isAdjustingZ);
            UpdateHeaderButtons();
        }

        private void OnGeometryChanged(object sender, EventArgs e)
        {
            if (_isInitializing) return;
            AppConfig cfg = _controller.Config;
            cfg.PanelLeft = Left;
            cfg.PanelTop = Top;
            if (ActualWidth >= 560 && ActualHeight >= 420)
            {
                cfg.PanelWidth = Width;
                cfg.PanelHeight = Height;
            }
            _controller.ScheduleConfigSave();
        }

        private void BuildUi()
        {
            _outerCard = new Border
            {
                Margin = new Thickness(10),
                CornerRadius = new CornerRadius(14),
                BorderThickness = new Thickness(1),
                Effect = new DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 18,
                    ShadowDepth = 3,
                    Opacity = 0.14
                }
            };

            var rootDock = new DockPanel();

            // Header bar (Bug 3 fix: NO collapse button, only Pin, Lock, Minimize/Hide)
            _headerBar = new Border
            {
                CornerRadius = new CornerRadius(14, 14, 0, 0),
                Padding = new Thickness(16, 10, 12, 10),
                BorderThickness = new Thickness(0, 0, 0, 1)
            };
            _headerBar.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                if (e.ButtonState == MouseButtonState.Pressed && !_controller.Config.PanelLocked)
                {
                    try { DragMove(); } catch { }
                }
            };

            var headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var titleStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            _headerTitleText = new TextBlock
            {
                Text = "桌面备忘录",
                FontFamily = ThemeService.AppFontFamily,
                FontSize = 15,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center
            };
            titleStack.Children.Add(_headerTitleText);

            _headerSummaryText = new TextBlock
            {
                FontFamily = ThemeService.AppFontFamily,
                FontSize = 12,
                Margin = new Thickness(10, 1, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            titleStack.Children.Add(_headerSummaryText);
            Grid.SetColumn(titleStack, 0);
            headerGrid.Children.Add(titleStack);

            // Display Mode Switcher (Flat Pill Bar)
            _modeBoxBorder = new Border
            {
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(3),
                Margin = new Thickness(8, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            var modeRow = new StackPanel { Orientation = Orientation.Horizontal };
            _modePanelBtn = MakeModeButton("大面板", "仅显示桌面整合大面板", delegate { _controller.SetDisplayMode("panel"); });
            _modeStickyBtn = MakeModeButton("独立便签", "仅显示桌面各张独立便利贴", delegate { _controller.SetDisplayMode("sticky"); });
            _modeBothBtn = MakeModeButton("混合共存", "大面板与独立便签同时贴在桌面", delegate { _controller.SetDisplayMode("both"); });
            modeRow.Children.Add(_modePanelBtn);
            modeRow.Children.Add(_modeStickyBtn);
            modeRow.Children.Add(_modeBothBtn);
            _modeBoxBorder.Child = modeRow;
            Grid.SetColumn(_modeBoxBorder, 1);
            headerGrid.Children.Add(_modeBoxBorder);

            var winCtrl = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            _pinBtn = MakeIconBtn("📌", "切换：贴在桌面 / 置顶悬浮 / 普通窗口", delegate
            {
                _controller.Config.PanelPinMode = Win32Native.CyclePinMode(_controller.Config.PanelPinMode);
                ApplyPinModeNow();
                _controller.ScheduleConfigSave();
            });
            _lockBtn = MakeIconBtn("🔓", "锁定/解锁主面板位置与大小", delegate
            {
                _controller.Config.PanelLocked = !_controller.Config.PanelLocked;
                UpdateHeaderButtons();
                _controller.ScheduleConfigSave();
            });
            _hideBtn = MakeIconBtn("—", "隐藏到系统托盘", delegate
            {
                _controller.HideDashboardWindow();
            });

            winCtrl.Children.Add(_pinBtn);
            winCtrl.Children.Add(_lockBtn);
            winCtrl.Children.Add(_hideBtn);
            Grid.SetColumn(winCtrl, 2);
            headerGrid.Children.Add(winCtrl);

            _headerBar.Child = headerGrid;
            DockPanel.SetDock(_headerBar, Dock.Top);
            rootDock.Children.Add(_headerBar);

            // Main Content
            _mainContentGrid = new Grid { Margin = new Thickness(15, 11, 15, 13) };
            _mainContentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // iOS Floating Dock + AI Actions
            _mainContentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Quick add bar
            _mainContentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // AI status banner
            _mainContentGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Animated Page Content

            // Top Nav Dock + Right AI Buttons
            var navDock = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };

            _dockContainer = new Border
            {
                CornerRadius = new CornerRadius(15),
                Padding = new Thickness(4),
                BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            var dockGrid = new Grid();
            _dockIndicatorPill = new Border
            {
                CornerRadius = new CornerRadius(11),
                BorderThickness = new Thickness(1.1),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Stretch,
                IsHitTestVisible = false
            };
            dockGrid.Children.Add(_dockIndicatorPill);

            var tabRow = new StackPanel { Orientation = Orientation.Horizontal };
            string[] tabTitles = new string[] { "四象限矩阵", "日程 & DDL", "独立便利贴", "AI 与设置" };
            for (int i = 0; i < 4; i++)
            {
                int idx = i;
                var tb = GlassControlStyles.CreateGlassButton(tabTitles[i], Brushes.Transparent, Brushes.Black, null, 13, new Thickness(14, 6, 14, 6), 8);
                tb.Margin = new Thickness(0, 0, 2, 0);
                tb.FontWeight = FontWeights.SemiBold;
                tb.Click += delegate { SwitchTab(idx); };
                _navTabButtons[i] = tb;
                tabRow.Children.Add(tb);
            }
            dockGrid.Children.Add(tabRow);
            _dockContainer.Child = dockGrid;
            DockPanel.SetDock(_dockContainer, Dock.Left);
            navDock.Children.Add(_dockContainer);

            var aiRightRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            _undoAiBtn = GlassControlStyles.CreateGlassButton("↶ 撤销AI排序",
                Brushes.Transparent, Brushes.Black, Brushes.Gray, 12, new Thickness(10, 5, 10, 5), 8);
            _undoAiBtn.Margin = new Thickness(0, 0, 6, 0);
            _undoAiBtn.Visibility = Visibility.Collapsed;
            _undoAiBtn.Click += delegate { _controller.UndoLastAiSort(); };

            _chatExtractBtn = GlassControlStyles.CreateGlassButton("💬 微信/文本提取",
                Brushes.Transparent, Brushes.Black, Brushes.Gray, 12.5, new Thickness(11, 6, 11, 6), 9);
            _chatExtractBtn.Margin = new Thickness(0, 0, 6, 0);
            _chatExtractBtn.ToolTip = "粘贴微信聊天记录或通知，AI 自动拆解 DDL 与四象限任务";
            _chatExtractBtn.Click += delegate { _controller.OpenChatExtractDialog("", this); };

            _aiSortBtn = GlassControlStyles.CreateGlassButton("✨ AI 智能排序",
                ThemeService.BrushFromHex("#6D28D9"), Brushes.White,
                null, 12.5, new Thickness(13, 6, 13, 6), 9);
            _aiSortBtn.FontWeight = FontWeights.SemiBold;
            _aiSortBtn.Click += OnAiSortClicked;

            aiRightRow.Children.Add(_undoAiBtn);
            aiRightRow.Children.Add(_chatExtractBtn);
            aiRightRow.Children.Add(_aiSortBtn);
            navDock.Children.Add(aiRightRow);
            Grid.SetRow(navDock, 0);
            _mainContentGrid.Children.Add(navDock);

            // Quick add bar (unified flat glass style)
            _quickAddBar = new Border
            {
                CornerRadius = new CornerRadius(10),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8, 7, 8, 7),
                Margin = new Thickness(0, 0, 0, 8)
            };
            var qaGrid = new Grid();
            qaGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            qaGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            qaGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            qaGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            qaGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _quickTitleBox = new TextBox
            {
                Padding = new Thickness(9, 5, 9, 5),
                ToolTip = "输入待办内容，按 Enter 快速添加"
            };
            _quickTitleBox.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.Key == Key.Enter)
                {
                    AddQuickTask();
                    e.Handled = true;
                }
            };
            Grid.SetColumn(_quickTitleBox, 0);
            qaGrid.Children.Add(_quickTitleBox);

            _quickQuadBox = new ComboBox { Width = 122, Margin = new Thickness(6, 0, 0, 0) };
            _quickQuadBox.Items.Add("Q1 重要紧急");
            _quickQuadBox.Items.Add("Q2 重要不紧急");
            _quickQuadBox.Items.Add("Q3 紧急不重要");
            _quickQuadBox.Items.Add("Q4 不重要不紧急");
            _quickQuadBox.Items.Add("未分类（不进象限）");
            _quickQuadBox.SelectedIndex = 0;
            Grid.SetColumn(_quickQuadBox, 1);
            qaGrid.Children.Add(_quickQuadBox);

            _quickDdlBox = new ComboBox { Width = 98, Margin = new Thickness(6, 0, 0, 0) };
            _quickDdlBox.Items.Add("无 DDL");
            _quickDdlBox.Items.Add("1小时后");
            _quickDdlBox.Items.Add("今天18:00");
            _quickDdlBox.Items.Add("今天23:59");
            _quickDdlBox.Items.Add("明天12:00");
            _quickDdlBox.Items.Add("明天23:59");
            _quickDdlBox.Items.Add("3天后");
            _quickDdlBox.SelectedIndex = 0;
            Grid.SetColumn(_quickDdlBox, 2);
            qaGrid.Children.Add(_quickDdlBox);

            _qaAddBtn = GlassControlStyles.CreateGlassButton("+ 添加",
                ThemeService.BrushFromHex("#2563EB"), Brushes.White, null, 12.5, new Thickness(12, 5, 12, 5), 8);
            _qaAddBtn.Margin = new Thickness(6, 0, 0, 0);
            _qaAddBtn.Click += delegate { AddQuickTask(); };
            Grid.SetColumn(_qaAddBtn, 3);
            qaGrid.Children.Add(_qaAddBtn);

            _qaDetailBtn = GlassControlStyles.CreateGlassButton("设提醒/详情...",
                Brushes.Transparent, Brushes.Black, Brushes.Gray, 12, new Thickness(10, 5, 10, 5), 8);
            _qaDetailBtn.Margin = new Thickness(6, 0, 0, 0);
            _qaDetailBtn.Click += delegate
            {
                int q = _quickQuadBox.SelectedIndex == 4 ? 0 : (_quickQuadBox.SelectedIndex + 1);
                var draft = new MemoTask
                {
                    Title = (_quickTitleBox.Text ?? "").Trim(),
                    Quadrant = q,
                    DeadlineIso = MemoTask.FormatIso(ResolveQuickDdl(_quickDdlBox.SelectedIndex))
                };
                if (_controller.OpenNewTaskDialog(draft, this))
                {
                    _quickTitleBox.Text = "";
                    _quickDdlBox.SelectedIndex = 0;
                }
            };
            Grid.SetColumn(_qaDetailBtn, 4);
            qaGrid.Children.Add(_qaDetailBtn);

            _quickAddBar.Child = qaGrid;
            Grid.SetRow(_quickAddBar, 1);
            _mainContentGrid.Children.Add(_quickAddBar);

            _aiStatusBanner = new TextBlock
            {
                FontFamily = ThemeService.AppFontFamily,
                FontSize = 12,
                Visibility = Visibility.Collapsed,
                Margin = new Thickness(2, 0, 2, 8),
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetRow(_aiStatusBanner, 2);
            _mainContentGrid.Children.Add(_aiStatusBanner);

            BuildQuadrantsView();
            BuildScheduleView();
            BuildStickiesView();
            BuildSettingsView();

            Grid.SetRow(_quadrantsView, 3);
            Grid.SetRow(_scheduleView, 3);
            Grid.SetRow(_stickiesView, 3);
            Grid.SetRow(_settingsView, 3);

            _mainContentGrid.Children.Add(_quadrantsView);
            _mainContentGrid.Children.Add(_scheduleView);
            _mainContentGrid.Children.Add(_stickiesView);
            _mainContentGrid.Children.Add(_settingsView);

            rootDock.Children.Add(_mainContentGrid);
            _outerCard.Child = rootDock;
            Content = _outerCard;
        }

        private void BuildQuadrantsView()
        {
            _quadrantsView = new Grid();
            _quadrantsView.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _quadrantsView.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _quadrantsView.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            _quadrantsView.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            for (int q = 1; q <= 4; q++)
            {
                int quadrant = q;
                var card = new Border
                {
                    CornerRadius = new CornerRadius(11),
                    BorderThickness = new Thickness(1),
                    Margin = new Thickness(q % 2 == 1 ? 0 : 5, q <= 2 ? 0 : 5, q % 2 == 1 ? 5 : 0, q <= 2 ? 5 : 0),
                    Tag = "quadCard_" + q
                };
                var dock = new DockPanel();

                var qHeader = new Border
                {
                    CornerRadius = new CornerRadius(10, 10, 0, 0),
                    Padding = new Thickness(11, 7, 8, 7),
                    Tag = "quadHeader_" + q
                };
                var qhd = new DockPanel();
                var titleLbl = new TextBlock
                {
                    FontFamily = ThemeService.AppFontFamily,
                    FontSize = 13,
                    FontWeight = FontWeights.Bold,
                    VerticalAlignment = VerticalAlignment.Center,
                    Tag = "quadTitle_" + q
                };
                _quadCountLabels[q - 1] = titleLbl;
                DockPanel.SetDock(titleLbl, Dock.Left);
                qhd.Children.Add(titleLbl);

                var qBtns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
                var pinOutBtn = GlassControlStyles.CreateGlassButton("📌贴出", Brushes.Transparent, Brushes.Black, null, 11, new Thickness(6, 2, 6, 2), 6);
                pinOutBtn.ToolTip = "在桌面单独贴出一张同步此象限的便利贴";
                pinOutBtn.Click += delegate { _controller.PinOrShowQuadrantSticky(quadrant); };
                _quadPinBtns[q - 1] = pinOutBtn;

                var addQBtn = GlassControlStyles.CreateGlassButton("+", Brushes.Transparent, Brushes.Black, null, 13, new Thickness(7, 0, 7, 1), 6);
                addQBtn.ToolTip = "在此象限新建待办";
                addQBtn.Click += delegate { _controller.OpenNewTaskDialog(new MemoTask { Quadrant = quadrant }, this); };
                _quadAddBtns[q - 1] = addQBtn;

                qBtns.Children.Add(pinOutBtn);
                qBtns.Children.Add(addQBtn);
                qhd.Children.Add(qBtns);
                qHeader.Child = qhd;
                DockPanel.SetDock(qHeader, Dock.Top);
                dock.Children.Add(qHeader);

                var scroller = new ScrollViewer
                {
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    Padding = new Thickness(8, 6, 8, 6)
                };
                var listPanel = new StackPanel();
                _quadListPanels[q - 1] = listPanel;
                scroller.Content = listPanel;
                dock.Children.Add(scroller);

                card.Child = dock;
                Grid.SetRow(card, (q - 1) / 2);
                Grid.SetColumn(card, (q - 1) % 2);
                _quadrantsView.Children.Add(card);
            }
        }

        private void BuildScheduleView()
        {
            _scheduleView = new Grid { Visibility = Visibility.Collapsed };
            _scheduleView.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _scheduleView.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var filterBar = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            _searchBox = new TextBox
            {
                Width = 190,
                Padding = new Thickness(9, 4, 9, 4),
                Margin = new Thickness(0, 0, 8, 4),
                ToolTip = "按关键词筛选日程"
            };
            _searchBox.TextChanged += delegate { RefreshTaskViews(); };

            _filterCombo = new ComboBox { Width = 132, Margin = new Thickness(0, 0, 8, 4) };
            _filterCombo.Items.Add("未完成待办");
            _filterCombo.Items.Add("临近/已逾期DDL");
            _filterCombo.Items.Add("未分类(待AI整理)");
            _filterCombo.Items.Add("全部事项");
            _filterCombo.Items.Add("已完成事项");
            _filterCombo.SelectedIndex = 0;
            _filterCombo.SelectionChanged += delegate { RefreshTaskViews(); };

            _sortCombo = new ComboBox { Width = 154, Margin = new Thickness(0, 0, 8, 4) };
            _sortCombo.Items.Add(new ComboBoxItem { Content = "排序：AI 综合优先", Tag = "ai" });
            _sortCombo.Items.Add(new ComboBoxItem { Content = "排序：DDL 最近优先", Tag = "ddl" });
            _sortCombo.Items.Add(new ComboBoxItem { Content = "排序：四象限顺序", Tag = "quadrant" });
            _sortCombo.Items.Add(new ComboBoxItem { Content = "排序：最新创建", Tag = "created" });
            SelectSortComboValue(_controller.Config.SortBy);
            _sortCombo.SelectionChanged += delegate
            {
                var item = _sortCombo.SelectedItem as ComboBoxItem;
                if (item != null && item.Tag != null)
                {
                    _controller.Config.SortBy = item.Tag.ToString();
                    _controller.ScheduleConfigSave();
                    _controller.RefreshAllViews();
                }
            };

            _clearDoneBtn = GlassControlStyles.CreateGlassButton("清理已完成",
                Brushes.Transparent, Brushes.Black, Brushes.Gray, 12, new Thickness(10, 4, 10, 4), 8);
            _clearDoneBtn.Margin = new Thickness(0, 0, 0, 4);
            _clearDoneBtn.Click += delegate { _controller.ClearCompletedTasks(); };

            filterBar.Children.Add(_searchBox);
            filterBar.Children.Add(_filterCombo);
            filterBar.Children.Add(_sortCombo);
            filterBar.Children.Add(_clearDoneBtn);
            Grid.SetRow(filterBar, 0);
            _scheduleView.Children.Add(filterBar);

            var scroller = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            _scheduleListPanel = new StackPanel();
            scroller.Content = _scheduleListPanel;
            Grid.SetRow(scroller, 1);
            _scheduleView.Children.Add(scroller);
        }

        private void BuildStickiesView()
        {
            _stickiesView = new Grid { Visibility = Visibility.Collapsed };
            _stickiesView.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _stickiesView.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var toolbar = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };

            var newPureBtn = GlassControlStyles.CreateGlassButton("+ 新建纯待办便签",
                ThemeService.BrushFromHex("#2563EB"), Brushes.White, null, 12.5, new Thickness(12, 6, 12, 6), 8);
            newPureBtn.Margin = new Thickness(0, 0, 8, 4);
            newPureBtn.Click += delegate { _controller.CreateNewStickyNote("custom", false); };

            var newRichBtn = GlassControlStyles.CreateGlassButton("+ 新建图文随手记",
                ThemeService.BrushFromHex("#EFF6FF"), ThemeService.BrushFromHex("#1D4ED8"),
                ThemeService.BrushFromHex("#BFDBFE"), 12, new Thickness(10, 6, 10, 6), 8);
            newRichBtn.Margin = new Thickness(0, 0, 8, 4);
            newRichBtn.Click += delegate { _controller.CreateNewStickyNote("custom", true); };

            var newAllBtn = GlassControlStyles.CreateGlassButton("+ 贴出「全部日程」",
                ThemeService.BrushFromHex("#F1F5F9"), ThemeService.BrushFromHex("#334155"),
                ThemeService.BrushFromHex("#CBD5E1"), 12, new Thickness(10, 6, 10, 6), 8);
            newAllBtn.Margin = new Thickness(0, 0, 8, 4);
            newAllBtn.Click += delegate { _controller.CreateNewStickyNote("all", false); };

            var showAllBtn = GlassControlStyles.CreateGlassButton("显示全部便签",
                ThemeService.BrushFromHex("#F1F5F9"), ThemeService.BrushFromHex("#334155"),
                ThemeService.BrushFromHex("#CBD5E1"), 12, new Thickness(10, 6, 10, 6), 8);
            showAllBtn.Margin = new Thickness(0, 0, 8, 4);
            showAllBtn.Click += delegate { _controller.SetAllStickiesVisible(true); };

            // Bug 1 fix: Lock/Unlock ONLY sticky notes, never locks Dashboard!
            var lockStickiesBtn = GlassControlStyles.CreateGlassButton("锁定全部便签",
                ThemeService.BrushFromHex("#F1F5F9"), ThemeService.BrushFromHex("#334155"),
                ThemeService.BrushFromHex("#CBD5E1"), 12, new Thickness(10, 6, 10, 6), 8);
            lockStickiesBtn.Margin = new Thickness(0, 0, 8, 4);
            lockStickiesBtn.ToolTip = "仅锁定所有独立桌面便签的位置与大小，不影响主面板";
            lockStickiesBtn.Click += delegate { _controller.SetAllStickiesLocked(true); };

            var unlockStickiesBtn = GlassControlStyles.CreateGlassButton("解锁全部便签",
                ThemeService.BrushFromHex("#F1F5F9"), ThemeService.BrushFromHex("#334155"),
                ThemeService.BrushFromHex("#CBD5E1"), 12, new Thickness(10, 6, 10, 6), 8);
            unlockStickiesBtn.Margin = new Thickness(0, 0, 0, 4);
            unlockStickiesBtn.Click += delegate { _controller.SetAllStickiesLocked(false); };

            toolbar.Children.Add(newPureBtn);
            toolbar.Children.Add(newRichBtn);
            toolbar.Children.Add(newAllBtn);
            toolbar.Children.Add(showAllBtn);
            toolbar.Children.Add(lockStickiesBtn);
            toolbar.Children.Add(unlockStickiesBtn);
            Grid.SetRow(toolbar, 0);
            _stickiesView.Children.Add(toolbar);

            var scroller = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            _stickiesListPanel = new StackPanel();
            scroller.Content = _stickiesListPanel;
            Grid.SetRow(scroller, 1);
            _stickiesView.Children.Add(scroller);
        }

        private void BuildSettingsView()
        {
            _settingsView = new ScrollViewer
            {
                Visibility = Visibility.Collapsed,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };

            var stack = new StackPanel { Margin = new Thickness(0, 0, 6, 12) };

            // Section 1: AI Settings
            var aiCard = CreateSettingsCard("🤖 AI 智能排序与聊天提取（兼容 DeepSeek / OpenAI 接口）");
            var aiGrid = new Grid();
            aiGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
            aiGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            _aiPresetCombo = new ComboBox { Margin = new Thickness(0, 3, 0, 6) };
            for (int i = 0; i < AiSortService.Presets.Length; i++)
            {
                _aiPresetCombo.Items.Add(new ComboBoxItem { Content = AiSortService.Presets[i].DisplayName, Tag = AiSortService.Presets[i].Key });
            }
            _aiPresetCombo.SelectionChanged += delegate
            {
                if (_suppressSettingEvents) return;
                var sel = _aiPresetCombo.SelectedItem as ComboBoxItem;
                if (sel != null && sel.Tag != null)
                {
                    AiPreset preset = AiSortService.FindPreset(sel.Tag.ToString());
                    _controller.Config.AiProviderPreset = preset.Key;
                    if (!string.IsNullOrEmpty(preset.BaseUrl)) _aiBaseUrlBox.Text = preset.BaseUrl;
                    if (!string.IsNullOrEmpty(preset.DefaultModel)) _aiModelBox.Text = preset.DefaultModel;
                    SaveAiInputsToConfig();
                }
            };
            AddSettingRow(aiGrid, "服务商预设：", _aiPresetCombo);

            _aiBaseUrlBox = new TextBox { Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(0, 3, 0, 6) };
            _aiBaseUrlBox.LostFocus += delegate { if (!_suppressSettingEvents) SaveAiInputsToConfig(); };
            AddSettingRow(aiGrid, "API Base URL：", _aiBaseUrlBox);

            var keyRow = new Grid { Margin = new Thickness(0, 3, 0, 6) };
            keyRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            keyRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _aiKeyPasswordBox = new PasswordBox { Padding = new Thickness(8, 5, 8, 5) };
            _aiKeyPlainBox = new TextBox { Padding = new Thickness(8, 5, 8, 5), Visibility = Visibility.Collapsed };
            _aiKeyPasswordBox.LostFocus += delegate { if (!_suppressSettingEvents) SaveAiInputsToConfig(); };
            _aiKeyPlainBox.LostFocus += delegate { if (!_suppressSettingEvents) SaveAiInputsToConfig(); };
            Grid.SetColumn(_aiKeyPasswordBox, 0);
            Grid.SetColumn(_aiKeyPlainBox, 0);
            keyRow.Children.Add(_aiKeyPasswordBox);
            keyRow.Children.Add(_aiKeyPlainBox);

            var toggleKeyBtn = GlassControlStyles.CreateGlassButton("显示",
                ThemeService.BrushFromHex("#F1F5F9"), ThemeService.BrushFromHex("#334155"), ThemeService.BrushFromHex("#CBD5E1"),
                11.5, new Thickness(10, 4, 10, 4), 7);
            toggleKeyBtn.Margin = new Thickness(6, 0, 0, 0);
            toggleKeyBtn.Click += delegate
            {
                _showPlainKey = !_showPlainKey;
                if (_showPlainKey)
                {
                    _aiKeyPlainBox.Text = _aiKeyPasswordBox.Password;
                    _aiKeyPasswordBox.Visibility = Visibility.Collapsed;
                    _aiKeyPlainBox.Visibility = Visibility.Visible;
                    toggleKeyBtn.Content = "隐藏";
                }
                else
                {
                    _aiKeyPasswordBox.Password = _aiKeyPlainBox.Text;
                    _aiKeyPlainBox.Visibility = Visibility.Collapsed;
                    _aiKeyPasswordBox.Visibility = Visibility.Visible;
                    toggleKeyBtn.Content = "显示";
                }
            };
            Grid.SetColumn(toggleKeyBtn, 1);
            keyRow.Children.Add(toggleKeyBtn);
            AddSettingRow(aiGrid, "API Key：", keyRow);

            _aiModelBox = new TextBox { Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(0, 3, 0, 6) };
            _aiModelBox.LostFocus += delegate { if (!_suppressSettingEvents) SaveAiInputsToConfig(); };
            AddSettingRow(aiGrid, "模型名称：", _aiModelBox);

            _aiCustomPromptBox = new TextBox
            {
                Padding = new Thickness(8, 5, 8, 5),
                Margin = new Thickness(0, 3, 0, 8),
                ToolTip = "可选：例如「考试、论文、面试最优先，日常琐事排在后面」"
            };
            _aiCustomPromptBox.LostFocus += delegate { if (!_suppressSettingEvents) SaveAiInputsToConfig(); };
            AddSettingRow(aiGrid, "个人排序偏好：", _aiCustomPromptBox);

            var aiBodyStack = (StackPanel)aiCard.Child;
            aiBodyStack.Children.Add(aiGrid);

            var aiBtnRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
            var testBtn = GlassControlStyles.CreateGlassButton("测试连接", ThemeService.BrushFromHex("#EFF6FF"), ThemeService.BrushFromHex("#1D4ED8"), ThemeService.BrushFromHex("#BFDBFE"), 12.5, new Thickness(12, 5, 12, 5), 8);
            testBtn.Margin = new Thickness(0, 0, 8, 0);
            testBtn.Click += OnTestAiConnectionClicked;

            var saveAiBtn = GlassControlStyles.CreateGlassButton("保存 AI 配置", ThemeService.BrushFromHex("#2563EB"), Brushes.White, null, 12.5, new Thickness(14, 5, 14, 5), 8);
            saveAiBtn.Click += delegate
            {
                SaveAiInputsToConfig();
                _aiTestResultText.Text = "✓ 已保存到本地配置文件。";
                _aiTestResultText.Foreground = ThemeService.BrushFromHex("#059669");
                _aiTestResultText.Visibility = Visibility.Visible;
            };
            aiBtnRow.Children.Add(testBtn);
            aiBtnRow.Children.Add(saveAiBtn);
            aiBodyStack.Children.Add(aiBtnRow);

            _aiTestResultText = new TextBlock
            {
                FontFamily = ThemeService.AppFontFamily,
                FontSize = 12,
                Margin = new Thickness(0, 4, 0, 0),
                TextWrapping = TextWrapping.Wrap,
                Visibility = Visibility.Collapsed
            };
            aiBodyStack.Children.Add(_aiTestResultText);
            stack.Children.Add(aiCard);

            // Section 2: Appearance, Scale & System AutoStart
            var sysCard = CreateSettingsCard("🎨 液态玻璃外观、按钮字号尺度与开机自启");
            var sysStack = (StackPanel)sysCard.Child;

            _autoStartBox = new CheckBox
            {
                Content = "Windows 开机自动启动桌面备忘录",
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 2, 0, 3)
            };
            _autoStartBox.Click += delegate
            {
                if (_suppressSettingEvents) return;
                bool desired = _autoStartBox.IsChecked == true;
                string err;
                if (_controller.SetAutoStart(desired, out err))
                {
                    _autoStartStatusText.Text = desired ? "✓ 已写入当前用户开机启动项" : "已关闭开机自动启动";
                    _autoStartStatusText.Foreground = ThemeService.BrushFromHex(desired ? "#059669" : "#64748B");
                }
                else
                {
                    _autoStartBox.IsChecked = !desired;
                    _autoStartStatusText.Text = "设置开机自启失败：" + err;
                    _autoStartStatusText.Foreground = ThemeService.BrushFromHex("#DC2626");
                }
            };
            sysStack.Children.Add(_autoStartBox);

            _autoStartStatusText = new TextBlock
            {
                FontFamily = ThemeService.AppFontFamily,
                FontSize = 11.5,
                Margin = new Thickness(24, 0, 0, 10)
            };
            sysStack.Children.Add(_autoStartStatusText);

            _hotkeysBox = new CheckBox
            {
                Content = "启用全局快捷键（Ctrl+Alt+N 新建 · Ctrl+Alt+M 显隐 · Ctrl+Alt+P 面板 · Ctrl+Alt+S 新便签）",
                Margin = new Thickness(0, 0, 0, 8)
            };
            _hotkeysBox.Click += delegate
            {
                if (_suppressSettingEvents) return;
                _controller.Config.HotkeysEnabled = _hotkeysBox.IsChecked == true;
                _controller.RefreshGlobalHotkeys();
                _controller.ScheduleConfigSave();
            };
            sysStack.Children.Add(_hotkeysBox);

            _soundBox = new CheckBox
            {
                Content = "日程到点弹出提醒时播放提示音",
                Margin = new Thickness(0, 0, 0, 12)
            };
            _soundBox.Click += delegate
            {
                if (_suppressSettingEvents) return;
                _controller.Config.ReminderSoundEnabled = _soundBox.IsChecked == true;
                _controller.ScheduleConfigSave();
            };
            sysStack.Children.Add(_soundBox);

            var lookRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            lookRow.Children.Add(MakeSettingLabel("外观主题："));
            _panelThemeCombo = new ComboBox { Width = 148, Margin = new Thickness(0, 0, 14, 4) };
            for (int i = 0; i < ThemeService.ThemeKeys.Length; i++)
            {
                string key = ThemeService.ThemeKeys[i];
                _panelThemeCombo.Items.Add(new ComboBoxItem { Content = ThemeService.GetThemeDisplayName(key), Tag = key });
            }
            _panelThemeCombo.SelectionChanged += delegate
            {
                if (_suppressSettingEvents) return;
                var sel = _panelThemeCombo.SelectedItem as ComboBoxItem;
                if (sel != null && sel.Tag != null)
                {
                    _controller.Config.PanelTheme = sel.Tag.ToString();
                    ApplyThemeAndRefreshAll();
                    _controller.ScheduleConfigSave();
                }
            };
            lookRow.Children.Add(_panelThemeCombo);

            lookRow.Children.Add(MakeSettingLabel("按钮与界面尺度："));
            _uiScaleCombo = new ComboBox { Width = 128, Margin = new Thickness(0, 0, 14, 4) };
            _uiScaleCombo.Items.Add(new ComboBoxItem { Content = "紧凑小巧 (S)", Tag = "compact" });
            _uiScaleCombo.Items.Add(new ComboBoxItem { Content = "标准舒适 (M)", Tag = "standard" });
            _uiScaleCombo.Items.Add(new ComboBoxItem { Content = "大按钮易点 (L)", Tag = "large" });
            _uiScaleCombo.SelectionChanged += delegate
            {
                if (_suppressSettingEvents) return;
                var sel = _uiScaleCombo.SelectedItem as ComboBoxItem;
                if (sel != null && sel.Tag != null)
                {
                    _controller.Config.UiScale = sel.Tag.ToString();
                    _controller.ApplyAppearanceToAllWindows();
                    _controller.ScheduleConfigSave();
                }
            };
            lookRow.Children.Add(_uiScaleCombo);
            sysStack.Children.Add(lookRow);

            var sliderRow = new WrapPanel { Margin = new Thickness(0, 2, 0, 4) };
            _panelFontText = MakeSettingLabel("全局字号(13px)：");
            sliderRow.Children.Add(_panelFontText);
            _panelFontSlider = new Slider { Minimum = 11, Maximum = 18, TickFrequency = 1, IsSnapToTickEnabled = true, Width = 130, Margin = new Thickness(0, 0, 16, 4), VerticalAlignment = VerticalAlignment.Center };
            _panelFontSlider.ValueChanged += delegate
            {
                if (_suppressSettingEvents) return;
                _controller.Config.PanelFontSize = Math.Round(_panelFontSlider.Value);
                _panelFontText.Text = string.Format("全局字号({0}px)：", (int)_controller.Config.PanelFontSize);
                _controller.ApplyAppearanceToAllWindows();
                _controller.ScheduleConfigSave();
            };
            sliderRow.Children.Add(_panelFontSlider);

            _panelOpacityText = MakeSettingLabel("窗口不透明度(96%)：");
            sliderRow.Children.Add(_panelOpacityText);
            _panelOpacitySlider = new Slider { Minimum = 0.60, Maximum = 1.00, Width = 140, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 4) };
            _panelOpacitySlider.ValueChanged += delegate
            {
                if (_suppressSettingEvents) return;
                _controller.Config.PanelOpacity = _panelOpacitySlider.Value;
                _panelOpacityText.Text = string.Format("窗口不透明度({0}%)：", (int)Math.Round(_panelOpacitySlider.Value * 100));
                ApplyThemeAndRefreshAll();
                _controller.ScheduleConfigSave();
            };
            sliderRow.Children.Add(_panelOpacitySlider);
            sysStack.Children.Add(sliderRow);

            stack.Children.Add(sysCard);

            // Section 3: Web Server & Windows Server Sync
            var webCard = CreateSettingsCard("🌐 类 Claude 网页端与 Windows Server 远程同步");
            var webStack = (StackPanel)webCard.Child;
            var webGrid = new Grid();
            webGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(145) });
            webGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            _serverPortBox = new TextBox { Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(0, 3, 0, 6) };
            _serverPortBox.LostFocus += delegate { if (!_suppressSettingEvents) SaveServerInputsToConfig(); };
            AddSettingRow(webGrid, "本地网页端口：", _serverPortBox);

            _serverPassBox = new TextBox { Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(0, 3, 0, 8), ToolTip = "留空将在首次启动网页服务时自动生成" };
            _serverPassBox.LostFocus += delegate { if (!_suppressSettingEvents) SaveServerInputsToConfig(); };
            AddSettingRow(webGrid, "本地网页访问口令：", _serverPassBox);
            webStack.Children.Add(webGrid);

            var localSrvBtns = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            var launchWebBtn = GlassControlStyles.CreateGlassButton("启动本地网页后端并打开",
                ThemeService.BrushFromHex("#EFF6FF"), ThemeService.BrushFromHex("#1D4ED8"), ThemeService.BrushFromHex("#BFDBFE"),
                12, new Thickness(12, 5, 12, 5), 8);
            launchWebBtn.Margin = new Thickness(0, 0, 8, 0);
            launchWebBtn.Click += delegate
            {
                SaveServerInputsToConfig();
                _controller.LaunchLocalWebServerAndOpenBrowser();
            };
            localSrvBtns.Children.Add(launchWebBtn);
            webStack.Children.Add(localSrvBtns);

            _remoteSyncBox = new CheckBox
            {
                Content = "启用远程 Windows Server 同步（勾选后桌面端每 30 秒自动与服务器双向/拉取同步）",
                Margin = new Thickness(0, 2, 0, 6)
            };
            _remoteSyncBox.Click += delegate
            {
                if (_suppressSettingEvents) return;
                _controller.Config.RemoteSyncEnabled = _remoteSyncBox.IsChecked == true;
                SaveServerInputsToConfig();
            };
            webStack.Children.Add(_remoteSyncBox);

            var remoteGrid = new Grid();
            remoteGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(145) });
            remoteGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            _remoteUrlBox = new TextBox { Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(0, 3, 0, 6), ToolTip = "例如 http://your-server-ip:8765" };
            _remoteUrlBox.LostFocus += delegate { if (!_suppressSettingEvents) SaveServerInputsToConfig(); };
            AddSettingRow(remoteGrid, "远程 Server 地址：", _remoteUrlBox);

            _remotePassBox = new TextBox { Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(0, 3, 0, 8) };
            _remotePassBox.LostFocus += delegate { if (!_suppressSettingEvents) SaveServerInputsToConfig(); };
            AddSettingRow(remoteGrid, "远程访问口令：", _remotePassBox);
            webStack.Children.Add(remoteGrid);

            var syncRow = new StackPanel { Orientation = Orientation.Horizontal };
            var syncNowBtn = GlassControlStyles.CreateGlassButton("从服务器拉取 / 同步一次",
                ThemeService.BrushFromHex("#2563EB"), Brushes.White, null, 12, new Thickness(12, 5, 12, 5), 8);
            syncNowBtn.Margin = new Thickness(0, 0, 8, 0);
            syncNowBtn.Click += async delegate
            {
                SaveServerInputsToConfig();
                _syncStatusText.Text = "正在同步...";
                _syncStatusText.Visibility = Visibility.Visible;
                string msg = await _controller.PullFromRemoteServerAsync();
                _syncStatusText.Text = msg;
            };
            var pushNowBtn = GlassControlStyles.CreateGlassButton("推送本地数据到服务器",
                ThemeService.BrushFromHex("#F1F5F9"), ThemeService.BrushFromHex("#334155"), ThemeService.BrushFromHex("#CBD5E1"),
                12, new Thickness(12, 5, 12, 5), 8);
            pushNowBtn.Click += async delegate
            {
                SaveServerInputsToConfig();
                _syncStatusText.Text = "正在推送...";
                _syncStatusText.Visibility = Visibility.Visible;
                string msg = await _controller.PushToRemoteServerAsync();
                _syncStatusText.Text = msg;
            };
            syncRow.Children.Add(syncNowBtn);
            syncRow.Children.Add(pushNowBtn);
            webStack.Children.Add(syncRow);

            _syncStatusText = new TextBlock
            {
                FontFamily = ThemeService.AppFontFamily,
                FontSize = 12,
                Margin = new Thickness(0, 5, 0, 0),
                TextWrapping = TextWrapping.Wrap,
                Visibility = Visibility.Collapsed
            };
            webStack.Children.Add(_syncStatusText);
            stack.Children.Add(webCard);

            // Section 4: Portable Storage
            var dataCard = CreateSettingsCard("💾 便携数据存储（data 目录随程序携带，方便直接拷贝/网盘同步）");
            var dataStack = (StackPanel)dataCard.Child;
            var pathTb = new TextBlock
            {
                Text = "当前数据目录：" + _controller.Storage.DataDirectory,
                FontFamily = ThemeService.AppFontFamily,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            };
            _settingsLabels.Add(pathTb);
            dataStack.Children.Add(pathTb);

            var dataBtns = new StackPanel { Orientation = Orientation.Horizontal };
            var openDirBtn = GlassControlStyles.CreateGlassButton("打开数据目录", ThemeService.BrushFromHex("#F1F5F9"), ThemeService.BrushFromHex("#0F172A"), ThemeService.BrushFromHex("#CBD5E1"), 12, new Thickness(12, 5, 12, 5), 8);
            openDirBtn.Margin = new Thickness(0, 0, 8, 0);
            openDirBtn.Click += delegate { _controller.OpenDataDirectory(); };

            var backupBtn = GlassControlStyles.CreateGlassButton("立即备份 data.json", ThemeService.BrushFromHex("#ECFDF5"), ThemeService.BrushFromHex("#047857"), ThemeService.BrushFromHex("#A7F3D0"), 12, new Thickness(12, 5, 12, 5), 8);
            backupBtn.Click += delegate
            {
                string path = _controller.Storage.CreateBackup();
                MessageBox.Show(string.IsNullOrEmpty(path) ? "当前暂无可备份文件。" : "已创建备份：\n" + path, "便携备份完成", MessageBoxButton.OK, MessageBoxImage.Information);
            };
            dataBtns.Children.Add(openDirBtn);
            dataBtns.Children.Add(backupBtn);
            dataStack.Children.Add(dataBtns);
            stack.Children.Add(dataCard);

            _settingsView.Content = stack;
        }

        private void SwitchTab(int idx)
        {
            if (idx == _activeTabIndex) return;
            int prevIdx = _activeTabIndex;
            _activeTabIndex = idx;

            _quadrantsView.Visibility = idx == 0 ? Visibility.Visible : Visibility.Collapsed;
            _scheduleView.Visibility = idx == 1 ? Visibility.Visible : Visibility.Collapsed;
            _stickiesView.Visibility = idx == 2 ? Visibility.Visible : Visibility.Collapsed;
            _settingsView.Visibility = idx == 3 ? Visibility.Visible : Visibility.Collapsed;

            _quickAddBar.Visibility = (idx == 0 || idx == 1) ? Visibility.Visible : Visibility.Collapsed;
            if (idx == 3) LoadSettingsInputs();

            ApplyThemeAndRefreshAll();

            // iOS Physics Transitions: Dock sliding pill + View directional spring glide
            UpdateDockIndicator(true);
            UIElement activeView = idx == 0 ? (UIElement)_quadrantsView : (idx == 1 ? (UIElement)_scheduleView : (idx == 2 ? (UIElement)_stickiesView : _settingsView));
            double offset = idx > prevIdx ? 26.0 : -26.0;
            SpringAnimationHelper.AnimateViewTransition(activeView, offset, 340);
        }

        private void UpdateDockIndicator(bool animate)
        {
            if (_dockContainer == null || _dockIndicatorPill == null) return;
            Dispatcher.BeginInvoke(new Action(delegate
            {
                try
                {
                    Button targetBtn = _navTabButtons[_activeTabIndex];
                    if (targetBtn == null || targetBtn.ActualWidth <= 0) return;
                    Point rel = targetBtn.TransformToAncestor((Visual)_dockIndicatorPill.Parent).Transform(new Point(0, 0));
                    if (animate)
                    {
                        SpringAnimationHelper.AnimateSlideIndicator(_dockIndicatorPill, rel.X, targetBtn.ActualWidth, 340);
                    }
                    else
                    {
                        _dockIndicatorPill.RenderTransform = new TranslateTransform(rel.X, 0);
                        _dockIndicatorPill.Width = targetBtn.ActualWidth;
                    }
                }
                catch { }
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private void AddQuickTask()
        {
            string title = (_quickTitleBox.Text ?? "").Trim();
            if (string.IsNullOrEmpty(title)) return;

            int q = _quickQuadBox.SelectedIndex == 4 ? 0 : MemoTask.ClampQuadrant(_quickQuadBox.SelectedIndex + 1);
            var task = new MemoTask
            {
                Title = title,
                Quadrant = q,
                DeadlineIso = MemoTask.FormatIso(ResolveQuickDdl(_quickDdlBox.SelectedIndex))
            };
            _quickTitleBox.Text = "";
            _quickDdlBox.SelectedIndex = 0;
            _controller.AddTask(task);
        }

        private static DateTime? ResolveQuickDdl(int index)
        {
            DateTime now = DateTime.Now;
            switch (index)
            {
                case 1: return now.AddHours(1);
                case 2: return DateTime.Today.AddHours(18);
                case 3: return DateTime.Today.AddHours(23).AddMinutes(59);
                case 4: return DateTime.Today.AddDays(1).AddHours(12);
                case 5: return DateTime.Today.AddDays(1).AddHours(23).AddMinutes(59);
                case 6: return DateTime.Today.AddDays(3).AddHours(20);
                default: return null;
            }
        }

        public void TriggerAiSortFromOutside()
        {
            OnAiSortClicked(this, new RoutedEventArgs());
        }

        private async void OnAiSortClicked(object sender, RoutedEventArgs e)
        {
            if (_activeTabIndex == 3) SaveAiInputsToConfig();

            _aiSortBtn.IsEnabled = false;
            _aiSortBtn.Content = "⏳ AI 正在评估...";
            _aiStatusBanner.Text = "正在调用 " + _controller.Config.AiModel + " 分析任务（含未分类便签待办）的 DDL 紧迫度与四象限...";
            _aiStatusBanner.Foreground = ThemeService.BrushFromHex("#6D28D9");
            _aiStatusBanner.Visibility = Visibility.Visible;

            try
            {
                AiSortOutcome outcome = await _controller.RunAiSortAsync();
                if (outcome.Success)
                {
                    _aiStatusBanner.Text = string.Format("✓ AI 已完成 {0} 条待办的四象限与优先级评分排序（可点击「撤销AI排序」恢复）。", outcome.Items.Count);
                    _aiStatusBanner.Foreground = ThemeService.BrushFromHex("#059669");
                }
                else
                {
                    _aiStatusBanner.Text = "⚠ " + outcome.ErrorMessage;
                    _aiStatusBanner.Foreground = ThemeService.BrushFromHex("#DC2626");
                }
            }
            catch (Exception ex)
            {
                _aiStatusBanner.Text = "⚠ AI 排序异常：" + ex.Message;
                _aiStatusBanner.Foreground = ThemeService.BrushFromHex("#DC2626");
            }
            finally
            {
                _aiSortBtn.IsEnabled = true;
                _aiSortBtn.Content = "✨ AI 智能排序";
                _undoAiBtn.Visibility = _controller.CanUndoAiSort ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private async void OnTestAiConnectionClicked(object sender, RoutedEventArgs e)
        {
            SaveAiInputsToConfig();
            _aiTestResultText.Text = "正在测试连接...";
            _aiTestResultText.Foreground = ThemeService.BrushFromHex("#475569");
            _aiTestResultText.Visibility = Visibility.Visible;

            AiConnectionTestOutcome res = await AiSortService.TestConnectionAsync(_controller.Config);
            _aiTestResultText.Text = res.Message;
            _aiTestResultText.Foreground = ThemeService.BrushFromHex(res.Success ? "#059669" : "#DC2626");
        }

        private void SaveAiInputsToConfig()
        {
            AppConfig cfg = _controller.Config;
            cfg.AiBaseUrl = (_aiBaseUrlBox.Text ?? "").Trim();
            cfg.AiApiKey = _showPlainKey ? (_aiKeyPlainBox.Text ?? "").Trim() : (_aiKeyPasswordBox.Password ?? "").Trim();
            cfg.AiModel = (_aiModelBox.Text ?? "").Trim();
            cfg.AiCustomInstruction = (_aiCustomPromptBox.Text ?? "").Trim();
            _controller.ScheduleConfigSave();
        }

        private void SaveServerInputsToConfig()
        {
            AppConfig cfg = _controller.Config;
            int port;
            if (int.TryParse((_serverPortBox.Text ?? "").Trim(), out port) && port >= 1024 && port <= 65535)
            {
                cfg.ServerPort = port;
            }
            cfg.ServerPassword = (_serverPassBox.Text ?? "").Trim();
            cfg.RemoteSyncEnabled = _remoteSyncBox.IsChecked == true;
            cfg.RemoteServerUrl = (_remoteUrlBox.Text ?? "").Trim().TrimEnd('/');
            cfg.RemoteServerPassword = (_remotePassBox.Text ?? "").Trim();
            _controller.ScheduleConfigSave();
        }

        private void LoadSettingsInputs()
        {
            _suppressSettingEvents = true;
            try
            {
                AppConfig cfg = _controller.Config;
                for (int i = 0; i < _aiPresetCombo.Items.Count; i++)
                {
                    var item = _aiPresetCombo.Items[i] as ComboBoxItem;
                    if (item != null && string.Equals(item.Tag as string, cfg.AiProviderPreset, StringComparison.Ordinal))
                    {
                        _aiPresetCombo.SelectedIndex = i;
                        break;
                    }
                }
                _aiBaseUrlBox.Text = cfg.AiBaseUrl ?? "";
                _aiKeyPasswordBox.Password = cfg.AiApiKey ?? "";
                _aiKeyPlainBox.Text = cfg.AiApiKey ?? "";
                _aiModelBox.Text = cfg.AiModel ?? "";
                _aiCustomPromptBox.Text = cfg.AiCustomInstruction ?? "";

                bool regAuto = Win32Native.IsAutoStartEnabled();
                _autoStartBox.IsChecked = regAuto;
                _autoStartStatusText.Text = regAuto ? "✓ 当前已开启开机自动启动" : "当前未开启开机自启（勾选即可开启）";
                _autoStartStatusText.Foreground = ThemeService.BrushFromHex(regAuto ? "#059669" : "#64748B");

                _hotkeysBox.IsChecked = cfg.HotkeysEnabled;
                _soundBox.IsChecked = cfg.ReminderSoundEnabled;

                string normTheme = ThemeService.NormalizeThemeKey(cfg.PanelTheme);
                for (int i = 0; i < _panelThemeCombo.Items.Count; i++)
                {
                    var item = _panelThemeCombo.Items[i] as ComboBoxItem;
                    if (item != null && string.Equals(item.Tag as string, normTheme, StringComparison.Ordinal))
                    {
                        _panelThemeCombo.SelectedIndex = i;
                        break;
                    }
                }
                for (int i = 0; i < _uiScaleCombo.Items.Count; i++)
                {
                    var item = _uiScaleCombo.Items[i] as ComboBoxItem;
                    if (item != null && string.Equals(item.Tag as string, cfg.UiScale, StringComparison.Ordinal))
                    {
                        _uiScaleCombo.SelectedIndex = i;
                        break;
                    }
                }

                _panelFontSlider.Value = cfg.PanelFontSize;
                _panelFontText.Text = string.Format("全局字号({0}px)：", (int)cfg.PanelFontSize);
                _panelOpacitySlider.Value = cfg.PanelOpacity;
                _panelOpacityText.Text = string.Format("窗口不透明度({0}%)：", (int)Math.Round(cfg.PanelOpacity * 100));

                _serverPortBox.Text = cfg.ServerPort.ToString();
                _serverPassBox.Text = cfg.ServerPassword ?? "";
                _remoteSyncBox.IsChecked = cfg.RemoteSyncEnabled;
                _remoteUrlBox.Text = cfg.RemoteServerUrl ?? "";
                _remotePassBox.Text = cfg.RemoteServerPassword ?? "";
            }
            finally
            {
                _suppressSettingEvents = false;
            }
        }

        public void ApplyThemeAndRefreshAll()
        {
            AppConfig cfg = _controller.Config;
            ThemePalette pal = ThemeService.GetPalette(cfg.PanelTheme, cfg.PanelOpacity);
            UiScaleMetrics scale = ThemeService.GetScaleMetrics(cfg.UiScale, cfg.PanelFontSize);

            GlassControlStyles.ApplyToResources(Resources, pal, scale);

            _outerCard.Background = pal.CardBg;
            _outerCard.BorderBrush = pal.Border;
            _headerBar.Background = pal.HeaderBg;
            _headerBar.BorderBrush = pal.Border;

            _headerTitleText.Foreground = pal.TextPrimary;
            _headerTitleText.FontSize = scale.HeaderFontSize;
            _headerSummaryText.Foreground = pal.TextSecondary;
            _headerSummaryText.FontSize = scale.SmallFontSize + 0.5;
            _modeBoxBorder.Background = pal.PillGlassBg;

            UpdateModeButtonStyle(_modePanelBtn, cfg.DisplayMode == "panel", pal, scale);
            UpdateModeButtonStyle(_modeStickyBtn, cfg.DisplayMode == "sticky", pal, scale);
            UpdateModeButtonStyle(_modeBothBtn, cfg.DisplayMode == "both", pal, scale);

            _pinBtn.Foreground = pal.TextPrimary;
            _lockBtn.Foreground = pal.TextPrimary;
            _hideBtn.Foreground = pal.TextPrimary;

            // Floating Segmented Tab Dock
            _dockContainer.Background = pal.PillGlassBg;
            _dockContainer.BorderBrush = pal.Border;
            _dockIndicatorPill.Background = pal.ActivePillGlassBg;
            _dockIndicatorPill.BorderBrush = pal.Border;

            for (int i = 0; i < 4; i++)
            {
                bool active = i == _activeTabIndex;
                _navTabButtons[i].Background = Brushes.Transparent;
                _navTabButtons[i].Foreground = active ? pal.TextPrimary : pal.TextSecondary;
                _navTabButtons[i].FontWeight = active ? FontWeights.Bold : FontWeights.SemiBold;
                _navTabButtons[i].FontSize = scale.FontSize;
                _navTabButtons[i].Padding = scale.ButtonPadding;
            }

            _chatExtractBtn.Background = pal.SurfaceBg;
            _chatExtractBtn.Foreground = pal.TextPrimary;
            _chatExtractBtn.BorderBrush = pal.Border;
            _chatExtractBtn.BorderThickness = new Thickness(1);
            _chatExtractBtn.FontSize = scale.SmallFontSize + 1;
            _chatExtractBtn.Padding = scale.ButtonPadding;

            _aiSortBtn.Background = pal.AccentBg;
            _aiSortBtn.Foreground = pal.AccentFg;
            _aiSortBtn.FontSize = scale.SmallFontSize + 1;
            _aiSortBtn.Padding = scale.ButtonPadding;

            _undoAiBtn.Background = pal.SurfaceBg;
            _undoAiBtn.Foreground = pal.TextPrimary;
            _undoAiBtn.BorderBrush = pal.Border;
            _undoAiBtn.FontSize = scale.SmallFontSize + 0.5;
            _undoAiBtn.Padding = scale.SmallButtonPadding;

            _quickAddBar.Background = pal.SurfaceBg;
            _quickAddBar.BorderBrush = pal.Border;
            _qaAddBtn.Background = pal.AccentBg;
            _qaAddBtn.Foreground = pal.AccentFg;
            _qaAddBtn.FontSize = scale.FontSize;
            _qaAddBtn.Padding = scale.ButtonPadding;

            _qaDetailBtn.Background = pal.HoverBg;
            _qaDetailBtn.Foreground = pal.TextPrimary;
            _qaDetailBtn.BorderBrush = pal.Border;
            _qaDetailBtn.BorderThickness = new Thickness(1);
            _qaDetailBtn.FontSize = scale.SmallFontSize + 1;
            _qaDetailBtn.Padding = scale.ButtonPadding;

            _clearDoneBtn.Background = pal.SurfaceBg;
            _clearDoneBtn.Foreground = pal.TextSecondary;
            _clearDoneBtn.BorderBrush = pal.Border;
            _clearDoneBtn.BorderThickness = new Thickness(1);

            for (int i = 0; i < _settingsCards.Count; i++)
            {
                _settingsCards[i].Background = pal.SurfaceBg;
                _settingsCards[i].BorderBrush = pal.Border;
            }
            for (int i = 0; i < _settingsCardTitles.Count; i++) _settingsCardTitles[i].Foreground = pal.TextPrimary;
            for (int i = 0; i < _settingsLabels.Count; i++) _settingsLabels[i].Foreground = pal.TextSecondary;

            _undoAiBtn.Visibility = _controller.CanUndoAiSort ? Visibility.Visible : Visibility.Collapsed;

            UpdateHeaderButtons();
            UpdateDockIndicator(false);
            RefreshTaskViews();
            RefreshStickiesManagerView();
        }

        public void RefreshTaskViews()
        {
            AppData data = _controller.Data;
            AppConfig cfg = _controller.Config;
            ThemePalette pal = ThemeService.GetPalette(cfg.PanelTheme, cfg.PanelOpacity);
            UiScaleMetrics scale = ThemeService.GetScaleMetrics(cfg.UiScale, cfg.PanelFontSize);
            DateTime now = DateTime.Now;

            int pendingCount = data.Tasks.Count(t => !t.Completed);
            int urgentOrOverdue = data.Tasks.Count(t =>
            {
                if (t.Completed) return false;
                return t.GetDeadlineDisplay(now).UrgencyLevel >= 3;
            });
            _headerSummaryText.Text = string.Format("· {0} 条待办{1}",
                pendingCount,
                urgentOrOverdue > 0 ? string.Format(" · {0} 条临近/已逾期", urgentOrOverdue) : "");

            for (int q = 1; q <= 4; q++)
            {
                QuadrantVisual qv = ThemeService.GetQuadrantVisual(q, pal.IsDark);
                Border card = FindTaggedElement<Border>(_quadrantsView, "quadCard_" + q);
                Border qHeader = FindTaggedElement<Border>(_quadrantsView, "quadHeader_" + q);
                if (card != null)
                {
                    card.Background = ThemeService.BrushFromHexAlpha(qv.SoftBgHex, 235);
                    card.BorderBrush = pal.Border;
                }
                if (qHeader != null)
                {
                    qHeader.Background = ThemeService.BrushFromHexAlpha(qv.HeaderBgHex, 235);
                }

                List<MemoTask> qTasks = _controller.SortTaskList(
                    data.Tasks.Where(t => !t.Completed && t.Quadrant == q).ToList());
                _quadCountLabels[q - 1].Text = string.Format("{0} ({1})", qv.Title, qTasks.Count);
                _quadCountLabels[q - 1].Foreground = ThemeService.BrushFromHex(qv.BadgeFgHex);
                _quadCountLabels[q - 1].FontSize = scale.FontSize;
                _quadPinBtns[q - 1].Foreground = ThemeService.BrushFromHex(qv.BadgeFgHex);
                _quadAddBtns[q - 1].Foreground = ThemeService.BrushFromHex(qv.BadgeFgHex);

                StackPanel panel = _quadListPanels[q - 1];
                panel.Children.Clear();
                if (qTasks.Count == 0)
                {
                    panel.Children.Add(new TextBlock
                    {
                        Text = "暂无此象限待办",
                        FontFamily = ThemeService.AppFontFamily,
                        FontSize = scale.SmallFontSize + 0.5,
                        Foreground = pal.TextSecondary,
                        Margin = new Thickness(4, 10, 4, 4)
                    });
                }
                else
                {
                    for (int i = 0; i < qTasks.Count; i++)
                    {
                        panel.Children.Add(BuildTaskCard(qTasks[i], pal, scale, now, false));
                    }
                }
            }

            // Populate Schedule & DDL list
            _scheduleListPanel.Children.Clear();
            string kw = (_searchBox.Text ?? "").Trim().ToLowerInvariant();
            int fIdx = _filterCombo.SelectedIndex;

            IEnumerable<MemoTask> query = data.Tasks;
            if (fIdx == 0) query = query.Where(t => !t.Completed);
            else if (fIdx == 1) query = query.Where(t => !t.Completed && t.GetDeadlineDisplay(now).UrgencyLevel >= 2);
            else if (fIdx == 2) query = query.Where(t => !t.Completed && t.Quadrant == 0);
            else if (fIdx == 4) query = query.Where(t => t.Completed);

            if (!string.IsNullOrEmpty(kw))
            {
                query = query.Where(t =>
                    (t.Title ?? "").ToLowerInvariant().Contains(kw) ||
                    (t.Notes ?? "").ToLowerInvariant().Contains(kw) ||
                    (t.AiReason ?? "").ToLowerInvariant().Contains(kw));
            }

            List<MemoTask> list = _controller.SortTaskList(query.ToList());
            if (list.Count == 0)
            {
                _scheduleListPanel.Children.Add(new TextBlock
                {
                    Text = "当前筛选条件下没有匹配的日程。",
                    FontFamily = ThemeService.AppFontFamily,
                    FontSize = scale.FontSize,
                    Foreground = pal.TextSecondary,
                    Margin = new Thickness(8, 18, 8, 8)
                });
            }
            else
            {
                for (int i = 0; i < list.Count; i++)
                {
                    _scheduleListPanel.Children.Add(BuildTaskCard(list[i], pal, scale, now, true));
                }
            }
        }

        private UIElement BuildTaskCard(MemoTask task, ThemePalette pal, UiScaleMetrics scale, DateTime now, bool wideRow)
        {
            QuadrantVisual qv = ThemeService.GetQuadrantVisual(task.Quadrant, pal.IsDark);
            DeadlineDisplayInfo ddl = task.GetDeadlineDisplay(now);

            var card = new Border
            {
                Background = pal.SurfaceBg,
                BorderBrush = pal.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(scale.CornerRadius),
                Padding = scale.CardPadding,
                Margin = new Thickness(0, 0, 0, 6)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var cb = new CheckBox
            {
                IsChecked = task.Completed,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 2, 8, 0)
            };
            cb.Click += delegate { _controller.ToggleTaskCompleted(task.Id, cb.IsChecked == true); };
            Grid.SetColumn(cb, 0);
            grid.Children.Add(cb);

            var body = new StackPanel();
            var titleRow = new WrapPanel();
            if (wideRow)
            {
                titleRow.Children.Add(new Border
                {
                    Background = ThemeService.BrushFromHex(qv.BadgeBgHex),
                    CornerRadius = new CornerRadius(5),
                    Padding = new Thickness(6, 1, 6, 1),
                    Margin = new Thickness(0, 0, 6, 2),
                    Child = new TextBlock
                    {
                        Text = qv.ShortTitle,
                        FontFamily = ThemeService.AppFontFamily,
                        FontSize = scale.SmallFontSize,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = ThemeService.BrushFromHex(qv.BadgeFgHex)
                    }
                });
            }

            titleRow.Children.Add(new TextBlock
            {
                Text = task.Title,
                FontFamily = ThemeService.AppFontFamily,
                FontSize = wideRow ? scale.FontSize + 0.5 : scale.FontSize,
                FontWeight = FontWeights.SemiBold,
                Foreground = task.Completed ? pal.TextSecondary : pal.TextPrimary,
                TextWrapping = TextWrapping.Wrap,
                TextDecorations = task.Completed ? TextDecorations.Strikethrough : null
            });
            body.Children.Add(titleRow);

            if (wideRow && !string.IsNullOrWhiteSpace(task.Notes))
            {
                body.Children.Add(new TextBlock
                {
                    Text = task.Notes.Trim(),
                    FontFamily = ThemeService.AppFontFamily,
                    FontSize = scale.SmallFontSize + 0.5,
                    Foreground = pal.TextSecondary,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 2, 0, 0)
                });
            }

            if (!string.IsNullOrEmpty(ddl.Text) || !string.IsNullOrEmpty(task.RemindAtIso) || task.AiScore > 0)
            {
                var meta = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
                if (!string.IsNullOrEmpty(ddl.Text))
                {
                    meta.Children.Add(new Border
                    {
                        Background = ThemeService.BrushFromHex(ddl.BgHex),
                        CornerRadius = new CornerRadius(5),
                        Padding = new Thickness(6, 1, 6, 1),
                        Margin = new Thickness(0, 0, 5, 2),
                        Child = new TextBlock
                        {
                            Text = "DDL: " + ddl.Text,
                            FontFamily = ThemeService.AppFontFamily,
                            FontSize = scale.SmallFontSize,
                            FontWeight = FontWeights.SemiBold,
                            Foreground = ThemeService.BrushFromHex(ddl.FgHex)
                        }
                    });
                }
                if (!string.IsNullOrEmpty(task.RemindAtIso) && !task.Completed)
                {
                    meta.Children.Add(new Border
                    {
                        Background = pal.HoverBg,
                        CornerRadius = new CornerRadius(5),
                        Padding = new Thickness(5, 1, 5, 1),
                        Margin = new Thickness(0, 0, 5, 2),
                        Child = new TextBlock
                        {
                            Text = (task.Reminded ? "✓已提醒 " : "⏰ ") + task.RemindAtIso.Substring(5),
                            FontFamily = ThemeService.AppFontFamily,
                            FontSize = scale.SmallFontSize,
                            Foreground = pal.TextSecondary
                        }
                    });
                }
                if (task.AiScore > 0)
                {
                    string aiText = string.IsNullOrWhiteSpace(task.AiReason)
                        ? string.Format("AI {0}分", task.AiScore)
                        : string.Format("AI {0}分 · {1}", task.AiScore, task.AiReason);
                    meta.Children.Add(new Border
                    {
                        Background = ThemeService.BrushFromHex(pal.IsDark ? "#312E81" : "#EDE9FE"),
                        CornerRadius = new CornerRadius(5),
                        Padding = new Thickness(5, 1, 5, 1),
                        Margin = new Thickness(0, 0, 5, 2),
                        Child = new TextBlock
                        {
                            Text = aiText,
                            FontFamily = ThemeService.AppFontFamily,
                            FontSize = scale.SmallFontSize,
                            Foreground = ThemeService.BrushFromHex(pal.IsDark ? "#C4B5FD" : "#6D28D9")
                        }
                    });
                }
                body.Children.Add(meta);
            }

            body.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                if (e.ClickCount == 2) _controller.OpenEditTaskDialog(task, this);
            };
            Grid.SetColumn(body, 1);
            grid.Children.Add(body);

            var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
            var moveBtn = GlassControlStyles.CreateGlassButton("⇄", Brushes.Transparent, pal.TextSecondary, null, 11.5, new Thickness(4, 1, 4, 1), 6);
            moveBtn.ToolTip = "快速切换象限 (Q1→Q2→Q3→Q4)";
            moveBtn.Click += delegate
            {
                int nextQ = task.Quadrant == 0 ? 1 : (task.Quadrant % 4) + 1;
                _controller.MoveTaskQuadrant(task.Id, nextQ);
            };

            var editBtn = GlassControlStyles.CreateGlassButton("✎", Brushes.Transparent, pal.TextSecondary, null, 11.5, new Thickness(4, 1, 4, 1), 6);
            editBtn.ToolTip = "编辑日程与DDL";
            editBtn.Click += delegate { _controller.OpenEditTaskDialog(task, this); };

            var delBtn = GlassControlStyles.CreateGlassButton("×", Brushes.Transparent, pal.TextSecondary, null, 13.5, new Thickness(4, 0, 4, 0), 6);
            delBtn.ToolTip = "删除";
            delBtn.Click += delegate { _controller.RequestDeleteTask(task.Id, this); };

            actions.Children.Add(moveBtn);
            actions.Children.Add(editBtn);
            actions.Children.Add(delBtn);
            Grid.SetColumn(actions, 2);
            grid.Children.Add(actions);

            card.Child = grid;
            return card;
        }

        public void RefreshStickiesManagerView()
        {
            if (_stickiesListPanel == null) return;
            ThemePalette pal = ThemeService.GetPalette(_controller.Config.PanelTheme, _controller.Config.PanelOpacity);
            UiScaleMetrics scale = ThemeService.GetScaleMetrics(_controller.Config.UiScale, _controller.Config.PanelFontSize);
            _stickiesListPanel.Children.Clear();

            List<StickyNote> notes = _controller.Data.StickyNotes;
            if (notes.Count == 0)
            {
                _stickiesListPanel.Children.Add(new TextBlock
                {
                    Text = "还没有独立便利贴，点击上方「+ 新建纯待办便签」即可在桌面贴出一张！",
                    FontFamily = ThemeService.AppFontFamily,
                    FontSize = scale.FontSize,
                    Foreground = pal.TextSecondary,
                    Margin = new Thickness(6, 12, 6, 6)
                });
                return;
            }

            for (int i = 0; i < notes.Count; i++)
            {
                StickyNote note = notes[i];
                ThemePalette ntp = ThemeService.GetPalette(note.Theme, 1.0);

                var row = new Border
                {
                    Background = pal.SurfaceBg,
                    BorderBrush = pal.Border,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(scale.CornerRadius),
                    Padding = new Thickness(12, 9, 12, 9),
                    Margin = new Thickness(0, 0, 0, 8)
                };

                var dock = new DockPanel();
                var swatch = new Border
                {
                    Width = 18,
                    Height = 18,
                    CornerRadius = new CornerRadius(5),
                    Background = ThemeService.BrushFromHex(ntp.SwatchHex),
                    BorderBrush = pal.Border,
                    BorderThickness = new Thickness(1),
                    Margin = new Thickness(0, 0, 10, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                DockPanel.SetDock(swatch, Dock.Left);
                dock.Children.Add(swatch);

                var infoStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                infoStack.Children.Add(new TextBlock
                {
                    Text = string.IsNullOrWhiteSpace(note.Title) ? "（未命名便签）" : note.Title,
                    FontFamily = ThemeService.AppFontFamily,
                    FontSize = scale.FontSize + 0.5,
                    FontWeight = FontWeights.Bold,
                    Foreground = pal.TextPrimary
                });
                infoStack.Children.Add(new TextBlock
                {
                    Text = string.Format("类型：{0} · 主题：{1} · {2} · {3}",
                        StickyNote.GetFilterModeLabel(note.FilterMode),
                        ntp.DisplayName,
                        Win32Native.GetPinModeLabel(note.PinMode),
                        note.Locked ? "🔒已锁定" : "未锁定"),
                    FontFamily = ThemeService.AppFontFamily,
                    FontSize = scale.SmallFontSize,
                    Foreground = pal.TextSecondary
                });
                dock.Children.Add(infoStack);

                var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
                var visBtn = GlassControlStyles.CreateGlassButton(note.Visible ? "隐藏" : "贴到桌面",
                    note.Visible ? pal.HoverBg : pal.AccentBg,
                    note.Visible ? pal.TextPrimary : pal.AccentFg,
                    note.Visible ? pal.Border : null,
                    scale.SmallFontSize + 1, scale.SmallButtonPadding, 7);
                visBtn.Margin = new Thickness(0, 0, 6, 0);
                visBtn.Click += delegate
                {
                    if (!note.Visible && _controller.Config.DisplayMode == "panel")
                    {
                        _controller.SetDisplayMode("both");
                    }
                    _controller.SetStickyNoteVisible(note.Id, !note.Visible);
                };

                var delBtn = GlassControlStyles.CreateGlassButton("删除",
                    ThemeService.BrushFromHex("#FEE2E2"), ThemeService.BrushFromHex("#DC2626"),
                    ThemeService.BrushFromHex("#FCA5A5"), scale.SmallFontSize + 1, scale.SmallButtonPadding, 7);
                delBtn.Click += delegate
                {
                    if (MessageBox.Show("确定删除便签「" + note.Title + "」吗？", "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                    {
                        _controller.DeleteStickyNote(note.Id);
                    }
                };

                btns.Children.Add(visBtn);
                btns.Children.Add(delBtn);
                DockPanel.SetDock(btns, Dock.Right);
                dock.Children.Add(btns);

                row.Child = dock;
                _stickiesListPanel.Children.Add(row);
            }
        }

        private void UpdateHeaderButtons()
        {
            AppConfig cfg = _controller.Config;
            _pinBtn.Content = cfg.PanelPinMode == "topmost" ? "📍" : (cfg.PanelPinMode == "normal" ? "🗔" : "📌");
            _pinBtn.ToolTip = "当前：" + Win32Native.GetPinModeLabel(cfg.PanelPinMode) + "（点击切换）";
            _lockBtn.Content = cfg.PanelLocked ? "🔒" : "🔓";
            _lockBtn.ToolTip = cfg.PanelLocked ? "主面板已锁定位置与大小（点击解锁）" : "未锁定主面板（点击锁定防止误拖）";
        }

        private static void UpdateModeButtonStyle(Button btn, bool active, ThemePalette pal, UiScaleMetrics scale)
        {
            btn.Background = active ? pal.ActivePillGlassBg : Brushes.Transparent;
            btn.Foreground = active ? pal.TextPrimary : pal.TextSecondary;
            btn.FontWeight = active ? FontWeights.Bold : FontWeights.Normal;
            btn.FontSize = scale.SmallFontSize + 0.5;
            btn.Padding = scale.SmallButtonPadding;
        }

        private void SelectSortComboValue(string sortBy)
        {
            for (int i = 0; i < _sortCombo.Items.Count; i++)
            {
                var item = _sortCombo.Items[i] as ComboBoxItem;
                if (item != null && string.Equals(item.Tag as string, sortBy, StringComparison.Ordinal))
                {
                    _sortCombo.SelectedIndex = i;
                    return;
                }
            }
            _sortCombo.SelectedIndex = 0;
        }

        private static Button MakeModeButton(string text, string tooltip, Action onClick)
        {
            var b = GlassControlStyles.CreateGlassButton(text, Brushes.Transparent, Brushes.Black, null, 11.5, new Thickness(9, 3, 9, 3), 7);
            b.ToolTip = tooltip;
            b.Click += delegate { onClick(); };
            return b;
        }

        private static Button MakeIconBtn(string text, string tooltip, Action onClick)
        {
            var b = GlassControlStyles.CreateGlassButton(text, Brushes.Transparent, Brushes.Black, null, 13, new Thickness(6, 2, 6, 2), 7);
            b.ToolTip = tooltip;
            b.Click += delegate { onClick(); };
            return b;
        }

        private Border CreateSettingsCard(string title)
        {
            var card = new Border
            {
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(11),
                Padding = new Thickness(14, 12, 14, 12),
                Margin = new Thickness(0, 0, 0, 10)
            };
            _settingsCards.Add(card);
            var stack = new StackPanel();
            var titleTb = new TextBlock
            {
                Text = title,
                FontFamily = ThemeService.AppFontFamily,
                FontSize = 13.5,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 10)
            };
            _settingsCardTitles.Add(titleTb);
            stack.Children.Add(titleTb);
            card.Child = stack;
            return card;
        }

        private TextBlock MakeSettingLabel(string text)
        {
            var tb = new TextBlock
            {
                Text = text,
                FontFamily = ThemeService.AppFontFamily,
                FontSize = 12.5,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 4)
            };
            _settingsLabels.Add(tb);
            return tb;
        }

        private void AddSettingRow(Grid grid, string label, UIElement control)
        {
            int rowIdx = grid.RowDefinitions.Count;
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var tb = new TextBlock
            {
                Text = label,
                FontFamily = ThemeService.AppFontFamily,
                FontSize = 12.5,
                VerticalAlignment = VerticalAlignment.Center
            };
            _settingsLabels.Add(tb);
            Grid.SetRow(tb, rowIdx);
            Grid.SetColumn(tb, 0);
            grid.Children.Add(tb);

            Grid.SetRow(control, rowIdx);
            Grid.SetColumn(control, 1);
            grid.Children.Add(control);
        }

        private static T FindTaggedElement<T>(DependencyObject parent, string tag) where T : FrameworkElement
        {
            if (parent == null) return null;
            if (parent is Visual || parent is System.Windows.Media.Media3D.Visual3D)
            {
                int count = VisualTreeHelper.GetChildrenCount(parent);
                for (int i = 0; i < count; i++)
                {
                    DependencyObject child = VisualTreeHelper.GetChild(parent, i);
                    T typed = child as T;
                    if (typed != null && string.Equals(typed.Tag as string, tag, StringComparison.Ordinal))
                    {
                        return typed;
                    }
                    T deeper = FindTaggedElement<T>(child, tag);
                    if (deeper != null) return deeper;
                }
            }
            foreach (object obj in LogicalTreeHelper.GetChildren(parent))
            {
                DependencyObject d = obj as DependencyObject;
                if (d == null) continue;
                T typed = d as T;
                if (typed != null && string.Equals(typed.Tag as string, tag, StringComparison.Ordinal))
                {
                    return typed;
                }
                T deeper = FindTaggedElement<T>(d, tag);
                if (deeper != null) return deeper;
            }
            return null;
        }
    }
}
