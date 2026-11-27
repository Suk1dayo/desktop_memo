using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace DesktopMemo
{
    public class VoiceMemoOverlayWindow : Window
    {
        private const int BarCount = 19;

        private readonly ThemePalette _pal;
        private readonly DispatcherTimer _clockTimer;
        private readonly DispatcherTimer _waveTimer;
        private readonly DateTime _startedUtc;

        private Border _rootCard;
        private Ellipse _pulseHalo;
        private Ellipse _pulseDot;
        private TextBlock _titleText;
        private TextBlock _timerText;
        private TextBlock _subText;
        private Border _waveStage;
        private readonly Border[] _levelBars = new Border[BarCount];
        private readonly double[] _levelHistory = new double[BarCount];
        private Button _sendBtn;
        private Button _cancelBtn;

        private double _targetLevel = 0.0;
        private double _smoothedLevel = 0.0;
        private double _wavePhase = 0.0;
        private int _historyTick = 0;
        private bool _isProcessing = false;

        public event Action FinishRequested;
        public event Action CancelRequested;

        public VoiceMemoOverlayWindow(string themeKey)
        {
            _pal = ThemeService.GetPalette(themeKey, 0.98);
            _startedUtc = DateTime.UtcNow;

            Title = "语音快捷备忘";
            Width = 396;
            SizeToContent = SizeToContent.Height;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            ResizeMode = ResizeMode.NoResize;

            Rect workArea = SystemParameters.WorkArea;
            Left = workArea.Left + (workArea.Width - Width) / 2.0;
            Top = workArea.Top + 26;

            BuildUi();

            _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _clockTimer.Tick += delegate
            {
                TimeSpan elapsed = DateTime.UtcNow - _startedUtc;
                if (_timerText != null)
                {
                    _timerText.Text = string.Format("{0:00}:{1:00}", (int)elapsed.TotalMinutes, elapsed.Seconds);
                }
            };
            _clockTimer.Start();

            _waveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
            _waveTimer.Tick += delegate { RenderWaveformFrame(); };
            _waveTimer.Start();

            Loaded += delegate { SpringAnimationHelper.AnimatePopIn(_rootCard, 220); };
            Closed += delegate
            {
                if (_clockTimer != null) _clockTimer.Stop();
                if (_waveTimer != null) _waveTimer.Stop();
            };
        }

        private void BuildUi()
        {
            _rootCard = new Border
            {
                Margin = new Thickness(10),
                CornerRadius = new CornerRadius(14),
                Background = _pal.PopupBg,
                BorderBrush = _pal.Border,
                BorderThickness = new Thickness(1.2),
                Padding = new Thickness(14, 11, 14, 11),
                Effect = new DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 22,
                    ShadowDepth = 4,
                    Opacity = _pal.IsDark ? 0.42 : 0.20
                }
            };

            var stack = new StackPanel();

            // Header row: status dot with halo + title + timer
            var topRow = new DockPanel();
            var leftRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

            var dotHost = new Grid { Width = 18, Height = 18, Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center };
            _pulseHalo = new Ellipse
            {
                Width = 16,
                Height = 16,
                Fill = ThemeService.BrushFromHexAlpha("#EF4444", 55),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            _pulseDot = new Ellipse
            {
                Width = 9,
                Height = 9,
                Fill = ThemeService.BrushFromHex("#EF4444"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            dotHost.Children.Add(_pulseHalo);
            dotHost.Children.Add(_pulseDot);

            _titleText = new TextBlock
            {
                Text = "正在聆听…",
                FontFamily = ThemeService.AppFontFamily,
                FontSize = 13.5,
                FontWeight = FontWeights.Bold,
                Foreground = _pal.TextPrimary,
                VerticalAlignment = VerticalAlignment.Center
            };
            leftRow.Children.Add(dotHost);
            leftRow.Children.Add(_titleText);
            DockPanel.SetDock(leftRow, Dock.Left);
            topRow.Children.Add(leftRow);

            _timerText = new TextBlock
            {
                Text = "00:00",
                FontFamily = ThemeService.AppFontFamily,
                FontSize = 12.5,
                FontWeight = FontWeights.Bold,
                Foreground = _pal.TextSecondary,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            DockPanel.SetDock(_timerText, Dock.Right);
            topRow.Children.Add(_timerText);
            stack.Children.Add(topRow);

            // Prominent central waveform stage
            _waveStage = new Border
            {
                Height = 48,
                Margin = new Thickness(0, 8, 0, 6),
                CornerRadius = new CornerRadius(10),
                Background = _pal.IsDark ? ThemeService.BrushFromHexAlpha("#0F172A", 180) : ThemeService.BrushFromHexAlpha("#F8FAFC", 230),
                BorderBrush = _pal.Border,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10, 4, 10, 4)
            };
            var barsPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Height = 40
            };
            for (int i = 0; i < BarCount; i++)
            {
                var bar = new Border
                {
                    Width = 5.5,
                    Height = 6,
                    CornerRadius = new CornerRadius(2.75),
                    Background = _pal.AccentBg,
                    Margin = new Thickness(2.5, 0, 2.5, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                _levelBars[i] = bar;
                barsPanel.Children.Add(bar);
            }
            _waveStage.Child = barsPanel;
            stack.Children.Add(_waveStage);

            _subText = new TextBlock
            {
                Text = "再次按下快捷键发送至 AI · 按 Esc 取消",
                FontFamily = ThemeService.AppFontFamily,
                FontSize = 11.5,
                Foreground = _pal.TextSecondary,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8)
            };
            stack.Children.Add(_subText);

            var btnRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            _cancelBtn = GlassControlStyles.CreateGlassButton("取消",
                _pal.HoverBg, _pal.TextSecondary, _pal.Border, 11.5, new Thickness(10, 3, 10, 3), 7);
            _cancelBtn.Margin = new Thickness(0, 0, 6, 0);
            _cancelBtn.Click += delegate
            {
                Action h = CancelRequested;
                if (h != null) h();
            };

            _sendBtn = GlassControlStyles.CreateGlassButton("完成并发送 AI",
                _pal.AccentBg, _pal.AccentFg, null, 11.5, new Thickness(12, 3, 12, 3), 7);
            _sendBtn.FontWeight = FontWeights.SemiBold;
            _sendBtn.Click += delegate
            {
                Action h = FinishRequested;
                if (h != null) h();
            };

            btnRow.Children.Add(_cancelBtn);
            btnRow.Children.Add(_sendBtn);
            stack.Children.Add(btnRow);

            _rootCard.Child = stack;
            Content = _rootCard;
            RenderWaveformFrame();
        }

        public void SetRecordingHint(string hotkeyLabel, bool isHoldMode)
        {
            _isProcessing = false;
            _pulseDot.Fill = ThemeService.BrushFromHex("#EF4444");
            _pulseHalo.Fill = ThemeService.BrushFromHexAlpha("#EF4444", 60);
            _titleText.Text = isHoldMode ? "按住说话中…" : "正在语音记录…";
            _subText.Text = isHoldMode
                ? ("松开 " + hotkeyLabel + " 立即发送至 AI · 按 Esc 取消")
                : ("再次按 " + hotkeyLabel + " 结束并发送至 AI · 按 Esc 取消");
            _sendBtn.Visibility = isHoldMode ? Visibility.Collapsed : Visibility.Visible;
            _cancelBtn.Visibility = Visibility.Visible;
        }

        public void SetProcessingState(string title, string detail)
        {
            _isProcessing = true;
            if (_clockTimer != null) _clockTimer.Stop();
            _pulseDot.Fill = _pal.AccentBg;
            _pulseHalo.Fill = ThemeService.BrushFromHexAlpha("#2563EB", 45);
            _titleText.Text = title;
            _subText.Text = detail;
            _sendBtn.Visibility = Visibility.Collapsed;
            _cancelBtn.Visibility = Visibility.Collapsed;
            _targetLevel = 0.25;
        }

        public void UpdateAudioLevel(double level)
        {
            _targetLevel = Math.Max(0.0, Math.Min(1.0, level));
        }

        private void RenderWaveformFrame()
        {
            _wavePhase += _isProcessing ? 0.28 : 0.34;
            int center = BarCount / 2;

            if (_isProcessing)
            {
                _pulseHalo.Width = 13 + 3.5 * (0.5 + 0.5 * Math.Sin(_wavePhase * 1.4));
                _pulseHalo.Height = _pulseHalo.Width;
                for (int i = 0; i < BarCount; i++)
                {
                    double wave = 0.5 + 0.5 * Math.Sin(_wavePhase - i * 0.52);
                    double env = 1.0 - (Math.Abs(i - center) / (double)(center + 2)) * 0.45;
                    _levelBars[i].Height = 6.0 + wave * 18.0 * env;
                    _levelBars[i].Background = _pal.AccentBg;
                }
                return;
            }

            // Fast attack, smooth natural decay for crisp live meter motion
            double coeff = _targetLevel > _smoothedLevel ? 0.55 : 0.18;
            _smoothedLevel += (_targetLevel - _smoothedLevel) * coeff;

            _historyTick++;
            if ((_historyTick % 2) == 0)
            {
                // Propagate ripple outward from center index
                for (int d = center; d >= 1; d--)
                {
                    _levelHistory[center - d] = _levelHistory[center - d + 1] * 0.90;
                    _levelHistory[center + d] = _levelHistory[center + d - 1] * 0.90;
                }
                _levelHistory[center] = _smoothedLevel;
            }

            // Pulse red recording halo subtly even during pauses, expand strongly on voice input
            double haloSize = 13.0 + Math.Max(0.15 * (0.5 + 0.5 * Math.Sin(_wavePhase * 0.8)), _smoothedLevel) * 5.0;
            _pulseHalo.Width = haloSize;
            _pulseHalo.Height = haloSize;

            Brush quietBrush = _pal.AccentBg;
            Brush activeBrush = ThemeService.BrushFromHex("#F97316");
            Brush peakBrush = ThemeService.BrushFromHex("#EF4444");

            for (int i = 0; i < BarCount; i++)
            {
                int dist = Math.Abs(i - center);
                double envelope = 1.0 - (dist / (double)(center + 2.0)) * 0.52;
                double idleBreathing = (0.08 + 0.06 * Math.Sin(_wavePhase + i * 0.55)) * envelope;
                double ripple = _levelHistory[i] * 0.65 + _smoothedLevel * envelope * 0.60;
                double harmonic = 0.08 * _smoothedLevel * Math.Sin(_wavePhase * 1.7 + i * 0.9);
                double amp = Math.Max(idleBreathing, Math.Min(1.0, ripple + harmonic));

                double h = 6.0 + amp * 32.0; // min 6px, max 38px
                _levelBars[i].Height = h;

                if (_smoothedLevel > 0.60 && dist <= 4) _levelBars[i].Background = peakBrush;
                else if (amp > 0.28) _levelBars[i].Background = activeBrush;
                else _levelBars[i].Background = quietBrush;
            }
        }
    }
}
