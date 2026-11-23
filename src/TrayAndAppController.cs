using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using WinForms = System.Windows.Forms;

namespace DesktopMemo
{
    public class TrayAndAppController
    {
        private const int HotkeyNewTask = 9001;
        private const int HotkeyToggleAll = 9002;
        private const int HotkeyShowPanel = 9003;
        private const int HotkeyNewSticky = 9004;

        public StorageService Storage { get; private set; }
        public AppData Data { get; private set; }
        public AppConfig Config { get; private set; }

        private readonly DispatcherTimer _saveDataTimer;
        private readonly DispatcherTimer _saveConfigTimer;
        private readonly DispatcherTimer _reloadWatcherTimer;
        private readonly DispatcherTimer _remoteSyncTimer;
        private readonly ReminderService _reminderService;
        private readonly Dictionary<string, StickyNoteWindow> _stickyWindows = new Dictionary<string, StickyNoteWindow>();

        private DashboardWindow _dashboardWindow;
        private WinForms.NotifyIcon _trayIcon;
        private WinForms.ToolStripMenuItem _trayAutoStartItem;
        private HwndSource _hotkeySource;
        private FileSystemWatcher _dataWatcher;
        private DateTime _lastSelfWriteUtc = DateTime.MinValue;
        private AiSortSnapshot _lastAiSnapshot;
        private MemoTask _lastDeletedTask;
        private int _lastDeletedIndex = -1;
        private UndoToastWindow _undoToast;
        private bool _allHiddenByUser = false;

        public bool CanUndoAiSort { get { return _lastAiSnapshot != null; } }
        public bool CanUndoDelete { get { return _lastDeletedTask != null; } }

        public TrayAndAppController(StorageService storage)
        {
            Storage = storage;
            Data = Storage.LoadData();
            Config = Storage.LoadConfig();

            _saveDataTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
            _saveDataTimer.Tick += delegate
            {
                _saveDataTimer.Stop();
                SaveDataMarkSelf();
            };

            _saveConfigTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
            _saveConfigTimer.Tick += delegate
            {
                _saveConfigTimer.Stop();
                Storage.SaveConfig(Config);
            };

            _reloadWatcherTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
            _reloadWatcherTimer.Tick += delegate
            {
                _reloadWatcherTimer.Stop();
                ReloadDataFromDiskExternal();
            };

            _remoteSyncTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            _remoteSyncTimer.Tick += async delegate
            {
                if (Config.RemoteSyncEnabled && !string.IsNullOrWhiteSpace(Config.RemoteServerUrl))
                {
                    await PullFromRemoteServerAsync();
                }
            };

            _reminderService = new ReminderService();
            _reminderService.GetData = delegate { return Data; };
            _reminderService.GetConfig = delegate { return Config; };
            _reminderService.SaveAndRefresh = delegate
            {
                SaveDataMarkSelf();
                RefreshAllViews();
            };
            _reminderService.RefreshUiTick = RefreshAllViews;
        }

        private void SaveDataMarkSelf()
        {
            _lastSelfWriteUtc = DateTime.UtcNow;
            Storage.SaveData(Data);
        }

        public void Start(bool startedFromAutoStart)
        {
            Config.AutoStart = Win32Native.IsAutoStartEnabled();

            InitializeTrayIcon();
            InitializeHotkeyHost();
            RefreshGlobalHotkeys();
            InitializeDataWatcher();

            ApplyDisplayModeVisibility();
            _reminderService.Start();
            _remoteSyncTimer.Start();

            if (!startedFromAutoStart && Config.DisplayMode == "panel")
            {
                ShowDashboardWindow();
            }
        }

        public void Shutdown()
        {
            _saveDataTimer.Stop();
            _saveConfigTimer.Stop();
            _reloadWatcherTimer.Stop();
            _remoteSyncTimer.Stop();
            _reminderService.Stop();

            if (_dataWatcher != null)
            {
                _dataWatcher.EnableRaisingEvents = false;
                _dataWatcher.Dispose();
                _dataWatcher = null;
            }

            SaveDataMarkSelf();
            Storage.SaveConfig(Config);

            UnregisterAllHotkeys();
            if (_hotkeySource != null)
            {
                _hotkeySource.Dispose();
                _hotkeySource = null;
            }
            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
                _trayIcon = null;
            }

            foreach (StickyNoteWindow w in _stickyWindows.Values.ToList())
            {
                try { w.Close(); } catch { }
            }
            _stickyWindows.Clear();

            CloseUndoToast();

            if (_dashboardWindow != null)
            {
                try { _dashboardWindow.Close(); } catch { }
                _dashboardWindow = null;
            }
        }

        private void InitializeDataWatcher()
        {
            try
            {
                Storage.EnsureDirectories();
                _dataWatcher = new FileSystemWatcher(Storage.DataDirectory, "data.json");
                _dataWatcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName;
                _dataWatcher.Changed += OnExternalDataChanged;
                _dataWatcher.Created += OnExternalDataChanged;
                _dataWatcher.EnableRaisingEvents = true;
            }
            catch { }
        }

        private void OnExternalDataChanged(object sender, FileSystemEventArgs e)
        {
            if ((DateTime.UtcNow - _lastSelfWriteUtc).TotalMilliseconds < 1400) return;
            if (Application.Current != null && !Application.Current.Dispatcher.HasShutdownStarted)
            {
                Application.Current.Dispatcher.BeginInvoke(new Action(delegate
                {
                    _reloadWatcherTimer.Stop();
                    _reloadWatcherTimer.Start();
                }));
            }
        }

        private void ReloadDataFromDiskExternal()
        {
            if ((DateTime.UtcNow - _lastSelfWriteUtc).TotalMilliseconds < 1400) return;
            Data = Storage.LoadData();
            ApplyDisplayModeVisibility();
            RefreshAllViews();
        }

        public void ScheduleSave()
        {
            _saveDataTimer.Stop();
            _saveDataTimer.Start();
        }

        public void ScheduleConfigSave()
        {
            _saveConfigTimer.Stop();
            _saveConfigTimer.Start();
        }

        public void SaveAllNow()
        {
            _saveDataTimer.Stop();
            _saveConfigTimer.Stop();
            SaveDataMarkSelf();
            Storage.SaveConfig(Config);
        }

        public void ApplyAppearanceToAllWindows()
        {
            if (_dashboardWindow != null)
            {
                _dashboardWindow.ApplyThemeAndRefreshAll();
            }
            foreach (StickyNoteWindow w in _stickyWindows.Values)
            {
                w.ApplyThemeAndContent();
            }
        }

        public void RefreshAllViews()
        {
            if (_dashboardWindow != null && _dashboardWindow.IsVisible)
            {
                _dashboardWindow.ApplyThemeAndRefreshAll();
            }
            else if (_dashboardWindow != null)
            {
                _dashboardWindow.RefreshTaskViews();
            }

            foreach (StickyNoteWindow w in _stickyWindows.Values)
            {
                w.RefreshTasksOnly();
            }
        }

        public void SetDisplayMode(string mode)
        {
            if (mode != "panel" && mode != "sticky" && mode != "both") mode = "both";
            Config.DisplayMode = mode;
            if (mode == "panel" || mode == "both") Config.PanelVisible = true;
            _allHiddenByUser = false;

            if ((mode == "sticky" || mode == "both") && Data.StickyNotes.Count == 0)
            {
                var starter = new StickyNote();
                starter.Title = "桌面便签";
                starter.ShowScratchpad = false;
                Data.StickyNotes.Add(starter);
                SaveDataMarkSelf();
            }
            else if (mode == "sticky" || mode == "both")
            {
                bool anyVisible = Data.StickyNotes.Any(n => n.Visible);
                if (!anyVisible && Data.StickyNotes.Count > 0)
                {
                    Data.StickyNotes[0].Visible = true;
                    SaveDataMarkSelf();
                }
            }

            ApplyDisplayModeVisibility();
            ScheduleConfigSave();
        }

        public void ApplyDisplayModeVisibility()
        {
            if (_allHiddenByUser)
            {
            if (_dashboardWindow != null) _dashboardWindow.Hide();
                foreach (StickyNoteWindow w in _stickyWindows.Values) w.Hide();
                return;
            }

            bool showPanel = (Config.DisplayMode == "panel" || Config.DisplayMode == "both") && Config.PanelVisible;
            bool showStickies = Config.DisplayMode == "sticky" || Config.DisplayMode == "both";

            if (showPanel)
            {
                EnsureDashboardWindow();
                _dashboardWindow.Show();
                _dashboardWindow.ApplyPinModeNow();
                _dashboardWindow.ApplyThemeAndRefreshAll();
            }
            else if (_dashboardWindow != null)
            {
                _dashboardWindow.Hide();
            }

            SyncStickyNoteWindows(showStickies);
        }

        public void ShowDashboardWindow()
        {
            _allHiddenByUser = false;
            Config.PanelVisible = true;
            if (Config.DisplayMode == "sticky")
            {
                Config.DisplayMode = "both";
            }
            EnsureDashboardWindow();
            _dashboardWindow.Show();
            _dashboardWindow.ApplyPinModeNow();
            _dashboardWindow.ApplyThemeAndRefreshAll();
            _dashboardWindow.Activate();
            ScheduleConfigSave();
        }

        public void HideDashboardWindow()
        {
            Config.PanelVisible = false;
            if (_dashboardWindow != null) _dashboardWindow.Hide();
            ScheduleConfigSave();
        }

        public void ToggleShowHideAll()
        {
            _allHiddenByUser = !_allHiddenByUser;
            ApplyDisplayModeVisibility();
        }

        public void WakeupFromSecondInstance()
        {
            _allHiddenByUser = false;
            ShowDashboardWindow();
            SyncStickyNoteWindows(Config.DisplayMode == "sticky" || Config.DisplayMode == "both");
        }

        private void EnsureDashboardWindow()
        {
            if (_dashboardWindow == null)
            {
                _dashboardWindow = new DashboardWindow(this);
            }
        }

        private void SyncStickyNoteWindows(bool allowShow)
        {
            var validIds = new HashSet<string>(Data.StickyNotes.Select(n => n.Id));
            foreach (string existingId in _stickyWindows.Keys.ToList())
            {
                if (!validIds.Contains(existingId))
                {
                    try { _stickyWindows[existingId].Close(); } catch { }
                    _stickyWindows.Remove(existingId);
                }
            }

            for (int i = 0; i < Data.StickyNotes.Count; i++)
            {
                StickyNote note = Data.StickyNotes[i];
                bool shouldShow = allowShow && note.Visible;
                StickyNoteWindow win;
                if (!_stickyWindows.TryGetValue(note.Id, out win))
                {
                    if (!shouldShow) continue;
                    win = new StickyNoteWindow(note, this);
                    _stickyWindows[note.Id] = win;
                }

                if (shouldShow)
                {
                    if (!win.IsVisible) win.Show();
                    win.ApplyPinModeNow();
                    win.ApplyThemeAndContent();
                }
                else
                {
                    if (win.IsVisible) win.Hide();
                }
            }
        }

        public StickyNote CreateNewStickyNote(string filterMode)
        {
            return CreateNewStickyNote(filterMode, false);
        }

        public StickyNote CreateNewStickyNote(string filterMode, bool showScratchpad)
        {
            int offset = (Data.StickyNotes.Count % 6) * 28;
            var note = new StickyNote();
            note.FilterMode = string.IsNullOrEmpty(filterMode) ? "custom" : filterMode;
            note.ShowScratchpad = showScratchpad;
            note.Left = 60 + offset;
            note.Top = 80 + offset;
            note.Visible = true;

            if (note.FilterMode == "all")
            {
                note.Title = "全部日程清单";
                note.Theme = "notion_light";
            }
            else if (note.FilterMode == "q1")
            {
                note.Title = "Q1 重要且紧急";
                note.Theme = "rose_flat";
            }
            else if (note.FilterMode == "q2")
            {
                note.Title = "Q2 重要不紧急";
                note.Theme = "nord_slate";
            }
            else if (note.FilterMode == "q3")
            {
                note.Title = "Q3 紧急不重要";
                note.Theme = "claude_warm";
            }
            else if (note.FilterMode == "q4")
            {
                note.Title = "Q4 稍后清理";
                note.Theme = "matcha_flat";
            }
            else
            {
                note.Title = "桌面便签 " + (Data.StickyNotes.Count + 1);
                note.Theme = ThemeService.ThemeKeys[Data.StickyNotes.Count % ThemeService.ThemeKeys.Length];
            }

            Data.StickyNotes.Add(note);
            if (Config.DisplayMode == "panel") Config.DisplayMode = "both";
            _allHiddenByUser = false;

            SaveDataMarkSelf();
            ScheduleConfigSave();
            ApplyDisplayModeVisibility();
            return note;
        }

        public void PinOrShowQuadrantSticky(int quadrant)
        {
            string mode = "q" + MemoTask.ClampQuadrant(quadrant);
            StickyNote existing = Data.StickyNotes.FirstOrDefault(n => n.FilterMode == mode);
            if (existing != null)
            {
                existing.Visible = true;
                if (Config.DisplayMode == "panel") Config.DisplayMode = "both";
                _allHiddenByUser = false;
                SaveDataMarkSelf();
                ApplyDisplayModeVisibility();
            }
            else
            {
                CreateNewStickyNote(mode, false);
            }
        }

        public void SetStickyNoteVisible(string noteId, bool visible)
        {
            StickyNote note = Data.StickyNotes.FirstOrDefault(n => n.Id == noteId);
            if (note == null) return;
            note.Visible = visible;
            SaveDataMarkSelf();
            ApplyDisplayModeVisibility();
            RefreshAllViews();
        }

        public void SetAllStickiesVisible(bool visible)
        {
            if (visible && Data.StickyNotes.Count == 0)
            {
                CreateNewStickyNote("custom", false);
                return;
            }
            for (int i = 0; i < Data.StickyNotes.Count; i++)
            {
                Data.StickyNotes[i].Visible = visible;
            }
            if (visible && Config.DisplayMode == "panel") Config.DisplayMode = "both";
            _allHiddenByUser = false;
            SaveDataMarkSelf();
            ApplyDisplayModeVisibility();
            RefreshAllViews();
        }

        public void SetAllPinToDesktop()
        {
            Config.PanelPinMode = "desktop";
            if (_dashboardWindow != null) _dashboardWindow.ApplyPinModeNow();
            for (int i = 0; i < Data.StickyNotes.Count; i++)
            {
                Data.StickyNotes[i].PinMode = "desktop";
            }
            foreach (StickyNoteWindow w in _stickyWindows.Values)
            {
                w.ApplyPinModeNow();
            }
            SaveAllNow();
        }

        // Bug 1 fix: ONLY lock/unlock sticky notes, NEVER touch Dashboard PanelLocked!
        public void SetAllStickiesLocked(bool locked)
        {
            for (int i = 0; i < Data.StickyNotes.Count; i++)
            {
                Data.StickyNotes[i].Locked = locked;
            }
            foreach (StickyNoteWindow w in _stickyWindows.Values)
            {
                w.ApplyThemeAndContent();
            }
            if (_dashboardWindow != null) _dashboardWindow.RefreshStickiesManagerView();
            SaveDataMarkSelf();
        }

        public void SetAllLocked(bool locked)
        {
            Config.PanelLocked = locked;
            SetAllStickiesLocked(locked);
            if (_dashboardWindow != null) _dashboardWindow.ApplyThemeAndRefreshAll();
            SaveAllNow();
        }

        public void DeleteStickyNote(string noteId)
        {
            Data.StickyNotes.RemoveAll(n => n.Id == noteId);
            Data.Tasks.RemoveAll(t => t.NoteId == noteId);
            StickyNoteWindow win;
            if (_stickyWindows.TryGetValue(noteId, out win))
            {
                _stickyWindows.Remove(noteId);
                try { win.Close(); } catch { }
            }
            SaveDataMarkSelf();
            RefreshAllViews();
        }

        public void AddTask(MemoTask task)
        {
            if (task == null) return;
            task.Quadrant = MemoTask.NormalizeQuadrant(task.Quadrant);
            Data.Tasks.Add(task);
            SaveDataMarkSelf();
            RefreshAllViews();
            _reminderService.CheckDueReminders();
        }

        public void AddTasksBatch(List<MemoTask> tasks)
        {
            if (tasks == null || tasks.Count == 0) return;
            for (int i = 0; i < tasks.Count; i++)
            {
                tasks[i].Quadrant = MemoTask.NormalizeQuadrant(tasks[i].Quadrant);
                Data.Tasks.Add(tasks[i]);
            }
            SaveDataMarkSelf();
            RefreshAllViews();
            _reminderService.CheckDueReminders();
        }

        public void ToggleTaskCompleted(string taskId, bool completed)
        {
            MemoTask task = Data.Tasks.FirstOrDefault(t => t.Id == taskId);
            if (task == null) return;
            task.Completed = completed;
            task.CompletedAtIso = completed ? DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") : "";
            SaveDataMarkSelf();
            RefreshAllViews();
        }

        public void MoveTaskQuadrant(string taskId, int quadrant)
        {
            MemoTask task = Data.Tasks.FirstOrDefault(t => t.Id == taskId);
            if (task == null) return;
            task.Quadrant = MemoTask.NormalizeQuadrant(quadrant);
            SaveDataMarkSelf();
            RefreshAllViews();
        }

        public void RequestDeleteTask(string taskId, Window owner)
        {
            MemoTask task = Data.Tasks.FirstOrDefault(t => t.Id == taskId);
            if (task == null) return;
            string title = string.IsNullOrWhiteSpace(task.Title) ? "未命名日程" : task.Title;
            string msg = "确定要删除日程「" + title + "」吗？";
            MessageBoxResult res = (owner != null && owner.IsVisible)
                ? MessageBox.Show(owner, msg, "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Question)
                : MessageBox.Show(msg, "确认删除", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (res == MessageBoxResult.Yes)
            {
                DeleteTaskWithUndo(taskId, true);
            }
        }

        public void DeleteTask(string taskId)
        {
            DeleteTaskWithUndo(taskId, false);
        }

        public void DeleteTaskWithUndo(string taskId, bool showToast)
        {
            int idx = Data.Tasks.FindIndex(t => t.Id == taskId);
            if (idx < 0) return;

            _lastDeletedTask = Data.Tasks[idx];
            _lastDeletedIndex = idx;
            Data.Tasks.RemoveAt(idx);
            SaveDataMarkSelf();
            RefreshAllViews();

            if (showToast)
            {
                ShowUndoToast(_lastDeletedTask.Title);
            }
        }

        public void UndoLastDeletedTask()
        {
            if (_lastDeletedTask == null) return;
            MemoTask restore = _lastDeletedTask;
            int idx = _lastDeletedIndex;
            _lastDeletedTask = null;
            _lastDeletedIndex = -1;

            if (!Data.Tasks.Any(t => t.Id == restore.Id))
            {
                int insertIdx = idx < 0 ? Data.Tasks.Count : Math.Min(idx, Data.Tasks.Count);
                Data.Tasks.Insert(insertIdx, restore);
            }
            SaveDataMarkSelf();
            RefreshAllViews();
            _reminderService.CheckDueReminders();
        }

        private void ShowUndoToast(string title)
        {
            CloseUndoToast();
            var toast = new UndoToastWindow(title, Config.PanelTheme);
            toast.UndoClicked += delegate { UndoLastDeletedTask(); };
            toast.Closed += delegate
            {
                if (_undoToast == toast) _undoToast = null;
            };
            _undoToast = toast;
            toast.Show();
            toast.PositionBottomRight();
        }

        private void CloseUndoToast()
        {
            if (_undoToast != null)
            {
                UndoToastWindow prev = _undoToast;
                _undoToast = null;
                try { prev.Close(); } catch { }
            }
        }

        public void ClearCompletedTasks()
        {
            int removed = Data.Tasks.RemoveAll(t => t.Completed);
            if (removed > 0)
            {
                SaveDataMarkSelf();
                RefreshAllViews();
            }
        }

        public bool OpenNewTaskDialog(MemoTask template, Window owner)
        {
            MemoTask draft = template ?? new MemoTask();
            var dlg = new TaskEditDialog(draft, true, Data.StickyNotes, Config.PanelTheme, Config.UiScale, Config.PanelFontSize);
            if (owner != null && owner.IsVisible) dlg.Owner = owner;
            if (dlg.ShowDialog() == true)
            {
                AddTask(draft);
                return true;
            }
            return false;
        }

        public void OpenEditTaskDialog(MemoTask task, Window owner)
        {
            if (task == null) return;
            var dlg = new TaskEditDialog(task, false, Data.StickyNotes, Config.PanelTheme, Config.UiScale, Config.PanelFontSize);
            if (owner != null && owner.IsVisible) dlg.Owner = owner;
            if (dlg.ShowDialog() == true)
            {
                if (dlg.DeleteRequested)
                {
                    DeleteTaskWithUndo(task.Id, true);
                }
                else
                {
                    SaveDataMarkSelf();
                    RefreshAllViews();
                    _reminderService.CheckDueReminders();
                }
            }
        }

        public void OpenChatExtractDialog(string targetNoteId, Window owner)
        {
            var dlg = new ChatExtractDialog(this, targetNoteId);
            if (owner != null && owner.IsVisible) dlg.Owner = owner;
            dlg.ShowDialog();
        }

        public async Task<AiSortOutcome> RunAiSortAsync()
        {
            List<MemoTask> active = Data.Tasks.Where(t => !t.Completed).ToList();
            var snapshot = new AiSortSnapshot { PreviousSortBy = Config.SortBy };
            for (int i = 0; i < active.Count; i++)
            {
                snapshot.Items.Add(new AiSortSnapshotItem
                {
                    Id = active[i].Id,
                    Quadrant = active[i].Quadrant,
                    AiScore = active[i].AiScore,
                    AiReason = active[i].AiReason
                });
            }

            AiSortOutcome outcome = await AiSortService.SortTasksAsync(Config, active, DateTime.Now);
            if (!outcome.Success) return outcome;

            _lastAiSnapshot = snapshot;
            var byId = active.ToDictionary(t => t.Id, t => t);
            for (int i = 0; i < outcome.Items.Count; i++)
            {
                AiSortItem res = outcome.Items[i];
                MemoTask target;
                if (byId.TryGetValue(res.Id, out target))
                {
                    target.Quadrant = res.Quadrant;
                    target.AiScore = res.Score;
                    target.AiReason = res.Reason ?? "";
                }
            }

            Config.SortBy = "ai";
            SaveAllNow();
            RefreshAllViews();
            return outcome;
        }

        public void UndoLastAiSort()
        {
            if (_lastAiSnapshot == null) return;
            var byId = Data.Tasks.ToDictionary(t => t.Id, t => t);
            for (int i = 0; i < _lastAiSnapshot.Items.Count; i++)
            {
                AiSortSnapshotItem item = _lastAiSnapshot.Items[i];
                MemoTask target;
                if (byId.TryGetValue(item.Id, out target))
                {
                    target.Quadrant = item.Quadrant;
                    target.AiScore = item.AiScore;
                    target.AiReason = item.AiReason;
                }
            }
            Config.SortBy = _lastAiSnapshot.PreviousSortBy;
            _lastAiSnapshot = null;
            SaveAllNow();
            RefreshAllViews();
        }

        public List<MemoTask> SortTaskList(List<MemoTask> tasks)
        {
            if (tasks == null) return new List<MemoTask>();
            string sortBy = Config.SortBy ?? "ai";

            return tasks.OrderBy(t => t.Completed ? 1 : 0)
                .ThenBy(t =>
                {
                    if (sortBy == "ai") return -t.AiScore;
                    if (sortBy == "quadrant") return t.Quadrant == 0 ? 9 : t.Quadrant;
                    return 0;
                })
                .ThenBy(t =>
                {
                    if (sortBy == "ai" || sortBy == "quadrant") return t.Quadrant == 0 ? 9 : t.Quadrant;
                    return 0;
                })
                .ThenBy(t =>
                {
                    if (sortBy == "created") return -t.GetCreatedAt().Ticks;
                    DateTime? ddl = t.GetDeadline();
                    return ddl.HasValue ? ddl.Value.Ticks : DateTime.MaxValue.Ticks;
                })
                .ThenByDescending(t => t.GetCreatedAt())
                .ToList();
        }

        public bool SetAutoStart(bool enabled, out string error)
        {
            string exePath = Process.GetCurrentProcess().MainModule.FileName;
            bool ok = Win32Native.SetAutoStart(enabled, exePath, out error);
            if (ok)
            {
                Config.AutoStart = enabled;
                if (_trayAutoStartItem != null) _trayAutoStartItem.Checked = enabled;
                ScheduleConfigSave();
            }
            return ok;
        }

        public void OpenDataDirectory()
        {
            try
            {
                Storage.EnsureDirectories();
                Process.Start("explorer.exe", Storage.DataDirectory);
            }
            catch (Exception ex)
            {
                MessageBox.Show("无法打开数据目录：" + ex.Message, "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        public void LaunchLocalWebServerAndOpenBrowser()
        {
            try
            {
                Storage.SaveConfig(Config);
                string srvExe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DesktopMemoServer.exe");
                if (!File.Exists(srvExe))
                {
                    MessageBox.Show("未找到 DesktopMemoServer.exe，请先运行 build.bat 编译。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                int port = Config.ServerPort > 0 ? Config.ServerPort : 8765;
                Process.Start(new ProcessStartInfo
                {
                    FileName = srvExe,
                    Arguments = "--port=" + port,
                    WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory,
                    UseShellExecute = true
                });
                Process.Start(new ProcessStartInfo
                {
                    FileName = "http://127.0.0.1:" + port + "/",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show("启动本地网页后端失败：" + ex.Message, "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async Task<string> LoginRemoteServerAsync(HttpClient client, string baseUrl, string password)
        {
            var ser = new JavaScriptSerializer();
            string loginJson = ser.Serialize(new Dictionary<string, object> { { "password", password ?? "" } });
            using (var content = new StringContent(loginJson, Encoding.UTF8, "application/json"))
            using (HttpResponseMessage resp = await client.PostAsync(baseUrl + "/api/login", content))
            {
                string body = await resp.Content.ReadAsStringAsync();
                if (!resp.IsSuccessStatusCode)
                {
                    throw new InvalidOperationException("登录失败(" + (int)resp.StatusCode + "): " + body);
                }
                var dict = ser.Deserialize<Dictionary<string, object>>(body);
                if (dict != null && dict.ContainsKey("token") && dict["token"] != null)
                {
                    return dict["token"].ToString();
                }
                throw new InvalidOperationException("服务器未返回登录 token");
            }
        }

        public async Task<string> PullFromRemoteServerAsync()
        {
            string baseUrl = (Config.RemoteServerUrl ?? "").Trim().TrimEnd('/');
            if (string.IsNullOrEmpty(baseUrl)) return "⚠ 请先填写远程 Server 地址。";

            try
            {
                using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) })
                {
                    string token = await LoginRemoteServerAsync(client, baseUrl, Config.RemoteServerPassword);
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
                    string stateBody = await client.GetStringAsync(baseUrl + "/api/state");

                    var ser = new JavaScriptSerializer { MaxJsonLength = 32 * 1024 * 1024 };
                    AppData pulled = ser.Deserialize<AppData>(stateBody);
                    if (pulled != null && pulled.Tasks != null)
                    {
                        if (pulled.StickyNotes == null) pulled.StickyNotes = Data.StickyNotes;
                        Data.Tasks = pulled.Tasks;
                        if (pulled.StickyNotes.Count > 0) Data.StickyNotes = pulled.StickyNotes;
                        SaveDataMarkSelf();
                        ApplyDisplayModeVisibility();
                        RefreshAllViews();
                        return string.Format("✓ 已从服务器同步 {0} 条任务 ({1})", Data.Tasks.Count, DateTime.Now.ToString("HH:mm:ss"));
                    }
                    return "⚠ 服务器返回数据为空。";
                }
            }
            catch (Exception ex)
            {
                return "⚠ 同步失败：" + ex.Message;
            }
        }

        public async Task<string> PushToRemoteServerAsync()
        {
            string baseUrl = (Config.RemoteServerUrl ?? "").Trim().TrimEnd('/');
            if (string.IsNullOrEmpty(baseUrl)) return "⚠ 请先填写远程 Server 地址。";

            try
            {
                using (var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) })
                {
                    string token = await LoginRemoteServerAsync(client, baseUrl, Config.RemoteServerPassword);
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

                    var ser = new JavaScriptSerializer { MaxJsonLength = 32 * 1024 * 1024 };
                    string json = ser.Serialize(Data);
                    using (var content = new StringContent(json, Encoding.UTF8, "application/json"))
                    using (HttpResponseMessage resp = await client.PostAsync(baseUrl + "/api/sync/replace", content))
                    {
                        if (!resp.IsSuccessStatusCode)
                        {
                            return "⚠ 推送失败 HTTP " + (int)resp.StatusCode;
                        }
                        return string.Format("✓ 已推送本地 {0} 条任务到服务器 ({1})", Data.Tasks.Count, DateTime.Now.ToString("HH:mm:ss"));
                    }
                }
            }
            catch (Exception ex)
            {
                return "⚠ 推送失败：" + ex.Message;
            }
        }

        private void InitializeTrayIcon()
        {
            _trayIcon = new WinForms.NotifyIcon();
            _trayIcon.Icon = ThemeService.CreateAppTrayIcon();
            _trayIcon.Text = "桌面备忘录 (双击呼出面板)";
            _trayIcon.Visible = true;
            _trayIcon.DoubleClick += delegate { ShowDashboardWindow(); };

            var menu = new WinForms.ContextMenuStrip();
            menu.Items.Add("打开整合主面板 (Ctrl+Alt+P)", null, delegate { ShowDashboardWindow(); });
            menu.Items.Add("新建日程 / DDL (Ctrl+Alt+N)", null, delegate { OpenNewTaskDialog(new MemoTask(), _dashboardWindow); });
            menu.Items.Add("新建独立便利贴 (Ctrl+Alt+S)", null, delegate { CreateNewStickyNote("custom", false); });
            menu.Items.Add("💬 AI 微信聊天提取任务", null, delegate { OpenChatExtractDialog("", _dashboardWindow); });
            menu.Items.Add("✨ AI 智能排序", null, delegate
            {
                ShowDashboardWindow();
                _dashboardWindow.TriggerAiSortFromOutside();
            });
            menu.Items.Add(new WinForms.ToolStripSeparator());

            var modeMenu = new WinForms.ToolStripMenuItem("桌面展示模式");
            modeMenu.DropDownItems.Add("大面板模式", null, delegate { SetDisplayMode("panel"); });
            modeMenu.DropDownItems.Add("独立便利贴模式", null, delegate { SetDisplayMode("sticky"); });
            modeMenu.DropDownItems.Add("混合共存模式", null, delegate { SetDisplayMode("both"); });
            menu.Items.Add(modeMenu);

            menu.Items.Add("显示 / 隐藏全部 (Ctrl+Alt+M)", null, delegate { ToggleShowHideAll(); });
            menu.Items.Add("全部贴在桌面", null, delegate { SetAllPinToDesktop(); });
            menu.Items.Add("锁定全部独立便签", null, delegate { SetAllStickiesLocked(true); });
            menu.Items.Add("解锁全部独立便签", null, delegate { SetAllStickiesLocked(false); });
            menu.Items.Add(new WinForms.ToolStripSeparator());

            _trayAutoStartItem = new WinForms.ToolStripMenuItem("Windows 开机自动启动");
            _trayAutoStartItem.CheckOnClick = false;
            _trayAutoStartItem.Checked = Win32Native.IsAutoStartEnabled();
            _trayAutoStartItem.Click += delegate
            {
                bool target = !_trayAutoStartItem.Checked;
                string err;
                if (!SetAutoStart(target, out err))
                {
                    MessageBox.Show("设置开机自启失败：" + err, "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                else if (_dashboardWindow != null && _dashboardWindow.IsVisible)
                {
                    _dashboardWindow.ApplyThemeAndRefreshAll();
                }
            };
            menu.Items.Add(_trayAutoStartItem);

            menu.Items.Add("启动本地网页后端", null, delegate { LaunchLocalWebServerAndOpenBrowser(); });
            menu.Items.Add("打开便携数据目录", null, delegate { OpenDataDirectory(); });
            menu.Items.Add("立即备份数据", null, delegate
            {
                string path = Storage.CreateBackup();
                if (!string.IsNullOrEmpty(path))
                {
                    _trayIcon.ShowBalloonTip(2500, "桌面备忘录", "已备份到：" + path, WinForms.ToolTipIcon.Info);
                }
            });
            menu.Items.Add(new WinForms.ToolStripSeparator());
            menu.Items.Add("退出桌面备忘录", null, delegate
            {
                Shutdown();
                Application.Current.Shutdown();
            });

            _trayIcon.ContextMenuStrip = menu;
        }

        private void InitializeHotkeyHost()
        {
            var parameters = new HwndSourceParameters("DesktopMemoHotkeyHost")
            {
                Width = 0,
                Height = 0,
                WindowStyle = unchecked((int)0x80000000)
            };
            _hotkeySource = new HwndSource(parameters);
            _hotkeySource.AddHook(HotkeyWndProc);
        }

        public void RefreshGlobalHotkeys()
        {
            UnregisterAllHotkeys();
            if (!Config.HotkeysEnabled || _hotkeySource == null) return;

            IntPtr hwnd = _hotkeySource.Handle;
            uint mods = Win32Native.MOD_CONTROL | Win32Native.MOD_ALT | Win32Native.MOD_NOREPEAT;
            Win32Native.RegisterHotKey(hwnd, HotkeyNewTask, mods, 0x4E);   // N
            Win32Native.RegisterHotKey(hwnd, HotkeyToggleAll, mods, 0x4D); // M
            Win32Native.RegisterHotKey(hwnd, HotkeyShowPanel, mods, 0x50); // P
            Win32Native.RegisterHotKey(hwnd, HotkeyNewSticky, mods, 0x53); // S
        }

        private void UnregisterAllHotkeys()
        {
            if (_hotkeySource == null) return;
            IntPtr hwnd = _hotkeySource.Handle;
            Win32Native.UnregisterHotKey(hwnd, HotkeyNewTask);
            Win32Native.UnregisterHotKey(hwnd, HotkeyToggleAll);
            Win32Native.UnregisterHotKey(hwnd, HotkeyShowPanel);
            Win32Native.UnregisterHotKey(hwnd, HotkeyNewSticky);
        }

        private IntPtr HotkeyWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == Win32Native.WM_HOTKEY)
            {
                int id = wParam.ToInt32();
                if (id == HotkeyNewTask)
                {
                    OpenNewTaskDialog(new MemoTask(), _dashboardWindow);
                    handled = true;
                }
                else if (id == HotkeyToggleAll)
                {
                    ToggleShowHideAll();
                    handled = true;
                }
                else if (id == HotkeyShowPanel)
                {
                    ShowDashboardWindow();
                    handled = true;
                }
                else if (id == HotkeyNewSticky)
                {
                    CreateNewStickyNote("custom", false);
                    handled = true;
                }
            }
            return IntPtr.Zero;
        }
    }
}
