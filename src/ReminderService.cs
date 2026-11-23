using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Threading;

namespace DesktopMemo
{
    public class ReminderService
    {
        private readonly DispatcherTimer _timer;
        private readonly List<ReminderToastWindow> _openToasts = new List<ReminderToastWindow>();
        private int _tickCount = 0;

        public Func<AppData> GetData { get; set; }
        public Func<AppConfig> GetConfig { get; set; }
        public Action SaveAndRefresh { get; set; }
        public Action RefreshUiTick { get; set; }

        public ReminderService()
        {
            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromSeconds(10);
            _timer.Tick += OnTimerTick;
        }

        public void Start()
        {
            _timer.Start();
            CheckDueReminders();
        }

        public void Stop()
        {
            _timer.Stop();
            CloseAllToasts();
        }

        public void CloseAllToasts()
        {
            for (int i = _openToasts.Count - 1; i >= 0; i--)
            {
                try { _openToasts[i].Close(); } catch { }
            }
            _openToasts.Clear();
        }

        private void OnTimerTick(object sender, EventArgs e)
        {
            _tickCount++;
            if (_tickCount % 3 == 0 && RefreshUiTick != null)
            {
                RefreshUiTick();
            }
            CheckDueReminders();
        }

        public void CheckDueReminders()
        {
            if (GetData == null || GetConfig == null) return;
            AppData data = GetData();
            AppConfig cfg = GetConfig();
            if (data == null || data.Tasks == null) return;

            DateTime now = DateTime.Now;
            bool dirty = false;

            for (int i = 0; i < data.Tasks.Count; i++)
            {
                MemoTask task = data.Tasks[i];
                if (task.Completed || task.Reminded) continue;
                DateTime? remindAt = task.GetRemindAt();
                if (!remindAt.HasValue) continue;
                if (remindAt.Value <= now)
                {
                    if (!IsToastOpenFor(task.Id))
                    {
                        task.Reminded = true;
                        dirty = true;
                        ShowToast(task, cfg != null && cfg.ReminderSoundEnabled);
                    }
                }
            }

            if (dirty && SaveAndRefresh != null)
            {
                SaveAndRefresh();
            }
        }

        private bool IsToastOpenFor(string taskId)
        {
            for (int i = 0; i < _openToasts.Count; i++)
            {
                if (_openToasts[i].TaskId == taskId) return true;
            }
            return false;
        }

        private void ShowToast(MemoTask task, bool playSound)
        {
            var toast = new ReminderToastWindow(task, playSound);
            toast.CompleteClicked += delegate(string id)
            {
                AppData data = GetData();
                if (data != null)
                {
                    MemoTask target = FindTask(data, id);
                    if (target != null)
                    {
                        target.Completed = true;
                        target.CompletedAtIso = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                        if (SaveAndRefresh != null) SaveAndRefresh();
                    }
                }
            };
            toast.SnoozeClicked += delegate(string id, int mins)
            {
                AppData data = GetData();
                if (data != null)
                {
                    MemoTask target = FindTask(data, id);
                    if (target != null)
                    {
                        target.Reminded = false;
                        target.RemindAtIso = MemoTask.FormatIso(DateTime.Now.AddMinutes(mins));
                        if (SaveAndRefresh != null) SaveAndRefresh();
                    }
                }
            };
            toast.Closed += delegate
            {
                _openToasts.Remove(toast);
                RepositionToasts();
            };

            _openToasts.Add(toast);
            toast.Show();
            toast.UpdateLayout();
            RepositionToasts();
        }

        private void RepositionToasts()
        {
            Rect work = SystemParameters.WorkArea;
            double bottomY = work.Bottom - 12;
            for (int i = _openToasts.Count - 1; i >= 0; i--)
            {
                ReminderToastWindow t = _openToasts[i];
                double h = t.ActualHeight > 20 ? t.ActualHeight : 160;
                double w = t.ActualWidth > 20 ? t.ActualWidth : 360;
                t.Left = work.Right - w - 12;
                t.Top = bottomY - h;
                bottomY = t.Top - 8;
            }
        }

        private static MemoTask FindTask(AppData data, string id)
        {
            for (int i = 0; i < data.Tasks.Count; i++)
            {
                if (data.Tasks[i].Id == id) return data.Tasks[i];
            }
            return null;
        }
    }
}
