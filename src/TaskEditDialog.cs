using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace DesktopMemo
{
    public class TaskEditDialog : Window
    {
        private readonly MemoTask _task;
        private readonly bool _isNew;
        private readonly List<StickyNote> _notes;
        private readonly ThemePalette _pal;
        private readonly UiScaleMetrics _scale;
        private readonly StorageService _storage;

        private Border _outer;
        private TextBox _titleBox;
        private TextBox _notesBox;
        private readonly RadioButton[] _quadButtons = new RadioButton[5]; // 0=Unclassified, 1..4=Q1..Q4

        private CheckBox _ddlEnabledBox;
        private DatePicker _ddlDatePicker;
        private ComboBox _ddlHourBox;
        private ComboBox _ddlMinBox;

        private CheckBox _remEnabledBox;
        private DatePicker _remDatePicker;
        private ComboBox _remHourBox;
        private ComboBox _remMinBox;

        private ComboBox _noteLinkBox;
        private WrapPanel _attWrapPanel;
        private TextBlock _attEmptyHint;
        private TextBlock _errorText;

        private readonly List<AttachmentDraft> _attachmentDrafts = new List<AttachmentDraft>();

        private class AttachmentDraft
        {
            public string SavedFileName;
            public byte[] NewBytes;
            public string Label;
            public BitmapImage Thumb;
        }

        public bool DeleteRequested { get; private set; }

        public TaskEditDialog(MemoTask task, bool isNew, List<StickyNote> stickyNotes)
            : this(task, isNew, stickyNotes, "claude_warm", "standard", 13, null)
        {
        }

        public TaskEditDialog(MemoTask task, bool isNew, List<StickyNote> stickyNotes, string themeKey, string uiScale, double fontSize)
            : this(task, isNew, stickyNotes, themeKey, uiScale, fontSize, null)
        {
        }

        public TaskEditDialog(MemoTask task, bool isNew, List<StickyNote> stickyNotes, string themeKey, string uiScale, double fontSize, StorageService storage)
        {
            _task = task ?? new MemoTask();
            _isNew = isNew;
            _notes = stickyNotes ?? new List<StickyNote>();
            _storage = storage;
            _pal = ThemeService.GetPalette(themeKey, 0.97);
            _scale = ThemeService.GetScaleMetrics(uiScale, fontSize);

            Title = isNew ? "新建日程 / 待办" : "编辑日程 / 待办";
            Width = 560;
            SizeToContent = SizeToContent.Height;
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

            GlassControlStyles.ApplyToResources(Resources, _pal, _scale);
            BuildUi();
            LoadTaskValues();
        }

        private void BuildUi()
        {
            _outer = new Border();
            _outer.Margin = new Thickness(12);
            _outer.CornerRadius = new CornerRadius(14);
            _outer.Background = _pal.PopupBg;
            _outer.BorderBrush = _pal.Border;
            _outer.BorderThickness = new Thickness(1);
            _outer.Effect = new DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 22,
                ShadowDepth = 4,
                Opacity = 0.24
            };

            var rootStack = new StackPanel();

            var header = new Border();
            header.Background = _pal.HeaderBg;
            header.BorderBrush = _pal.Border;
            header.BorderThickness = new Thickness(0, 0, 0, 1);
            header.CornerRadius = new CornerRadius(14, 14, 0, 0);
            header.Padding = new Thickness(18, 12, 14, 12);
            header.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                if (e.ButtonState == MouseButtonState.Pressed) { try { DragMove(); } catch { } }
            };

            var headerDock = new DockPanel();
            var hTitle = new TextBlock
            {
                Text = _isNew ? "📌 新建日程 / 待办" : "✏️ 编辑日程 / 待办",
                FontFamily = ThemeService.AppFontFamily,
                FontSize = _scale.HeaderFontSize,
                FontWeight = FontWeights.Bold,
                Foreground = _pal.TextPrimary,
                VerticalAlignment = VerticalAlignment.Center
            };
            DockPanel.SetDock(hTitle, Dock.Left);
            headerDock.Children.Add(hTitle);

            var closeBtn = GlassControlStyles.CreateGlassButton("✕", Brushes.Transparent,
                _pal.TextSecondary, null, 13, new Thickness(8, 2, 8, 2), 7);
            closeBtn.HorizontalAlignment = HorizontalAlignment.Right;
            closeBtn.Click += delegate { DialogResult = false; Close(); };
            DockPanel.SetDock(closeBtn, Dock.Right);
            headerDock.Children.Add(closeBtn);

            header.Child = headerDock;
            rootStack.Children.Add(header);

            var body = new StackPanel { Margin = new Thickness(18, 14, 18, 16) };

            body.Children.Add(MakeSectionLabel("待办标题 *"));
            _titleBox = new TextBox
            {
                FontSize = _scale.FontSize + 1,
                Padding = new Thickness(10, 7, 10, 7),
                Margin = new Thickness(0, 4, 0, 10)
            };
            _titleBox.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
                {
                    TrySaveAndClose();
                }
            };
            body.Children.Add(_titleBox);

            body.Children.Add(MakeSectionLabel("详细备注（可选）"));
            _notesBox = new TextBox
            {
                FontSize = _scale.FontSize,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                Height = 62,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(0, 4, 0, 10)
            };
            body.Children.Add(_notesBox);

            body.Children.Add(MakeSectionLabel("重要紧急四象限（可选「未分类」）"));
            var quadGrid = new Grid { Margin = new Thickness(0, 5, 0, 12) };
            quadGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            quadGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            quadGrid.RowDefinitions.Add(new RowDefinition());
            quadGrid.RowDefinitions.Add(new RowDefinition());
            quadGrid.RowDefinitions.Add(new RowDefinition());

            for (int q = 1; q <= 4; q++)
            {
                QuadrantVisual qv = ThemeService.GetQuadrantVisual(q, _pal.IsDark);
                var rb = new RadioButton
                {
                    GroupName = "QuadrantGroup",
                    Content = qv.Title,
                    FontFamily = ThemeService.AppFontFamily,
                    FontSize = _scale.SmallFontSize + 1,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = ThemeService.BrushFromHex(qv.BadgeFgHex),
                    Background = ThemeService.BrushFromHex(qv.SoftBgHex),
                    Padding = new Thickness(8, 6, 8, 6),
                    Margin = new Thickness(q % 2 == 1 ? 0 : 4, q <= 2 ? 0 : 4, q % 2 == 1 ? 4 : 0, 0),
                    Tag = q
                };
                _quadButtons[q] = rb;
                Grid.SetRow(rb, (q - 1) / 2);
                Grid.SetColumn(rb, (q - 1) % 2);
                quadGrid.Children.Add(rb);
            }

            QuadrantVisual qv0 = ThemeService.GetQuadrantVisual(0, _pal.IsDark);
            var rb0 = new RadioButton
            {
                GroupName = "QuadrantGroup",
                Content = "未分类（仅保留在便签或等待 AI 归类）",
                FontFamily = ThemeService.AppFontFamily,
                FontSize = _scale.SmallFontSize + 0.5,
                Foreground = _pal.TextSecondary,
                Background = _pal.SurfaceBg,
                Padding = new Thickness(8, 5, 8, 5),
                Margin = new Thickness(0, 4, 0, 0),
                Tag = 0
            };
            _quadButtons[0] = rb0;
            Grid.SetRow(rb0, 2);
            Grid.SetColumn(rb0, 0);
            Grid.SetColumnSpan(rb0, 2);
            quadGrid.Children.Add(rb0);
            body.Children.Add(quadGrid);

            var ddlCard = new Border
            {
                Background = _pal.SurfaceBg,
                BorderBrush = _pal.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 0, 0, 10)
            };
            var ddlStack = new StackPanel();
            _ddlEnabledBox = new CheckBox
            {
                Content = "设置 DDL 截止时间",
                FontWeight = FontWeights.SemiBold
            };
            _ddlEnabledBox.Checked += delegate { ToggleDdlInputs(true); };
            _ddlEnabledBox.Unchecked += delegate { ToggleDdlInputs(false); };
            ddlStack.Children.Add(_ddlEnabledBox);

            var ddlPickerRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 7, 0, 6) };
            _ddlDatePicker = new DatePicker { Width = 148, SelectedDate = DateTime.Today };
            _ddlHourBox = CreateNumberCombo(0, 23, 1);
            _ddlMinBox = CreateNumberCombo(0, 55, 5);
            ddlPickerRow.Children.Add(_ddlDatePicker);
            ddlPickerRow.Children.Add(new TextBlock { Text = "  时间 ", VerticalAlignment = VerticalAlignment.Center, FontFamily = ThemeService.AppFontFamily, FontSize = _scale.SmallFontSize + 0.5, Foreground = _pal.TextSecondary });
            ddlPickerRow.Children.Add(_ddlHourBox);
            ddlPickerRow.Children.Add(new TextBlock { Text = " : ", VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.Bold, Foreground = _pal.TextSecondary });
            ddlPickerRow.Children.Add(_ddlMinBox);
            ddlStack.Children.Add(ddlPickerRow);

            var ddlPresets = new WrapPanel();
            AddQuickPresetButton(ddlPresets, "1小时后", delegate { ApplyDdlDateTime(DateTime.Now.AddHours(1)); });
            AddQuickPresetButton(ddlPresets, "今晚18:00", delegate { ApplyDdlDateTime(DateTime.Today.AddHours(18)); });
            AddQuickPresetButton(ddlPresets, "今晚23:59", delegate { ApplyDdlDateTime(DateTime.Today.AddHours(23).AddMinutes(59)); });
            AddQuickPresetButton(ddlPresets, "明天12:00", delegate { ApplyDdlDateTime(DateTime.Today.AddDays(1).AddHours(12)); });
            AddQuickPresetButton(ddlPresets, "明天23:59", delegate { ApplyDdlDateTime(DateTime.Today.AddDays(1).AddHours(23).AddMinutes(59)); });
            AddQuickPresetButton(ddlPresets, "3天后", delegate { ApplyDdlDateTime(DateTime.Today.AddDays(3).AddHours(20)); });
            ddlStack.Children.Add(ddlPresets);
            ddlCard.Child = ddlStack;
            body.Children.Add(ddlCard);

            var remCard = new Border
            {
                Background = _pal.SurfaceBg,
                BorderBrush = _pal.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 0, 0, 10)
            };
            var remStack = new StackPanel();
            _remEnabledBox = new CheckBox
            {
                Content = "开启到点桌面弹窗提醒",
                FontWeight = FontWeights.SemiBold
            };
            _remEnabledBox.Checked += delegate { ToggleRemInputs(true); };
            _remEnabledBox.Unchecked += delegate { ToggleRemInputs(false); };
            remStack.Children.Add(_remEnabledBox);

            var remPickerRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 7, 0, 6) };
            _remDatePicker = new DatePicker { Width = 148, SelectedDate = DateTime.Today };
            _remHourBox = CreateNumberCombo(0, 23, 1);
            _remMinBox = CreateNumberCombo(0, 55, 5);
            remPickerRow.Children.Add(_remDatePicker);
            remPickerRow.Children.Add(new TextBlock { Text = "  时间 ", VerticalAlignment = VerticalAlignment.Center, FontFamily = ThemeService.AppFontFamily, FontSize = _scale.SmallFontSize + 0.5, Foreground = _pal.TextSecondary });
            remPickerRow.Children.Add(_remHourBox);
            remPickerRow.Children.Add(new TextBlock { Text = " : ", VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.Bold, Foreground = _pal.TextSecondary });
            remPickerRow.Children.Add(_remMinBox);
            remStack.Children.Add(remPickerRow);

            var remPresets = new WrapPanel();
            AddQuickPresetButton(remPresets, "准点提醒(同DDL)", delegate { ApplyRemOffsetFromDdl(TimeSpan.Zero); });
            AddQuickPresetButton(remPresets, "DDL前15分", delegate { ApplyRemOffsetFromDdl(TimeSpan.FromMinutes(-15)); });
            AddQuickPresetButton(remPresets, "DDL前1小时", delegate { ApplyRemOffsetFromDdl(TimeSpan.FromHours(-1)); });
            AddQuickPresetButton(remPresets, "DDL前1天", delegate { ApplyRemOffsetFromDdl(TimeSpan.FromDays(-1)); });
            AddQuickPresetButton(remPresets, "10分钟后", delegate { ApplyRemDateTime(DateTime.Now.AddMinutes(10)); });
            remStack.Children.Add(remPresets);
            remCard.Child = remStack;
            body.Children.Add(remCard);

            var linkRow = new DockPanel { Margin = new Thickness(0, 2, 0, 8) };
            var linkLbl = MakeSectionLabel("所属列表：");
            linkLbl.VerticalAlignment = VerticalAlignment.Center;
            linkLbl.Margin = new Thickness(0, 0, 8, 0);
            DockPanel.SetDock(linkLbl, Dock.Left);
            linkRow.Children.Add(linkLbl);

            _noteLinkBox = new ComboBox();
            _noteLinkBox.Items.Add(new ComboBoxItem { Content = "全局日程（主面板与同步便签可见）", Tag = "" });
            for (int i = 0; i < _notes.Count; i++)
            {
                if (_notes[i].FilterMode == "custom")
                {
                    _noteLinkBox.Items.Add(new ComboBoxItem { Content = "独立便签：" + _notes[i].Title, Tag = _notes[i].Id });
                }
            }
            linkRow.Children.Add(_noteLinkBox);
            body.Children.Add(linkRow);

            var attCard = new Border
            {
                Background = _pal.SurfaceBg,
                BorderBrush = _pal.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 0, 0, 8)
            };
            var attStack = new StackPanel();
            var attHead = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            var attTitle = MakeSectionLabel("🖼 图片附件（支持拖拽图片或 Ctrl+V 粘贴截图）");
            attTitle.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(attTitle, Dock.Left);
            attHead.Children.Add(attTitle);

            var attBtns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var addAttBtn = GlassControlStyles.CreateGlassButton("+ 添加图片",
                _pal.HoverBg, _pal.TextPrimary, _pal.Border, _scale.SmallFontSize, _scale.SmallButtonPadding, 7);
            addAttBtn.Margin = new Thickness(0, 0, 6, 0);
            addAttBtn.Click += OnAddAttachmentClicked;
            var pasteAttBtn = GlassControlStyles.CreateGlassButton("📋 粘贴截图",
                _pal.HoverBg, _pal.TextPrimary, _pal.Border, _scale.SmallFontSize, _scale.SmallButtonPadding, 7);
            pasteAttBtn.Click += delegate { TryAddClipboardImage(); };
            attBtns.Children.Add(addAttBtn);
            attBtns.Children.Add(pasteAttBtn);
            attHead.Children.Add(attBtns);
            attStack.Children.Add(attHead);

            _attEmptyHint = new TextBlock
            {
                Text = "暂无图片附件（点击右上按钮添加，或直接截图后按 Ctrl+V 粘贴）",
                FontFamily = ThemeService.AppFontFamily,
                FontSize = _scale.SmallFontSize,
                Foreground = _pal.TextSecondary
            };
            attStack.Children.Add(_attEmptyHint);

            _attWrapPanel = new WrapPanel { Margin = new Thickness(0, 2, 0, 0) };
            attStack.Children.Add(_attWrapPanel);
            attCard.Child = attStack;
            body.Children.Add(attCard);

            _errorText = new TextBlock
            {
                FontFamily = ThemeService.AppFontFamily,
                FontSize = _scale.SmallFontSize + 0.5,
                Foreground = ThemeService.BrushFromHex("#DC2626"),
                Visibility = Visibility.Collapsed,
                Margin = new Thickness(0, 2, 0, 6)
            };
            body.Children.Add(_errorText);

            var footer = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
            if (!_isNew)
            {
                var delBtn = GlassControlStyles.CreateGlassButton("删除此日程",
                    ThemeService.BrushFromHex("#FEE2E2"), ThemeService.BrushFromHex("#DC2626"),
                    ThemeService.BrushFromHex("#FCA5A5"), _scale.FontSize, _scale.ButtonPadding, _scale.CornerRadius);
                delBtn.Click += delegate
                {
                    if (MessageBox.Show("确定要删除这条日程吗？", "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                    {
                        DeleteRequested = true;
                        DialogResult = true;
                        Close();
                    }
                };
                DockPanel.SetDock(delBtn, Dock.Left);
                footer.Children.Add(delBtn);
            }

            var rightBtns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var cancelBtn = GlassControlStyles.CreateGlassButton("取消",
                _pal.SurfaceBg, _pal.TextPrimary, _pal.Border, _scale.FontSize, _scale.ButtonPadding, _scale.CornerRadius);
            cancelBtn.Margin = new Thickness(0, 0, 8, 0);
            cancelBtn.Click += delegate { DialogResult = false; Close(); };

            var saveBtn = GlassControlStyles.CreateGlassButton("保存",
                _pal.AccentBg, _pal.AccentFg, null, _scale.FontSize, _scale.ButtonPadding, _scale.CornerRadius);
            saveBtn.Click += delegate { TrySaveAndClose(); };

            rightBtns.Children.Add(cancelBtn);
            rightBtns.Children.Add(saveBtn);
            footer.Children.Add(rightBtns);
            body.Children.Add(footer);

            rootStack.Children.Add(new ScrollViewer
            {
                MaxHeight = 610,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = body
            });
            _outer.Child = rootStack;
            Content = _outer;

            Loaded += delegate
            {
                SpringAnimationHelper.AnimatePopIn(_outer, 280);
                _titleBox.Focus();
                _titleBox.SelectAll();
            };
        }

        private void LoadTaskValues()
        {
            _titleBox.Text = _task.Title ?? "";
            _notesBox.Text = _task.Notes ?? "";
            int q = MemoTask.NormalizeQuadrant(_task.Quadrant);
            _quadButtons[q].IsChecked = true;

            DateTime? ddl = _task.GetDeadline();
            if (ddl.HasValue)
            {
                _ddlEnabledBox.IsChecked = true;
                SetPickerValue(_ddlDatePicker, _ddlHourBox, _ddlMinBox, ddl.Value);
                ToggleDdlInputs(true);
            }
            else
            {
                _ddlEnabledBox.IsChecked = false;
                SetPickerValue(_ddlDatePicker, _ddlHourBox, _ddlMinBox, DateTime.Today.AddHours(23).AddMinutes(59));
                ToggleDdlInputs(false);
            }

            DateTime? rem = _task.GetRemindAt();
            if (rem.HasValue)
            {
                _remEnabledBox.IsChecked = true;
                SetPickerValue(_remDatePicker, _remHourBox, _remMinBox, rem.Value);
                ToggleRemInputs(true);
            }
            else
            {
                _remEnabledBox.IsChecked = false;
                SetPickerValue(_remDatePicker, _remHourBox, _remMinBox, DateTime.Now.AddHours(1));
                ToggleRemInputs(false);
            }

            _noteLinkBox.SelectedIndex = 0;
            if (!string.IsNullOrEmpty(_task.NoteId))
            {
                for (int i = 0; i < _noteLinkBox.Items.Count; i++)
                {
                    var item = _noteLinkBox.Items[i] as ComboBoxItem;
                    if (item != null && string.Equals(item.Tag as string, _task.NoteId, StringComparison.Ordinal))
                    {
                        _noteLinkBox.SelectedIndex = i;
                        break;
                    }
                }
            }

            _attachmentDrafts.Clear();
            if (_task.AttachmentFiles != null)
            {
                for (int i = 0; i < _task.AttachmentFiles.Count; i++)
                {
                    string fname = _task.AttachmentFiles[i];
                    if (string.IsNullOrWhiteSpace(fname)) continue;
                    BitmapImage thumb = null;
                    if (_storage != null)
                    {
                        string p = _storage.GetAttachmentPath(fname);
                        if (!string.IsNullOrEmpty(p) && File.Exists(p))
                        {
                            thumb = ImageAttachmentHelper.LoadThumbnail(p, 150);
                        }
                    }
                    _attachmentDrafts.Add(new AttachmentDraft
                    {
                        SavedFileName = fname,
                        Label = fname,
                        Thumb = thumb
                    });
                }
            }
            RenderAttachmentDrafts();
        }

        private void TrySaveAndClose()
        {
            string title = (_titleBox.Text ?? "").Trim();
            if (string.IsNullOrEmpty(title))
            {
                _errorText.Text = "请输入待办标题。";
                _errorText.Visibility = Visibility.Visible;
                _titleBox.Focus();
                return;
            }

            _task.Title = title;
            _task.Notes = (_notesBox.Text ?? "").Trim();

            for (int i = 0; i < 5; i++)
            {
                if (_quadButtons[i].IsChecked == true)
                {
                    _task.Quadrant = i;
                    break;
                }
            }

            if (_ddlEnabledBox.IsChecked == true)
            {
                DateTime ddl = ReadPickerValue(_ddlDatePicker, _ddlHourBox, _ddlMinBox);
                _task.DeadlineIso = MemoTask.FormatIso(ddl);
            }
            else
            {
                _task.DeadlineIso = "";
            }

            if (_remEnabledBox.IsChecked == true)
            {
                DateTime rem = ReadPickerValue(_remDatePicker, _remHourBox, _remMinBox);
                string newRemIso = MemoTask.FormatIso(rem);
                if (newRemIso != _task.RemindAtIso && rem > DateTime.Now)
                {
                    _task.Reminded = false;
                }
                _task.RemindAtIso = newRemIso;
            }
            else
            {
                _task.RemindAtIso = "";
                _task.Reminded = false;
            }

            var selectedNote = _noteLinkBox.SelectedItem as ComboBoxItem;
            _task.NoteId = selectedNote != null && selectedNote.Tag != null ? selectedNote.Tag.ToString() : "";

            var finalFiles = new List<string>();
            for (int i = 0; i < _attachmentDrafts.Count; i++)
            {
                AttachmentDraft draft = _attachmentDrafts[i];
                if (!string.IsNullOrEmpty(draft.SavedFileName))
                {
                    if (!finalFiles.Contains(draft.SavedFileName)) finalFiles.Add(draft.SavedFileName);
                }
                else if (draft.NewBytes != null && _storage != null)
                {
                    string ext = ImageAttachmentHelper.DetectFileExtension(draft.NewBytes);
                    string saved = _storage.SaveAttachmentBytes(draft.NewBytes, ext);
                    if (!string.IsNullOrEmpty(saved) && !finalFiles.Contains(saved)) finalFiles.Add(saved);
                }
            }
            _task.AttachmentFiles = finalFiles;

            DialogResult = true;
            Close();
        }

        private void OnDialogPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.V && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                if (TryAddClipboardImage())
                {
                    e.Handled = true;
                }
            }
        }

        private bool TryAddClipboardImage()
        {
            try
            {
                if (Clipboard.ContainsImage())
                {
                    BitmapSource src = Clipboard.GetImage();
                    byte[] jpg = ImageAttachmentHelper.CompressBitmapSourceToJpeg(src, 1600, 85);
                    if (jpg != null)
                    {
                        AddNewAttachmentBytes(jpg, "剪贴板截图");
                        return true;
                    }
                }
                else if (Clipboard.ContainsFileDropList())
                {
                    StringCollection files = Clipboard.GetFileDropList();
                    bool any = false;
                    for (int i = 0; i < files.Count; i++)
                    {
                        if (ImageAttachmentHelper.IsSupportedImageExtension(files[i]))
                        {
                            AddNewAttachmentFile(files[i]);
                            any = true;
                        }
                    }
                    return any;
                }
            }
            catch { }
            return false;
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
            if (paths == null) return;
            for (int i = 0; i < paths.Length; i++)
            {
                if (ImageAttachmentHelper.IsSupportedImageExtension(paths[i]))
                {
                    AddNewAttachmentFile(paths[i]);
                }
            }
        }

        private void OnAddAttachmentClicked(object sender, RoutedEventArgs e)
        {
            var ofd = new Microsoft.Win32.OpenFileDialog
            {
                Title = "选择日程图片附件",
                Filter = "图片文件|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.webp",
                Multiselect = true
            };
            if (ofd.ShowDialog(this) == true)
            {
                for (int i = 0; i < ofd.FileNames.Length; i++)
                {
                    AddNewAttachmentFile(ofd.FileNames[i]);
                }
            }
        }

        private void AddNewAttachmentFile(string path)
        {
            byte[] compressed = ImageAttachmentHelper.CompressImageFileToJpeg(path, 1600, 85);
            if (compressed == null) return;
            AddNewAttachmentBytes(compressed, Path.GetFileName(path));
        }

        private void AddNewAttachmentBytes(byte[] bytes, string label)
        {
            if (bytes == null || bytes.Length == 0) return;
            _attachmentDrafts.Add(new AttachmentDraft
            {
                NewBytes = bytes,
                Label = label,
                Thumb = ImageAttachmentHelper.LoadThumbnailFromBytes(bytes, 150)
            });
            RenderAttachmentDrafts();
        }

        private void RenderAttachmentDrafts()
        {
            _attWrapPanel.Children.Clear();
            _attEmptyHint.Visibility = _attachmentDrafts.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            for (int i = 0; i < _attachmentDrafts.Count; i++)
            {
                int index = i;
                AttachmentDraft draft = _attachmentDrafts[i];
                var card = new Border
                {
                    Width = 78,
                    Height = 58,
                    Margin = new Thickness(0, 2, 8, 4),
                    CornerRadius = new CornerRadius(7),
                    BorderBrush = _pal.Border,
                    BorderThickness = new Thickness(1),
                    Background = _pal.CardBg,
                    ClipToBounds = true,
                    ToolTip = draft.Label + "（点击预览）"
                };

                var g = new Grid();
                if (draft.Thumb != null)
                {
                    var imgCtrl = new Image
                    {
                        Source = draft.Thumb,
                        Stretch = Stretch.UniformToFill,
                        Cursor = Cursors.Hand
                    };
                    imgCtrl.MouseLeftButtonUp += delegate { PreviewDraftAttachment(index); };
                    g.Children.Add(imgCtrl);
                }
                else
                {
                    var ph = new TextBlock
                    {
                        Text = "🖼\n待同步",
                        FontFamily = ThemeService.AppFontFamily,
                        FontSize = 10,
                        Foreground = _pal.TextSecondary,
                        TextAlignment = TextAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    g.Children.Add(ph);
                }

                var delBtn = GlassControlStyles.CreateGlassButton("✕",
                    ThemeService.BrushFromHexAlpha("#111827", 190), Brushes.White, null, 10, new Thickness(4, 0, 4, 0), 5);
                delBtn.HorizontalAlignment = HorizontalAlignment.Right;
                delBtn.VerticalAlignment = VerticalAlignment.Top;
                delBtn.Margin = new Thickness(2);
                delBtn.Click += delegate
                {
                    _attachmentDrafts.RemoveAt(index);
                    RenderAttachmentDrafts();
                };
                g.Children.Add(delBtn);

                card.Child = g;
                _attWrapPanel.Children.Add(card);
            }
        }

        private void PreviewDraftAttachment(int index)
        {
            var tempFiles = new List<string>();
            var paths = new List<string>();
            try
            {
                for (int i = 0; i < _attachmentDrafts.Count; i++)
                {
                    AttachmentDraft d = _attachmentDrafts[i];
                    if (!string.IsNullOrEmpty(d.SavedFileName) && _storage != null)
                    {
                        string p = _storage.GetAttachmentPath(d.SavedFileName);
                        if (!string.IsNullOrEmpty(p) && File.Exists(p)) paths.Add(p);
                    }
                    else if (d.NewBytes != null)
                    {
                        string tmp = Path.Combine(Path.GetTempPath(), "dm_att_" + Guid.NewGuid().ToString("N").Substring(0, 8) + ImageAttachmentHelper.DetectFileExtension(d.NewBytes));
                        File.WriteAllBytes(tmp, d.NewBytes);
                        tempFiles.Add(tmp);
                        paths.Add(tmp);
                    }
                }
                if (paths.Count > 0)
                {
                    ImageAttachmentHelper.ShowImagePreviewDialog(paths, Math.Min(index, paths.Count - 1), this, "claude_warm");
                }
            }
            finally
            {
                for (int i = 0; i < tempFiles.Count; i++)
                {
                    try { File.Delete(tempFiles[i]); } catch { }
                }
            }
        }

        private void ToggleDdlInputs(bool enabled)
        {
            _ddlDatePicker.IsEnabled = enabled;
            _ddlHourBox.IsEnabled = enabled;
            _ddlMinBox.IsEnabled = enabled;
        }

        private void ToggleRemInputs(bool enabled)
        {
            _remDatePicker.IsEnabled = enabled;
            _remHourBox.IsEnabled = enabled;
            _remMinBox.IsEnabled = enabled;
        }

        private void ApplyDdlDateTime(DateTime dt)
        {
            _ddlEnabledBox.IsChecked = true;
            SetPickerValue(_ddlDatePicker, _ddlHourBox, _ddlMinBox, dt);
        }

        private void ApplyRemDateTime(DateTime dt)
        {
            _remEnabledBox.IsChecked = true;
            SetPickerValue(_remDatePicker, _remHourBox, _remMinBox, dt);
        }

        private void ApplyRemOffsetFromDdl(TimeSpan offset)
        {
            DateTime baseDt = _ddlEnabledBox.IsChecked == true
                ? ReadPickerValue(_ddlDatePicker, _ddlHourBox, _ddlMinBox)
                : DateTime.Now.AddHours(1);
            ApplyRemDateTime(baseDt.Add(offset));
        }

        private static void SetPickerValue(DatePicker dp, ComboBox hourBox, ComboBox minBox, DateTime dt)
        {
            dp.SelectedDate = dt.Date;
            hourBox.Text = dt.Hour.ToString("00", CultureInfo.InvariantCulture);
            minBox.Text = dt.Minute.ToString("00", CultureInfo.InvariantCulture);
        }

        private static DateTime ReadPickerValue(DatePicker dp, ComboBox hourBox, ComboBox minBox)
        {
            DateTime d = dp.SelectedDate.HasValue ? dp.SelectedDate.Value.Date : DateTime.Today;
            int h = 12;
            int m = 0;
            int.TryParse(hourBox.Text, out h);
            int.TryParse(minBox.Text, out m);
            if (h < 0) h = 0;
            if (h > 23) h = 23;
            if (m < 0) m = 0;
            if (m > 59) m = 59;
            return d.AddHours(h).AddMinutes(m);
        }

        private static ComboBox CreateNumberCombo(int min, int max, int step)
        {
            var cb = new ComboBox
            {
                Width = 58,
                IsEditable = true
            };
            for (int v = min; v <= max; v += step)
            {
                cb.Items.Add(v.ToString("00", CultureInfo.InvariantCulture));
            }
            if (max == 55) cb.Items.Add("59");
            return cb;
        }

        private void AddQuickPresetButton(WrapPanel panel, string text, Action onClick)
        {
            var btn = GlassControlStyles.CreateGlassButton(text,
                _pal.HoverBg, _pal.TextPrimary,
                _pal.Border, _scale.SmallFontSize, _scale.SmallButtonPadding, 7);
            btn.Margin = new Thickness(0, 2, 5, 2);
            btn.Click += delegate { onClick(); };
            panel.Children.Add(btn);
        }

        private TextBlock MakeSectionLabel(string text)
        {
            return new TextBlock
            {
                Text = text,
                FontFamily = ThemeService.AppFontFamily,
                FontSize = _scale.SmallFontSize + 0.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = _pal.TextSecondary
            };
        }
    }
}
