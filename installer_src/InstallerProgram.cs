using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Microsoft.Win32;

namespace DesktopMemo
{
    public static class InstallerProgram
    {
        private const string PayloadResourceName = "DesktopMemoPayload.zip";
        private const string UninstallRegKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\DesktopMemo";
        private const string AppRegKey = @"Software\DesktopMemo";

        [STAThread]
        public static int Main(string[] args)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | (SecurityProtocolType)3072;
            Win32Native.EnablePerMonitorDpiAwareness();

            bool runSelfTest = false;
            bool uninstallMode = false;
            string reportPath = null;

            if (args != null)
            {
                for (int i = 0; i < args.Length; i++)
                {
                    string a = args[i] ?? "";
                    if (a == "--self-test") runSelfTest = true;
                    else if (a.StartsWith("--self-test=", StringComparison.OrdinalIgnoreCase))
                    {
                        runSelfTest = true;
                        reportPath = a.Substring("--self-test=".Length).Trim('"');
                    }
                    else if (a.Equals("--uninstall", StringComparison.OrdinalIgnoreCase))
                    {
                        uninstallMode = true;
                    }
                }
            }

            if (runSelfTest)
            {
                return RunSelfTest(reportPath);
            }

            var app = new Application();
            app.ShutdownMode = ShutdownMode.OnMainWindowClose;

            Window mainWin = uninstallMode ? (Window)new UninstallWindow() : new InstallerWindow();
            app.MainWindow = mainWin;
            mainWin.Show();
            return app.Run();
        }

        private static int RunSelfTest(string reportPath)
        {
            int passed = 0;
            int failed = 0;
            var log = new StringBuilder();

            Action<string, Action> check = delegate(string name, Action body)
            {
                try
                {
                    body();
                    passed++;
                    log.AppendLine("[PASS] " + name);
                }
                catch (Exception ex)
                {
                    failed++;
                    log.AppendLine("[FAIL] " + name + " -> " + ex.Message);
                }
            };

            check("EmbeddedPayloadZipIntegrity", delegate
            {
                Assembly asm = Assembly.GetExecutingAssembly();
                using (Stream s = asm.GetManifestResourceStream(PayloadResourceName))
                {
                    if (s == null) throw new Exception("Missing resource: " + PayloadResourceName);
                    using (var za = new ZipArchive(s, ZipArchiveMode.Read))
                    {
                        bool hasExe = false;
                        bool hasSrv = false;
                        bool hasWeb = false;
                        for (int i = 0; i < za.Entries.Count; i++)
                        {
                            string fn = za.Entries[i].FullName.Replace('\\', '/');
                            if (fn.Equals("DesktopMemo.exe", StringComparison.OrdinalIgnoreCase)) hasExe = true;
                            if (fn.Equals("DesktopMemoServer.exe", StringComparison.OrdinalIgnoreCase)) hasSrv = true;
                            if (fn.Equals("web/index.html", StringComparison.OrdinalIgnoreCase)) hasWeb = true;
                        }
                        if (!hasExe || !hasSrv || !hasWeb)
                        {
                            throw new Exception("Payload missing required entries");
                        }
                    }
                }
            });

            check("InstallerAndUninstallUiSmoke", delegate
            {
                var inst = new InstallerWindow();
                if (inst.WindowStyle != WindowStyle.None || !inst.AllowsTransparency)
                {
                    throw new Exception("Installer window must be borderless transparent flat style");
                }
                if (!inst.DefaultDesktopShortcutChecked)
                {
                    throw new Exception("Desktop shortcut checkbox must be checked by default");
                }
                var uninst = new UninstallWindow();
                if (uninst.WindowStyle != WindowStyle.None)
                {
                    throw new Exception("Uninstall window must use borderless flat style");
                }
                uninst.Close();
                inst.Close();
            });

            check("StripSingleRootDirExtraction", delegate
            {
                string tmpRoot = Path.Combine(Path.GetTempPath(), "DesktopMemo_StripZip_" + Guid.NewGuid().ToString("N").Substring(0, 8));
                try
                {
                    using (var ms = new MemoryStream())
                    {
                        using (var za = new ZipArchive(ms, ZipArchiveMode.Create, true))
                        {
                            ZipArchiveEntry e1 = za.CreateEntry("AppBundle/App.exe");
                            using (var w = new StreamWriter(e1.Open(), new UTF8Encoding(false))) w.Write("exe");
                            ZipArchiveEntry e2 = za.CreateEntry("AppBundle/sub/data.bin");
                            using (var w = new StreamWriter(e2.Open(), new UTF8Encoding(false))) w.Write("bin");
                        }
                        ms.Position = 0;
                        ExtractStreamToDirectory(ms, tmpRoot, null, true);
                    }
                    string directExe = Path.Combine(tmpRoot, "App.exe");
                    string nestedExe = Path.Combine(tmpRoot, "AppBundle", "App.exe");
                    string directBin = Path.Combine(tmpRoot, "sub", "data.bin");
                    if (!File.Exists(directExe) || File.Exists(nestedExe) || !File.Exists(directBin))
                    {
                        throw new Exception("Single root folder should be stripped during zip extraction");
                    }
                }
                finally
                {
                    try { if (Directory.Exists(tmpRoot)) Directory.Delete(tmpRoot, true); } catch { }
                }
            });

            log.AppendLine(string.Format("SUMMARY: passed={0}, failed={1}", passed, failed));
            if (!string.IsNullOrWhiteSpace(reportPath))
            {
                try { File.WriteAllText(reportPath, log.ToString(), new UTF8Encoding(false)); } catch { }
            }
            return failed == 0 ? 0 : 1;
        }

        public static void ExtractStreamToDirectory(Stream zipStream, string targetDir, IProgress<string> statusProgress)
        {
            ExtractStreamToDirectory(zipStream, targetDir, statusProgress, false);
        }

        public static void ExtractStreamToDirectory(Stream zipStream, string targetDir, IProgress<string> statusProgress, bool stripSingleRootDir)
        {
            string targetFull = Path.GetFullPath(targetDir);
            if (!targetFull.EndsWith(Path.DirectorySeparatorChar.ToString()))
            {
                targetFull += Path.DirectorySeparatorChar;
            }
            Directory.CreateDirectory(targetFull);

            using (var za = new ZipArchive(zipStream, ZipArchiveMode.Read))
            {
                string commonPrefix = null;
                if (stripSingleRootDir)
                {
                    for (int i = 0; i < za.Entries.Count; i++)
                    {
                        string norm = (za.Entries[i].FullName ?? "").Replace('\\', '/').TrimStart('/');
                        if (string.IsNullOrEmpty(norm)) continue;
                        int slash = norm.IndexOf('/');
                        if (slash <= 0)
                        {
                            commonPrefix = null;
                            break;
                        }
                        string cand = norm.Substring(0, slash + 1);
                        if (commonPrefix == null) commonPrefix = cand;
                        else if (!string.Equals(commonPrefix, cand, StringComparison.OrdinalIgnoreCase))
                        {
                            commonPrefix = null;
                            break;
                        }
                    }
                }

                int count = 0;
                for (int i = 0; i < za.Entries.Count; i++)
                {
                    ZipArchiveEntry entry = za.Entries[i];
                    string normPath = (entry.FullName ?? "").Replace('\\', '/').TrimStart('/');
                    if (!string.IsNullOrEmpty(commonPrefix) && normPath.StartsWith(commonPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        normPath = normPath.Substring(commonPrefix.Length);
                    }
                    if (string.IsNullOrEmpty(normPath)) continue;

                    string rel = normPath.Replace('/', Path.DirectorySeparatorChar);
                    if (string.IsNullOrWhiteSpace(rel)) continue;

                    string destPath = Path.GetFullPath(Path.Combine(targetFull, rel));
                    if (!destPath.StartsWith(targetFull, StringComparison.OrdinalIgnoreCase)) continue;

                    if (string.IsNullOrEmpty(entry.Name))
                    {
                        Directory.CreateDirectory(destPath);
                        continue;
                    }

                    string parent = Path.GetDirectoryName(destPath);
                    if (!string.IsNullOrEmpty(parent) && !Directory.Exists(parent))
                    {
                        Directory.CreateDirectory(parent);
                    }

                    using (Stream inStream = entry.Open())
                    using (var outStream = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        inStream.CopyTo(outStream);
                    }
                    count++;
                    if (statusProgress != null && count % 25 == 0)
                    {
                        statusProgress.Report("正在解压组件 (" + count + "/" + za.Entries.Count + ")...");
                    }
                }
            }
        }

        public static bool ContainsNonAscii(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] > 127) return true;
            }
            return false;
        }

        public static bool IsDirWritable(string dirPath, out string error)
        {
            error = "";
            try
            {
                Directory.CreateDirectory(dirPath);
                string probe = Path.Combine(dirPath, ".dm_write_probe_" + Guid.NewGuid().ToString("N").Substring(0, 6));
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }

    public class InstallerWindow : Window
    {
        private readonly ThemePalette _pal;
        private readonly UiScaleMetrics _scale;

        private Border _rootCard;
        private StackPanel _pageConfig;
        private StackPanel _pageProgress;
        private StackPanel _pageDone;

        private TextBox _installDirBox;
        private RadioButton _dataDecoupledRadio;
        private RadioButton _dataPortableRadio;
        private TextBox _dataDirBox;
        private Button _browseDataBtn;

        private CheckBox _desktopShortcutBox;
        private CheckBox _startMenuShortcutBox;
        private CheckBox _autoStartBox;

        private ProgressBar _progressBar;
        private TextBlock _progressStatusText;
        private TextBlock _progressDetailText;

        private TextBlock _doneSummaryText;
        private CheckBox _launchNowBox;

        private string _installedExePath = "";

        public bool DefaultDesktopShortcutChecked { get { return _desktopShortcutBox != null && _desktopShortcutBox.IsChecked == true; } }

        public InstallerWindow()
        {
            _pal = ThemeService.GetPalette("claude_warm", 0.99);
            _scale = ThemeService.GetScaleMetrics("standard", 13);

            Title = "DesktopMemo " + AppMeta.VersionTag + " 安装向导";
            Width = 650;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = true;

            GlassControlStyles.ApplyToResources(Resources, _pal, _scale);
            BuildUi();
            Loaded += delegate { SpringAnimationHelper.AnimatePopIn(_rootCard, 260); };
        }

        private void BuildUi()
        {
            _rootCard = new Border
            {
                Margin = new Thickness(14),
                CornerRadius = new CornerRadius(16),
                Background = _pal.CardBg,
                BorderBrush = _pal.Border,
                BorderThickness = new Thickness(1),
                Effect = new DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 24,
                    ShadowDepth = 4,
                    Opacity = 0.20
                }
            };

            var rootStack = new StackPanel();

            // Header matching DesktopMemo editorial style
            var header = new Border
            {
                Background = _pal.HeaderBg,
                BorderBrush = _pal.Border,
                BorderThickness = new Thickness(0, 0, 0, 1),
                CornerRadius = new CornerRadius(16, 16, 0, 0),
                Padding = new Thickness(20, 13, 14, 13)
            };
            header.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                if (e.ButtonState == MouseButtonState.Pressed) { try { DragMove(); } catch { } }
            };

            var hDock = new DockPanel();
            var brand = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            brand.Children.Add(new TextBlock
            {
                Text = "✦ 桌面备忘录 DesktopMemo",
                FontFamily = ThemeService.AppFontFamily,
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = _pal.TextPrimary,
                VerticalAlignment = VerticalAlignment.Center
            });
            brand.Children.Add(new Border
            {
                Background = _pal.HoverBg,
                BorderBrush = _pal.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(7, 1, 7, 1),
                Margin = new Thickness(9, 1, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = AppMeta.VersionTag + " 安装向导",
                    FontFamily = ThemeService.AppFontFamily,
                    FontSize = 11.5,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = _pal.TextSecondary
                }
            });
            DockPanel.SetDock(brand, Dock.Left);
            hDock.Children.Add(brand);

            var closeBtn = GlassControlStyles.CreateGlassButton("✕", Brushes.Transparent, _pal.TextSecondary, null, 13, new Thickness(8, 2, 8, 2), 7);
            closeBtn.HorizontalAlignment = HorizontalAlignment.Right;
            closeBtn.Click += delegate { Close(); };
            DockPanel.SetDock(closeBtn, Dock.Right);
            hDock.Children.Add(closeBtn);
            header.Child = hDock;
            rootStack.Children.Add(header);

            var contentHost = new Grid { Margin = new Thickness(22, 16, 22, 20) };

            BuildConfigPage();
            BuildProgressPage();
            BuildDonePage();

            contentHost.Children.Add(_pageConfig);
            contentHost.Children.Add(_pageProgress);
            contentHost.Children.Add(_pageDone);

            rootStack.Children.Add(contentHost);
            _rootCard.Child = rootStack;
            Content = _rootCard;
        }

        private void BuildConfigPage()
        {
            _pageConfig = new StackPanel();

            string defaultInstall = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs", "DesktopMemo");
            string defaultData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "DesktopMemo", "data");

            // Card 1: Install path & Data path
            var pathCard = CreateCard("📁 安装位置与配置存储路径");
            var pathStack = (StackPanel)pathCard.Child;

            pathStack.Children.Add(MakeHint("1. 软件程序安装路径（可安装到任意盘符，默认无需管理员权限）："));
            var instRow = new Grid { Margin = new Thickness(0, 4, 0, 10) };
            instRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            instRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _installDirBox = new TextBox { Text = defaultInstall, Padding = new Thickness(9, 6, 9, 6) };
            Grid.SetColumn(_installDirBox, 0);
            instRow.Children.Add(_installDirBox);

            var browseInstBtn = GlassControlStyles.CreateGlassButton("浏览...",
                _pal.HoverBg, _pal.TextPrimary, _pal.Border, 12.5, new Thickness(12, 5, 12, 5), 8);
            browseInstBtn.Margin = new Thickness(8, 0, 0, 0);
            browseInstBtn.Click += delegate
            {
                string picked = BrowseFolder("选择 DesktopMemo 软件安装路径", _installDirBox.Text);
                if (!string.IsNullOrEmpty(picked)) _installDirBox.Text = picked;
            };
            Grid.SetColumn(browseInstBtn, 1);
            instRow.Children.Add(browseInstBtn);
            pathStack.Children.Add(instRow);

            pathStack.Children.Add(MakeHint("2. config.json 与日程数据存储路径："));
            _dataDecoupledRadio = new RadioButton
            {
                GroupName = "DataMode",
                Content = "独立目录保存配置与待办数据（推荐 · 覆盖升级或重装不丢记录）",
                IsChecked = true,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 5, 0, 5)
            };
            _dataPortableRadio = new RadioButton
            {
                GroupName = "DataMode",
                Content = "便携模式（跟随安装在 <软件目录>\\data 下）",
                Margin = new Thickness(0, 2, 0, 6)
            };
            _dataDecoupledRadio.Checked += delegate { ToggleDataDirInput(true); };
            _dataPortableRadio.Checked += delegate { ToggleDataDirInput(false); };
            pathStack.Children.Add(_dataDecoupledRadio);

            var dataRow = new Grid { Margin = new Thickness(20, 0, 0, 6) };
            dataRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            dataRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _dataDirBox = new TextBox { Text = defaultData, Padding = new Thickness(9, 6, 9, 6) };
            Grid.SetColumn(_dataDirBox, 0);
            dataRow.Children.Add(_dataDirBox);

            _browseDataBtn = GlassControlStyles.CreateGlassButton("浏览...",
                _pal.HoverBg, _pal.TextPrimary, _pal.Border, 12.5, new Thickness(12, 5, 12, 5), 8);
            _browseDataBtn.Margin = new Thickness(8, 0, 0, 0);
            _browseDataBtn.Click += delegate
            {
                string picked = BrowseFolder("选择配置与数据存储目录", _dataDirBox.Text);
                if (!string.IsNullOrEmpty(picked)) _dataDirBox.Text = picked;
            };
            Grid.SetColumn(_browseDataBtn, 1);
            dataRow.Children.Add(_browseDataBtn);
            pathStack.Children.Add(dataRow);
            pathStack.Children.Add(_dataPortableRadio);
            _pageConfig.Children.Add(pathCard);

            // Card 2: Shortcuts & AutoStart
            var optCard = CreateCard("🧩 快捷方式与开机启动");
            var optStack = (StackPanel)optCard.Child;

            var scWrap = new WrapPanel { Margin = new Thickness(0, 2, 0, 2) };
            _desktopShortcutBox = new CheckBox
            {
                Content = "创建桌面快捷方式（推荐）",
                IsChecked = true,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 18, 4)
            };
            _startMenuShortcutBox = new CheckBox
            {
                Content = "创建开始菜单快捷方式",
                IsChecked = true,
                Margin = new Thickness(0, 0, 18, 4)
            };
            _autoStartBox = new CheckBox
            {
                Content = "Windows 开机自动启动",
                IsChecked = false,
                Margin = new Thickness(0, 0, 0, 4)
            };
            scWrap.Children.Add(_desktopShortcutBox);
            scWrap.Children.Add(_startMenuShortcutBox);
            scWrap.Children.Add(_autoStartBox);
            optStack.Children.Add(scWrap);
            _pageConfig.Children.Add(optCard);

            var footer = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
            footer.Children.Add(new TextBlock
            {
                Text = "若目标配置目录已有旧 data.json / config.json，将完整保留不会覆盖。",
                FontFamily = ThemeService.AppFontFamily,
                FontSize = 12,
                Foreground = _pal.TextSecondary,
                VerticalAlignment = VerticalAlignment.Center
            });

            var installBtn = GlassControlStyles.CreateGlassButton("立即安装 →",
                _pal.AccentBg, _pal.AccentFg, null, 14, new Thickness(22, 8, 22, 8), 9);
            installBtn.FontWeight = FontWeights.SemiBold;
            installBtn.HorizontalAlignment = HorizontalAlignment.Right;
            installBtn.Click += delegate { StartInstallAsync(); };
            DockPanel.SetDock(installBtn, Dock.Right);
            footer.Children.Add(installBtn);
            _pageConfig.Children.Add(footer);
        }

        private void BuildProgressPage()
        {
            _pageProgress = new StackPanel { Visibility = Visibility.Collapsed };
            var card = CreateCard("⏳ 正在安装 DesktopMemo " + AppMeta.VersionTag);
            var st = (StackPanel)card.Child;

            _progressStatusText = new TextBlock
            {
                Text = "准备安装环境...",
                FontFamily = ThemeService.AppFontFamily,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = _pal.TextPrimary,
                Margin = new Thickness(0, 6, 0, 10)
            };
            st.Children.Add(_progressStatusText);

            _progressBar = new ProgressBar
            {
                Height = 14,
                Minimum = 0,
                Maximum = 100,
                Value = 10,
                Foreground = _pal.AccentBg,
                Background = _pal.HoverBg,
                BorderBrush = _pal.Border,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 0, 10)
            };
            st.Children.Add(_progressBar);

            _progressDetailText = new TextBlock
            {
                Text = "正在写入程序与初始化目录...",
                FontFamily = ThemeService.AppFontFamily,
                FontSize = 12.5,
                Foreground = _pal.TextSecondary,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12)
            };
            st.Children.Add(_progressDetailText);

            _pageProgress.Children.Add(card);
        }

        private void BuildDonePage()
        {
            _pageDone = new StackPanel { Visibility = Visibility.Collapsed };
            var card = CreateCard("✅ 安装完成 · DesktopMemo " + AppMeta.VersionTag);
            var st = (StackPanel)card.Child;

            _doneSummaryText = new TextBlock
            {
                FontFamily = ThemeService.AppFontFamily,
                FontSize = 13,
                Foreground = _pal.TextPrimary,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 22,
                Margin = new Thickness(0, 4, 0, 14)
            };
            st.Children.Add(_doneSummaryText);

            _launchNowBox = new CheckBox
            {
                Content = "立即启动 DesktopMemo " + AppMeta.VersionTag,
                IsChecked = true,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 6)
            };
            st.Children.Add(_launchNowBox);
            _pageDone.Children.Add(card);

            var finishBtn = GlassControlStyles.CreateGlassButton("完成安装",
                _pal.AccentBg, _pal.AccentFg, null, 14, new Thickness(24, 8, 24, 8), 9);
            finishBtn.FontWeight = FontWeights.SemiBold;
            finishBtn.HorizontalAlignment = HorizontalAlignment.Right;
            finishBtn.Margin = new Thickness(0, 6, 0, 0);
            finishBtn.Click += delegate
            {
                if (_launchNowBox.IsChecked == true && !string.IsNullOrEmpty(_installedExePath) && File.Exists(_installedExePath))
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = _installedExePath,
                            WorkingDirectory = Path.GetDirectoryName(_installedExePath),
                            UseShellExecute = true
                        });
                    }
                    catch { }
                }
                Close();
            };
            _pageDone.Children.Add(finishBtn);
        }

        private async void StartInstallAsync()
        {
            string installDir = (_installDirBox.Text ?? "").Trim().Trim('"');
            bool decoupled = _dataDecoupledRadio.IsChecked == true;
            string dataDir = decoupled
                ? (_dataDirBox.Text ?? "").Trim().Trim('"')
                : Path.Combine(installDir, "data");

            if (string.IsNullOrWhiteSpace(installDir))
            {
                MessageBox.Show("请选择有效的软件安装路径。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (string.IsNullOrWhiteSpace(dataDir))
            {
                MessageBox.Show("请选择有效的配置与数据存储路径。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string writeErr;
            if (!InstallerProgram.IsDirWritable(installDir, out writeErr))
            {
                MessageBox.Show("安装目录无写入权限（如选择了 Program Files 可改选用户目录，或右键以管理员身份运行安装程序）：\n" + writeErr,
                    "目录不可写", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!InstallerProgram.IsDirWritable(dataDir, out writeErr))
            {
                MessageBox.Show("配置数据目录无写入权限：\n" + writeErr, "目录不可写", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string targetExe = Path.Combine(installDir, "DesktopMemo.exe");
            if (!EnsureRunningAppClosed(targetExe)) return;

            bool wantDesktop = _desktopShortcutBox.IsChecked == true;
            bool wantStartMenu = _startMenuShortcutBox.IsChecked == true;
            bool wantAutoStart = _autoStartBox.IsChecked == true;

            _pageConfig.Visibility = Visibility.Collapsed;
            _pageProgress.Visibility = Visibility.Visible;
            SpringAnimationHelper.AnimateViewTransition(_pageProgress, 24, 300);

            string shortcutResult = wantDesktop ? "未创建" : "已按用户选择跳过";

            try
            {
                _progressStatusText.Text = "正在安装 DesktopMemo 主程序...";
                _progressBar.Value = 25;
                var statusReporter = new Progress<string>(delegate(string msg) { _progressDetailText.Text = msg; });

                await Task.Run(delegate
                {
                    Assembly asm = Assembly.GetExecutingAssembly();
                    using (Stream resStream = asm.GetManifestResourceStream("DesktopMemoPayload.zip"))
                    {
                        if (resStream == null) throw new InvalidOperationException("安装包内缺少程序压缩载荷。");
                        InstallerProgram.ExtractStreamToDirectory(resStream, installDir, statusReporter);
                    }
                });

                _progressBar.Value = 60;
                _progressDetailText.Text = "正在初始化配置与数据目录...";

                Directory.CreateDirectory(dataDir);
                Directory.CreateDirectory(Path.Combine(dataDir, "backups"));
                Directory.CreateDirectory(Path.Combine(dataDir, "attachments"));

                string iniPath = Path.Combine(installDir, "storage_path.ini");
                if (decoupled)
                {
                    File.WriteAllText(iniPath, "# DesktopMemo data/config directory\r\nDataDir=" + dataDir + "\r\n", new UTF8Encoding(false));
                    try
                    {
                        using (RegistryKey rk = Registry.CurrentUser.CreateSubKey(@"Software\DesktopMemo"))
                        {
                            if (rk != null) rk.SetValue("DataDir", dataDir);
                        }
                    }
                    catch { }
                }
                else
                {
                    if (File.Exists(iniPath)) { try { File.Delete(iniPath); } catch { } }
                    try
                    {
                        using (RegistryKey rk = Registry.CurrentUser.OpenSubKey(@"Software\DesktopMemo", true))
                        {
                            if (rk != null) rk.DeleteValue("DataDir", false);
                        }
                    }
                    catch { }
                }

                // Register Uninstall.exe
                string uninstallExe = Path.Combine(installDir, "Uninstall.exe");
                try
                {
                    string selfExe = Assembly.GetExecutingAssembly().Location;
                    if (!string.Equals(Path.GetFullPath(selfExe), Path.GetFullPath(uninstallExe), StringComparison.OrdinalIgnoreCase))
                    {
                        File.Copy(selfExe, uninstallExe, true);
                    }
                }
                catch { }

                try
                {
                    using (RegistryKey uKey = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\DesktopMemo"))
                    {
                        if (uKey != null)
                        {
                            uKey.SetValue("DisplayName", "DesktopMemo 桌面备忘录");
                            uKey.SetValue("DisplayVersion", AppMeta.Version);
                            uKey.SetValue("Publisher", "DesktopMemo");
                            uKey.SetValue("InstallLocation", installDir);
                            uKey.SetValue("DataLocation", dataDir);
                            uKey.SetValue("DisplayIcon", targetExe + ",0");
                            uKey.SetValue("UninstallString", "\"" + uninstallExe + "\" --uninstall");
                            uKey.SetValue("NoModify", 1, RegistryValueKind.DWord);
                            uKey.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                        }
                    }
                }
                catch { }

                _installedExePath = targetExe;
                _progressBar.Value = 85;

                // Shortcuts & AutoStart
                if (wantDesktop)
                {
                    string scPath;
                    string scErr;
                    if (Win32Native.CreateDesktopShortcut(targetExe, "桌面备忘录", "DesktopMemo " + AppMeta.VersionTag, out scPath, out scErr))
                    {
                        shortcutResult = "已创建 (" + scPath + ")";
                    }
                    else
                    {
                        shortcutResult = "创建失败：" + scErr;
                    }
                }

                if (wantStartMenu)
                {
                    string startDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "DesktopMemo");
                    string smErr;
                    Win32Native.CreateShortcutAt(Path.Combine(startDir, "DesktopMemo 桌面备忘录.lnk"), targetExe, installDir, "DesktopMemo " + AppMeta.VersionTag, out smErr);
                    Win32Native.CreateShortcutAt(Path.Combine(startDir, "卸载 DesktopMemo.lnk"), uninstallExe, installDir, "卸载 DesktopMemo", out smErr);
                }

                if (wantAutoStart)
                {
                    string asErr;
                    Win32Native.SetAutoStart(true, targetExe, out asErr);
                }

                _progressBar.Value = 100;
                _doneSummaryText.Text = string.Format(
                    "• 软件安装目录：{0}\r\n• 配置与数据目录：{1}\r\n• 桌面快捷方式：{2}",
                    installDir, dataDir, shortcutResult);

                _pageProgress.Visibility = Visibility.Collapsed;
                _pageDone.Visibility = Visibility.Visible;
                SpringAnimationHelper.AnimateViewTransition(_pageDone, 24, 300);
            }
            catch (Exception ex)
            {
                MessageBox.Show("安装过程中发生错误：\n" + ex.Message, "安装失败", MessageBoxButton.OK, MessageBoxImage.Error);
                _pageProgress.Visibility = Visibility.Collapsed;
                _pageConfig.Visibility = Visibility.Visible;
            }
        }

        private static bool EnsureRunningAppClosed(string targetExe)
        {
            try
            {
                string targetFull = Path.GetFullPath(targetExe);
                Process[] procs = Process.GetProcessesByName("DesktopMemo");
                var matching = new List<Process>();
                for (int i = 0; i < procs.Length; i++)
                {
                    try
                    {
                        if (string.Equals(Path.GetFullPath(procs[i].MainModule.FileName), targetFull, StringComparison.OrdinalIgnoreCase))
                        {
                            matching.Add(procs[i]);
                        }
                    }
                    catch { }
                }
                if (matching.Count == 0) return true;

                MessageBoxResult r = MessageBox.Show(
                    "检测到安装目录中的 DesktopMemo.exe 正在运行。\n是否自动关闭正在运行的桌面备忘录并继续安装？",
                    "关闭运行中的程序",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                if (r != MessageBoxResult.Yes) return false;

                for (int i = 0; i < matching.Count; i++)
                {
                    try
                    {
                        matching[i].Kill();
                        matching[i].WaitForExit(3000);
                    }
                    catch { }
                }
                return true;
            }
            catch
            {
                return true;
            }
        }

        private void ToggleDataDirInput(bool enabled)
        {
            if (_dataDirBox != null) _dataDirBox.IsEnabled = enabled;
            if (_browseDataBtn != null) _browseDataBtn.IsEnabled = enabled;
        }

        private string BrowseFolder(string description, string initialPath)
        {
            using (var dlg = new System.Windows.Forms.FolderBrowserDialog { Description = description })
            {
                if (!string.IsNullOrWhiteSpace(initialPath) && Directory.Exists(initialPath.Trim()))
                {
                    dlg.SelectedPath = initialPath.Trim();
                }
                if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    return dlg.SelectedPath;
                }
            }
            return "";
        }

        private Border CreateCard(string title)
        {
            var card = new Border
            {
                Background = _pal.SurfaceBg,
                BorderBrush = _pal.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(16, 12, 16, 12),
                Margin = new Thickness(0, 0, 0, 12)
            };
            var st = new StackPanel();
            st.Children.Add(new TextBlock
            {
                Text = title,
                FontFamily = ThemeService.AppFontFamily,
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = _pal.TextPrimary,
                Margin = new Thickness(0, 0, 0, 8)
            });
            card.Child = st;
            return card;
        }

        private TextBlock MakeHint(string text)
        {
            return new TextBlock
            {
                Text = text,
                FontFamily = ThemeService.AppFontFamily,
                FontSize = 12.5,
                Foreground = _pal.TextSecondary
            };
        }
    }

    public class UninstallWindow : Window
    {
        private readonly ThemePalette _pal;
        private readonly UiScaleMetrics _scale;
        private Border _rootCard;
        private CheckBox _deleteDataBox;

        public UninstallWindow()
        {
            _pal = ThemeService.GetPalette("claude_warm", 0.99);
            _scale = ThemeService.GetScaleMetrics("standard", 13);

            Title = "卸载 DesktopMemo";
            Width = 500;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = true;

            GlassControlStyles.ApplyToResources(Resources, _pal, _scale);
            BuildUi();
        }

        private void BuildUi()
        {
            _rootCard = new Border
            {
                Margin = new Thickness(14),
                CornerRadius = new CornerRadius(16),
                Background = _pal.CardBg,
                BorderBrush = _pal.Border,
                BorderThickness = new Thickness(1),
                Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 22, ShadowDepth = 4, Opacity = 0.22 }
            };

            var stack = new StackPanel { Margin = new Thickness(20, 16, 20, 18) };
            stack.Children.Add(new TextBlock
            {
                Text = "卸载 DesktopMemo 桌面备忘录",
                FontFamily = ThemeService.AppFontFamily,
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = _pal.TextPrimary,
                Margin = new Thickness(0, 0, 0, 8)
            });

            string installDir = "";
            string dataDir = "";
            ReadInstallMetadata(out installDir, out dataDir);
            if (string.IsNullOrEmpty(installDir)) installDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');

            stack.Children.Add(new TextBlock
            {
                Text = "安装目录：" + installDir + (string.IsNullOrEmpty(dataDir) ? "" : ("\r\n配置与数据目录：" + dataDir)),
                FontFamily = ThemeService.AppFontFamily,
                FontSize = 12.5,
                Foreground = _pal.TextSecondary,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12)
            });

            _deleteDataBox = new CheckBox
            {
                Content = "同时删除配置与待办数据（默认不勾选，保留你的备忘录数据）",
                IsChecked = false,
                Margin = new Thickness(0, 0, 0, 16)
            };
            stack.Children.Add(_deleteDataBox);

            var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var cancelBtn = GlassControlStyles.CreateGlassButton("取消", _pal.SurfaceBg, _pal.TextPrimary, _pal.Border, 13, new Thickness(14, 6, 14, 6), 8);
            cancelBtn.Margin = new Thickness(0, 0, 8, 0);
            cancelBtn.Click += delegate { Close(); };

            var uninstBtn = GlassControlStyles.CreateGlassButton("确认卸载",
                ThemeService.BrushFromHex("#FEE2E2"), ThemeService.BrushFromHex("#DC2626"), ThemeService.BrushFromHex("#FCA5A5"),
                13, new Thickness(14, 6, 14, 6), 8);
            uninstBtn.FontWeight = FontWeights.SemiBold;
            uninstBtn.Click += delegate { RunUninstall(installDir, dataDir, _deleteDataBox.IsChecked == true); };
            btns.Children.Add(cancelBtn);
            btns.Children.Add(uninstBtn);
            stack.Children.Add(btns);

            _rootCard.Child = stack;
            Content = _rootCard;
        }

        private static void ReadInstallMetadata(out string installDir, out string dataDir)
        {
            installDir = "";
            dataDir = "";
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\DesktopMemo", false))
                {
                    if (key != null)
                    {
                        object il = key.GetValue("InstallLocation");
                        object dl = key.GetValue("DataLocation");
                        if (il != null) installDir = il.ToString();
                        if (dl != null) dataDir = dl.ToString();
                    }
                }
            }
            catch { }
        }

        private void RunUninstall(string installDir, string dataDir, bool deleteData)
        {
            try
            {
                Process[] procs = Process.GetProcessesByName("DesktopMemo");
                for (int i = 0; i < procs.Length; i++)
                {
                    try { procs[i].Kill(); procs[i].WaitForExit(2000); } catch { }
                }

                string desktopLnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "桌面备忘录.lnk");
                if (File.Exists(desktopLnk)) { try { File.Delete(desktopLnk); } catch { } }

                string startMenuDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "DesktopMemo");
                if (Directory.Exists(startMenuDir)) { try { Directory.Delete(startMenuDir, true); } catch { } }

                string err;
                Win32Native.SetAutoStart(false, "", out err);

                try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\DesktopMemo", false); } catch { }
                try { Registry.CurrentUser.DeleteSubKeyTree(@"Software\DesktopMemo", false); } catch { }

                if (deleteData && !string.IsNullOrEmpty(dataDir) && Directory.Exists(dataDir))
                {
                    try { Directory.Delete(dataDir, true); } catch { }
                }

                string selfExe = Assembly.GetExecutingAssembly().Location;
                if (Directory.Exists(installDir))
                {
                    string[] topFiles = Directory.GetFiles(installDir);
                    for (int i = 0; i < topFiles.Length; i++)
                    {
                        if (string.Equals(Path.GetFullPath(topFiles[i]), Path.GetFullPath(selfExe), StringComparison.OrdinalIgnoreCase)) continue;
                        try { File.Delete(topFiles[i]); } catch { }
                    }
                    foreach (string sub in new[] { "web", "plugins" })
                    {
                        string p = Path.Combine(installDir, sub);
                        if (Directory.Exists(p)) { try { Directory.Delete(p, true); } catch { } }
                    }
                    if (deleteData)
                    {
                        string portableData = Path.Combine(installDir, "data");
                        if (Directory.Exists(portableData)) { try { Directory.Delete(portableData, true); } catch { } }
                    }
                }

                MessageBox.Show("DesktopMemo 已成功卸载。", "卸载完成", MessageBoxButton.OK, MessageBoxImage.Information);
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = string.Format("/c ping 127.0.0.1 -n 3 >nul & del /f /q \"{0}\" & rmdir \"{1}\" 2>nul", selfExe, installDir),
                        WindowStyle = ProcessWindowStyle.Hidden,
                        CreateNoWindow = true,
                        UseShellExecute = false
                    });
                }
                catch { }
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show("卸载时出现异常：" + ex.Message, "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
