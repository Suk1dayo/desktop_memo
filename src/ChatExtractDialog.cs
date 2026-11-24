using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace DesktopMemo
{
    public class ChatExtractDialog : Window
    {
        private readonly TrayAndAppController _controller;
        private readonly string _initialNoteId;

        private Border _rootCard;
        private TextBox _chatInputBox;
        private ComboBox _wxDaysCombo;
        private Border _imageStripContainer;
        private WrapPanel _imageStripPanel;
        private Button _extractBtn;
        private TextBlock _statusText;
        private StackPanel _previewListPanel;
        private ScrollViewer _previewScroller;
        private ComboBox _destCombo;
        private CheckBox _attachImagesBox;
        private Button _confirmBtn;

        private List<AiExtractedTask> _extractedTasks = new List<AiExtractedTask>();
        private readonly List<PreviewRowBindings> _rowBindings = new List<PreviewRowBindings>();
        private readonly List<PendingImageItem> _pendingImages = new List<PendingImageItem>();

        private class PendingImageItem
        {
            public byte[] Bytes;
            public string DataUrl;
            public string Label;
            public string OriginalPath;
            public BitmapImage Thumb;
        }

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

            Title = "AI 微信导出 / 识图 / 文本任务提取";
            Width = 730;
            Height = 710;
            MinWidth = 580;
            MinHeight = 520;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = true;
            AllowDrop = true;

            PreviewDragOver += OnPreviewDragOver;
            Drop += OnFilesDropped;
            PreviewKeyDown += OnDialogPreviewKeyDown;

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
                Text = "💬 AI 智能识别（聊天记录 / 导出文件 / 截图识图）",
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

            // Input area
            var inputStack = new StackPanel { Margin = new Thickness(18, 10, 18, 8) };
            inputStack.Children.Add(new TextBlock
            {
                Text = "支持粘贴聊天文本、直接 Ctrl+V 粘贴截图 / 拖入图片，或一键导入 .md / .jsonl / .txt 聊天导出文件与配图：",
                FontFamily = ThemeService.AppFontFamily,
                FontSize = scale.SmallFontSize + 0.5,
                Foreground = pal.TextSecondary,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 6)
            });

            // Quick import & vision toolbar
            var toolsWrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
            var addImgBtn = GlassControlStyles.CreateGlassButton("📷 添加图片",
                pal.SurfaceBg, pal.TextPrimary, pal.Border, scale.SmallFontSize + 0.5, new Thickness(9, 4, 9, 4), 7);
            addImgBtn.Margin = new Thickness(0, 0, 6, 4);
            addImgBtn.ToolTip = "选择截图或海报（也可直接 Ctrl+V 粘贴剪贴板截图，或拖拽图片到窗口）";
            addImgBtn.Click += OnPickImagesClicked;
            toolsWrap.Children.Add(addImgBtn);

            var importFileBtn = GlassControlStyles.CreateGlassButton("📂 导入导出文件",
                pal.SurfaceBg, pal.TextPrimary, pal.Border, scale.SmallFontSize + 0.5, new Thickness(9, 4, 9, 4), 7);
            importFileBtn.Margin = new Thickness(0, 0, 6, 4);
            importFileBtn.ToolTip = "选择聊天工具导出的 .md / .jsonl / .txt 文件";
            importFileBtn.Click += OnImportExportFileClicked;
            toolsWrap.Children.Add(importFileBtn);

            var importDirBtn = GlassControlStyles.CreateGlassButton("📁 导入导出目录",
                pal.SurfaceBg, pal.TextPrimary, pal.Border, scale.SmallFontSize + 0.5, new Thickness(9, 4, 9, 4), 7);
            importDirBtn.Margin = new Thickness(0, 0, 6, 4);
            importDirBtn.ToolTip = "选择一次批量导出所在的文件夹（自动汇总多群消息并匹配图片）";
            importDirBtn.Click += OnImportExportDirClicked;
            toolsWrap.Children.Add(importDirBtn);

            var loadLatestBtn = GlassControlStyles.CreateGlassButton("🔄 加载最新导出",
                pal.SurfaceBg, pal.TextPrimary, pal.Border, scale.SmallFontSize + 0.5, new Thickness(9, 4, 9, 4), 7);
            loadLatestBtn.Margin = new Thickness(0, 0, 6, 4);
            loadLatestBtn.ToolTip = "自动从设置的导出默认目录读取最新导出的聊天文件";
            loadLatestBtn.Click += OnLoadLatestExportClicked;
            toolsWrap.Children.Add(loadLatestBtn);

            _wxDaysCombo = new ComboBox { Width = 88, Margin = new Thickness(0, 0, 0, 4), ToolTip = "导入聊天记录的时间范围筛选" };
            _wxDaysCombo.Items.Add(new ComboBoxItem { Content = "最近3天", Tag = 3 });
            _wxDaysCombo.Items.Add(new ComboBoxItem { Content = "今天", Tag = 1 });
            _wxDaysCombo.Items.Add(new ComboBoxItem { Content = "最近7天", Tag = 7 });
            _wxDaysCombo.Items.Add(new ComboBoxItem { Content = "全部记录", Tag = 0 });
            _wxDaysCombo.SelectedIndex = 0;
            toolsWrap.Children.Add(_wxDaysCombo);

            inputStack.Children.Add(toolsWrap);

            _chatInputBox = new TextBox
            {
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                Height = 102,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(10, 8, 10, 8),
                FontSize = scale.FontSize
            };
            inputStack.Children.Add(_chatInputBox);

            // Pending images strip
            _imageStripContainer = new Border
            {
                Background = pal.SurfaceBg,
                BorderBrush = pal.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(8, 6, 8, 2),
                Margin = new Thickness(0, 6, 0, 0),
                Visibility = Visibility.Collapsed
            };
            _imageStripPanel = new WrapPanel();
            _imageStripContainer.Child = _imageStripPanel;
            inputStack.Children.Add(_imageStripContainer);

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
                Text = "在上方输入聊天文本、添加截图（支持 Ctrl+V 粘贴）或导入聊天导出文件，点击「✨ AI 解析提取任务」。",
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

            _destCombo = new ComboBox { Width = 165 };
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

            _attachImagesBox = new CheckBox
            {
                Content = "将识别图片保存为日程附件",
                IsChecked = true,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 0, 0),
                FontSize = scale.SmallFontSize + 0.5
            };
            leftFooter.Children.Add(_attachImagesBox);

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

        private int GetSelectedRecentDays()
        {
            var sel = _wxDaysCombo.SelectedItem as ComboBoxItem;
            if (sel != null && sel.Tag is int) return (int)sel.Tag;
            return 3;
        }

        private void OnDialogPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.V && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                try
                {
                    if (Clipboard.ContainsImage())
                    {
                        BitmapSource img = Clipboard.GetImage();
                        byte[] jpg = ImageAttachmentHelper.CompressBitmapSourceToJpeg(img, 1600, 85);
                        if (jpg != null)
                        {
                            AddPendingImageBytes(jpg, "剪贴板截图_" + (_pendingImages.Count + 1), "");
                            e.Handled = true;
                        }
                    }
                    else if (Clipboard.ContainsFileDropList())
                    {
                        StringCollection files = Clipboard.GetFileDropList();
                        bool handledAny = false;
                        for (int i = 0; i < files.Count; i++)
                        {
                            if (ImageAttachmentHelper.IsSupportedImageExtension(files[i]))
                            {
                                AddPendingImageFile(files[i]);
                                handledAny = true;
                            }
                        }
                        if (handledAny) e.Handled = true;
                    }
                }
                catch { }
            }
        }

        private void OnPreviewDragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
            }
        }

        private void OnFilesDropped(object sender, DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            string[] paths = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (paths == null || paths.Length == 0) return;

            for (int i = 0; i < paths.Length; i++)
            {
                string p = paths[i];
                if (Directory.Exists(p) || WeChatExportImportService.IsSupportedExportFile(p))
                {
                    ImportWeChatExportPath(p);
                }
                else if (ImageAttachmentHelper.IsSupportedImageExtension(p))
                {
                    AddPendingImageFile(p);
                }
            }
        }

        private void OnPickImagesClicked(object sender, RoutedEventArgs e)
        {
            var ofd = new Microsoft.Win32.OpenFileDialog
            {
                Title = "选择日程截图 / 活动海报 / 通知图片（最多 4 张）",
                Filter = "图片文件|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.webp",
                Multiselect = true
            };
            if (ofd.ShowDialog(this) == true)
            {
                for (int i = 0; i < ofd.FileNames.Length; i++)
                {
                    AddPendingImageFile(ofd.FileNames[i]);
                }
            }
        }

        private void OnImportExportFileClicked(object sender, RoutedEventArgs e)
        {
            var ofd = new Microsoft.Win32.OpenFileDialog
            {
                Title = "选择聊天导出文件",
                Filter = "聊天导出文件 (*.md;*.jsonl;*.txt)|*.md;*.jsonl;*.txt|所有文件|*.*"
            };
            string defRoot = WeChatExportImportService.ResolveDefaultExportSearchRoot(_controller.Config);
            if (!string.IsNullOrEmpty(defRoot) && Directory.Exists(defRoot))
            {
                ofd.InitialDirectory = defRoot;
            }
            if (ofd.ShowDialog(this) == true)
            {
                ImportWeChatExportPath(ofd.FileName);
            }
        }

        private void OnImportExportDirClicked(object sender, RoutedEventArgs e)
        {
            using (var fbd = new System.Windows.Forms.FolderBrowserDialog { Description = "选择聊天记录导出目录" })
            {
                string defRoot = WeChatExportImportService.ResolveDefaultExportSearchRoot(_controller.Config);
                if (!string.IsNullOrEmpty(defRoot) && Directory.Exists(defRoot))
                {
                    fbd.SelectedPath = defRoot;
                }
                if (fbd.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    ImportWeChatExportPath(fbd.SelectedPath);
                }
            }
        }

        private void OnLoadLatestExportClicked(object sender, RoutedEventArgs e)
        {
            string searchRoot = WeChatExportImportService.ResolveDefaultExportSearchRoot(_controller.Config);
            if (string.IsNullOrEmpty(searchRoot))
            {
                _statusText.Text = "⚠ 尚未设置默认导出目录，请先在「AI 与设置」中配置导出默认目录，或手动点击「导入导出文件/目录」。";
                _statusText.Foreground = ThemeService.BrushFromHex("#D97706");
                return;
            }

            string latestDir = WeChatExportImportService.FindLatestExportDirectory(searchRoot);
            if (string.IsNullOrEmpty(latestDir))
            {
                _statusText.Text = "⚠ 在导出目录下暂未找到 .md / .jsonl 文件：" + searchRoot;
                _statusText.Foreground = ThemeService.BrushFromHex("#D97706");
                return;
            }

            ImportWeChatExportPath(latestDir);
        }

        private void ImportWeChatExportPath(string path)
        {
            int days = GetSelectedRecentDays();
            WeChatImportResult res = WeChatExportImportService.ImportFromPath(path, days, 14000);
            if (string.IsNullOrWhiteSpace(res.FormattedText) && res.MatchedImagePaths.Count == 0)
            {
                _statusText.Text = "⚠ " + (string.IsNullOrEmpty(res.SourceSummary) ? "未导入到有效聊天消息（可尝试切换为「全部记录」）。" : res.SourceSummary);
                _statusText.Foreground = ThemeService.BrushFromHex("#D97706");
                return;
            }

            _chatInputBox.Text = res.FormattedText;
            if (res.MatchedImagePaths != null && res.MatchedImagePaths.Count > 0)
            {
                int addedCount = 0;
                for (int i = res.MatchedImagePaths.Count - 1; i >= 0 && _pendingImages.Count < 4; i--)
                {
                    if (AddPendingImageFile(res.MatchedImagePaths[i])) addedCount++;
                }
                if (addedCount > 0)
                {
                    _statusText.Text = "✓ " + res.SourceSummary + "（已载入最新 " + addedCount + " 张图片一起识图）";
                    _statusText.Foreground = ThemeService.BrushFromHex("#059669");
                    return;
                }
            }

            _statusText.Text = "✓ " + res.SourceSummary;
            _statusText.Foreground = ThemeService.BrushFromHex("#059669");
        }

        private bool AddPendingImageFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return false;
            if (_pendingImages.Count >= 4)
            {
                _statusText.Text = "⚠ 每次最多支持同时识别 4 张图片。";
                _statusText.Foreground = ThemeService.BrushFromHex("#D97706");
                return false;
            }

            for (int i = 0; i < _pendingImages.Count; i++)
            {
                if (string.Equals(_pendingImages[i].OriginalPath, filePath, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            byte[] compressed = ImageAttachmentHelper.CompressImageFileToJpeg(filePath, 1600, 85);
            if (compressed == null) return false;
            return AddPendingImageBytes(compressed, Path.GetFileName(filePath), filePath);
        }

        private bool AddPendingImageBytes(byte[] bytes, string label, string originalPath)
        {
            if (bytes == null || bytes.Length == 0) return false;
            if (_pendingImages.Count >= 4)
            {
                _statusText.Text = "⚠ 每次最多支持同时识别 4 张图片。";
                _statusText.Foreground = ThemeService.BrushFromHex("#D97706");
                return false;
            }

            string dataUrl = ImageAttachmentHelper.ToDataUrl(bytes);
            BitmapImage thumb = ImageAttachmentHelper.LoadThumbnailFromBytes(bytes, 160);
            _pendingImages.Add(new PendingImageItem
            {
                Bytes = bytes,
                DataUrl = dataUrl,
                Label = label,
                OriginalPath = originalPath ?? "",
                Thumb = thumb
            });
            RenderImageStrip();
            return true;
        }

        private void RenderImageStrip()
        {
            _imageStripPanel.Children.Clear();
            if (_pendingImages.Count == 0)
            {
                _imageStripContainer.Visibility = Visibility.Collapsed;
                return;
            }

            ThemePalette pal = ThemeService.GetPalette(_controller.Config.PanelTheme, 0.97);
            _imageStripContainer.Visibility = Visibility.Visible;

            for (int i = 0; i < _pendingImages.Count; i++)
            {
                int index = i;
                PendingImageItem item = _pendingImages[i];
                var card = new Border
                {
                    Width = 84,
                    Height = 64,
                    Margin = new Thickness(0, 0, 8, 4),
                    CornerRadius = new CornerRadius(7),
                    BorderBrush = pal.Border,
                    BorderThickness = new Thickness(1),
                    Background = pal.CardBg,
                    ClipToBounds = true,
                    ToolTip = item.Label + "（点击预览）"
                };

                var g = new Grid();
                var imgCtrl = new Image
                {
                    Source = item.Thumb,
                    Stretch = Stretch.UniformToFill,
                    Cursor = Cursors.Hand
                };
                imgCtrl.MouseLeftButtonUp += delegate { PreviewPendingImage(index); };
                g.Children.Add(imgCtrl);

                var delBtn = GlassControlStyles.CreateGlassButton("✕",
                    ThemeService.BrushFromHexAlpha("#111827", 190), Brushes.White, null, 10, new Thickness(4, 0, 4, 0), 5);
                delBtn.HorizontalAlignment = HorizontalAlignment.Right;
                delBtn.VerticalAlignment = VerticalAlignment.Top;
                delBtn.Margin = new Thickness(2);
                delBtn.Click += delegate
                {
                    _pendingImages.RemoveAt(index);
                    RenderImageStrip();
                };
                g.Children.Add(delBtn);

                card.Child = g;
                _imageStripPanel.Children.Add(card);
            }
        }

        private void PreviewPendingImage(int index)
        {
            var tempFiles = new List<string>();
            var paths = new List<string>();
            try
            {
                for (int i = 0; i < _pendingImages.Count; i++)
                {
                    if (!string.IsNullOrEmpty(_pendingImages[i].OriginalPath) && File.Exists(_pendingImages[i].OriginalPath))
                    {
                        paths.Add(_pendingImages[i].OriginalPath);
                    }
                    else
                    {
                        string tmp = Path.Combine(Path.GetTempPath(), "dm_clip_" + Guid.NewGuid().ToString("N").Substring(0, 8) + ImageAttachmentHelper.DetectFileExtension(_pendingImages[i].Bytes));
                        File.WriteAllBytes(tmp, _pendingImages[i].Bytes);
                        tempFiles.Add(tmp);
                        paths.Add(tmp);
                    }
                }
                ImageAttachmentHelper.ShowImagePreviewDialog(paths, index, this, _controller.Config.PanelTheme);
            }
            finally
            {
                for (int i = 0; i < tempFiles.Count; i++)
                {
                    try { File.Delete(tempFiles[i]); } catch { }
                }
            }
        }

        private async void OnExtractClicked(object sender, RoutedEventArgs e)
        {
            string rawText = (_chatInputBox.Text ?? "").Trim();
            if (string.IsNullOrEmpty(rawText) && _pendingImages.Count == 0)
            {
                _statusText.Text = "请先粘贴聊天内容，或添加 / Ctrl+V 粘贴需要识图的图片。";
                _statusText.Foreground = ThemeService.BrushFromHex("#DC2626");
                return;
            }

            _extractBtn.IsEnabled = false;
            _extractBtn.Content = "⏳ 正在解析任务...";
            _statusText.Text = string.Format("正在调用 {0} {1}...",
                _controller.Config.AiModel,
                _pendingImages.Count > 0 ? ("进行图文识图提取 (" + _pendingImages.Count + "张图)") : "提取 DDL 与四象限");
            _statusText.Foreground = ThemeService.BrushFromHex("#6D28D9");

            var imageUrls = new List<string>();
            for (int i = 0; i < _pendingImages.Count; i++)
            {
                if (!string.IsNullOrEmpty(_pendingImages[i].DataUrl))
                {
                    imageUrls.Add(_pendingImages[i].DataUrl);
                }
            }

            try
            {
                AiChatExtractOutcome outcome = await AiSortService.ExtractTasksFromMultimodalAsync(
                    _controller.Config, rawText, imageUrls, DateTime.Now);
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

        private bool HasSimilarExistingTask(string candidateTitle)
        {
            string norm = NormalizeTitleKey(candidateTitle);
            if (norm.Length < 3) return false;
            List<MemoTask> tasks = _controller.Data.Tasks;
            for (int i = 0; i < tasks.Count; i++)
            {
                if (tasks[i].Completed) continue;
                string existing = NormalizeTitleKey(tasks[i].Title);
                if (existing.Length < 3) continue;
                if (existing == norm || existing.IndexOf(norm, StringComparison.Ordinal) >= 0 || norm.IndexOf(existing, StringComparison.Ordinal) >= 0)
                {
                    return true;
                }
            }
            return false;
        }

        private static string NormalizeTitleKey(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            var sb = new System.Text.StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char ch = s[i];
                if (!char.IsWhiteSpace(ch) && !char.IsPunctuation(ch))
                {
                    sb.Append(char.ToLowerInvariant(ch));
                }
            }
            return sb.ToString();
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

                if (HasSimilarExistingTask(item.Title))
                {
                    metaRow.Children.Add(new Border
                    {
                        Background = ThemeService.BrushFromHex("#FEF3C7"),
                        CornerRadius = new CornerRadius(5),
                        Padding = new Thickness(6, 2, 6, 2),
                        Margin = new Thickness(0, 0, 6, 2),
                        VerticalAlignment = VerticalAlignment.Center,
                        Child = new TextBlock
                        {
                            Text = "⚠ 已有相似待办",
                            FontFamily = ThemeService.AppFontFamily,
                            FontSize = scale.SmallFontSize,
                            FontWeight = FontWeights.SemiBold,
                            Foreground = ThemeService.BrushFromHex("#B45309")
                        }
                    });
                }

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

            var savedAttachments = new List<string>();
            if (_attachImagesBox.IsChecked == true && _pendingImages.Count > 0)
            {
                for (int i = 0; i < _pendingImages.Count; i++)
                {
                    string ext = ImageAttachmentHelper.DetectFileExtension(_pendingImages[i].Bytes);
                    string saved = _controller.Storage.SaveAttachmentBytes(_pendingImages[i].Bytes, ext);
                    if (!string.IsNullOrEmpty(saved)) savedAttachments.Add(saved);
                }
            }

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
                    NoteId = noteId,
                    AttachmentFiles = new List<string>(savedAttachments)
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
