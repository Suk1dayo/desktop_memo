using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace DesktopMemo
{
    public class DashboardWindow : Window
    {
        private readonly TrayAndAppController _controller;

        private bool _isInitializing = true;
        private bool _isAdjustingZ = false;
        private bool _suppressSettingEvents = false;
        private int _activeTabIndex = 0; // 0=Quadrants, 1=Schedule, 2=Stickies, 3=Settings
        private string _appliedThemeKey;
        private string _appliedControlStyleKey;
        private string _appliedPinMode;
        private bool _isImeComposing = false;
        private DispatcherTimer _fontApplyTimer;
        private DispatcherTimer _opacityRefreshTimer;
        private bool _isDraggingFontSlider = false;
        private bool _isDraggingOpacitySlider = false;

        public bool IsImeComposing { get { return _isImeComposing; } }

        private Border _outerCard;
        private Border _headerBar;
        private TextBlock _headerTitleText;
        private Border _headerVersionBadge;
        private TextBlock _headerVersionText;
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
        private readonly Button[] _navTabButtons = new Button[5];

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
        private Border _quadTodayCourseStrip;
        private WrapPanel _quadTodayCourseWrap;
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

        private Grid _timetableView;
        private TextBlock _ttWeekTitleText;
        private Grid _ttMatrixHost;
        private ScrollViewer _ttScroller;
        private Grid _ttWeekGrid;
        private Canvas _ttDragOverlayCanvas;
        private int _viewingTimetableWeek = 0;
        private Point _ttDragStartPoint;
        private Point _ttDragPointerOffset;
        private Point _ttLastHostPos;
        private CourseItem _ttPressedCourse;
        private Border _ttPressedCard;
        private DateTime? _ttPressedCardDate;
        private CourseItem _ttDraggingCourse;
        private Border _ttSourceCard;
        private Border _ttDragGhost;
        private Border _ttDropHighlight;
        private DispatcherTimer _ttAutoScrollTimer;
        private int _ttHoverDay = 0;
        private int _ttHoverSec = 0;
        private int _ttVisibleDays = 7;
        private int _ttMaxSections = 10;

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
        private TextBox _wxExportOutBox;
        private TextBlock _aiTestResultText;

        private CheckBox _misEnabledBox;
        private CheckBox _showCoursesQuadBox;
        private TextBlock _misSummaryText;
        private Border _misOptionsPanel;

        private CheckBox _voiceEnabledBox;
        private ComboBox _voiceModeCombo;
        private CheckBox _voiceAutoCommitBox;
        private CheckBox _hotkeysBox;
        private TextBlock _hotkeyStatusText;
        private readonly List<Action> _refreshHotkeyPills = new List<Action>();

        private CheckBox _autoStartBox;
        private TextBlock _autoStartStatusText;
        private TextBlock _shortcutStatusText;
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

            Title = "桌面备忘录 " + AppMeta.VersionTag + " · 智能四象限";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.CanResize;
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

            _fontApplyTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(140) };
            _fontApplyTimer.Tick += delegate
            {
                _fontApplyTimer.Stop();
                if (!_isDraggingFontSlider)
                {
                    _controller.ApplyAppearanceToAllWindows();
                }
            };
            _opacityRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(140) };
            _opacityRefreshTimer.Tick += delegate
            {
                _opacityRefreshTimer.Stop();
                if (!_isDraggingOpacitySlider)
                {
                    RefreshTaskViews();
                    RefreshStickiesManagerView();
                }
            };
            Closed += delegate
            {
                if (_fontApplyTimer != null) _fontApplyTimer.Stop();
                if (_opacityRefreshTimer != null) _opacityRefreshTimer.Stop();
                if (_ttAutoScrollTimer != null) _ttAutoScrollTimer.Stop();
            };

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
            TextCompositionManager.AddPreviewTextInputStartHandler(this, delegate { _isImeComposing = true; });
            TextCompositionManager.AddPreviewTextInputUpdateHandler(this, delegate(object s, TextCompositionEventArgs e)
            {
                _isImeComposing = (e.TextComposition != null && !string.IsNullOrEmpty(e.TextComposition.CompositionText));
            });
            TextCompositionManager.AddPreviewTextInputHandler(this, delegate { _isImeComposing = false; });
            Deactivated += delegate
            {
                _isImeComposing = false;
                if (_ttDraggingCourse != null) FinishTimetableCardDrag(false);
            };
            PreviewKeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.Key == Key.Escape)
                {
                    _isImeComposing = false;
                    if (_ttDraggingCourse != null)
                    {
                        FinishTimetableCardDrag(false);
                        e.Handled = true;
                    }
                }
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
            AppConfig cfg = _controller.Config;
            return Win32Native.HandleDesktopAndResizeMessages(
                this, hwnd, msg, wParam, lParam,
                cfg.PanelPinMode, cfg.PanelLocked, false, _isAdjustingZ, 10.0, ref handled);
        }

        public void ApplyPinModeNow()
        {
            Win32Native.ApplyPinMode(this, _controller.Config.PanelPinMode, ref _isAdjustingZ);
            _appliedPinMode = _controller.Config.PanelPinMode;
            UpdateHeaderButtons();
        }

        public void EnsurePinModeApplied()
        {
            if (!string.Equals(_appliedPinMode, _controller.Config.PanelPinMode, StringComparison.Ordinal))
            {
                ApplyPinModeNow();
            }
            else
            {
                UpdateHeaderButtons();
            }
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
                Margin = new Thickness(0),
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

            _headerVersionText = new TextBlock
            {
                Text = AppMeta.VersionTag,
                FontFamily = ThemeService.AppFontFamily,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold
            };
            _headerVersionBadge = new Border
            {
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(6, 1, 6, 1),
                Margin = new Thickness(8, 1, 0, 0),
                BorderThickness = new Thickness(1),
                VerticalAlignment = VerticalAlignment.Center,
                Child = _headerVersionText
            };
            titleStack.Children.Add(_headerVersionBadge);

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
            string[] tabTitles = new string[] { "四象限矩阵", "日程 & DDL", "课程表", "独立便利贴", "AI 与设置" };
            for (int i = 0; i < 5; i++)
            {
                int idx = i;
                var tb = GlassControlStyles.CreateGlassButton(tabTitles[i], Brushes.Transparent, Brushes.Black, null, 13, new Thickness(14, 6, 14, 6), 8);
                tb.Margin = new Thickness(0, 0, 2, 0);
                tb.FontWeight = FontWeights.SemiBold;
                tb.Click += delegate { SwitchTab(idx); };
                if (i == 2 && !_controller.Config.MisEnabled)
                {
                    tb.Visibility = Visibility.Collapsed;
                }
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

            _chatExtractBtn = GlassControlStyles.CreateGlassButton("💬 AI 识图/提取",
                Brushes.Transparent, Brushes.Black, Brushes.Gray, 12.5, new Thickness(11, 6, 11, 6), 9);
            _chatExtractBtn.Margin = new Thickness(0, 0, 6, 0);
            _chatExtractBtn.ToolTip = "粘贴聊天内容、导入导出文件或贴截图识别日程";
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
            BuildTimetableView();
            BuildStickiesView();
            BuildSettingsView();

            Grid.SetRow(_quadrantsView, 3);
            Grid.SetRow(_scheduleView, 3);
            Grid.SetRow(_timetableView, 3);
            Grid.SetRow(_stickiesView, 3);
            Grid.SetRow(_settingsView, 3);

            _mainContentGrid.Children.Add(_quadrantsView);
            _mainContentGrid.Children.Add(_scheduleView);
            _mainContentGrid.Children.Add(_timetableView);
            _mainContentGrid.Children.Add(_stickiesView);
            _mainContentGrid.Children.Add(_settingsView);

            rootDock.Children.Add(_mainContentGrid);
            _outerCard.Child = rootDock;
            Content = new Border
            {
                Margin = new Thickness(7),
                Padding = new Thickness(3),
                CornerRadius = new CornerRadius(16),
                Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)),
                Child = _outerCard
            };
        }

        private void BuildQuadrantsView()
        {
            _quadrantsView = new Grid();
            _quadrantsView.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _quadrantsView.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _quadrantsView.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _quadrantsView.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            _quadrantsView.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            _quadTodayCourseStrip = new Border
            {
                CornerRadius = new CornerRadius(9),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(0, 0, 0, 8),
                Visibility = Visibility.Collapsed
            };
            _quadTodayCourseWrap = new WrapPanel();
            _quadTodayCourseStrip.Child = _quadTodayCourseWrap;
            Grid.SetRow(_quadTodayCourseStrip, 0);
            Grid.SetColumnSpan(_quadTodayCourseStrip, 2);
            _quadrantsView.Children.Add(_quadTodayCourseStrip);

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
                Grid.SetRow(card, 1 + (q - 1) / 2);
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

        private void BuildTimetableView()
        {
            _timetableView = new Grid { Visibility = Visibility.Collapsed };
            _timetableView.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _timetableView.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var topBar = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };

            var navRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var prevBtn = GlassControlStyles.CreateGlassButton("◀ 上一周",
                ThemeService.BrushFromHex("#F1F5F9"), ThemeService.BrushFromHex("#334155"), ThemeService.BrushFromHex("#CBD5E1"),
                12, new Thickness(10, 4, 10, 4), 7);
            prevBtn.Margin = new Thickness(0, 0, 6, 0);
            prevBtn.Click += delegate
            {
                if (_viewingTimetableWeek <= 1) _viewingTimetableWeek = 1;
                else _viewingTimetableWeek--;
                RefreshTimetableView();
            };

            _ttWeekTitleText = new TextBlock
            {
                FontFamily = ThemeService.AppFontFamily,
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 6, 0),
                Text = "第 1 周"
            };

            var nextBtn = GlassControlStyles.CreateGlassButton("下一周 ▶",
                ThemeService.BrushFromHex("#F1F5F9"), ThemeService.BrushFromHex("#334155"), ThemeService.BrushFromHex("#CBD5E1"),
                12, new Thickness(10, 4, 10, 4), 7);
            nextBtn.Margin = new Thickness(6, 0, 6, 0);
            nextBtn.Click += delegate
            {
                int maxW = _controller.Data.Timetable != null && _controller.Data.Timetable.TotalWeeks > 0 ? _controller.Data.Timetable.TotalWeeks : 20;
                if (_viewingTimetableWeek < maxW) _viewingTimetableWeek++;
                RefreshTimetableView();
            };

            var curBtn = GlassControlStyles.CreateGlassButton("本周",
                ThemeService.BrushFromHex("#EFF6FF"), ThemeService.BrushFromHex("#1D4ED8"), ThemeService.BrushFromHex("#BFDBFE"),
                12, new Thickness(10, 4, 10, 4), 7);
            curBtn.Click += delegate
            {
                _viewingTimetableWeek = 0;
                RefreshTimetableView();
            };

            navRow.Children.Add(prevBtn);
            navRow.Children.Add(_ttWeekTitleText);
            navRow.Children.Add(nextBtn);
            navRow.Children.Add(curBtn);
            DockPanel.SetDock(navRow, Dock.Left);
            topBar.Children.Add(navRow);

            var rightBtns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            var addCourseBtn = GlassControlStyles.CreateGlassButton("+ 加课",
                ThemeService.BrushFromHex("#F1F5F9"), ThemeService.BrushFromHex("#334155"), ThemeService.BrushFromHex("#CBD5E1"),
                12, new Thickness(10, 4, 10, 4), 7);
            addCourseBtn.Margin = new Thickness(0, 0, 6, 0);
            addCourseBtn.ToolTip = "手动添加一门课程";
            addCourseBtn.Click += delegate
            {
                var draft = new CourseItem { DayOfWeek = 1, StartSection = 1, EndSection = 2 };
                if (MisImportDialog.OpenCourseEditorDialog(this, _controller.Data.Timetable, _controller.Config, draft, true))
                {
                    _controller.SaveTimetable(_controller.Data.Timetable);
                    RefreshTaskViews();
                }
            };

            var ttSettingsBtn = GlassControlStyles.CreateGlassButton("⚙️ 课表设置",
                ThemeService.BrushFromHex("#F1F5F9"), ThemeService.BrushFromHex("#334155"), ThemeService.BrushFromHex("#CBD5E1"),
                12, new Thickness(10, 4, 10, 4), 7);
            ttSettingsBtn.Margin = new Thickness(0, 0, 6, 0);
            ttSettingsBtn.ToolTip = "设置每天节数、周末显示、卡片信息与作息时间";
            ttSettingsBtn.Click += delegate
            {
                if (MisImportDialog.OpenTimetableSettingsDialog(this, _controller.Data.Timetable, _controller.Config))
                {
                    _controller.SaveTimetable(_controller.Data.Timetable);
                    RefreshTaskViews();
                }
            };

            var stickyBtn = GlassControlStyles.CreateGlassButton("📌 贴课表便签",
                ThemeService.BrushFromHex("#EFF6FF"), ThemeService.BrushFromHex("#1D4ED8"), ThemeService.BrushFromHex("#BFDBFE"),
                12, new Thickness(10, 4, 10, 4), 7);
            stickyBtn.Margin = new Thickness(0, 0, 6, 0);
            stickyBtn.Click += delegate { _controller.OpenOrFocusTimetableStickyNote(); };

            var syncBtn = GlassControlStyles.CreateGlassButton("🔄 MIS 导入",
                ThemeService.BrushFromHex("#2563EB"), Brushes.White, null,
                12, new Thickness(12, 4, 12, 4), 7);
            syncBtn.FontWeight = FontWeights.SemiBold;
            syncBtn.Click += delegate { _controller.OpenMisImportDialog(this); };

            rightBtns.Children.Add(addCourseBtn);
            rightBtns.Children.Add(ttSettingsBtn);
            rightBtns.Children.Add(stickyBtn);
            rightBtns.Children.Add(syncBtn);
            topBar.Children.Add(rightBtns);

            Grid.SetRow(topBar, 0);
            _timetableView.Children.Add(topBar);

            _ttMatrixHost = new Grid { ClipToBounds = true };
            _ttScroller = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            _ttWeekGrid = new Grid();
            _ttScroller.Content = _ttWeekGrid;
            _ttMatrixHost.Children.Add(_ttScroller);

            _ttDragOverlayCanvas = new Canvas
            {
                IsHitTestVisible = false,
                ClipToBounds = true
            };
            Panel.SetZIndex(_ttDragOverlayCanvas, 100);
            _ttMatrixHost.Children.Add(_ttDragOverlayCanvas);

            _ttMatrixHost.MouseMove += OnTimetableHostMouseMove;
            _ttMatrixHost.MouseLeftButtonUp += OnTimetableHostMouseLeftButtonUp;
            _ttMatrixHost.LostMouseCapture += OnTimetableHostLostMouseCapture;

            _ttAutoScrollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _ttAutoScrollTimer.Tick += OnTimetableAutoScrollTick;

            Grid.SetRow(_ttMatrixHost, 1);
            _timetableView.Children.Add(_ttMatrixHost);
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

            var newTtBtn = GlassControlStyles.CreateGlassButton("+ 贴出「今日课表」",
                ThemeService.BrushFromHex("#F1F5F9"), ThemeService.BrushFromHex("#334155"),
                ThemeService.BrushFromHex("#CBD5E1"), 12, new Thickness(10, 6, 10, 6), 8);
            newTtBtn.Margin = new Thickness(0, 0, 8, 4);
            newTtBtn.Click += delegate { _controller.OpenOrFocusTimetableStickyNote(); };

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
            lockStickiesBtn.ToolTip = "仅锁定所有独立桌面便签，不影响主面板";
            lockStickiesBtn.Click += delegate { _controller.SetAllStickiesLocked(true); };

            var unlockStickiesBtn = GlassControlStyles.CreateGlassButton("解锁全部便签",
                ThemeService.BrushFromHex("#F1F5F9"), ThemeService.BrushFromHex("#334155"),
                ThemeService.BrushFromHex("#CBD5E1"), 12, new Thickness(10, 6, 10, 6), 8);
            unlockStickiesBtn.Margin = new Thickness(0, 0, 0, 4);
            unlockStickiesBtn.Click += delegate { _controller.SetAllStickiesLocked(false); };

            toolbar.Children.Add(newPureBtn);
            toolbar.Children.Add(newRichBtn);
            toolbar.Children.Add(newAllBtn);
            toolbar.Children.Add(newTtBtn);
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

            // Section 1: AI Settings (DeepSeek prioritized)
            var aiCard = CreateSettingsCard("🤖 AI 配置");
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

            var wxOutRow = new Grid { Margin = new Thickness(0, 3, 0, 8) };
            wxOutRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            wxOutRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _wxExportOutBox = new TextBox
            {
                Padding = new Thickness(8, 5, 8, 5),
                ToolTip = "可选：聊天记录导出默认根目录（支持 .md / .jsonl / .txt），便于在「💬 AI提取」一键加载最新导出"
            };
            _wxExportOutBox.LostFocus += delegate { if (!_suppressSettingEvents) SaveAiInputsToConfig(); };
            Grid.SetColumn(_wxExportOutBox, 0);
            wxOutRow.Children.Add(_wxExportOutBox);
            var browseWxOutBtn = GlassControlStyles.CreateGlassButton("浏览...",
                ThemeService.BrushFromHex("#F1F5F9"), ThemeService.BrushFromHex("#334155"), ThemeService.BrushFromHex("#CBD5E1"),
                11.5, new Thickness(10, 4, 10, 4), 7);
            browseWxOutBtn.Margin = new Thickness(6, 0, 0, 0);
            browseWxOutBtn.Click += delegate
            {
                using (var fbd = new System.Windows.Forms.FolderBrowserDialog { Description = "选择聊天记录默认导出目录" })
                {
                    if (!string.IsNullOrWhiteSpace(_wxExportOutBox.Text) && Directory.Exists(_wxExportOutBox.Text.Trim()))
                    {
                        fbd.SelectedPath = _wxExportOutBox.Text.Trim();
                    }
                    if (fbd.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    {
                        _wxExportOutBox.Text = fbd.SelectedPath;
                        SaveAiInputsToConfig();
                    }
                }
            };
            Grid.SetColumn(browseWxOutBtn, 1);
            wxOutRow.Children.Add(browseWxOutBtn);
            AddSettingRow(aiGrid, "导出默认目录：", wxOutRow);

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

            // Section 2: MIS Timetable Integration
            var misCard = CreateSettingsCard("📅 校园 MIS 课程表与教学周");
            var misStack = (StackPanel)misCard.Child;

            _misEnabledBox = new CheckBox
            {
                Content = "接入学校 MIS 课程表",
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 2, 0, 6)
            };
            _misEnabledBox.Click += delegate
            {
                if (_suppressSettingEvents) return;
                bool enabled = _misEnabledBox.IsChecked == true;
                _controller.Config.MisEnabled = enabled;
                _controller.ScheduleConfigSave();
                _controller.RebuildTrayMenu();
                UpdateMisTabVisibility();
                if (_misOptionsPanel != null) _misOptionsPanel.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
                if (enabled)
                {
                    _controller.OpenMisImportDialog(this);
                }
                else
                {
                    RefreshTaskViews();
                }
            };
            misStack.Children.Add(_misEnabledBox);

            _misOptionsPanel = new Border
            {
                Margin = new Thickness(20, 2, 0, 0),
                Visibility = Visibility.Collapsed
            };
            var misOptStack = new StackPanel();

            _misSummaryText = new TextBlock
            {
                FontFamily = ThemeService.AppFontFamily,
                FontSize = 12,
                Foreground = ThemeService.BrushFromHex("#64748B"),
                Margin = new Thickness(0, 0, 0, 8)
            };
            misOptStack.Children.Add(_misSummaryText);

            var misBtnRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            var openMisDlgBtn = GlassControlStyles.CreateGlassButton("🌐 打开 MIS 登录与课表同步",
                ThemeService.BrushFromHex("#EFF6FF"), ThemeService.BrushFromHex("#1D4ED8"), ThemeService.BrushFromHex("#BFDBFE"),
                12, new Thickness(12, 5, 12, 5), 8);
            openMisDlgBtn.Margin = new Thickness(0, 0, 8, 0);
            openMisDlgBtn.Click += delegate { _controller.OpenMisImportDialog(this); };

            var pinCourseStickyBtn = GlassControlStyles.CreateGlassButton("📌 贴桌面课表便签",
                ThemeService.BrushFromHex("#F1F5F9"), ThemeService.BrushFromHex("#334155"), ThemeService.BrushFromHex("#CBD5E1"),
                12, new Thickness(12, 5, 12, 5), 8);
            pinCourseStickyBtn.Click += delegate { _controller.OpenOrFocusTimetableStickyNote(); };

            misBtnRow.Children.Add(openMisDlgBtn);
            misBtnRow.Children.Add(pinCourseStickyBtn);
            misOptStack.Children.Add(misBtnRow);

            _showCoursesQuadBox = new CheckBox
            {
                Content = "四象限顶部显示今日课程",
                Margin = new Thickness(0, 2, 0, 2)
            };
            _showCoursesQuadBox.Click += delegate
            {
                if (_suppressSettingEvents) return;
                _controller.Config.ShowTodayCoursesInQuadrants = _showCoursesQuadBox.IsChecked == true;
                _controller.ScheduleConfigSave();
                RefreshTaskViews();
            };
            misOptStack.Children.Add(_showCoursesQuadBox);
            _misOptionsPanel.Child = misOptStack;
            misStack.Children.Add(_misOptionsPanel);
            stack.Children.Add(misCard);

            // Section 3: Voice Quick Memo & Custom Global Hotkeys
            var hkCard = CreateSettingsCard("🎙 语音快捷备忘与全局快捷键");
            var hkStack = (StackPanel)hkCard.Child;

            _voiceEnabledBox = new CheckBox
            {
                Content = "启用语音快捷备忘（需已配置 AI）",
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 2, 0, 6)
            };
            _voiceEnabledBox.Click += delegate
            {
                if (_suppressSettingEvents) return;
                _controller.Config.VoiceMemoEnabled = _voiceEnabledBox.IsChecked == true;
                _controller.RefreshGlobalHotkeys();
                _controller.ScheduleConfigSave();
                UpdateHotkeyStatusLabel();
            };
            hkStack.Children.Add(_voiceEnabledBox);

            var voiceCfgRow = new WrapPanel { Margin = new Thickness(20, 0, 0, 8) };
            voiceCfgRow.Children.Add(MakeSettingLabel("触发方式："));
            _voiceModeCombo = new ComboBox { Width = 165, Margin = new Thickness(0, 0, 14, 4) };
            _voiceModeCombo.Items.Add(new ComboBoxItem { Content = "点按两次发送", Tag = "toggle" });
            _voiceModeCombo.Items.Add(new ComboBoxItem { Content = "长按松手直接发送", Tag = "hold" });
            _voiceModeCombo.SelectionChanged += delegate
            {
                if (_suppressSettingEvents) return;
                var sel = _voiceModeCombo.SelectedItem as ComboBoxItem;
                if (sel != null && sel.Tag != null)
                {
                    _controller.Config.VoiceTriggerMode = sel.Tag.ToString();
                    _controller.ScheduleConfigSave();
                }
            };
            voiceCfgRow.Children.Add(_voiceModeCombo);

            _voiceAutoCommitBox = new CheckBox
            {
                Content = "解析后直接添加日程",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 4)
            };
            _voiceAutoCommitBox.Click += delegate
            {
                if (_suppressSettingEvents) return;
                _controller.Config.VoiceAutoCommit = _voiceAutoCommitBox.IsChecked == true;
                _controller.ScheduleConfigSave();
            };
            voiceCfgRow.Children.Add(_voiceAutoCommitBox);
            hkStack.Children.Add(voiceCfgRow);

            _hotkeysBox = new CheckBox
            {
                Content = "启用全局快捷键",
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 2, 0, 6)
            };
            _hotkeysBox.Click += delegate
            {
                if (_suppressSettingEvents) return;
                _controller.Config.HotkeysEnabled = _hotkeysBox.IsChecked == true;
                _controller.RefreshGlobalHotkeys();
                _controller.ScheduleConfigSave();
                UpdateHotkeyStatusLabel();
            };
            hkStack.Children.Add(_hotkeysBox);

            _refreshHotkeyPills.Clear();
            BuildHotkeyRecorderRow(hkStack, "语音备忘：", delegate { return _controller.Config.HotkeyVoiceMemo; }, delegate(string v) { _controller.Config.HotkeyVoiceMemo = v; });
            BuildHotkeyRecorderRow(hkStack, "新建日程：", delegate { return _controller.Config.HotkeyNewTask; }, delegate(string v) { _controller.Config.HotkeyNewTask = v; });
            BuildHotkeyRecorderRow(hkStack, "显隐全部：", delegate { return _controller.Config.HotkeyToggleAll; }, delegate(string v) { _controller.Config.HotkeyToggleAll = v; });
            BuildHotkeyRecorderRow(hkStack, "打开主面板：", delegate { return _controller.Config.HotkeyShowPanel; }, delegate(string v) { _controller.Config.HotkeyShowPanel = v; });
            BuildHotkeyRecorderRow(hkStack, "新建便签：", delegate { return _controller.Config.HotkeyNewSticky; }, delegate(string v) { _controller.Config.HotkeyNewSticky = v; });

            var hkFooter = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
            var resetHkBtn = GlassControlStyles.CreateGlassButton("恢复默认快捷键",
                ThemeService.BrushFromHex("#F1F5F9"), ThemeService.BrushFromHex("#334155"), ThemeService.BrushFromHex("#CBD5E1"),
                11.5, new Thickness(10, 4, 10, 4), 7);
            resetHkBtn.Click += delegate
            {
                _controller.Config.HotkeyNewTask = "Ctrl+Alt+N";
                _controller.Config.HotkeyToggleAll = "Ctrl+Alt+M";
                _controller.Config.HotkeyShowPanel = "Ctrl+Alt+P";
                _controller.Config.HotkeyNewSticky = "Ctrl+Alt+S";
                _controller.Config.HotkeyVoiceMemo = "Ctrl+Alt+V";
                for (int i = 0; i < _refreshHotkeyPills.Count; i++) _refreshHotkeyPills[i]();
                _controller.RefreshGlobalHotkeys();
                _controller.ScheduleConfigSave();
                UpdateHotkeyStatusLabel();
            };
            hkFooter.Children.Add(resetHkBtn);

            _hotkeyStatusText = new TextBlock
            {
                FontFamily = ThemeService.AppFontFamily,
                FontSize = 11.5,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 0, 0),
                TextWrapping = TextWrapping.Wrap,
                Foreground = ThemeService.BrushFromHex("#64748B")
            };
            hkFooter.Children.Add(_hotkeyStatusText);
            hkStack.Children.Add(hkFooter);
            stack.Children.Add(hkCard);

            // Section 4: Appearance & System (cleaned outdated copy)
            var sysCard = CreateSettingsCard("🎨 外观与系统");
            var sysStack = (StackPanel)sysCard.Child;

            _autoStartBox = new CheckBox
            {
                Content = "开机自动启动",
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 2, 0, 2)
            };
            _autoStartBox.Click += delegate
            {
                if (_suppressSettingEvents) return;
                bool desired = _autoStartBox.IsChecked == true;
                string err;
                if (_controller.SetAutoStart(desired, out err))
                {
                    _autoStartStatusText.Text = desired ? "✓ 已开启开机自动启动" : "已关闭开机自动启动";
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
                Margin = new Thickness(24, 0, 0, 8)
            };
            sysStack.Children.Add(_autoStartStatusText);

            var shortcutRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            var createShortcutBtn = GlassControlStyles.CreateGlassButton("📌 创建桌面快捷方式",
                ThemeService.BrushFromHex("#F1F5F9"), ThemeService.BrushFromHex("#0F172A"), ThemeService.BrushFromHex("#CBD5E1"),
                12, new Thickness(12, 5, 12, 5), 8);
            createShortcutBtn.Click += delegate
            {
                string exePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DesktopMemo.exe");
                if (!File.Exists(exePath))
                {
                    try { exePath = Process.GetCurrentProcess().MainModule.FileName; } catch { }
                }
                string createdPath;
                string err;
                if (Win32Native.CreateDesktopShortcut(exePath, "桌面备忘录", "DesktopMemo " + AppMeta.VersionTag, out createdPath, out err))
                {
                    _shortcutStatusText.Text = "✓ 已创建";
                    _shortcutStatusText.Foreground = ThemeService.BrushFromHex("#059669");
                }
                else
                {
                    _shortcutStatusText.Text = "创建失败：" + err;
                    _shortcutStatusText.Foreground = ThemeService.BrushFromHex("#DC2626");
                }
            };
            _shortcutStatusText = new TextBlock
            {
                FontFamily = ThemeService.AppFontFamily,
                FontSize = 11.5,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 0, 0),
                Foreground = ThemeService.BrushFromHex("#64748B")
            };
            shortcutRow.Children.Add(createShortcutBtn);
            shortcutRow.Children.Add(_shortcutStatusText);
            sysStack.Children.Add(shortcutRow);

            _soundBox = new CheckBox
            {
                Content = "到期提醒播放提示音",
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
            _panelFontSlider.AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler(delegate
            {
                _isDraggingFontSlider = true;
                if (_fontApplyTimer != null) _fontApplyTimer.Stop();
            }));
            _panelFontSlider.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler(delegate
            {
                _isDraggingFontSlider = false;
                if (_fontApplyTimer != null) _fontApplyTimer.Stop();
                _controller.ApplyAppearanceToAllWindows();
            }));
            _panelFontSlider.ValueChanged += delegate
            {
                if (_suppressSettingEvents) return;
                _controller.Config.PanelFontSize = Math.Round(_panelFontSlider.Value);
                _panelFontText.Text = string.Format("全局字号({0}px)：", (int)_controller.Config.PanelFontSize);
                ApplyFontPreviewOnly();
                _controller.ScheduleConfigSave();
                if (!_isDraggingFontSlider && _fontApplyTimer != null)
                {
                    _fontApplyTimer.Stop();
                    _fontApplyTimer.Start();
                }
            };
            sliderRow.Children.Add(_panelFontSlider);

            _panelOpacityText = MakeSettingLabel("窗口不透明度(96%)：");
            sliderRow.Children.Add(_panelOpacityText);
            _panelOpacitySlider = new Slider { Minimum = 0.60, Maximum = 1.00, Width = 140, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 4) };
            _panelOpacitySlider.AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler(delegate
            {
                _isDraggingOpacitySlider = true;
                if (_opacityRefreshTimer != null) _opacityRefreshTimer.Stop();
            }));
            _panelOpacitySlider.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler(delegate
            {
                _isDraggingOpacitySlider = false;
                if (_opacityRefreshTimer != null) _opacityRefreshTimer.Stop();
                RefreshTaskViews();
                RefreshStickiesManagerView();
            }));
            _panelOpacitySlider.ValueChanged += delegate
            {
                if (_suppressSettingEvents) return;
                _controller.Config.PanelOpacity = _panelOpacitySlider.Value;
                _panelOpacityText.Text = string.Format("窗口不透明度({0}%)：", (int)Math.Round(_panelOpacitySlider.Value * 100));
                ApplyOpacityBrushesOnly();
                _controller.ScheduleConfigSave();
                if (!_isDraggingOpacitySlider && _opacityRefreshTimer != null)
                {
                    _opacityRefreshTimer.Stop();
                    _opacityRefreshTimer.Start();
                }
            };
            sliderRow.Children.Add(_panelOpacitySlider);
            sysStack.Children.Add(sliderRow);

            stack.Children.Add(sysCard);

            // Section 5: Web Server & Windows Server Sync
            var webCard = CreateSettingsCard("🌐 网页端与远程同步");
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
                Content = "启用远程服务器自动同步",
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

            // Section 6: Storage & Version
            var dataCard = CreateSettingsCard("💾 版本与数据目录");
            var dataStack = (StackPanel)dataCard.Child;
            var versionTb = new TextBlock
            {
                Text = "当前版本：DesktopMemo " + AppMeta.VersionTag,
                FontFamily = ThemeService.AppFontFamily,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 4)
            };
            _settingsLabels.Add(versionTb);
            dataStack.Children.Add(versionTb);

            var exeDirTb = new TextBlock
            {
                Text = "程序安装目录：" + AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\'),
                FontFamily = ThemeService.AppFontFamily,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 4)
            };
            _settingsLabels.Add(exeDirTb);
            dataStack.Children.Add(exeDirTb);

            var pathTb = new TextBlock
            {
                Text = "配置与数据目录：" + _controller.Storage.DataDirectory,
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

        private void BuildHotkeyRecorderRow(StackPanel parent, string label, Func<string> getter, Action<string> setter)
        {
            var row = new DockPanel { Margin = new Thickness(20, 0, 0, 6) };
            var lbl = MakeSettingLabel(label);
            lbl.Width = 92;
            lbl.Margin = new Thickness(0, 0, 6, 0);
            DockPanel.SetDock(lbl, Dock.Left);
            row.Children.Add(lbl);

            var actionWrap = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var keyBtn = GlassControlStyles.CreateGlassButton(getter(),
                ThemeService.BrushFromHex("#F8FAFC"), ThemeService.BrushFromHex("#0F172A"), ThemeService.BrushFromHex("#CBD5E1"),
                11.5, new Thickness(12, 3, 12, 3), 7);
            keyBtn.Focusable = true;
            keyBtn.MinWidth = 148;
            keyBtn.Margin = new Thickness(0, 0, 6, 0);

            var saveBtn = GlassControlStyles.CreateGlassButton("✓ 保存",
                ThemeService.BrushFromHex("#2563EB"), Brushes.White, null, 11, new Thickness(8, 2, 8, 2), 6);
            saveBtn.Margin = new Thickness(0, 0, 4, 0);
            saveBtn.Visibility = Visibility.Collapsed;

            var clearBtn = GlassControlStyles.CreateGlassButton("清空",
                ThemeService.BrushFromHex("#F1F5F9"), ThemeService.BrushFromHex("#475569"), ThemeService.BrushFromHex("#CBD5E1"),
                11, new Thickness(8, 2, 8, 2), 6);
            clearBtn.Margin = new Thickness(0, 0, 4, 0);
            clearBtn.Visibility = Visibility.Collapsed;

            var cancelBtn = GlassControlStyles.CreateGlassButton("✕",
                Brushes.Transparent, ThemeService.BrushFromHex("#64748B"), ThemeService.BrushFromHex("#CBD5E1"),
                11, new Thickness(7, 2, 7, 2), 6);
            cancelBtn.Visibility = Visibility.Collapsed;

            bool isRecording = false;
            string pendingVal = getter() ?? "";

            Action refreshPill = delegate
            {
                string cur = getter() ?? "";
                keyBtn.Content = string.IsNullOrWhiteSpace(cur) ? "未设置（点击录制）" : cur;
                keyBtn.BorderBrush = ThemeService.BrushFromHex("#CBD5E1");
                saveBtn.Visibility = Visibility.Collapsed;
                clearBtn.Visibility = Visibility.Collapsed;
                cancelBtn.Visibility = Visibility.Collapsed;
            };
            _refreshHotkeyPills.Add(refreshPill);

            Action<bool> finishRecording = delegate(bool commit)
            {
                if (!isRecording) return;
                isRecording = false;
                if (commit)
                {
                    setter(pendingVal ?? "");
                    _controller.ScheduleConfigSave();
                }
                _controller.ResumeGlobalHotkeys();
                refreshPill();
                UpdateHotkeyStatusLabel();
            };

            keyBtn.Click += delegate
            {
                if (isRecording) return;
                isRecording = true;
                pendingVal = getter() ?? "";
                _controller.PauseGlobalHotkeys();
                keyBtn.Content = "请按组合键 (Enter 保存)";
                keyBtn.BorderBrush = ThemeService.BrushFromHex("#2563EB");
                saveBtn.Visibility = Visibility.Visible;
                clearBtn.Visibility = Visibility.Visible;
                cancelBtn.Visibility = Visibility.Visible;
                keyBtn.Focus();
            };

            keyBtn.PreviewKeyDown += delegate(object s, KeyEventArgs e)
            {
                if (!isRecording) return;
                if (e.Key == Key.Escape)
                {
                    finishRecording(false);
                    e.Handled = true;
                    return;
                }
                if (e.Key == Key.Enter)
                {
                    finishRecording(true);
                    e.Handled = true;
                    return;
                }
                if ((e.Key == Key.Back || e.Key == Key.Delete) && Keyboard.Modifiers == ModifierKeys.None)
                {
                    pendingVal = "";
                    keyBtn.Content = "已清空（按 Enter 保存）";
                    e.Handled = true;
                    return;
                }

                Key realKey = e.Key == Key.System ? e.SystemKey : e.Key;
                string captured;
                if (Win32Native.FormatHotkeyFromWpf(Keyboard.Modifiers, realKey, out captured))
                {
                    pendingVal = captured;
                    keyBtn.Content = captured + "（Enter 保存）";
                }
                else
                {
                    var modsList = new List<string>();
                    if ((Keyboard.Modifiers & ModifierKeys.Control) != 0) modsList.Add("Ctrl");
                    if ((Keyboard.Modifiers & ModifierKeys.Alt) != 0) modsList.Add("Alt");
                    if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) modsList.Add("Shift");
                    if ((Keyboard.Modifiers & ModifierKeys.Windows) != 0) modsList.Add("Win");
                    if (modsList.Count > 0)
                    {
                        keyBtn.Content = string.Join("+", modsList.ToArray()) + "+...";
                    }
                }
                e.Handled = true;
            };

            keyBtn.LostKeyboardFocus += delegate
            {
                if (isRecording) finishRecording(false);
            };
            saveBtn.Click += delegate { finishRecording(true); };
            cancelBtn.Click += delegate { finishRecording(false); };
            clearBtn.Click += delegate
            {
                pendingVal = "";
                keyBtn.Content = "已清空（Enter 保存）";
                keyBtn.Focus();
            };

            actionWrap.Children.Add(keyBtn);
            actionWrap.Children.Add(saveBtn);
            actionWrap.Children.Add(clearBtn);
            actionWrap.Children.Add(cancelBtn);
            row.Children.Add(actionWrap);
            parent.Children.Add(row);
            refreshPill();
        }

        private void UpdateHotkeyStatusLabel()
        {
            if (_hotkeyStatusText == null) return;
            if (!_controller.Config.HotkeysEnabled)
            {
                _hotkeyStatusText.Text = "全局快捷键已关闭";
                _hotkeyStatusText.Foreground = ThemeService.BrushFromHex("#64748B");
                return;
            }
            if (!string.IsNullOrEmpty(_controller.HotkeyWarningMessage))
            {
                _hotkeyStatusText.Text = "⚠ " + _controller.HotkeyWarningMessage;
                _hotkeyStatusText.Foreground = ThemeService.BrushFromHex("#DC2626");
                return;
            }
            _hotkeyStatusText.Text = "✓ 全局快捷键已生效";
            _hotkeyStatusText.Foreground = ThemeService.BrushFromHex("#059669");
        }

        public void OpenSettingsTab()
        {
            SwitchTab(4);
        }

        public void UpdateMisTabVisibility()
        {
            bool showMis = _controller.Config.MisEnabled;
            if (_navTabButtons[2] != null)
            {
                _navTabButtons[2].Visibility = showMis ? Visibility.Visible : Visibility.Collapsed;
            }
            if (!showMis && _activeTabIndex == 2)
            {
                SwitchTab(0);
                return;
            }
            UpdateDockIndicator(false);
        }

        private void SwitchTab(int idx)
        {
            if (idx == _activeTabIndex) return;
            int prevIdx = _activeTabIndex;
            _activeTabIndex = idx;

            _quadrantsView.Visibility = idx == 0 ? Visibility.Visible : Visibility.Collapsed;
            _scheduleView.Visibility = idx == 1 ? Visibility.Visible : Visibility.Collapsed;
            _timetableView.Visibility = idx == 2 ? Visibility.Visible : Visibility.Collapsed;
            _stickiesView.Visibility = idx == 3 ? Visibility.Visible : Visibility.Collapsed;
            _settingsView.Visibility = idx == 4 ? Visibility.Visible : Visibility.Collapsed;

            _quickAddBar.Visibility = (idx == 0 || idx == 1) ? Visibility.Visible : Visibility.Collapsed;
            if (idx == 4) LoadSettingsInputs();

            ApplyThemeAndRefreshAll();

            // Physics Transitions: Dock sliding pill + View directional spring glide
            UpdateDockIndicator(true);
            UIElement activeView = idx == 0 ? (UIElement)_quadrantsView
                : (idx == 1 ? (UIElement)_scheduleView
                : (idx == 2 ? (UIElement)_timetableView
                : (idx == 3 ? (UIElement)_stickiesView : _settingsView)));
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
            if (_activeTabIndex == 4) SaveAiInputsToConfig();

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
            if (_wxExportOutBox != null) cfg.WeChatExportOutputDir = (_wxExportOutBox.Text ?? "").Trim();
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
                if (_wxExportOutBox != null) _wxExportOutBox.Text = cfg.WeChatExportOutputDir ?? "";

                if (_misEnabledBox != null) _misEnabledBox.IsChecked = cfg.MisEnabled;
                if (_misOptionsPanel != null) _misOptionsPanel.Visibility = cfg.MisEnabled ? Visibility.Visible : Visibility.Collapsed;
                if (_showCoursesQuadBox != null) _showCoursesQuadBox.IsChecked = cfg.ShowTodayCoursesInQuadrants;
                if (_misSummaryText != null)
                {
                    TimetableData tt = _controller.Data != null ? _controller.Data.Timetable : null;
                    int curW = tt != null ? tt.GetWeekNumberForDate(DateTime.Now) : 0;
                    int cCount = tt != null && tt.Courses != null ? tt.Courses.Count : 0;
                    _misSummaryText.Text = cCount > 0
                        ? string.Format("当前第 {0} 周 · 已导入 {1} 门课程", curW >= 1 ? curW : 1, cCount)
                        : "尚未导入课表，点击下方按钮打开 MIS 登录并同步。";
                }

                if (_voiceEnabledBox != null) _voiceEnabledBox.IsChecked = cfg.VoiceMemoEnabled;
                if (_voiceModeCombo != null)
                {
                    _voiceModeCombo.SelectedIndex = string.Equals(cfg.VoiceTriggerMode, "hold", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
                }
                if (_voiceAutoCommitBox != null) _voiceAutoCommitBox.IsChecked = cfg.VoiceAutoCommit;
                for (int i = 0; i < _refreshHotkeyPills.Count; i++) _refreshHotkeyPills[i]();
                UpdateHotkeyStatusLabel();

                bool regAuto = Win32Native.IsAutoStartEnabled();
                _autoStartBox.IsChecked = regAuto;
                _autoStartStatusText.Text = regAuto ? "✓ 已开启开机自动启动" : "";
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

        private void ApplyOpacityBrushesOnly()
        {
            AppConfig cfg = _controller.Config;
            ThemePalette pal = ThemeService.GetPalette(cfg.PanelTheme, cfg.PanelOpacity);
            if (_outerCard != null) _outerCard.Background = pal.CardBg;
            if (_headerBar != null) _headerBar.Background = pal.HeaderBg;
            if (_chatExtractBtn != null) _chatExtractBtn.Background = pal.SurfaceBg;
            if (_undoAiBtn != null) _undoAiBtn.Background = pal.SurfaceBg;
            if (_quickAddBar != null) _quickAddBar.Background = pal.SurfaceBg;
            if (_clearDoneBtn != null) _clearDoneBtn.Background = pal.SurfaceBg;
            for (int i = 0; i < _settingsCards.Count; i++)
            {
                _settingsCards[i].Background = pal.SurfaceBg;
            }
        }

        private void ApplyFontPreviewOnly()
        {
            AppConfig cfg = _controller.Config;
            UiScaleMetrics scale = ThemeService.GetScaleMetrics(cfg.UiScale, cfg.PanelFontSize);
            if (_headerTitleText != null) _headerTitleText.FontSize = scale.HeaderFontSize;
            if (_headerVersionText != null) _headerVersionText.FontSize = Math.Max(10, scale.SmallFontSize);
            if (_headerSummaryText != null) _headerSummaryText.FontSize = scale.SmallFontSize + 0.5;
            for (int i = 0; i < 5; i++)
            {
                if (_navTabButtons[i] != null) _navTabButtons[i].FontSize = scale.FontSize;
            }
            for (int i = 0; i < _settingsCardTitles.Count; i++)
            {
                _settingsCardTitles[i].FontSize = scale.FontSize + 1;
            }
            for (int i = 0; i < _settingsLabels.Count; i++)
            {
                _settingsLabels[i].FontSize = scale.SmallFontSize + 1;
            }
        }

        public void ApplyThemeAndRefreshAll()
        {
            AppConfig cfg = _controller.Config;
            ThemePalette pal = ThemeService.GetPalette(cfg.PanelTheme, cfg.PanelOpacity);
            UiScaleMetrics scale = ThemeService.GetScaleMetrics(cfg.UiScale, cfg.PanelFontSize);

            string themeKey = ThemeService.NormalizeThemeKey(cfg.PanelTheme);
            string controlStyleKey = themeKey + "|"
                + (cfg.UiScale ?? "standard") + "|"
                + Math.Round(cfg.PanelFontSize, 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
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

            _headerTitleText.Foreground = pal.TextPrimary;
            _headerTitleText.FontSize = scale.HeaderFontSize;
            if (_headerVersionBadge != null && _headerVersionText != null)
            {
                _headerVersionBadge.Background = pal.HoverBg;
                _headerVersionBadge.BorderBrush = pal.Border;
                _headerVersionText.Foreground = pal.TextSecondary;
                _headerVersionText.FontSize = Math.Max(10, scale.SmallFontSize);
            }
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

            for (int i = 0; i < 5; i++)
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
            for (int i = 0; i < _settingsCardTitles.Count; i++)
            {
                _settingsCardTitles[i].Foreground = pal.TextPrimary;
                _settingsCardTitles[i].FontSize = scale.FontSize + 1;
            }
            for (int i = 0; i < _settingsLabels.Count; i++)
            {
                _settingsLabels[i].Foreground = pal.TextSecondary;
                _settingsLabels[i].FontSize = scale.SmallFontSize + 1;
            }

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
            int curWeek = (cfg.MisEnabled && data.Timetable != null) ? data.Timetable.GetWeekNumberForDate(now) : 0;
            _headerSummaryText.Text = string.Format("{0}· {1} 条待办{2}",
                curWeek >= 1 ? string.Format("· 第{0}周 ", curWeek) : "",
                pendingCount,
                urgentOrOverdue > 0 ? string.Format(" · {0} 条临近/已逾期", urgentOrOverdue) : "");

            // Today's courses strip in Quadrants tab
            if (_quadTodayCourseStrip != null && _quadTodayCourseWrap != null)
            {
                _quadTodayCourseWrap.Children.Clear();
                List<CourseItem> todayCourses = (cfg.MisEnabled && cfg.ShowTodayCoursesInQuadrants && data.Timetable != null)
                    ? data.Timetable.GetCoursesForDate(now)
                    : new List<CourseItem>();
                if (cfg.MisEnabled && cfg.ShowTodayCoursesInQuadrants && data.Timetable != null && data.Timetable.Courses.Count > 0)
                {
                    _quadTodayCourseStrip.Background = pal.SurfaceBg;
                    _quadTodayCourseStrip.BorderBrush = pal.Border;
                    _quadTodayCourseStrip.Visibility = Visibility.Visible;

                    var hdrLbl = new TextBlock
                    {
                        Text = curWeek >= 1 ? string.Format("📅 第{0}周今日课程：", curWeek) : "📅 今日课程：",
                        FontFamily = ThemeService.AppFontFamily,
                        FontSize = scale.SmallFontSize + 0.5,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = pal.TextPrimary,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 1, 8, 1)
                    };
                    _quadTodayCourseWrap.Children.Add(hdrLbl);

                    if (todayCourses.Count == 0)
                    {
                        _quadTodayCourseWrap.Children.Add(new TextBlock
                        {
                            Text = "今日无课",
                            FontFamily = ThemeService.AppFontFamily,
                            FontSize = scale.SmallFontSize + 0.5,
                            Foreground = pal.TextSecondary,
                            VerticalAlignment = VerticalAlignment.Center,
                            Margin = new Thickness(0, 1, 6, 1)
                        });
                    }
                    else
                    {
                        for (int i = 0; i < todayCourses.Count; i++)
                        {
                            CourseItem course = todayCourses[i];
                            string cStart = data.Timetable.GetCourseStartTime(course);
                            string cEnd = data.Timetable.GetCourseEndTime(course);
                            string chipText = string.Format("{0} {1}{2}",
                                cStart,
                                course.Name,
                                string.IsNullOrWhiteSpace(course.Location) ? "" : (" @" + course.Location));
                            var chip = GlassControlStyles.CreateGlassButton(chipText,
                                pal.HoverBg, pal.TextPrimary, pal.Border,
                                scale.SmallFontSize + 0.5, new Thickness(8, 2, 8, 2), 6);
                            chip.Margin = new Thickness(0, 1, 6, 1);
                            chip.ToolTip = "点击为此课程新建作业 / 备忘";
                            CourseItem capC = course;
                            string capEnd = cEnd;
                            chip.Click += delegate
                            {
                                var draft = new MemoTask
                                {
                                    Title = capC.Name + " ",
                                    Notes = string.IsNullOrWhiteSpace(capC.Location) ? "" : ("教室：" + capC.Location),
                                    Quadrant = 2,
                                    DeadlineIso = now.ToString("yyyy-MM-dd") + " " + capEnd
                                };
                                _controller.OpenNewTaskDialog(draft, this);
                            };
                            _quadTodayCourseWrap.Children.Add(chip);
                        }
                    }

                    var openTtChip = GlassControlStyles.CreateGlassButton("查看课表 →",
                        Brushes.Transparent, pal.AccentBg, null,
                        scale.SmallFontSize + 0.5, new Thickness(6, 2, 6, 2), 6);
                    openTtChip.Click += delegate { SwitchTab(2); };
                    _quadTodayCourseWrap.Children.Add(openTtChip);
                }
                else
                {
                    _quadTodayCourseStrip.Visibility = Visibility.Collapsed;
                }
            }

            RefreshTimetableView();

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

            if (!string.IsNullOrEmpty(ddl.Text) || !string.IsNullOrEmpty(task.RemindAtIso) || task.AiScore > 0 || (task.AttachmentFiles != null && task.AttachmentFiles.Count > 0))
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
                if (task.AttachmentFiles != null && task.AttachmentFiles.Count > 0)
                {
                    var attPaths = new List<string>();
                    for (int a = 0; a < task.AttachmentFiles.Count; a++)
                    {
                        string p = _controller.Storage.GetAttachmentPath(task.AttachmentFiles[a]);
                        if (!string.IsNullOrEmpty(p)) attPaths.Add(p);
                    }
                    var attBadge = new Border
                    {
                        Background = pal.HoverBg,
                        BorderBrush = pal.Border,
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(5),
                        Padding = new Thickness(6, 1, 6, 1),
                        Margin = new Thickness(0, 0, 5, 2),
                        Cursor = Cursors.Hand,
                        ToolTip = "点击预览日程图片附件",
                        Child = new TextBlock
                        {
                            Text = "🖼 " + task.AttachmentFiles.Count,
                            FontFamily = ThemeService.AppFontFamily,
                            FontSize = scale.SmallFontSize,
                            Foreground = pal.TextPrimary
                        }
                    };
                    attBadge.MouseLeftButtonUp += delegate
                    {
                        ImageAttachmentHelper.ShowImagePreviewDialog(attPaths, 0, this, _controller.Config.PanelTheme);
                    };
                    meta.Children.Add(attBadge);
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

        public void RefreshTimetableView()
        {
            if (_ttWeekGrid == null || _ttWeekTitleText == null) return;
            ThemePalette pal = ThemeService.GetPalette(_controller.Config.PanelTheme, _controller.Config.PanelOpacity);
            UiScaleMetrics scale = ThemeService.GetScaleMetrics(_controller.Config.UiScale, _controller.Config.PanelFontSize);

            TimetableData tt = _controller.Data != null ? _controller.Data.Timetable : new TimetableData();
            int actualCurrentWeek = tt.GetWeekNumberForDate(DateTime.Now);
            if (_viewingTimetableWeek <= 0)
            {
                _viewingTimetableWeek = actualCurrentWeek >= 1 ? actualCurrentWeek : 1;
            }

            DateTime? mon = tt.GetDateForWeekAndDay(_viewingTimetableWeek, 1);
            DateTime? sun = tt.GetDateForWeekAndDay(_viewingTimetableWeek, 7);
            string rangeStr = (mon.HasValue && sun.HasValue)
                ? string.Format(" · {0} - {1}", mon.Value.ToString("MM/dd"), sun.Value.ToString("MM/dd"))
                : "";
            string curBadge = (actualCurrentWeek >= 1 && _viewingTimetableWeek == actualCurrentWeek) ? " (本周)" : "";
            _ttWeekTitleText.Text = string.Format("第 {0} 周{1}{2}", _viewingTimetableWeek, curBadge, rangeStr);
            _ttWeekTitleText.Foreground = pal.TextPrimary;

            _ttWeekGrid.Children.Clear();
            _ttWeekGrid.ColumnDefinitions.Clear();
            _ttWeekGrid.RowDefinitions.Clear();

            int visibleDays = tt.ShowWeekends ? 7 : 5;
            _ttVisibleDays = visibleDays;
            // Column 0: Left Time Axis (74px) + Columns 1..visibleDays
            _ttWeekGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(74) });
            for (int c = 1; c <= visibleDays; c++)
            {
                _ttWeekGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            }

            int maxSec = tt.GetMaxSectionCount();
            _ttMaxSections = maxSec;
            int rowMinH = (tt.RowMinHeight >= 36 && tt.RowMinHeight <= 80) ? tt.RowMinHeight : 52;
            _ttWeekGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (int sec = 1; sec <= maxSec; sec++)
            {
                _ttWeekGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto, MinHeight = rowMinH });
            }

            int todayIso = TimetableData.ToIsoWeekday(DateTime.Today.DayOfWeek);

            _ttDropHighlight = new Border
            {
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = false,
                BorderBrush = pal.AccentBg,
                BorderThickness = new Thickness(2),
                CornerRadius = new CornerRadius(7),
                Background = ThemeService.BrushFromHexAlpha(pal.IsDark ? "#3B82F6" : "#2563EB", 45),
                Margin = new Thickness(2)
            };
            Panel.SetZIndex(_ttDropHighlight, 50);
            _ttWeekGrid.Children.Add(_ttDropHighlight);

            // Top-left header cell
            var cornerHeader = new Border
            {
                Background = pal.SurfaceBg,
                BorderBrush = pal.Border,
                BorderThickness = new Thickness(1, 1, 1, 1),
                CornerRadius = new CornerRadius(8, 0, 0, 0),
                Padding = new Thickness(4, 6, 4, 6),
                Cursor = Cursors.Hand,
                ToolTip = "点击进入课表设置（可调每日节数、周末、行高与作息时间）",
                Child = new TextBlock
                {
                    Text = "节次\n时间",
                    FontFamily = ThemeService.AppFontFamily,
                    FontSize = Math.Max(10, scale.SmallFontSize),
                    FontWeight = FontWeights.SemiBold,
                    Foreground = pal.TextSecondary,
                    TextAlignment = TextAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            cornerHeader.MouseLeftButtonUp += delegate
            {
                if (MisImportDialog.OpenTimetableSettingsDialog(this, _controller.Data.Timetable, _controller.Config))
                {
                    _controller.SaveTimetable(_controller.Data.Timetable);
                    RefreshTaskViews();
                }
            };
            cornerHeader.ContextMenu = BuildEmptyCellContextMenu(1, 1, maxSec);
            Grid.SetRow(cornerHeader, 0);
            Grid.SetColumn(cornerHeader, 0);
            _ttWeekGrid.Children.Add(cornerHeader);

            // Weekday header cells (Row 0, Col 1..visibleDays)
            for (int day = 1; day <= visibleDays; day++)
            {
                bool isTodayCol = (actualCurrentWeek == _viewingTimetableWeek) && (day == todayIso);
                DateTime? colDate = tt.GetDateForWeekAndDay(_viewingTimetableWeek, day);
                var dayHdr = new Border
                {
                    Background = isTodayCol
                        ? ThemeService.BrushFromHexAlpha(pal.IsDark ? "#3B82F6" : "#2563EB", pal.IsDark ? (byte)36 : (byte)20)
                        : pal.SurfaceBg,
                    BorderBrush = isTodayCol ? pal.AccentBg : pal.Border,
                    BorderThickness = new Thickness(0, 1, 1, isTodayCol ? 1.8 : 1),
                    CornerRadius = day == visibleDays ? new CornerRadius(0, 8, 0, 0) : new CornerRadius(0),
                    Padding = new Thickness(4, 6, 4, 6),
                    Child = new TextBlock
                    {
                        Text = colDate.HasValue
                            ? string.Format("{0}\n{1}", CourseItem.GetWeekdayName(day), colDate.Value.ToString("MM/dd"))
                            : CourseItem.GetWeekdayName(day),
                        FontFamily = ThemeService.AppFontFamily,
                        FontSize = scale.SmallFontSize + 0.5,
                        FontWeight = isTodayCol ? FontWeights.Bold : FontWeights.SemiBold,
                        Foreground = isTodayCol ? pal.AccentBg : pal.TextPrimary,
                        TextAlignment = TextAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                };
                Grid.SetRow(dayHdr, 0);
                Grid.SetColumn(dayHdr, day);
                _ttWeekGrid.Children.Add(dayHdr);
            }

            // Left Time Axis cells & interactive background matrix cells (Rows 1..maxSec)
            var axisContextMenu = new ContextMenu();
            var miEditTimes = new MenuItem { Header = "🕒 编辑作息时间…" };
            miEditTimes.Click += delegate
            {
                MisImportDialog.OpenSectionTimesEditor(this, _controller.Data.Timetable, _controller.Config, delegate
                {
                    _controller.SaveTimetable(_controller.Data.Timetable);
                });
            };
            var miTtSettings = new MenuItem { Header = "⚙️ 课表设置…" };
            miTtSettings.Click += delegate
            {
                if (MisImportDialog.OpenTimetableSettingsDialog(this, _controller.Data.Timetable, _controller.Config))
                {
                    _controller.SaveTimetable(_controller.Data.Timetable);
                    RefreshTaskViews();
                }
            };
            axisContextMenu.Items.Add(miEditTimes);
            axisContextMenu.Items.Add(miTtSettings);

            for (int sec = 1; sec <= maxSec; sec++)
            {
                string sTime = tt.GetSectionStartTime(sec);
                string eTime = tt.GetSectionEndTime(sec);
                bool blockBreak = false;
                if (sec > 1)
                {
                    string prevStart = tt.GetSectionStartTime(sec - 1);
                    if (!string.IsNullOrEmpty(sTime) && !string.IsNullOrEmpty(prevStart))
                    {
                        blockBreak = (string.Compare(prevStart, "12:00", StringComparison.Ordinal) < 0 && string.Compare(sTime, "12:00", StringComparison.Ordinal) >= 0)
                            || (string.Compare(prevStart, "18:00", StringComparison.Ordinal) < 0 && string.Compare(sTime, "18:00", StringComparison.Ordinal) >= 0);
                    }
                    else
                    {
                        blockBreak = (sec == 5 || sec == 9);
                    }
                }

                var axisCell = new Border
                {
                    Background = pal.SurfaceBg,
                    BorderBrush = pal.Border,
                    BorderThickness = new Thickness(1, blockBreak ? 1.6 : 0, 1, 1),
                    CornerRadius = sec == maxSec ? new CornerRadius(0, 0, 0, 8) : new CornerRadius(0),
                    Padding = new Thickness(4, 4, 4, 4),
                    Cursor = Cursors.Hand,
                    ToolTip = "左键编辑作息时间 · 右键课表设置",
                    ContextMenu = axisContextMenu
                };
                var axisStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
                axisStack.Children.Add(new TextBlock
                {
                    Text = sec.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    FontFamily = ThemeService.AppFontFamily,
                    FontSize = scale.FontSize,
                    FontWeight = FontWeights.Bold,
                    Foreground = pal.TextPrimary,
                    HorizontalAlignment = HorizontalAlignment.Center
                });
                axisStack.Children.Add(new TextBlock
                {
                    Text = sTime,
                    FontFamily = ThemeService.AppFontFamily,
                    FontSize = Math.Max(9.5, scale.SmallFontSize - 0.8),
                    Foreground = pal.TextSecondary,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 1, 0, 0)
                });
                axisStack.Children.Add(new TextBlock
                {
                    Text = eTime,
                    FontFamily = ThemeService.AppFontFamily,
                    FontSize = Math.Max(9.5, scale.SmallFontSize - 0.8),
                    Foreground = pal.TextSecondary,
                    HorizontalAlignment = HorizontalAlignment.Center
                });
                axisCell.Child = axisStack;
                axisCell.MouseLeftButtonUp += delegate
                {
                    MisImportDialog.OpenSectionTimesEditor(this, _controller.Data.Timetable, _controller.Config, delegate
                    {
                        _controller.SaveTimetable(_controller.Data.Timetable);
                    });
                };
                Grid.SetRow(axisCell, sec);
                Grid.SetColumn(axisCell, 0);
                _ttWeekGrid.Children.Add(axisCell);

                for (int day = 1; day <= visibleDays; day++)
                {
                    int capDay = day;
                    int capSec = sec;
                    bool isTodayCol = (actualCurrentWeek == _viewingTimetableWeek) && (day == todayIso);
                    var bgCell = new Border
                    {
                        Background = isTodayCol
                            ? ThemeService.BrushFromHexAlpha(pal.IsDark ? "#3B82F6" : "#2563EB", pal.IsDark ? (byte)16 : (byte)9)
                            : Brushes.Transparent,
                        BorderBrush = pal.Border,
                        BorderThickness = new Thickness(0, blockBreak ? 1.6 : 0, 1, 1),
                        CornerRadius = (sec == maxSec && day == visibleDays) ? new CornerRadius(0, 0, 8, 0) : new CornerRadius(0),
                        ContextMenu = BuildEmptyCellContextMenu(capDay, capSec, maxSec)
                    };
                    bgCell.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
                    {
                        if (e.ClickCount == 2)
                        {
                            var draft = new CourseItem
                            {
                                DayOfWeek = capDay,
                                StartSection = capSec,
                                EndSection = Math.Min(maxSec, capSec + 1)
                            };
                            if (MisImportDialog.OpenCourseEditorDialog(this, _controller.Data.Timetable, _controller.Config, draft, true))
                            {
                                _controller.SaveTimetable(_controller.Data.Timetable);
                                RefreshTaskViews();
                            }
                            e.Handled = true;
                        }
                    };
                    Grid.SetRow(bgCell, sec);
                    Grid.SetColumn(bgCell, day);
                    _ttWeekGrid.Children.Add(bgCell);
                }
            }

            // Place courses aligned accurately to day columns (1..visibleDays) and section rows (1..maxSec)
            for (int day = 1; day <= visibleDays; day++)
            {
                DateTime? colDate = tt.GetDateForWeekAndDay(_viewingTimetableWeek, day);
                List<CourseItem> dayCourses = tt.GetCoursesForWeekAndDay(_viewingTimetableWeek, day);
                var groupedSlots = new Dictionary<string, List<CourseItem>>();
                for (int i = 0; i < dayCourses.Count; i++)
                {
                    CourseItem c = dayCourses[i];
                    if (c.StartSection > maxSec) continue;
                    int sRow = Math.Max(1, Math.Min(maxSec, c.StartSection));
                    int eRow = Math.Max(sRow, Math.Min(maxSec, c.EndSection));
                    string slotKey = sRow + ":" + eRow;
                    if (!groupedSlots.ContainsKey(slotKey)) groupedSlots[slotKey] = new List<CourseItem>();
                    groupedSlots[slotKey].Add(c);
                }

                foreach (KeyValuePair<string, List<CourseItem>> slotKv in groupedSlots)
                {
                    List<CourseItem> slotCourses = slotKv.Value;
                    CourseItem firstC = slotCourses[0];
                    int sRow = Math.Max(1, Math.Min(maxSec, firstC.StartSection));
                    int eRow = Math.Max(sRow, Math.Min(maxSec, firstC.EndSection));

                    if (slotCourses.Count == 1)
                    {
                        Border cCard = BuildTimetableMatrixCourseCard(firstC, tt, colDate, pal, scale, maxSec);
                        Grid.SetColumn(cCard, day);
                        Grid.SetRow(cCard, sRow);
                        Grid.SetRowSpan(cCard, eRow - sRow + 1);
                        _ttWeekGrid.Children.Add(cCard);
                    }
                    else
                    {
                        var stackHolder = new Grid();
                        for (int k = 0; k < slotCourses.Count; k++)
                        {
                            stackHolder.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                            Border cCard = BuildTimetableMatrixCourseCard(slotCourses[k], tt, colDate, pal, scale, maxSec);
                            Grid.SetRow(cCard, k);
                            stackHolder.Children.Add(cCard);
                        }
                        Grid.SetColumn(stackHolder, day);
                        Grid.SetRow(stackHolder, sRow);
                        Grid.SetRowSpan(stackHolder, eRow - sRow + 1);
                        _ttWeekGrid.Children.Add(stackHolder);
                    }
                }
            }
        }

        private void StartTimetableCardDrag(CourseItem course, Border sourceCard, Point hostPos)
        {
            if (course == null || sourceCard == null || _ttMatrixHost == null || _ttDragOverlayCanvas == null) return;

            _ttDraggingCourse = course;
            _ttSourceCard = sourceCard;
            _ttPressedCourse = null;
            _ttPressedCard = null;
            _ttPressedCardDate = null;
            _ttLastHostPos = hostPos;
            _ttSourceCard.Opacity = 0.28;

            ThemePalette pal = ThemeService.GetPalette(_controller.Config.PanelTheme, 0.98);
            UiScaleMetrics scale = ThemeService.GetScaleMetrics(_controller.Config.UiScale, _controller.Config.PanelFontSize);
            TimetableData tt = _controller.Data != null ? _controller.Data.Timetable : new TimetableData();

            double ghostW = sourceCard.ActualWidth > 20 ? sourceCard.ActualWidth : 108;
            double ghostH = sourceCard.ActualHeight > 20 ? sourceCard.ActualHeight : 54;
            string colorHex = string.IsNullOrEmpty(course.ColorHex) ? "#3B82F6" : course.ColorHex;

            var body = new StackPanel();
            var titleWrap = new WrapPanel();
            titleWrap.Children.Add(new TextBlock
            {
                Text = course.Name,
                FontFamily = ThemeService.AppFontFamily,
                FontSize = scale.SmallFontSize + 0.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = pal.TextPrimary,
                TextWrapping = TextWrapping.Wrap
            });
            if (course.WeekType == "odd" || course.WeekType == "even")
            {
                titleWrap.Children.Add(new Border
                {
                    Background = ThemeService.BrushFromHex(course.WeekType == "odd" ? "#DBEAFE" : "#FCE7F3"),
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(4, 0, 4, 0),
                    Margin = new Thickness(4, 1, 0, 0),
                    Child = new TextBlock
                    {
                        Text = course.WeekType == "odd" ? "单周" : "双周",
                        FontFamily = ThemeService.AppFontFamily,
                        FontSize = 9.5,
                        FontWeight = FontWeights.Bold,
                        Foreground = ThemeService.BrushFromHex(course.WeekType == "odd" ? "#1D4ED8" : "#BE185D")
                    }
                });
            }
            body.Children.Add(titleWrap);

            if (tt.ShowTimeOnCard)
            {
                body.Children.Add(new TextBlock
                {
                    Text = string.Format("{0}-{1}", tt.GetCourseStartTime(course), tt.GetCourseEndTime(course)),
                    FontFamily = ThemeService.AppFontFamily,
                    FontSize = Math.Max(9.5, scale.SmallFontSize - 0.5),
                    Foreground = pal.TextSecondary,
                    Margin = new Thickness(0, 2, 0, 0)
                });
            }
            if (tt.ShowLocationOnCard && !string.IsNullOrWhiteSpace(course.Location))
            {
                body.Children.Add(new TextBlock
                {
                    Text = "@ " + course.Location,
                    FontFamily = ThemeService.AppFontFamily,
                    FontSize = Math.Max(9.5, scale.SmallFontSize - 0.5),
                    Foreground = pal.TextSecondary,
                    TextWrapping = TextWrapping.Wrap
                });
            }

            var liftScale = new ScaleTransform(1.0, 1.0);
            _ttDragGhost = new Border
            {
                Width = ghostW,
                MinHeight = ghostH,
                Background = pal.PopupBg,
                BorderBrush = ThemeService.BrushFromHex(colorHex),
                BorderThickness = new Thickness(3.5, 1.5, 1.5, 1.5),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(6, 5, 6, 5),
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = liftScale,
                Effect = new DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 24,
                    ShadowDepth = 8,
                    Opacity = pal.IsDark ? 0.45 : 0.30
                },
                Child = body
            };

            _ttDragOverlayCanvas.Children.Clear();
            _ttDragOverlayCanvas.Children.Add(_ttDragGhost);
            PositionTimetableDragGhost(hostPos);

            var scaleAnim = new DoubleAnimation(1.0, 1.045, TimeSpan.FromMilliseconds(110))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            liftScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
            liftScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);

            _ttMatrixHost.CaptureMouse();
            UpdateTimetableDropTarget(hostPos);
            if (_ttAutoScrollTimer != null) _ttAutoScrollTimer.Start();
        }

        private void PositionTimetableDragGhost(Point hostPos)
        {
            if (_ttDragGhost == null) return;
            double gw = _ttDragGhost.Width > 0 ? _ttDragGhost.Width : 108;
            double gh = _ttDragGhost.MinHeight > 0 ? _ttDragGhost.MinHeight : 54;
            double ox = Math.Max(8, Math.Min(gw - 8, _ttDragPointerOffset.X));
            double oy = Math.Max(8, Math.Min(gh - 8, _ttDragPointerOffset.Y));
            Canvas.SetLeft(_ttDragGhost, hostPos.X - ox);
            Canvas.SetTop(_ttDragGhost, hostPos.Y - oy);
        }

        private void OnTimetableHostMouseMove(object sender, MouseEventArgs e)
        {
            if (_ttDraggingCourse == null) return;
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                FinishTimetableCardDrag(false);
                return;
            }
            _ttLastHostPos = e.GetPosition(_ttMatrixHost);
            PositionTimetableDragGhost(_ttLastHostPos);
            UpdateTimetableDropTarget(_ttLastHostPos);
        }

        private void OnTimetableHostMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_ttDraggingCourse != null)
            {
                _ttLastHostPos = e.GetPosition(_ttMatrixHost);
                UpdateTimetableDropTarget(_ttLastHostPos);
                FinishTimetableCardDrag(true);
                e.Handled = true;
            }
        }

        private void OnTimetableHostLostMouseCapture(object sender, MouseEventArgs e)
        {
            if (_ttDraggingCourse != null)
            {
                FinishTimetableCardDrag(false);
            }
        }

        private void OnTimetableAutoScrollTick(object sender, EventArgs e)
        {
            if (_ttDraggingCourse == null || _ttScroller == null || _ttMatrixHost == null) return;
            double delta = ComputeEdgeAutoScrollDelta(
                _ttLastHostPos.Y,
                _ttScroller.ActualHeight,
                _ttScroller.VerticalOffset,
                _ttScroller.ScrollableHeight);
            if (Math.Abs(delta) > 0.05)
            {
                _ttScroller.ScrollToVerticalOffset(_ttScroller.VerticalOffset + delta);
                UpdateTimetableDropTarget(_ttLastHostPos);
            }
        }

        public static double ComputeEdgeAutoScrollDelta(
            double cursorY,
            double viewportHeight,
            double currentOffset,
            double scrollableHeight)
        {
            if (viewportHeight <= 0 || scrollableHeight <= 0) return 0;
            double edgeZone = Math.Min(64.0, Math.Max(36.0, viewportHeight * 0.16));

            if (cursorY >= viewportHeight - edgeZone && cursorY <= viewportHeight + 48.0 && currentOffset < scrollableHeight)
            {
                double distInto = Math.Min(edgeZone, cursorY - (viewportHeight - edgeZone));
                double ratio = Math.Max(0.15, distInto / edgeZone);
                double step = ratio * 11.5;
                double remaining = scrollableHeight - currentOffset;
                return Math.Min(step, remaining);
            }

            if (cursorY <= edgeZone && cursorY >= -48.0 && currentOffset > 0)
            {
                double distInto = Math.Min(edgeZone, edgeZone - cursorY);
                double ratio = Math.Max(0.15, distInto / edgeZone);
                double step = ratio * 11.5;
                return -Math.Min(step, currentOffset);
            }

            return 0;
        }

        private void UpdateTimetableDropTarget(Point hostPos)
        {
            if (_ttDraggingCourse == null || _ttDropHighlight == null || _ttMatrixHost == null || _ttWeekGrid == null) return;
            if (_ttWeekGrid.ColumnDefinitions.Count <= 1 || _ttWeekGrid.RowDefinitions.Count <= 1) return;

            Point gridPos = _ttMatrixHost.TranslatePoint(hostPos, _ttWeekGrid);

            int targetDay = 1;
            double accX = _ttWeekGrid.ColumnDefinitions[0].ActualWidth;
            int colLimit = Math.Min(_ttVisibleDays, _ttWeekGrid.ColumnDefinitions.Count - 1);
            for (int c = 1; c <= colLimit; c++)
            {
                double cw = _ttWeekGrid.ColumnDefinitions[c].ActualWidth;
                if (gridPos.X < accX + cw || c == colLimit)
                {
                    targetDay = c;
                    break;
                }
                accX += cw;
            }

            int targetSec = 1;
            double accY = _ttWeekGrid.RowDefinitions[0].ActualHeight;
            int rowLimit = Math.Min(_ttMaxSections, _ttWeekGrid.RowDefinitions.Count - 1);
            for (int r = 1; r <= rowLimit; r++)
            {
                double rh = _ttWeekGrid.RowDefinitions[r].ActualHeight;
                if (gridPos.Y < accY + rh || r == rowLimit)
                {
                    targetSec = r;
                    break;
                }
                accY += rh;
            }

            int span = Math.Max(0, _ttDraggingCourse.EndSection - _ttDraggingCourse.StartSection);
            int clampedStart = Math.Max(1, Math.Min(Math.Max(1, _ttMaxSections - span), targetSec));
            int clampedEnd = Math.Min(_ttMaxSections, clampedStart + span);

            _ttHoverDay = targetDay;
            _ttHoverSec = clampedStart;
            Grid.SetColumn(_ttDropHighlight, targetDay);
            Grid.SetRow(_ttDropHighlight, clampedStart);
            Grid.SetRowSpan(_ttDropHighlight, Math.Max(1, clampedEnd - clampedStart + 1));
            _ttDropHighlight.Visibility = Visibility.Visible;
        }

        private void FinishTimetableCardDrag(bool commit)
        {
            CourseItem dragged = _ttDraggingCourse;
            int hoverDay = _ttHoverDay;
            int hoverSec = _ttHoverSec;

            _ttDraggingCourse = null;
            _ttPressedCourse = null;
            _ttPressedCard = null;
            _ttHoverDay = 0;
            _ttHoverSec = 0;

            if (_ttAutoScrollTimer != null) _ttAutoScrollTimer.Stop();
            if (_ttMatrixHost != null && _ttMatrixHost.IsMouseCaptured)
            {
                try { _ttMatrixHost.ReleaseMouseCapture(); } catch { }
            }
            if (_ttDragOverlayCanvas != null) _ttDragOverlayCanvas.Children.Clear();
            _ttDragGhost = null;
            if (_ttDropHighlight != null) _ttDropHighlight.Visibility = Visibility.Collapsed;
            if (_ttSourceCard != null)
            {
                _ttSourceCard.Opacity = 1.0;
                _ttSourceCard = null;
            }

            if (commit && dragged != null && hoverDay >= 1 && hoverSec >= 1)
            {
                HandleCourseDroppedOnCell(dragged, hoverDay, hoverSec);
            }
        }

        private ContextMenu BuildEmptyCellContextMenu(int day, int sec, int maxSec)
        {
            var cm = new ContextMenu();
            var addItem = new MenuItem { Header = string.Format("+ 在 {0} 第{1}节 添加课程…", CourseItem.GetWeekdayName(day), sec) };
            addItem.Click += delegate
            {
                var draft = new CourseItem
                {
                    DayOfWeek = day,
                    StartSection = sec,
                    EndSection = Math.Min(maxSec, sec + 1)
                };
                if (MisImportDialog.OpenCourseEditorDialog(this, _controller.Data.Timetable, _controller.Config, draft, true))
                {
                    _controller.SaveTimetable(_controller.Data.Timetable);
                    RefreshTaskViews();
                }
            };
            var settingsItem = new MenuItem { Header = "⚙️ 课表设置…" };
            settingsItem.Click += delegate
            {
                if (MisImportDialog.OpenTimetableSettingsDialog(this, _controller.Data.Timetable, _controller.Config))
                {
                    _controller.SaveTimetable(_controller.Data.Timetable);
                    RefreshTaskViews();
                }
            };
            var timesItem = new MenuItem { Header = "🕒 编辑作息时间…" };
            timesItem.Click += delegate
            {
                MisImportDialog.OpenSectionTimesEditor(this, _controller.Data.Timetable, _controller.Config, delegate
                {
                    _controller.SaveTimetable(_controller.Data.Timetable);
                });
            };
            var stickyItem = new MenuItem { Header = "📌 贴桌面课表便签" };
            stickyItem.Click += delegate { _controller.OpenOrFocusTimetableStickyNote(); };
            var misItem = new MenuItem { Header = "🔄 MIS 导入与同步…" };
            misItem.Click += delegate { _controller.OpenMisImportDialog(this); };

            cm.Items.Add(addItem);
            cm.Items.Add(new Separator());
            cm.Items.Add(settingsItem);
            cm.Items.Add(timesItem);
            cm.Items.Add(stickyItem);
            cm.Items.Add(misItem);
            return cm;
        }

        private ContextMenu BuildCourseContextMenu(CourseItem course, DateTime? colDate)
        {
            var cm = new ContextMenu();
            var addTaskItem = new MenuItem { Header = "📝 为此课程新建作业 / DDL" };
            addTaskItem.Click += delegate
            {
                string endHm = _controller.Data.Timetable.GetCourseEndTime(course);
                var draft = new MemoTask
                {
                    Title = course.Name + " ",
                    Notes = string.IsNullOrWhiteSpace(course.Location) ? "" : ("教室：" + course.Location),
                    Quadrant = 2,
                    DeadlineIso = colDate.HasValue ? (colDate.Value.ToString("yyyy-MM-dd") + " " + endHm) : ""
                };
                _controller.OpenNewTaskDialog(draft, this);
            };
            var editItem = new MenuItem { Header = "✏️ 编辑此课程…" };
            editItem.Click += delegate
            {
                if (MisImportDialog.OpenCourseEditorDialog(this, _controller.Data.Timetable, _controller.Config, course, false))
                {
                    _controller.SaveTimetable(_controller.Data.Timetable);
                    RefreshTaskViews();
                }
            };
            var skipItem = new MenuItem { Header = string.Format("📅 仅第 {0} 周停课（本周隐藏）", _viewingTimetableWeek) };
            skipItem.Click += delegate
            {
                _controller.Data.Timetable.SkipCourseInWeek(course, _viewingTimetableWeek);
                _controller.SaveTimetable(_controller.Data.Timetable);
                RefreshTaskViews();
            };
            var delItem = new MenuItem { Header = "🗑 删除此课程" };
            delItem.Click += delegate
            {
                if (MessageBox.Show("确定删除课程「" + course.Name + "」吗？", "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    _controller.Data.Timetable.Courses.Remove(course);
                    _controller.SaveTimetable(_controller.Data.Timetable);
                    RefreshTaskViews();
                }
            };
            var ttSetItem = new MenuItem { Header = "⚙️ 课表设置…" };
            ttSetItem.Click += delegate
            {
                if (MisImportDialog.OpenTimetableSettingsDialog(this, _controller.Data.Timetable, _controller.Config))
                {
                    _controller.SaveTimetable(_controller.Data.Timetable);
                    RefreshTaskViews();
                }
            };
            var timesItem = new MenuItem { Header = "🕒 编辑作息时间…" };
            timesItem.Click += delegate
            {
                MisImportDialog.OpenSectionTimesEditor(this, _controller.Data.Timetable, _controller.Config, delegate
                {
                    _controller.SaveTimetable(_controller.Data.Timetable);
                });
            };

            cm.Items.Add(addTaskItem);
            cm.Items.Add(editItem);
            cm.Items.Add(skipItem);
            cm.Items.Add(delItem);
            cm.Items.Add(new Separator());
            cm.Items.Add(ttSetItem);
            cm.Items.Add(timesItem);
            return cm;
        }

        private Border BuildTimetableMatrixCourseCard(
            CourseItem c,
            TimetableData tt,
            DateTime? colDate,
            ThemePalette pal,
            UiScaleMetrics scale,
            int maxSec)
        {
            string colorHex = string.IsNullOrEmpty(c.ColorHex) ? "#3B82F6" : c.ColorHex;
            string startHm = tt.GetCourseStartTime(c);
            string endHm = tt.GetCourseEndTime(c);

            var cBox = new Border
            {
                Background = ThemeService.BrushFromHexAlpha(colorHex, pal.IsDark ? (byte)52 : (byte)28),
                BorderBrush = ThemeService.BrushFromHex(colorHex),
                BorderThickness = new Thickness(3, 1, 1, 1),
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(6, 5, 6, 5),
                Margin = new Thickness(2.5),
                VerticalAlignment = VerticalAlignment.Stretch,
                Cursor = Cursors.Hand,
                ToolTip = string.Format("{0}\n{1}-{2} · {3}{4}{5}\n左键查看详情 · 按住抬起拖拽调课 · 右键更多设置",
                    c.Name,
                    startHm,
                    endHm,
                    c.GetWeeksLabel(),
                    string.IsNullOrWhiteSpace(c.Location) ? "" : ("\n教室：" + c.Location),
                    string.IsNullOrWhiteSpace(c.Teacher) ? "" : ("\n教师：" + c.Teacher))
            };

            var cs = new StackPanel();
            var titleWrap = new WrapPanel();
            titleWrap.Children.Add(new TextBlock
            {
                Text = c.Name,
                FontFamily = ThemeService.AppFontFamily,
                FontSize = scale.SmallFontSize + 0.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = pal.TextPrimary,
                TextWrapping = TextWrapping.Wrap
            });
            if (c.WeekType == "odd" || c.WeekType == "even")
            {
                titleWrap.Children.Add(new Border
                {
                    Background = ThemeService.BrushFromHex(c.WeekType == "odd" ? "#DBEAFE" : "#FCE7F3"),
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(4, 0, 4, 0),
                    Margin = new Thickness(4, 1, 0, 0),
                    Child = new TextBlock
                    {
                        Text = c.WeekType == "odd" ? "单周" : "双周",
                        FontFamily = ThemeService.AppFontFamily,
                        FontSize = 9.5,
                        FontWeight = FontWeights.Bold,
                        Foreground = ThemeService.BrushFromHex(c.WeekType == "odd" ? "#1D4ED8" : "#BE185D")
                    }
                });
            }
            cs.Children.Add(titleWrap);

            if (tt.ShowTimeOnCard)
            {
                cs.Children.Add(new TextBlock
                {
                    Text = string.Format("{0}-{1}", startHm, endHm),
                    FontFamily = ThemeService.AppFontFamily,
                    FontSize = Math.Max(9.5, scale.SmallFontSize - 0.5),
                    Foreground = pal.TextSecondary,
                    Margin = new Thickness(0, 2, 0, 0)
                });
            }

            if (tt.ShowLocationOnCard && !string.IsNullOrWhiteSpace(c.Location))
            {
                cs.Children.Add(new TextBlock
                {
                    Text = "@ " + c.Location,
                    FontFamily = ThemeService.AppFontFamily,
                    FontSize = Math.Max(9.5, scale.SmallFontSize - 0.5),
                    Foreground = pal.TextSecondary,
                    TextWrapping = TextWrapping.Wrap
                });
            }

            if (tt.ShowTeacherOnCard && !string.IsNullOrWhiteSpace(c.Teacher))
            {
                cs.Children.Add(new TextBlock
                {
                    Text = c.Teacher,
                    FontFamily = ThemeService.AppFontFamily,
                    FontSize = Math.Max(9.5, scale.SmallFontSize - 0.5),
                    Foreground = pal.TextSecondary,
                    TextWrapping = TextWrapping.Wrap
                });
            }

            CourseItem capC = c;
            DateTime? capDate = colDate;
            cBox.ContextMenu = BuildCourseContextMenu(capC, capDate);

            cBox.PreviewMouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                if (_ttMatrixHost == null) return;
                _ttDragStartPoint = e.GetPosition(_ttMatrixHost);
                _ttDragPointerOffset = e.GetPosition(cBox);
                _ttPressedCourse = capC;
                _ttPressedCard = cBox;
                _ttPressedCardDate = capDate;
            };
            cBox.PreviewMouseMove += delegate(object s, MouseEventArgs e)
            {
                if (e.LeftButton == MouseButtonState.Pressed && _ttPressedCourse == capC && _ttDraggingCourse == null && _ttMatrixHost != null)
                {
                    Point curHostPos = e.GetPosition(_ttMatrixHost);
                    Vector diff = curHostPos - _ttDragStartPoint;
                    if (Math.Abs(diff.X) >= 5 || Math.Abs(diff.Y) >= 5)
                    {
                        StartTimetableCardDrag(capC, cBox, curHostPos);
                        e.Handled = true;
                    }
                }
            };
            cBox.PreviewMouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e)
            {
                if (_ttDraggingCourse == null && _ttPressedCourse == capC)
                {
                    _ttPressedCourse = null;
                    _ttPressedCard = null;
                    _ttPressedCardDate = null;
                    OpenCourseDetailDialog(capC, capDate);
                    e.Handled = true;
                }
            };

            cBox.Child = cs;
            return cBox;
        }

        private void OpenCourseDetailDialog(CourseItem course, DateTime? colDate)
        {
            if (course == null) return;
            TimetableData tt = _controller.Data.Timetable;
            ThemePalette pal = ThemeService.GetPalette(_controller.Config.PanelTheme, 0.98);
            UiScaleMetrics scale = ThemeService.GetScaleMetrics(_controller.Config.UiScale, _controller.Config.PanelFontSize);

            string colorHex = string.IsNullOrEmpty(course.ColorHex) ? "#3B82F6" : course.ColorHex;
            string startHm = tt.GetCourseStartTime(course);
            string endHm = tt.GetCourseEndTime(course);

            var dlg = new Window
            {
                Title = "课程详情",
                Width = 390,
                SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize,
                Background = pal.CardBg
            };
            GlassControlStyles.ApplyToResources(dlg.Resources, pal, scale);

            var stack = new StackPanel { Margin = new Thickness(18) };
            var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            head.Children.Add(new Border
            {
                Width = 6,
                CornerRadius = new CornerRadius(3),
                Background = ThemeService.BrushFromHex(colorHex),
                Margin = new Thickness(0, 2, 10, 2)
            });
            var titleStack = new StackPanel();
            titleStack.Children.Add(new TextBlock
            {
                Text = course.Name,
                FontFamily = ThemeService.AppFontFamily,
                FontSize = 15,
                FontWeight = FontWeights.Bold,
                Foreground = pal.TextPrimary,
                TextWrapping = TextWrapping.Wrap
            });
            titleStack.Children.Add(new TextBlock
            {
                Text = string.Format("{0} · {1} ({2}-{3})", CourseItem.GetWeekdayName(course.DayOfWeek), course.GetSectionLabel(), startHm, endHm),
                FontFamily = ThemeService.AppFontFamily,
                FontSize = 12,
                Foreground = pal.AccentBg,
                Margin = new Thickness(0, 2, 0, 0)
            });
            head.Children.Add(titleStack);
            stack.Children.Add(head);

            var infoCard = new Border
            {
                Background = pal.SurfaceBg,
                BorderBrush = pal.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(12, 9, 12, 9),
                Margin = new Thickness(0, 0, 0, 12)
            };
            var infoStack = new StackPanel();
            infoStack.Children.Add(new TextBlock
            {
                Text = "周次范围：" + course.GetWeeksLabel(),
                FontFamily = ThemeService.AppFontFamily,
                FontSize = 12.5,
                Foreground = pal.TextPrimary,
                Margin = new Thickness(0, 0, 0, 3)
            });
            if (!string.IsNullOrWhiteSpace(course.Location))
            {
                infoStack.Children.Add(new TextBlock
                {
                    Text = "上课教室：" + course.Location,
                    FontFamily = ThemeService.AppFontFamily,
                    FontSize = 12.5,
                    Foreground = pal.TextSecondary,
                    Margin = new Thickness(0, 0, 0, 3)
                });
            }
            if (!string.IsNullOrWhiteSpace(course.Teacher))
            {
                infoStack.Children.Add(new TextBlock
                {
                    Text = "任课教师：" + course.Teacher,
                    FontFamily = ThemeService.AppFontFamily,
                    FontSize = 12.5,
                    Foreground = pal.TextSecondary
                });
            }
            infoCard.Child = infoStack;
            stack.Children.Add(infoCard);

            var btnRow = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
            var taskBtn = GlassControlStyles.CreateGlassButton("📝 新建作业/DDL", pal.AccentBg, pal.AccentFg, null, 12, new Thickness(10, 5, 10, 5), 7);
            taskBtn.Margin = new Thickness(0, 0, 6, 4);
            taskBtn.Click += delegate
            {
                dlg.Close();
                var draft = new MemoTask
                {
                    Title = course.Name + " ",
                    Notes = string.IsNullOrWhiteSpace(course.Location) ? "" : ("教室：" + course.Location),
                    Quadrant = 2,
                    DeadlineIso = colDate.HasValue ? (colDate.Value.ToString("yyyy-MM-dd") + " " + endHm) : ""
                };
                _controller.OpenNewTaskDialog(draft, this);
            };
            var editBtn = GlassControlStyles.CreateGlassButton("✏️ 编辑课程", pal.HoverBg, pal.TextPrimary, pal.Border, 12, new Thickness(10, 5, 10, 5), 7);
            editBtn.Margin = new Thickness(0, 0, 6, 4);
            editBtn.Click += delegate
            {
                dlg.Close();
                if (MisImportDialog.OpenCourseEditorDialog(this, tt, _controller.Config, course, false))
                {
                    _controller.SaveTimetable(tt);
                    RefreshTaskViews();
                }
            };
            var skipBtn = GlassControlStyles.CreateGlassButton("📅 仅本周停课", pal.HoverBg, pal.TextSecondary, pal.Border, 12, new Thickness(10, 5, 10, 5), 7);
            skipBtn.Margin = new Thickness(0, 0, 6, 4);
            skipBtn.Click += delegate
            {
                dlg.Close();
                tt.SkipCourseInWeek(course, _viewingTimetableWeek);
                _controller.SaveTimetable(tt);
                RefreshTaskViews();
            };
            var delBtn = GlassControlStyles.CreateGlassButton("🗑 删除", ThemeService.BrushFromHex("#FEE2E2"), ThemeService.BrushFromHex("#DC2626"), ThemeService.BrushFromHex("#FCA5A5"), 12, new Thickness(10, 5, 10, 5), 7);
            delBtn.Margin = new Thickness(0, 0, 0, 4);
            delBtn.Click += delegate
            {
                if (MessageBox.Show("确定删除课程「" + course.Name + "」吗？", "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    dlg.Close();
                    tt.Courses.Remove(course);
                    _controller.SaveTimetable(tt);
                    RefreshTaskViews();
                }
            };

            btnRow.Children.Add(taskBtn);
            btnRow.Children.Add(editBtn);
            btnRow.Children.Add(skipBtn);
            btnRow.Children.Add(delBtn);
            stack.Children.Add(btnRow);

            dlg.Content = stack;
            dlg.ShowDialog();
        }

        private void HandleCourseDroppedOnCell(CourseItem course, int targetDay, int targetStartSec)
        {
            if (course == null || _controller.Data == null || _controller.Data.Timetable == null) return;
            TimetableData tt = _controller.Data.Timetable;
            int maxSec = tt.GetMaxSectionCount();
            int span = Math.Max(0, course.EndSection - course.StartSection);
            int clampedStart = Math.Max(1, Math.Min(Math.Max(1, maxSec - span), targetStartSec));
            int clampedEnd = Math.Min(maxSec, clampedStart + span);

            if (targetDay == course.DayOfWeek && clampedStart == course.StartSection) return;

            if (course.IsSingleWeekOnly(_viewingTimetableWeek))
            {
                tt.MoveCourse(course, targetDay, clampedStart, _viewingTimetableWeek, true);
                _controller.SaveTimetable(tt);
                RefreshTaskViews();
                return;
            }

            bool? singleOnly = ShowCourseMoveScopeDialog(course, targetDay, clampedStart, clampedEnd);
            if (!singleOnly.HasValue) return;

            tt.MoveCourse(course, targetDay, clampedStart, _viewingTimetableWeek, singleOnly.Value);
            _controller.SaveTimetable(tt);
            RefreshTaskViews();
        }

        private bool? ShowCourseMoveScopeDialog(CourseItem course, int targetDay, int clampedStart, int clampedEnd)
        {
            ThemePalette pal = ThemeService.GetPalette(_controller.Config.PanelTheme, 0.98);
            UiScaleMetrics scale = ThemeService.GetScaleMetrics(_controller.Config.UiScale, _controller.Config.PanelFontSize);

            bool? result = null;
            var dlg = new Window
            {
                Title = "选择调课生效范围",
                Width = 420,
                SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize,
                Background = pal.CardBg
            };
            GlassControlStyles.ApplyToResources(dlg.Resources, pal, scale);

            var stack = new StackPanel { Margin = new Thickness(18) };
            stack.Children.Add(new TextBlock
            {
                Text = "确认移动课程时间：",
                FontFamily = ThemeService.AppFontFamily,
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = pal.TextPrimary,
                Margin = new Thickness(0, 0, 0, 8)
            });
            stack.Children.Add(new TextBlock
            {
                Text = string.Format("「{0}」：{1} 第{2}-{3}节  →  {4} 第{5}-{6}节",
                    course.Name,
                    CourseItem.GetWeekdayName(course.DayOfWeek), course.StartSection, course.EndSection,
                    CourseItem.GetWeekdayName(targetDay), clampedStart, clampedEnd),
                FontFamily = ThemeService.AppFontFamily,
                FontSize = 12.5,
                Foreground = pal.TextSecondary,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 14)
            });

            var btnRow = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
            var cancelBtn = GlassControlStyles.CreateGlassButton("取消", pal.HoverBg, pal.TextSecondary, pal.Border, 12, new Thickness(12, 5, 12, 5), 7);
            cancelBtn.Margin = new Thickness(0, 0, 6, 4);
            cancelBtn.Click += delegate { result = null; dlg.Close(); };

            var globalBtn = GlassControlStyles.CreateGlassButton("全局调整（全部周次）", pal.SurfaceBg, pal.TextPrimary, pal.Border, 12, new Thickness(12, 5, 12, 5), 7);
            globalBtn.Margin = new Thickness(0, 0, 6, 4);
            globalBtn.Click += delegate { result = false; dlg.Close(); };

            var singleBtn = GlassControlStyles.CreateGlassButton(
                string.Format("仅调整第 {0} 周（临时调课）", _viewingTimetableWeek),
                pal.AccentBg, pal.AccentFg, null, 12, new Thickness(12, 5, 12, 5), 7);
            singleBtn.FontWeight = FontWeights.SemiBold;
            singleBtn.Margin = new Thickness(0, 0, 0, 4);
            singleBtn.Click += delegate { result = true; dlg.Close(); };

            btnRow.Children.Add(cancelBtn);
            btnRow.Children.Add(globalBtn);
            btnRow.Children.Add(singleBtn);
            stack.Children.Add(btnRow);

            dlg.Content = stack;
            dlg.ShowDialog();
            return result;
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
