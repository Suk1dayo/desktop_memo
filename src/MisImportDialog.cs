using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace DesktopMemo
{
    public class MisImportDialog : Window
    {
        private readonly TrayAndAppController _controller;
        private readonly TimetableData _workingTimetable;

        private Border _rootCard;
        private TextBox _misUrlBox;
        private TextBox _semesterNameBox;
        private TextBox _currentWeekBox;
        private TextBlock _weekStartPreviewText;
        private TextBlock _statusText;
        private StackPanel _courseListPanel;
        private Button _retryAiBtn;
        private MisPageCapture _lastCapture;

        private int _activeDebugPort = 0;

        public MisImportDialog(TrayAndAppController controller)
        {
            _controller = controller;
            _workingTimetable = CloneTimetable(_controller.Data.Timetable);

            Title = "接入 MIS 课程表与教学周数";
            Width = 760;
            Height = 680;
            MinWidth = 600;
            MinHeight = 520;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = true;
            AllowDrop = true;

            PreviewKeyDown += OnDialogPreviewKeyDown;
            BuildUi();
            RefreshPreviewList();

            Loaded += delegate { SpringAnimationHelper.AnimatePopIn(_rootCard, 260); };
        }

        private static TimetableData CloneTimetable(TimetableData src)
        {
            var copy = new TimetableData();
            if (src == null) return copy;
            copy.SemesterName = src.SemesterName ?? "";
            copy.SemesterStartMondayIso = src.SemesterStartMondayIso ?? "";
            copy.TotalWeeks = src.TotalWeeks > 0 ? src.TotalWeeks : 20;
            copy.SectionsPerDay = src.SectionsPerDay;
            copy.ShowWeekends = src.ShowWeekends;
            copy.ShowTimeOnCard = src.ShowTimeOnCard;
            copy.ShowLocationOnCard = src.ShowLocationOnCard;
            copy.ShowTeacherOnCard = src.ShowTeacherOnCard;
            copy.RowMinHeight = src.RowMinHeight;
            copy.LastSyncIso = src.LastSyncIso ?? "";
            if (src.Courses != null)
            {
                for (int i = 0; i < src.Courses.Count; i++)
                {
                    CourseItem c = src.Courses[i];
                    if (c == null) continue;
                    copy.Courses.Add(new CourseItem
                    {
                        Id = c.Id,
                        Name = c.Name,
                        Teacher = c.Teacher,
                        Location = c.Location,
                        DayOfWeek = c.DayOfWeek,
                        StartSection = c.StartSection,
                        EndSection = c.EndSection,
                        StartTime = c.StartTime,
                        EndTime = c.EndTime,
                        StartWeek = c.StartWeek,
                        EndWeek = c.EndWeek,
                        WeekType = c.WeekType,
                        CustomWeeks = c.CustomWeeks != null ? new List<int>(c.CustomWeeks) : new List<int>(),
                        ExcludedWeeks = c.ExcludedWeeks != null ? new List<int>(c.ExcludedWeeks) : new List<int>(),
                        ColorHex = c.ColorHex
                    });
                }
            }
            if (src.SectionTimes != null)
            {
                for (int i = 0; i < src.SectionTimes.Count; i++)
                {
                    SectionTimeSlot st = src.SectionTimes[i];
                    if (st == null) continue;
                    copy.SectionTimes.Add(new SectionTimeSlot
                    {
                        Section = st.Section,
                        StartTime = st.StartTime,
                        EndTime = st.EndTime
                    });
                }
            }
            return copy;
        }

        private void BuildUi()
        {
            ThemePalette pal = ThemeService.GetPalette(_controller.Config.PanelTheme, 0.98);
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
                    Opacity = 0.22
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
                Text = "📅 接入 MIS 课程表与教学周数",
                FontFamily = ThemeService.AppFontFamily,
                FontSize = scale.HeaderFontSize - 0.5,
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

            // Top Controls
            var topStack = new StackPanel { Margin = new Thickness(18, 12, 18, 8) };

            var urlGrid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            urlGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            urlGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            urlGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            urlGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var urlLbl = new TextBlock
            {
                Text = "MIS 网址：",
                FontFamily = ThemeService.AppFontFamily,
                FontSize = scale.FontSize,
                Foreground = pal.TextSecondary,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };
            Grid.SetColumn(urlLbl, 0);
            urlGrid.Children.Add(urlLbl);

            _misUrlBox = new TextBox
            {
                Text = _controller.Config.MisUrl ?? "",
                Padding = new Thickness(9, 5, 9, 5),
                FontSize = scale.FontSize,
                ToolTip = "输入学校教务 / MIS 系统的登录网址，例如 https://jw.xxx.edu.cn"
            };
            _misUrlBox.LostFocus += delegate
            {
                _controller.Config.MisUrl = (_misUrlBox.Text ?? "").Trim();
                _controller.ScheduleConfigSave();
            };
            Grid.SetColumn(_misUrlBox, 1);
            urlGrid.Children.Add(_misUrlBox);

            var openBrowserBtn = GlassControlStyles.CreateGlassButton("🌐 打开登录窗口",
                pal.SurfaceBg, pal.TextPrimary, pal.Border, scale.SmallFontSize + 1, new Thickness(11, 5, 11, 5), 7);
            openBrowserBtn.Margin = new Thickness(8, 0, 6, 0);
            openBrowserBtn.Click += OnOpenBrowserClicked;
            Grid.SetColumn(openBrowserBtn, 2);
            urlGrid.Children.Add(openBrowserBtn);

            var capturePageBtn = GlassControlStyles.CreateGlassButton("✨ 读取当前课表页",
                pal.AccentBg, pal.AccentFg, null, scale.SmallFontSize + 1, new Thickness(12, 5, 12, 5), 7);
            capturePageBtn.FontWeight = FontWeights.SemiBold;
            capturePageBtn.Click += OnCaptureMisPageClicked;
            Grid.SetColumn(capturePageBtn, 3);
            urlGrid.Children.Add(capturePageBtn);
            topStack.Children.Add(urlGrid);

            var fallbackTools = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            var pasteShotBtn = GlassControlStyles.CreateGlassButton("📷 贴课表截图识图 (Ctrl+V)",
                pal.SurfaceBg, pal.TextPrimary, pal.Border, scale.SmallFontSize + 0.5, new Thickness(10, 4, 10, 4), 7);
            pasteShotBtn.Margin = new Thickness(0, 0, 6, 4);
            pasteShotBtn.Click += OnPickOrPasteScreenshotClicked;
            fallbackTools.Children.Add(pasteShotBtn);

            var importFileBtn = GlassControlStyles.CreateGlassButton("📂 导入 .ics / .html",
                pal.SurfaceBg, pal.TextPrimary, pal.Border, scale.SmallFontSize + 0.5, new Thickness(10, 4, 10, 4), 7);
            importFileBtn.Margin = new Thickness(0, 0, 6, 4);
            importFileBtn.Click += OnImportFileClicked;
            fallbackTools.Children.Add(importFileBtn);

            var addManualBtn = GlassControlStyles.CreateGlassButton("+ 手动加课",
                pal.HoverBg, pal.TextPrimary, pal.Border, scale.SmallFontSize + 0.5, new Thickness(10, 4, 10, 4), 7);
            addManualBtn.Margin = new Thickness(0, 0, 6, 4);
            addManualBtn.Click += delegate { AddOrEditCourse(null); };
            fallbackTools.Children.Add(addManualBtn);

            var secTimesBtn = GlassControlStyles.CreateGlassButton("🕒 作息时间",
                pal.SurfaceBg, pal.TextPrimary, pal.Border, scale.SmallFontSize + 0.5, new Thickness(10, 4, 10, 4), 7);
            secTimesBtn.Margin = new Thickness(0, 0, 6, 4);
            secTimesBtn.ToolTip = "查看或调整第1-12节的具体上下课时间";
            secTimesBtn.Click += delegate { OpenSectionTimesEditor(this, _workingTimetable, _controller.Config, delegate { RefreshPreviewList(); }); };
            fallbackTools.Children.Add(secTimesBtn);

            _retryAiBtn = GlassControlStyles.CreateGlassButton("🤖 改用 AI 重解析",
                pal.SurfaceBg, pal.TextPrimary, pal.Border, scale.SmallFontSize + 0.5, new Thickness(10, 4, 10, 4), 7);
            _retryAiBtn.Margin = new Thickness(0, 0, 6, 4);
            _retryAiBtn.Visibility = Visibility.Collapsed;
            _retryAiBtn.Click += async delegate { await RunAiFallbackForLastCaptureAsync(); };
            fallbackTools.Children.Add(_retryAiBtn);

            var clearBtn = GlassControlStyles.CreateGlassButton("清空列表",
                Brushes.Transparent, pal.TextSecondary, pal.Border, scale.SmallFontSize + 0.5, new Thickness(9, 4, 9, 4), 7);
            clearBtn.Margin = new Thickness(0, 0, 0, 4);
            clearBtn.Click += delegate
            {
                _workingTimetable.Courses.Clear();
                RefreshPreviewList();
            };
            fallbackTools.Children.Add(clearBtn);
            topStack.Children.Add(fallbackTools);

            // Academic week bar
            var weekBar = new Border
            {
                Background = pal.SurfaceBg,
                BorderBrush = pal.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 0, 0, 6)
            };
            var weekWrap = new WrapPanel();
            weekWrap.Children.Add(new TextBlock
            {
                Text = "学期名称：",
                FontFamily = ThemeService.AppFontFamily,
                FontSize = scale.SmallFontSize + 1,
                Foreground = pal.TextSecondary,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            });
            _semesterNameBox = new TextBox
            {
                Width = 140,
                Padding = new Thickness(8, 3, 8, 3),
                Text = _workingTimetable.SemesterName ?? "",
                Margin = new Thickness(0, 0, 14, 0)
            };
            weekWrap.Children.Add(_semesterNameBox);

            weekWrap.Children.Add(new TextBlock
            {
                Text = "当前是第",
                FontFamily = ThemeService.AppFontFamily,
                FontSize = scale.SmallFontSize + 1,
                Foreground = pal.TextSecondary,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 4, 0)
            });
            int curWeek = _workingTimetable.GetWeekNumberForDate(DateTime.Now);
            _currentWeekBox = new TextBox
            {
                Width = 46,
                Padding = new Thickness(6, 3, 6, 3),
                Text = (curWeek >= 1 ? curWeek : 1).ToString(),
                HorizontalContentAlignment = HorizontalAlignment.Center
            };
            _currentWeekBox.TextChanged += delegate { UpdateWeekStartFromBox(); };
            weekWrap.Children.Add(_currentWeekBox);

            weekWrap.Children.Add(new TextBlock
            {
                Text = "周",
                FontFamily = ThemeService.AppFontFamily,
                FontSize = scale.SmallFontSize + 1,
                Foreground = pal.TextSecondary,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 0, 12, 0)
            });

            _weekStartPreviewText = new TextBlock
            {
                FontFamily = ThemeService.AppFontFamily,
                FontSize = scale.SmallFontSize + 0.5,
                Foreground = pal.TextSecondary,
                VerticalAlignment = VerticalAlignment.Center
            };
            weekWrap.Children.Add(_weekStartPreviewText);
            weekBar.Child = weekWrap;
            topStack.Children.Add(weekBar);

            if (string.IsNullOrEmpty(_workingTimetable.SemesterStartMondayIso))
            {
                _workingTimetable.SetCurrentWeekForDate(1, DateTime.Now);
            }
            UpdateWeekStartPreviewText();

            _statusText = new TextBlock
            {
                FontFamily = ThemeService.AppFontFamily,
                FontSize = scale.SmallFontSize + 0.5,
                Foreground = pal.TextSecondary,
                Text = "步骤：打开登录窗口登录 MIS 并切到个人课表页 → 点击「✨ 读取当前课表页」。",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(2, 2, 2, 0)
            };
            topStack.Children.Add(_statusText);

            Grid.SetRow(topStack, 1);
            mainGrid.Children.Add(topStack);

            // Course list preview
            var listBorder = new Border
            {
                Margin = new Thickness(18, 4, 18, 10),
                CornerRadius = new CornerRadius(10),
                Background = pal.SurfaceBg,
                BorderBrush = pal.Border,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8)
            };
            var scroller = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            _courseListPanel = new StackPanel();
            scroller.Content = _courseListPanel;
            listBorder.Child = scroller;
            Grid.SetRow(listBorder, 2);
            mainGrid.Children.Add(listBorder);

            // Footer
            var footer = new DockPanel { Margin = new Thickness(18, 0, 18, 16) };
            var cancelBtn = GlassControlStyles.CreateGlassButton("取消",
                pal.HoverBg, pal.TextPrimary, pal.Border, scale.FontSize, new Thickness(16, 6, 16, 6), 8);
            cancelBtn.Margin = new Thickness(0, 0, 8, 0);
            cancelBtn.Click += delegate { DialogResult = false; Close(); };

            var saveBtn = GlassControlStyles.CreateGlassButton("✓ 保存到课程表",
                pal.AccentBg, pal.AccentFg, null, scale.FontSize, new Thickness(18, 6, 18, 6), 8);
            saveBtn.FontWeight = FontWeights.SemiBold;
            saveBtn.Click += OnSaveClicked;

            var rightBtns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            rightBtns.Children.Add(cancelBtn);
            rightBtns.Children.Add(saveBtn);
            DockPanel.SetDock(rightBtns, Dock.Right);
            footer.Children.Add(rightBtns);

            Grid.SetRow(footer, 3);
            mainGrid.Children.Add(footer);

            _rootCard.Child = mainGrid;
            Content = _rootCard;
        }

        private void OnDialogPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                DialogResult = false;
                Close();
                e.Handled = true;
                return;
            }
            if (e.Key == Key.V && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
            {
                if (_misUrlBox != null && _misUrlBox.IsKeyboardFocused) return;
                if (_semesterNameBox != null && _semesterNameBox.IsKeyboardFocused) return;
                byte[] imgBytes = TryReadClipboardImageBytes();
                if (imgBytes != null && imgBytes.Length > 0)
                {
                    ParseTimetableImageBytesAsync(imgBytes);
                    e.Handled = true;
                }
            }
        }

        private static byte[] TryReadClipboardImageBytes()
        {
            try
            {
                if (Clipboard.ContainsImage())
                {
                    var bmp = Clipboard.GetImage();
                    if (bmp != null)
                        return ImageAttachmentHelper.CompressBitmapSourceToJpeg(bmp, 1800, 88);
                }
                if (Clipboard.ContainsFileDropList())
                {
                    var files = Clipboard.GetFileDropList();
                    if (files != null && files.Count > 0)
                    {
                        for (int i = 0; i < files.Count; i++)
                        {
                            string path = files[i];
                            if (ImageAttachmentHelper.IsSupportedImageExtension(path))
                            {
                                byte[] bytes = ImageAttachmentHelper.CompressImageFileToJpeg(path, 1800, 88);
                                if (bytes != null && bytes.Length > 0) return bytes;
                            }
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        private void UpdateWeekStartFromBox()
        {
            int w;
            if (int.TryParse((_currentWeekBox.Text ?? "").Trim(), out w) && w >= 1 && w <= 30)
            {
                _workingTimetable.SetCurrentWeekForDate(w, DateTime.Now);
                UpdateWeekStartPreviewText();
            }
        }

        private void UpdateWeekStartPreviewText()
        {
            if (_weekStartPreviewText == null) return;
            DateTime? start = _workingTimetable.GetSemesterStartMonday();
            _weekStartPreviewText.Text = start.HasValue
                ? ("第1周周一：" + start.Value.ToString("yyyy-MM-dd"))
                : "";
        }

        private async void OnOpenBrowserClicked(object sender, RoutedEventArgs e)
        {
            string url = (_misUrlBox.Text ?? "").Trim();
            _controller.Config.MisUrl = url;
            _controller.ScheduleConfigSave();

            string profileDir = Path.Combine(_controller.Storage.DataDirectory, "mis_browser_profile");
            int port;
            Process proc;
            string err;
            if (!TimetableService.LaunchMisBrowser(url, profileDir, out port, out proc, out err))
            {
                MessageBox.Show(err, "无法打开 MIS", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _activeDebugPort = port;
            if (port > 0)
            {
                _statusText.Text = "正在启动 MIS 专用窗口并连接读取通道…";
                _statusText.Foreground = ThemeService.BrushFromHex("#2563EB");
                bool ready = await TimetableService.WaitForCdpReadyAsync(port, 3500);
                if (ready)
                {
                    _statusText.Text = "✓ MIS 专用窗口与读取通道已就绪（登录并切到课表页后，点「✨ 读取当前课表页」）。";
                    _statusText.Foreground = ThemeService.BrushFromHex("#059669");
                }
                else
                {
                    _statusText.Text = "已启动 MIS 窗口；若等页面加载完再点「✨ 读取当前课表页」仍失败，也可直接截图按 Ctrl+V 粘贴识别。";
                    _statusText.Foreground = ThemeService.BrushFromHex("#D97706");
                }
            }
            else
            {
                _statusText.Text = "已在默认浏览器打开：登录课表页后可直接截图按 Ctrl+V 粘贴到此处识图。";
                _statusText.Foreground = ThemeService.BrushFromHex("#D97706");
            }
        }

        private async void OnCaptureMisPageClicked(object sender, RoutedEventArgs e)
        {
            string profileDir = Path.Combine(_controller.Storage.DataDirectory, "mis_browser_profile");
            if (_activeDebugPort <= 0 || !TimetableService.IsCdpPortAlive(_activeDebugPort))
            {
                int recovered = TimetableService.TryFindAliveDebugPort(profileDir);
                if (recovered > 0) _activeDebugPort = recovered;
            }

            if (_activeDebugPort <= 0)
            {
                _statusText.Text = "⚠ 请先点击「🌐 打开登录窗口」进入 MIS 课表页（或直接截图 Ctrl+V 识别）。";
                _statusText.Foreground = ThemeService.BrushFromHex("#D97706");
                return;
            }

            _statusText.Text = "正在抓取 MIS 页面结构与截图…";
            _statusText.Foreground = ThemeService.BrushFromHex("#2563EB");

            MisPageCapture cap = await TimetableService.CaptureActiveMisPageAsync(_activeDebugPort);
            if (!cap.Success)
            {
                _statusText.Text = "⚠ " + cap.ErrorMessage;
                _statusText.Foreground = ThemeService.BrushFromHex("#DC2626");
                return;
            }

            _lastCapture = cap;
            if (_retryAiBtn != null) _retryAiBtn.Visibility = Visibility.Visible;

            // Step 1: Try deterministic DOM table parser first (zero column drift, preserves odd/even & left time axis)
            DirectTimetableParseResult direct = TimetableService.ParseCapturedTimetable(cap, DateTime.Now);
            if (direct.Success && direct.Timetable != null && direct.Timetable.Courses.Count > 0)
            {
                ApplyDirectParseResult(direct, true);
                return;
            }

            // Step 2: Fallback to AI with structured coordinates + screenshot
            if (string.IsNullOrWhiteSpace(_controller.Config.AiApiKey))
            {
                TimetableData fb = TimetableService.ParseFallbackTimetableText(cap.ExtractedText, DateTime.Now);
                if (!string.IsNullOrEmpty(fb.SemesterStartMondayIso))
                {
                    _workingTimetable.SemesterStartMondayIso = fb.SemesterStartMondayIso;
                    int cw = _workingTimetable.GetWeekNumberForDate(DateTime.Now);
                    if (cw >= 1) _currentWeekBox.Text = cw.ToString();
                    UpdateWeekStartPreviewText();
                    _statusText.Text = "✓ 已识别教学周信息；未能识别到标准课表表格，可配置 AI 后点「🤖 改用 AI 重解析」或手动加课。";
                    _statusText.Foreground = ThemeService.BrushFromHex("#059669");
                }
                else
                {
                    _statusText.Text = "⚠ 当前页未检测到标准 HTML 课表表格，且未配置 AI API Key；可导入 .ics/.html 或先配置 DeepSeek。";
                    _statusText.Foreground = ThemeService.BrushFromHex("#D97706");
                }
                return;
            }

            await RunAiFallbackForLastCaptureAsync();
        }

        private async System.Threading.Tasks.Task RunAiFallbackForLastCaptureAsync()
        {
            if (_lastCapture == null) return;
            if (string.IsNullOrWhiteSpace(_controller.Config.AiApiKey))
            {
                _statusText.Text = "⚠ 请先在设置中配置 DeepSeek API Key。";
                _statusText.Foreground = ThemeService.BrushFromHex("#D97706");
                return;
            }

            string textInput = !string.IsNullOrWhiteSpace(_lastCapture.StructuredGridText)
                ? _lastCapture.StructuredGridText
                : _lastCapture.ExtractedText;
            _statusText.Text = _workingTimetable.Courses.Count == 0
                ? string.Format("未在当前页直读到标准课表（捕获表格数：{0}），改用 {1} 解析…", _lastCapture.CapturedTableCount, _controller.Config.AiModel)
                : ("正在调用 " + _controller.Config.AiModel + " 解析课表（携带坐标与单双周约束）…");
            _statusText.Foreground = ThemeService.BrushFromHex("#2563EB");
            AiTimetableExtractOutcome res = await AiSortService.ExtractTimetableAsync(
                _controller.Config, textInput, _lastCapture.ScreenshotDataUrl, DateTime.Now);
            ApplyExtractOutcome(res);
        }

        private void ApplyDirectParseResult(DirectTimetableParseResult direct, bool fromWebDom)
        {
            _workingTimetable.Courses = direct.Timetable.Courses ?? new List<CourseItem>();
            if (direct.Timetable.SectionTimes != null && direct.Timetable.SectionTimes.Count > 0)
            {
                _workingTimetable.SectionTimes.Clear();
                for (int i = 0; i < direct.Timetable.SectionTimes.Count; i++)
                {
                    SectionTimeSlot st = direct.Timetable.SectionTimes[i];
                    if (st != null) _workingTimetable.SetSectionTime(st.Section, st.StartTime, st.EndTime);
                }
            }
            if (direct.Timetable.SectionsPerDay >= 4 && direct.Timetable.SectionsPerDay <= 16)
            {
                _workingTimetable.SectionsPerDay = direct.Timetable.SectionsPerDay;
            }
            if (!string.IsNullOrWhiteSpace(direct.Timetable.SemesterName))
            {
                _workingTimetable.SemesterName = direct.Timetable.SemesterName;
                _semesterNameBox.Text = direct.Timetable.SemesterName;
            }
            if (direct.CurrentWeek >= 1 && direct.CurrentWeek <= 30)
            {
                _workingTimetable.SetCurrentWeekForDate(direct.CurrentWeek, DateTime.Now);
                _currentWeekBox.Text = direct.CurrentWeek.ToString();
                UpdateWeekStartPreviewText();
            }
            _workingTimetable.BackfillCourseTimesFromSections();
            RefreshPreviewList();
            _statusText.Text = string.Format(
                "✓ {0}：已直读 {1} 条课程{2}{3}，确认无误后点击「保存到课程表」。",
                fromWebDom ? "MIS 网页直接解析成功" : "HTML 课表直接解析成功",
                _workingTimetable.Courses.Count,
                _workingTimetable.SectionTimes.Count > 0 ? string.Format("、{0} 个节次时间", _workingTimetable.SectionTimes.Count) : "",
                direct.CurrentWeek >= 1 ? string.Format("（当前第{0}周）", direct.CurrentWeek) : "");
            _statusText.Foreground = ThemeService.BrushFromHex("#059669");
        }

        private void OnPickOrPasteScreenshotClicked(object sender, RoutedEventArgs e)
        {
            byte[] clipBytes = TryReadClipboardImageBytes();
            if (clipBytes != null && clipBytes.Length > 0)
            {
                ParseTimetableImageBytesAsync(clipBytes);
                return;
            }

            var ofd = new Microsoft.Win32.OpenFileDialog
            {
                Title = "选择课表截图",
                Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.webp"
            };
            if (ofd.ShowDialog(this) == true)
            {
                byte[] bytes = ImageAttachmentHelper.CompressImageFileToJpeg(ofd.FileName, 1800, 88);
                if (bytes != null) ParseTimetableImageBytesAsync(bytes);
            }
        }

        private async void ParseTimetableImageBytesAsync(byte[] imgBytes)
        {
            if (string.IsNullOrWhiteSpace(_controller.Config.AiApiKey))
            {
                _statusText.Text = "⚠ 课表截图识别需要先在设置中配置 DeepSeek API Key。";
                _statusText.Foreground = ThemeService.BrushFromHex("#D97706");
                return;
            }

            _statusText.Text = "正在识别课表截图中的课程与周数…";
            _statusText.Foreground = ThemeService.BrushFromHex("#2563EB");
            string dataUrl = ImageAttachmentHelper.ToDataUrl(imgBytes);
            AiTimetableExtractOutcome res = await AiSortService.ExtractTimetableAsync(_controller.Config, "", dataUrl, DateTime.Now);
            ApplyExtractOutcome(res);
        }

        private async void OnImportFileClicked(object sender, RoutedEventArgs e)
        {
            var ofd = new Microsoft.Win32.OpenFileDialog
            {
                Title = "选择课表文件 (.ics / .html / .txt)",
                Filter = "课表文件 (*.ics;*.html;*.htm;*.txt)|*.ics;*.html;*.htm;*.txt|所有文件|*.*"
            };
            if (ofd.ShowDialog(this) != true) return;

            string content = File.ReadAllText(ofd.FileName, Encoding.UTF8);
            string ext = Path.GetExtension(ofd.FileName).ToLowerInvariant();
            if (ext == ".ics")
            {
                TimetableData parsed = TimetableService.ParseIcsContent(content, DateTime.Now);
                if (parsed.Courses.Count > 0)
                {
                    _workingTimetable.Courses = parsed.Courses;
                    if (!string.IsNullOrEmpty(parsed.SemesterStartMondayIso))
                    {
                        _workingTimetable.SemesterStartMondayIso = parsed.SemesterStartMondayIso;
                        int cw = _workingTimetable.GetWeekNumberForDate(DateTime.Now);
                        if (cw >= 1) _currentWeekBox.Text = cw.ToString();
                        UpdateWeekStartPreviewText();
                    }
                    RefreshPreviewList();
                    _statusText.Text = string.Format("✓ 已从 .ics 导入 {0} 门课程。", parsed.Courses.Count);
                    _statusText.Foreground = ThemeService.BrushFromHex("#059669");
                    return;
                }
            }

            if (ext == ".html" || ext == ".htm")
            {
                DirectTimetableParseResult htmlDirect = TimetableService.ParseHtmlTimetable(content, DateTime.Now);
                if (htmlDirect.Success && htmlDirect.Timetable != null && htmlDirect.Timetable.Courses.Count > 0)
                {
                    ApplyDirectParseResult(htmlDirect, false);
                    return;
                }
            }

            if (!string.IsNullOrWhiteSpace(_controller.Config.AiApiKey))
            {
                _statusText.Text = "正在使用 AI 解析文件内容…";
                _statusText.Foreground = ThemeService.BrushFromHex("#2563EB");
                AiTimetableExtractOutcome res = await AiSortService.ExtractTimetableAsync(_controller.Config, content, "", DateTime.Now);
                ApplyExtractOutcome(res);
            }
            else
            {
                _statusText.Text = "⚠ 未能离线解析该文件，配置 DeepSeek API Key 后可智能解析 HTML 课表。";
                _statusText.Foreground = ThemeService.BrushFromHex("#D97706");
            }
        }

        private void ApplyExtractOutcome(AiTimetableExtractOutcome res)
        {
            if (res == null || !res.Success)
            {
                _statusText.Text = "⚠ " + (res != null ? res.ErrorMessage : "解析失败");
                _statusText.Foreground = ThemeService.BrushFromHex("#DC2626");
                return;
            }

            _workingTimetable.Courses = res.Courses ?? new List<CourseItem>();
            if (res.SectionTimes != null && res.SectionTimes.Count > 0)
            {
                for (int i = 0; i < res.SectionTimes.Count; i++)
                {
                    SectionTimeSlot st = res.SectionTimes[i];
                    if (st != null) _workingTimetable.SetSectionTime(st.Section, st.StartTime, st.EndTime);
                }
            }
            _workingTimetable.BackfillCourseTimesFromSections();
            if (!string.IsNullOrWhiteSpace(res.SemesterName))
            {
                _workingTimetable.SemesterName = res.SemesterName;
                _semesterNameBox.Text = res.SemesterName;
            }
            if (res.CurrentWeek >= 1 && res.CurrentWeek <= 30)
            {
                _workingTimetable.SetCurrentWeekForDate(res.CurrentWeek, DateTime.Now);
                _currentWeekBox.Text = res.CurrentWeek.ToString();
                UpdateWeekStartPreviewText();
            }

            RefreshPreviewList();
            _statusText.Text = string.Format("✓ 成功识别 {0} 条课程安排{1}{2}，确认无误后点击「保存到课程表」。",
                _workingTimetable.Courses.Count,
                _workingTimetable.SectionTimes.Count > 0 ? string.Format("（含 {0} 个作息时段）", _workingTimetable.SectionTimes.Count) : "",
                res.CurrentWeek >= 1 ? ("（当前第" + res.CurrentWeek + "周）") : "");
            _statusText.Foreground = ThemeService.BrushFromHex("#059669");
        }

        private void RefreshPreviewList()
        {
            _courseListPanel.Children.Clear();
            ThemePalette pal = ThemeService.GetPalette(_controller.Config.PanelTheme, 0.98);
            UiScaleMetrics scale = ThemeService.GetScaleMetrics(_controller.Config.UiScale, _controller.Config.PanelFontSize);

            if (_workingTimetable.Courses.Count == 0)
            {
                _courseListPanel.Children.Add(new TextBlock
                {
                    Text = "暂无课程数据。可在上方连接 MIS 课表页读取、贴截图，或点「+ 手动加课」。",
                    FontFamily = ThemeService.AppFontFamily,
                    FontSize = scale.SmallFontSize + 1,
                    Foreground = pal.TextSecondary,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(8, 28, 8, 8)
                });
                return;
            }

            var sorted = new List<CourseItem>(_workingTimetable.Courses);
            sorted.Sort(delegate(CourseItem a, CourseItem b)
            {
                int cmp = a.DayOfWeek.CompareTo(b.DayOfWeek);
                if (cmp != 0) return cmp;
                return a.StartSection.CompareTo(b.StartSection);
            });

            for (int i = 0; i < sorted.Count; i++)
            {
                CourseItem c = sorted[i];
                var rowCard = new Border
                {
                    Background = pal.CardBg,
                    BorderBrush = pal.Border,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(10, 7, 10, 7),
                    Margin = new Thickness(0, 0, 0, 6)
                };

                var dock = new DockPanel();
                var btns = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                CourseItem cap = c;
                var editBtn = GlassControlStyles.CreateGlassButton("编辑", pal.HoverBg, pal.TextPrimary, pal.Border, 11, new Thickness(8, 2, 8, 2), 6);
                editBtn.Margin = new Thickness(0, 0, 4, 0);
                editBtn.Click += delegate { AddOrEditCourse(cap); };
                var delBtn = GlassControlStyles.CreateGlassButton("删除", pal.HoverBg, ThemeService.BrushFromHex("#DC2626"), pal.Border, 11, new Thickness(8, 2, 8, 2), 6);
                delBtn.Click += delegate { _workingTimetable.Courses.Remove(cap); RefreshPreviewList(); };
                btns.Children.Add(editBtn);
                btns.Children.Add(delBtn);
                DockPanel.SetDock(btns, Dock.Right);
                dock.Children.Add(btns);

                var info = new StackPanel();
                var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
                titleRow.Children.Add(new Border
                {
                    Width = 8,
                    Height = 8,
                    CornerRadius = new CornerRadius(4),
                    Background = ThemeService.BrushFromHex(string.IsNullOrEmpty(c.ColorHex) ? "#3B82F6" : c.ColorHex),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 7, 0)
                });
                titleRow.Children.Add(new TextBlock
                {
                    Text = c.Name,
                    FontFamily = ThemeService.AppFontFamily,
                    FontSize = scale.FontSize,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = pal.TextPrimary
                });
                if (c.WeekType == "odd" || c.WeekType == "even")
                {
                    titleRow.Children.Add(new Border
                    {
                        Background = ThemeService.BrushFromHex(c.WeekType == "odd" ? "#DBEAFE" : "#FCE7F3"),
                        CornerRadius = new CornerRadius(4),
                        Padding = new Thickness(5, 1, 5, 1),
                        Margin = new Thickness(6, 0, 0, 0),
                        VerticalAlignment = VerticalAlignment.Center,
                        Child = new TextBlock
                        {
                            Text = c.WeekType == "odd" ? "单周" : "双周",
                            FontFamily = ThemeService.AppFontFamily,
                            FontSize = 10,
                            FontWeight = FontWeights.SemiBold,
                            Foreground = ThemeService.BrushFromHex(c.WeekType == "odd" ? "#1D4ED8" : "#BE185D")
                        }
                    });
                }
                info.Children.Add(titleRow);

                string sub = string.Format("{0} · {1} ({2}-{3}) · {4}",
                    CourseItem.GetWeekdayName(c.DayOfWeek),
                    c.GetSectionLabel(),
                    _workingTimetable.GetCourseStartTime(c),
                    _workingTimetable.GetCourseEndTime(c),
                    c.GetWeeksLabel());
                if (!string.IsNullOrWhiteSpace(c.Location)) sub += " · " + c.Location;
                if (!string.IsNullOrWhiteSpace(c.Teacher)) sub += " · " + c.Teacher;

                info.Children.Add(new TextBlock
                {
                    Text = sub,
                    FontFamily = ThemeService.AppFontFamily,
                    FontSize = scale.SmallFontSize,
                    Foreground = pal.TextSecondary,
                    Margin = new Thickness(15, 2, 0, 0)
                });

                dock.Children.Add(info);
                rowCard.Child = dock;
                _courseListPanel.Children.Add(rowCard);
            }
        }

        private void AddOrEditCourse(CourseItem existing)
        {
            CourseItem target = existing ?? new CourseItem();
            if (OpenCourseEditorDialog(this, _workingTimetable, _controller.Config, target, existing == null))
            {
                RefreshPreviewList();
            }
        }

        public static bool OpenCourseEditorDialog(Window owner, TimetableData tt, AppConfig cfg, CourseItem target, bool isNew)
        {
            if (tt == null || target == null) return false;
            ThemePalette pal = ThemeService.GetPalette(cfg != null ? cfg.PanelTheme : "claude_warm", 0.98);
            UiScaleMetrics scale = ThemeService.GetScaleMetrics(cfg != null ? cfg.UiScale : "standard", cfg != null ? cfg.PanelFontSize : 13);

            var dlg = new Window
            {
                Title = isNew ? "添加课程" : "编辑课程",
                Width = 448,
                SizeToContent = SizeToContent.Height,
                WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
                Owner = owner,
                ResizeMode = ResizeMode.NoResize,
                Background = pal.CardBg
            };
            GlassControlStyles.ApplyToResources(dlg.Resources, pal, scale);

            var grid = new Grid { Margin = new Thickness(18) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(88) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var nameBox = new TextBox { Text = target.Name ?? "", Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(0, 3, 0, 6) };
            var dayBox = new ComboBox { Margin = new Thickness(0, 3, 0, 6) };
            for (int d = 1; d <= 7; d++) dayBox.Items.Add(new ComboBoxItem { Content = CourseItem.GetWeekdayName(d), Tag = d });
            dayBox.SelectedIndex = Math.Max(0, Math.Min(6, target.DayOfWeek - 1));

            int maxHintSec = tt.GetMaxSectionCount();
            var secRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 6) };
            var sSecBox = new TextBox { Width = 48, Text = target.StartSection.ToString(), Padding = new Thickness(6, 4, 6, 4) };
            var eSecBox = new TextBox { Width = 48, Text = target.EndSection.ToString(), Padding = new Thickness(6, 4, 6, 4) };
            secRow.Children.Add(sSecBox);
            secRow.Children.Add(new TextBlock { Text = " 至 ", VerticalAlignment = VerticalAlignment.Center, Foreground = pal.TextSecondary });
            secRow.Children.Add(eSecBox);
            secRow.Children.Add(new TextBlock { Text = string.Format(" 节（当前共 {0} 节）", maxHintSec), VerticalAlignment = VerticalAlignment.Center, Foreground = pal.TextSecondary, FontSize = 11.5 });

            var wkRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 6) };
            var sWkBox = new TextBox { Width = 48, Text = target.StartWeek.ToString(), Padding = new Thickness(6, 4, 6, 4) };
            var eWkBox = new TextBox { Width = 48, Text = target.EndWeek.ToString(), Padding = new Thickness(6, 4, 6, 4) };
            var typeBox = new ComboBox { Width = 84, Margin = new Thickness(8, 0, 0, 0) };
            typeBox.Items.Add(new ComboBoxItem { Content = "全部周", Tag = "all" });
            typeBox.Items.Add(new ComboBoxItem { Content = "仅单周", Tag = "odd" });
            typeBox.Items.Add(new ComboBoxItem { Content = "仅双周", Tag = "even" });
            typeBox.SelectedIndex = target.WeekType == "odd" ? 1 : (target.WeekType == "even" ? 2 : 0);
            wkRow.Children.Add(sWkBox);
            wkRow.Children.Add(new TextBlock { Text = " - ", VerticalAlignment = VerticalAlignment.Center, Foreground = pal.TextSecondary });
            wkRow.Children.Add(eWkBox);
            wkRow.Children.Add(new TextBlock { Text = " 周", VerticalAlignment = VerticalAlignment.Center, Foreground = pal.TextSecondary });
            wkRow.Children.Add(typeBox);

            var locBox = new TextBox { Text = target.Location ?? "", Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(0, 3, 0, 6) };
            var teacherBox = new TextBox { Text = target.Teacher ?? "", Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(0, 3, 0, 6) };

            string selectedColor = string.IsNullOrEmpty(target.ColorHex) ? TimetableService.PickCourseColorHex(target.Name) : target.ColorHex;
            string[] colorChoices = new string[] { "#2563EB", "#D97706", "#059669", "#7C3AED", "#DC2626", "#0891B2", "#BE4B62", "#4F46E5" };
            var colorRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 6) };
            var colorBorders = new Border[colorChoices.Length];
            Action refreshColorDots = delegate
            {
                for (int ci = 0; ci < colorChoices.Length; ci++)
                {
                    bool active = string.Equals(selectedColor, colorChoices[ci], StringComparison.OrdinalIgnoreCase);
                    colorBorders[ci].BorderBrush = active ? pal.TextPrimary : Brushes.Transparent;
                    colorBorders[ci].BorderThickness = new Thickness(active ? 2 : 1);
                }
            };
            for (int ci = 0; ci < colorChoices.Length; ci++)
            {
                string hex = colorChoices[ci];
                var dot = new Border
                {
                    Width = 22,
                    Height = 22,
                    CornerRadius = new CornerRadius(11),
                    Background = ThemeService.BrushFromHex(hex),
                    Margin = new Thickness(0, 0, 7, 0),
                    Cursor = Cursors.Hand
                };
                dot.MouseLeftButtonUp += delegate { selectedColor = hex; refreshColorDots(); };
                colorBorders[ci] = dot;
                colorRow.Children.Add(dot);
            }
            refreshColorDots();

            Action<string, UIElement> addRow = delegate(string lbl, UIElement ctrl)
            {
                int r = grid.RowDefinitions.Count;
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var tb = new TextBlock { Text = lbl, VerticalAlignment = VerticalAlignment.Center, Foreground = pal.TextSecondary, FontFamily = ThemeService.AppFontFamily, FontSize = 12.5 };
                Grid.SetRow(tb, r); Grid.SetColumn(tb, 0); grid.Children.Add(tb);
                Grid.SetRow(ctrl, r); Grid.SetColumn(ctrl, 1); grid.Children.Add(ctrl);
            };

            addRow("课程名称：", nameBox);
            addRow("星期：", dayBox);
            addRow("节次：", secRow);
            addRow("周次：", wkRow);
            addRow("教室：", locBox);
            addRow("教师：", teacherBox);
            addRow("卡片配色：", colorRow);

            bool clearExcluded = false;
            if (target.ExcludedWeeks != null && target.ExcludedWeeks.Count > 0)
            {
                var sortedEx = new List<int>(target.ExcludedWeeks);
                sortedEx.Sort();
                var exRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 6) };
                var exLbl = new TextBlock
                {
                    Text = "已跳过第 " + string.Join(",", sortedEx.ConvertAll(delegate(int w) { return w.ToString(); }).ToArray()) + " 周",
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = pal.TextSecondary,
                    FontSize = 12,
                    Margin = new Thickness(0, 0, 8, 0)
                };
                var restoreBtn = GlassControlStyles.CreateGlassButton("恢复全部周", pal.HoverBg, pal.TextPrimary, pal.Border, 11, new Thickness(8, 2, 8, 2), 6);
                restoreBtn.Click += delegate
                {
                    clearExcluded = true;
                    exLbl.Text = "将在保存后恢复所有跳过周";
                    restoreBtn.IsEnabled = false;
                };
                exRow.Children.Add(exLbl);
                exRow.Children.Add(restoreBtn);
                addRow("单周调停：", exRow);
            }

            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            var cancelBtn = GlassControlStyles.CreateGlassButton("取消", pal.HoverBg, pal.TextPrimary, pal.Border, 12.5, new Thickness(14, 5, 14, 5), 7);
            cancelBtn.Margin = new Thickness(0, 0, 8, 0);
            cancelBtn.Click += delegate { dlg.DialogResult = false; dlg.Close(); };

            int origStartSec = target.StartSection;
            int origEndSec = target.EndSection;
            var okBtn = GlassControlStyles.CreateGlassButton("确定", pal.AccentBg, pal.AccentFg, null, 12.5, new Thickness(16, 5, 16, 5), 7);
            okBtn.FontWeight = FontWeights.SemiBold;
            okBtn.Click += delegate
            {
                string nm = (nameBox.Text ?? "").Trim();
                if (string.IsNullOrEmpty(nm)) return;
                target.Name = nm;
                var dSel = dayBox.SelectedItem as ComboBoxItem;
                target.DayOfWeek = dSel != null && dSel.Tag is int ? (int)dSel.Tag : 1;
                int ss, es, sw, ew;
                if (int.TryParse(sSecBox.Text, out ss)) target.StartSection = Math.Max(1, Math.Min(16, ss));
                if (int.TryParse(eSecBox.Text, out es)) target.EndSection = Math.Max(target.StartSection, Math.Min(16, es));
                if (target.StartSection != origStartSec || target.EndSection != origEndSec)
                {
                    target.StartTime = "";
                    target.EndTime = "";
                }
                if (int.TryParse(sWkBox.Text, out sw)) target.StartWeek = Math.Max(1, Math.Min(30, sw));
                if (int.TryParse(eWkBox.Text, out ew)) target.EndWeek = Math.Max(target.StartWeek, Math.Min(30, ew));
                var tSel = typeBox.SelectedItem as ComboBoxItem;
                target.WeekType = tSel != null && tSel.Tag != null ? tSel.Tag.ToString() : "all";
                if (target.CustomWeeks != null) target.CustomWeeks.Clear();
                if (clearExcluded && target.ExcludedWeeks != null) target.ExcludedWeeks.Clear();
                target.Location = (locBox.Text ?? "").Trim();
                target.Teacher = (teacherBox.Text ?? "").Trim();
                target.ColorHex = string.IsNullOrEmpty(selectedColor) ? TimetableService.PickCourseColorHex(target.Name) : selectedColor;
                if (isNew && tt.Courses != null && !tt.Courses.Contains(target)) tt.Courses.Add(target);
                dlg.DialogResult = true;
                dlg.Close();
            };
            footer.Children.Add(cancelBtn);
            footer.Children.Add(okBtn);
            Grid.SetRow(footer, grid.RowDefinitions.Count - 1);
            Grid.SetColumnSpan(footer, 2);
            grid.Children.Add(footer);

            dlg.Content = grid;
            return dlg.ShowDialog() == true;
        }

        public static bool OpenTimetableSettingsDialog(Window owner, TimetableData tt, AppConfig cfg)
        {
            if (tt == null) return false;
            ThemePalette pal = ThemeService.GetPalette(cfg != null ? cfg.PanelTheme : "claude_warm", 0.98);
            UiScaleMetrics scale = ThemeService.GetScaleMetrics(cfg != null ? cfg.UiScale : "standard", cfg != null ? cfg.PanelFontSize : 13);

            var dlg = new Window
            {
                Title = "课表设置",
                Width = 456,
                SizeToContent = SizeToContent.Height,
                WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
                Owner = owner,
                ResizeMode = ResizeMode.NoResize,
                Background = pal.CardBg
            };
            GlassControlStyles.ApplyToResources(dlg.Resources, pal, scale);

            var grid = new Grid { Margin = new Thickness(18) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(108) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            Action<string, UIElement> addRow = delegate(string lbl, UIElement ctrl)
            {
                int r = grid.RowDefinitions.Count;
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var tb = new TextBlock { Text = lbl, VerticalAlignment = VerticalAlignment.Center, Foreground = pal.TextSecondary, FontFamily = ThemeService.AppFontFamily, FontSize = 12.5 };
                Grid.SetRow(tb, r); Grid.SetColumn(tb, 0); grid.Children.Add(tb);
                Grid.SetRow(ctrl, r); Grid.SetColumn(ctrl, 1); grid.Children.Add(ctrl);
            };

            var semBox = new TextBox { Text = tt.SemesterName ?? "", Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(0, 3, 0, 6) };
            addRow("学期名称：", semBox);

            int curW = tt.GetWeekNumberForDate(DateTime.Now);
            if (curW < 1) curW = 1;
            var curWRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 6) };
            var curWBox = new TextBox { Width = 52, Text = curW.ToString(), Padding = new Thickness(6, 4, 6, 4), HorizontalContentAlignment = HorizontalAlignment.Center };
            var mondayHint = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Foreground = pal.TextSecondary, FontSize = 11.5, Margin = new Thickness(10, 0, 0, 0) };
            Action updateMondayHint = delegate
            {
                int wv;
                if (int.TryParse((curWBox.Text ?? "").Trim(), out wv) && wv >= 1 && wv <= 30)
                {
                    int delta = ((int)DateTime.Now.DayOfWeek + 6) % 7;
                    DateTime start = DateTime.Now.Date.AddDays(-delta).AddDays(-(wv - 1) * 7);
                    mondayHint.Text = "第1周周一：" + start.ToString("yyyy-MM-dd");
                }
                else
                {
                    mondayHint.Text = "";
                }
            };
            curWBox.TextChanged += delegate { updateMondayHint(); };
            updateMondayHint();
            curWRow.Children.Add(curWBox);
            curWRow.Children.Add(new TextBlock { Text = " 周", VerticalAlignment = VerticalAlignment.Center, Foreground = pal.TextSecondary });
            curWRow.Children.Add(mondayHint);
            addRow("当前教学周：", curWRow);

            var totalWRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 6) };
            var totalWBox = new TextBox { Width = 52, Text = (tt.TotalWeeks > 0 ? tt.TotalWeeks : 20).ToString(), Padding = new Thickness(6, 4, 6, 4), HorizontalContentAlignment = HorizontalAlignment.Center };
            totalWRow.Children.Add(totalWBox);
            totalWRow.Children.Add(new TextBlock { Text = " 周（8-30）", VerticalAlignment = VerticalAlignment.Center, Foreground = pal.TextSecondary, FontSize = 11.5 });
            addRow("学期总周数：", totalWRow);

            var secRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 6) };
            var secCombo = new ComboBox { Width = 148, Margin = new Thickness(0, 0, 8, 0) };
            secCombo.Items.Add(new ComboBoxItem { Content = "自动跟随课程", Tag = 0 });
            for (int n = 8; n <= 16; n++) secCombo.Items.Add(new ComboBoxItem { Content = n + " 节", Tag = n });
            var customSecBox = new TextBox
            {
                Width = 52,
                Text = tt.SectionsPerDay >= 4 && tt.SectionsPerDay <= 16 ? tt.SectionsPerDay.ToString() : "",
                Padding = new Thickness(6, 4, 6, 4),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                ToolTip = "也可直接输入 4-16 之间的节数，留空为自动"
            };
            bool syncingSec = false;
            int matchedIdx = 0;
            for (int i = 0; i < secCombo.Items.Count; i++)
            {
                var item = secCombo.Items[i] as ComboBoxItem;
                if (item != null && item.Tag is int && (int)item.Tag == tt.SectionsPerDay) { matchedIdx = i; break; }
            }
            secCombo.SelectedIndex = matchedIdx;
            secCombo.SelectionChanged += delegate
            {
                if (syncingSec) return;
                syncingSec = true;
                var sel = secCombo.SelectedItem as ComboBoxItem;
                int v = sel != null && sel.Tag is int ? (int)sel.Tag : 0;
                customSecBox.Text = v >= 4 ? v.ToString() : "";
                syncingSec = false;
            };
            customSecBox.TextChanged += delegate
            {
                if (syncingSec) return;
                syncingSec = true;
                int cv;
                if (int.TryParse((customSecBox.Text ?? "").Trim(), out cv) && cv >= 4 && cv <= 16)
                {
                    int found = -1;
                    for (int i = 0; i < secCombo.Items.Count; i++)
                    {
                        var it = secCombo.Items[i] as ComboBoxItem;
                        if (it != null && it.Tag is int && (int)it.Tag == cv) { found = i; break; }
                    }
                    if (found >= 0) secCombo.SelectedIndex = found;
                }
                else if (string.IsNullOrWhiteSpace(customSecBox.Text))
                {
                    secCombo.SelectedIndex = 0;
                }
                syncingSec = false;
            };
            secRow.Children.Add(secCombo);
            secRow.Children.Add(customSecBox);
            secRow.Children.Add(new TextBlock { Text = " 节 (4-16)", VerticalAlignment = VerticalAlignment.Center, Foreground = pal.TextSecondary, FontSize = 11.5 });
            addRow("每天节数：", secRow);

            var heightCombo = new ComboBox { Width = 148, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 3, 0, 8) };
            heightCombo.Items.Add(new ComboBoxItem { Content = "紧凑 (42px)", Tag = 42 });
            heightCombo.Items.Add(new ComboBoxItem { Content = "标准 (52px)", Tag = 52 });
            heightCombo.Items.Add(new ComboBoxItem { Content = "宽松 (64px)", Tag = 64 });
            heightCombo.SelectedIndex = tt.RowMinHeight <= 46 ? 0 : (tt.RowMinHeight >= 59 ? 2 : 1);
            addRow("行高密度：", heightCombo);

            var togglesStack = new StackPanel { Margin = new Thickness(0, 2, 0, 8) };
            var cbWeekend = new CheckBox { Content = "显示周六、周日列", IsChecked = tt.ShowWeekends, Margin = new Thickness(0, 2, 0, 4) };
            var cbTime = new CheckBox { Content = "卡片显示上课时间", IsChecked = tt.ShowTimeOnCard, Margin = new Thickness(0, 2, 0, 4) };
            var cbLoc = new CheckBox { Content = "卡片显示上课教室", IsChecked = tt.ShowLocationOnCard, Margin = new Thickness(0, 2, 0, 4) };
            var cbTeacher = new CheckBox { Content = "卡片显示任课教师", IsChecked = tt.ShowTeacherOnCard, Margin = new Thickness(0, 2, 0, 4) };
            togglesStack.Children.Add(cbWeekend);
            togglesStack.Children.Add(cbTime);
            togglesStack.Children.Add(cbLoc);
            togglesStack.Children.Add(cbTeacher);
            addRow("视图选项：", togglesStack);

            var timesBtn = GlassControlStyles.CreateGlassButton("🕒 编辑每节作息时间…",
                pal.SurfaceBg, pal.TextPrimary, pal.Border, 12, new Thickness(12, 4, 12, 4), 7);
            timesBtn.HorizontalAlignment = HorizontalAlignment.Left;
            timesBtn.Margin = new Thickness(0, 2, 0, 6);
            timesBtn.Click += delegate
            {
                int previewSec;
                if (int.TryParse((customSecBox.Text ?? "").Trim(), out previewSec) && previewSec >= 4 && previewSec <= 16)
                {
                    tt.SectionsPerDay = previewSec;
                }
                else
                {
                    var sIt = secCombo.SelectedItem as ComboBoxItem;
                    tt.SectionsPerDay = sIt != null && sIt.Tag is int ? (int)sIt.Tag : 0;
                }
                OpenSectionTimesEditor(dlg, tt, cfg, null);
            };
            addRow("作息表：", timesBtn);

            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            var cancelBtn = GlassControlStyles.CreateGlassButton("取消", pal.HoverBg, pal.TextPrimary, pal.Border, 12.5, new Thickness(14, 5, 14, 5), 7);
            cancelBtn.Margin = new Thickness(0, 0, 8, 0);
            cancelBtn.Click += delegate { dlg.DialogResult = false; dlg.Close(); };

            var saveBtn = GlassControlStyles.CreateGlassButton("保存课表设置", pal.AccentBg, pal.AccentFg, null, 12.5, new Thickness(16, 5, 16, 5), 7);
            saveBtn.FontWeight = FontWeights.SemiBold;
            saveBtn.Click += delegate
            {
                tt.SemesterName = (semBox.Text ?? "").Trim();
                int cwVal, twVal, customSecVal;
                if (int.TryParse((curWBox.Text ?? "").Trim(), out cwVal) && cwVal >= 1 && cwVal <= 30)
                {
                    tt.SetCurrentWeekForDate(cwVal, DateTime.Now);
                }
                if (int.TryParse((totalWBox.Text ?? "").Trim(), out twVal))
                {
                    tt.TotalWeeks = Math.Max(8, Math.Min(30, twVal));
                }
                if (int.TryParse((customSecBox.Text ?? "").Trim(), out customSecVal) && customSecVal >= 4 && customSecVal <= 16)
                {
                    tt.SectionsPerDay = customSecVal;
                }
                else if (string.IsNullOrWhiteSpace(customSecBox.Text))
                {
                    var sIt = secCombo.SelectedItem as ComboBoxItem;
                    tt.SectionsPerDay = sIt != null && sIt.Tag is int ? (int)sIt.Tag : 0;
                }
                var hIt = heightCombo.SelectedItem as ComboBoxItem;
                tt.RowMinHeight = hIt != null && hIt.Tag is int ? (int)hIt.Tag : 52;
                tt.ShowWeekends = cbWeekend.IsChecked == true;
                tt.ShowTimeOnCard = cbTime.IsChecked == true;
                tt.ShowLocationOnCard = cbLoc.IsChecked == true;
                tt.ShowTeacherOnCard = cbTeacher.IsChecked == true;

                dlg.DialogResult = true;
                dlg.Close();
            };
            footer.Children.Add(cancelBtn);
            footer.Children.Add(saveBtn);
            Grid.SetRow(footer, grid.RowDefinitions.Count - 1);
            Grid.SetColumnSpan(footer, 2);
            grid.Children.Add(footer);

            dlg.Content = grid;
            return dlg.ShowDialog() == true;
        }

        private void OnSaveClicked(object sender, RoutedEventArgs e)
        {
            _workingTimetable.SemesterName = (_semesterNameBox.Text ?? "").Trim();
            UpdateWeekStartFromBox();
            _workingTimetable.LastSyncIso = DateTime.Now.ToString("yyyy-MM-dd HH:mm");

            _controller.Config.MisUrl = (_misUrlBox.Text ?? "").Trim();
            _controller.Config.MisEnabled = true;
            _controller.SaveTimetable(_workingTimetable);
            DialogResult = true;
            Close();
        }

        public static void OpenSectionTimesEditor(Window owner, TimetableData tt, AppConfig cfg, Action onSaved)
        {
            if (tt == null) return;
            ThemePalette pal = ThemeService.GetPalette(cfg != null ? cfg.PanelTheme : "claude_warm", 0.98);
            UiScaleMetrics scale = ThemeService.GetScaleMetrics(cfg != null ? cfg.UiScale : "standard", cfg != null ? cfg.PanelFontSize : 13);

            var dlg = new Window
            {
                Title = "设置各节次作息时间",
                Width = 430,
                Height = 520,
                WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
                Owner = owner,
                ResizeMode = ResizeMode.NoResize,
                Background = pal.CardBg
            };
            GlassControlStyles.ApplyToResources(dlg.Resources, pal, scale);

            var main = new Grid { Margin = new Thickness(16) };
            main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            main.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            main.Children.Add(new TextBlock
            {
                Text = "左侧时间轴与课表卡片将按此处起止时间显示（格式 HH:mm）：",
                FontFamily = ThemeService.AppFontFamily,
                FontSize = scale.SmallFontSize + 0.5,
                Foreground = pal.TextSecondary,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10)
            });

            var scroller = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var rowsGrid = new Grid();
            rowsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(76) });
            rowsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            rowsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(24) });
            rowsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            int maxSec = Math.Max(4, tt.GetMaxSectionCount());
            var startBoxes = new TextBox[maxSec];
            var endBoxes = new TextBox[maxSec];
            for (int i = 0; i < maxSec; i++)
            {
                int sec = i + 1;
                rowsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var lbl = new TextBlock
                {
                    Text = string.Format("第 {0} 节", sec),
                    FontFamily = ThemeService.AppFontFamily,
                    FontSize = 12.5,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = pal.TextPrimary,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 4, 6, 4)
                };
                var sb = new TextBox
                {
                    Text = tt.GetSectionStartTime(sec),
                    Padding = new Thickness(8, 4, 8, 4),
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 3, 0, 3)
                };
                var dash = new TextBlock
                {
                    Text = "至",
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = pal.TextSecondary,
                    FontSize = 12
                };
                var eb = new TextBox
                {
                    Text = tt.GetSectionEndTime(sec),
                    Padding = new Thickness(8, 4, 8, 4),
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 3, 0, 3)
                };
                startBoxes[i] = sb;
                endBoxes[i] = eb;

                Grid.SetRow(lbl, i); Grid.SetColumn(lbl, 0); rowsGrid.Children.Add(lbl);
                Grid.SetRow(sb, i); Grid.SetColumn(sb, 1); rowsGrid.Children.Add(sb);
                Grid.SetRow(dash, i); Grid.SetColumn(dash, 2); rowsGrid.Children.Add(dash);
                Grid.SetRow(eb, i); Grid.SetColumn(eb, 3); rowsGrid.Children.Add(eb);
            }
            scroller.Content = rowsGrid;
            Grid.SetRow(scroller, 1);
            main.Children.Add(scroller);

            var footer = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
            var resetBtn = GlassControlStyles.CreateGlassButton("恢复默认作息",
                pal.HoverBg, pal.TextSecondary, pal.Border, 11.5, new Thickness(10, 5, 10, 5), 7);
            resetBtn.Click += delegate
            {
                for (int i = 0; i < maxSec; i++)
                {
                    startBoxes[i].Text = CourseItem.DefaultSectionStartTime(i + 1);
                    endBoxes[i].Text = CourseItem.DefaultSectionEndTime(i + 1);
                }
            };
            DockPanel.SetDock(resetBtn, Dock.Left);
            footer.Children.Add(resetBtn);

            var rightBtns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var cancelBtn = GlassControlStyles.CreateGlassButton("取消", pal.HoverBg, pal.TextPrimary, pal.Border, 12, new Thickness(14, 5, 14, 5), 7);
            cancelBtn.Margin = new Thickness(0, 0, 8, 0);
            cancelBtn.Click += delegate { dlg.DialogResult = false; dlg.Close(); };

            var okBtn = GlassControlStyles.CreateGlassButton("保存作息时间", pal.AccentBg, pal.AccentFg, null, 12, new Thickness(14, 5, 14, 5), 7);
            okBtn.FontWeight = FontWeights.SemiBold;
            okBtn.Click += delegate
            {
                for (int i = 0; i < maxSec; i++)
                {
                    tt.SetSectionTime(i + 1, startBoxes[i].Text, endBoxes[i].Text);
                }
                if (onSaved != null) onSaved();
                dlg.DialogResult = true;
                dlg.Close();
            };
            rightBtns.Children.Add(cancelBtn);
            rightBtns.Children.Add(okBtn);
            DockPanel.SetDock(rightBtns, Dock.Right);
            footer.Children.Add(rightBtns);

            Grid.SetRow(footer, 2);
            main.Children.Add(footer);
            dlg.Content = main;
            dlg.ShowDialog();
        }
    }
}
