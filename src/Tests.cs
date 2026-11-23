using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DesktopMemo
{
    public static class Tests
    {
        private static int _passed = 0;
        private static int _failed = 0;
        private static readonly StringBuilder _log = new StringBuilder();

        public static int RunAll(string reportPath)
        {
            _passed = 0;
            _failed = 0;
            _log.Clear();

            RunTest("StorageRoundTripAndUnclassified", TestStorageRoundTrip);
            RunTest("Bug1StickyLockDoesNotLockPanel", TestBug1StickyLockScope);
            RunTest("DeadlineCountdownMath", TestDeadlineCountdownMath);
            RunTest("AiEndpointNormalization", TestAiEndpointNormalization);
            RunTest("AiJsonAndChatExtractParser", TestAiJsonParser);
            RunTest("ThemeGlassScaleAndSpringEase", TestThemeAndQuadrantPalettes);
            RunTest("AutoStartRegistryRead", TestAutoStartRegistryRead);
            RunTest("WpfUiSmokeConstruction", TestWpfUiSmokeConstruction);

            string summary = string.Format("SUMMARY: passed={0}, failed={1}\r\n", _passed, _failed);
            _log.Append(summary);

            if (!string.IsNullOrWhiteSpace(reportPath))
            {
                try
                {
                    File.WriteAllText(reportPath, _log.ToString(), new UTF8Encoding(false));
                }
                catch { }
            }

            return _failed == 0 ? 0 : 1;
        }

        private static void RunTest(string name, Action body)
        {
            try
            {
                body();
                _passed++;
                _log.AppendLine("[PASS] " + name);
            }
            catch (Exception ex)
            {
                _failed++;
                _log.AppendLine("[FAIL] " + name + " -> " + ex.Message);
                _log.AppendLine(ex.StackTrace);
            }
        }

        private static void AssertTrue(bool cond, string message)
        {
            if (!cond) throw new Exception("Assertion failed: " + message);
        }

        private static void AssertEqual(object expected, object actual, string message)
        {
            if (!object.Equals(expected, actual))
            {
                throw new Exception(string.Format("{0} (expected={1}, actual={2})", message, expected, actual));
            }
        }

        private static void TestStorageRoundTrip()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "DesktopMemo_Test_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            try
            {
                var storage = new StorageService(tempDir);
                var data = new AppData();

                // Default MemoTask must be Quadrant 0 (unclassified) for Bug 4
                var tUnclassified = new MemoTask
                {
                    Title = "独立便签未分类测试",
                    NoteId = "note-1"
                };
                AssertEqual(0, tUnclassified.Quadrant, "Default task quadrant should be 0");
                data.Tasks.Add(tUnclassified);

                var tQ1 = new MemoTask
                {
                    Title = "完成中文备忘测试 🚀",
                    Notes = "支持DDL与四象限",
                    Quadrant = 1,
                    DeadlineIso = "2026-12-01 18:30",
                    RemindAtIso = "2026-12-01 18:00",
                    AiScore = 92,
                    AiReason = "DDL临近且为核心任务"
                };
                data.Tasks.Add(tQ1);

                var note = new StickyNote
                {
                    Title = "桌面玻璃便签",
                    Content = "可选文本",
                    ShowScratchpad = false,
                    Theme = "warm_yellow", // legacy name should migrate
                    FilterMode = "custom"
                };
                data.StickyNotes.Add(note);
                storage.SaveData(data);

                var cfg = new AppConfig
                {
                    DisplayMode = "both",
                    UiScale = "large",
                    ServerPort = 8765,
                    AiProviderPreset = "deepseek",
                    AiBaseUrl = "https://api.deepseek.com/v1",
                    AiModel = "deepseek-chat",
                    AiApiKey = "sk-test-123"
                };
                storage.SaveConfig(cfg);

                AppData loadedData = storage.LoadData();
                AppConfig loadedCfg = storage.LoadConfig();

                AssertEqual(2, loadedData.Tasks.Count, "Loaded task count");
                AssertEqual(0, loadedData.Tasks[0].Quadrant, "Quadrant 0 preserved across Save/Load");
                AssertEqual("完成中文备忘测试 🚀", loadedData.Tasks[1].Title, "Task title UTF-8 roundtrip");
                AssertEqual(92, loadedData.Tasks[1].AiScore, "Task AI score roundtrip");
                AssertEqual(1, loadedData.StickyNotes.Count, "Loaded sticky count");
                AssertEqual(false, loadedData.StickyNotes[0].ShowScratchpad, "ShowScratchpad preserved");
                AssertEqual("claude_warm", loadedData.StickyNotes[0].Theme, "Legacy theme migrated");
                AssertEqual("large", loadedCfg.UiScale, "UiScale roundtrip");
                AssertEqual("sk-test-123", loadedCfg.AiApiKey, "Config API key roundtrip");

                string backup = storage.CreateBackup();
                AssertTrue(File.Exists(backup), "Backup file should exist");
            }
            finally
            {
                try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
            }
        }

        private static void TestBug1StickyLockScope()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "DesktopMemo_Lock_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            try
            {
                var storage = new StorageService(tempDir);
                var controller = new TrayAndAppController(storage);
                controller.Config.PanelLocked = false;
                controller.Data.StickyNotes[0].Locked = false;

                controller.SetAllStickiesLocked(true);
                AssertEqual(true, controller.Data.StickyNotes[0].Locked, "Sticky note should be locked");
                AssertEqual(false, controller.Config.PanelLocked, "Main dashboard MUST remain unlocked when locking stickies");

                controller.SetAllStickiesLocked(false);
                AssertEqual(false, controller.Data.StickyNotes[0].Locked, "Sticky note should be unlocked");

                var undoTask = new MemoTask
                {
                    Title = "Undo Test Task",
                    Quadrant = 2,
                    Notes = "Verify delete and undo"
                };
                controller.AddTask(undoTask);
                int countBeforeDelete = controller.Data.Tasks.Count;
                controller.DeleteTaskWithUndo(undoTask.Id, false);
                AssertEqual(countBeforeDelete - 1, controller.Data.Tasks.Count, "Task count should decrease by 1 after DeleteTaskWithUndo");
                AssertEqual(true, controller.CanUndoDelete, "CanUndoDelete should be true after deletion");
                controller.UndoLastDeletedTask();
                AssertEqual(countBeforeDelete, controller.Data.Tasks.Count, "Task count should be restored after UndoLastDeletedTask");
                AssertEqual(false, controller.CanUndoDelete, "CanUndoDelete should reset after undo");
                AssertTrue(controller.Data.Tasks.Exists(t => t.Id == undoTask.Id && t.Title == "Undo Test Task"), "Restored task should preserve ID and title");
            }
            finally
            {
                try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
            }
        }

        private static void TestDeadlineCountdownMath()
        {
            DateTime now = new DateTime(2026, 11, 23, 20, 0, 0);

            DeadlineDisplayInfo none = MemoTask.FormatDeadlineCountdown(null, false, now);
            AssertEqual(0, none.UrgencyLevel, "Null DDL should be level 0");

            DeadlineDisplayInfo overdue = MemoTask.FormatDeadlineCountdown(now.AddMinutes(-35), false, now);
            AssertEqual(4, overdue.UrgencyLevel, "Overdue should be level 4");
            AssertTrue(overdue.Text.Contains("已逾期 35分钟"), "Overdue minutes text: " + overdue.Text);

            DeadlineDisplayInfo urgent = MemoTask.FormatDeadlineCountdown(now.AddHours(2), false, now);
            AssertEqual(3, urgent.UrgencyLevel, "<3h should be level 3");
            AssertTrue(urgent.Text.Contains("剩 2小时"), "Urgent hours text: " + urgent.Text);

            DeadlineDisplayInfo soon = MemoTask.FormatDeadlineCountdown(now.AddHours(10), false, now);
            AssertEqual(2, soon.UrgencyLevel, "<24h should be level 2");

            DeadlineDisplayInfo multiDay = MemoTask.FormatDeadlineCountdown(now.AddDays(3), false, now);
            AssertEqual(1, multiDay.UrgencyLevel, ">24h should be level 1");
            AssertTrue(multiDay.Text.Contains("剩3天"), "Multi-day text: " + multiDay.Text);

            DeadlineDisplayInfo done = MemoTask.FormatDeadlineCountdown(now.AddDays(-1), true, now);
            AssertEqual(5, done.UrgencyLevel, "Completed should be level 5");
        }

        private static void TestAiEndpointNormalization()
        {
            AssertEqual("https://api.deepseek.com/v1/chat/completions", AiSortService.NormalizeChatEndpoint("https://api.deepseek.com"), "Bare domain");
            AssertEqual("https://api.deepseek.com/v1/chat/completions", AiSortService.NormalizeChatEndpoint("https://api.deepseek.com/v1"), "/v1 suffix");
            AssertEqual("https://api.deepseek.com/v1/chat/completions", AiSortService.NormalizeChatEndpoint("https://api.deepseek.com/v1/"), "/v1/ suffix");
            AssertEqual("https://api.openai.com/v1/chat/completions", AiSortService.NormalizeChatEndpoint("https://api.openai.com/v1/chat/completions"), "Full endpoint");
            AssertEqual("https://dashscope.aliyuncs.com/compatible-mode/v1/chat/completions", AiSortService.NormalizeChatEndpoint("https://dashscope.aliyuncs.com/compatible-mode/v1"), "Qwen compatible mode");
        }

        private static void TestAiJsonParser()
        {
            string fenced = "好的，以下是排序结果：\n```json\n{\"items\":[{\"id\":\"a1\",\"quadrant\":1,\"score\":98,\"reason\":\"DDL今夜到期\"}]}\n```";
            List<AiSortItem> r1 = AiSortService.ParseAiSortJson(fenced);
            AssertEqual(1, r1.Count, "Fenced JSON parse count");
            AssertEqual("a1", r1[0].Id, "Fenced item id");
            AssertEqual(1, r1[0].Quadrant, "Fenced item quadrant");
            AssertEqual(98, r1[0].Score, "Fenced item score");

            string withThink = "<think>我先思考一下各个任务的截止日期...</think>\n[{\"id\":\"b2\",\"quadrant\":9,\"score\":120,\"reason\":\"测试超限截断\"}]";
            List<AiSortItem> r2 = AiSortService.ParseAiSortJson(withThink);
            AssertEqual(1, r2.Count, "Think+Array JSON parse count");
            AssertEqual(4, r2[0].Quadrant, "Quadrant clamped to 4");
            AssertEqual(100, r2[0].Score, "Score clamped to 100");

            List<AiSortItem> bad = AiSortService.ParseAiSortJson("完全不是JSON的回复");
            AssertEqual(0, bad.Count, "Invalid JSON returns empty list");

            string envelope = "{\"id\":\"cmp-1\",\"choices\":[{\"message\":{\"role\":\"assistant\",\"reasoning_content\":\"分析中\",\"content\":\"{\\\"items\\\":[{\\\"id\\\":\\\"x9\\\",\\\"quadrant\\\":2,\\\"score\\\":77,\\\"reason\\\":\\\"关键规划\\\"}]}\"}}]}";
            string extracted = AiSortService.ExtractChatCompletionContent(envelope);
            List<AiSortItem> r3 = AiSortService.ParseAiSortJson(extracted);
            AssertEqual(1, r3.Count, "Envelope extraction parse count");
            AssertEqual("x9", r3[0].Id, "Envelope item id");

            // Feature 5: WeChat chat task extraction parser
            string chatJson = "<think>解析微信群消息</think>```json\n{\"tasks\":[{\"title\":\"提交机器学习实验报告\",\"notes\":\"发至班长邮箱\",\"deadline\":\"2026-11-25 16:00\",\"remind_at\":\"2026-11-25 14:00\",\"quadrant\":1,\"reason\":\"课程硬性截止\"},{\"title\":\"小组PPT分工讨论会\",\"notes\":\"腾讯会议\",\"deadline\":\"2026-11-27 20:00\",\"remind_at\":\"\",\"quadrant\":2,\"reason\":\"重要协作\"}]}\n```";
            List<AiExtractedTask> ext = AiSortService.ParseExtractedTasksJson(chatJson);
            AssertEqual(2, ext.Count, "Extracted tasks count");
            AssertEqual("提交机器学习实验报告", ext[0].Title, "First extracted title");
            AssertEqual("2026-11-25 16:00", ext[0].DeadlineIso, "First extracted DDL");
            AssertEqual(1, ext[0].Quadrant, "First extracted quadrant");
            AssertEqual(true, ext[0].Selected, "Extracted selected by default");
        }

        private static void TestThemeAndQuadrantPalettes()
        {
            for (int i = 0; i < ThemeService.ThemeKeys.Length; i++)
            {
                ThemePalette p = ThemeService.GetPalette(ThemeService.ThemeKeys[i], 0.96);
                AssertTrue(p.CardBg != null && p.HeaderBg != null && p.SurfaceBg != null && p.Border != null && p.TextPrimary != null && p.PillGlassBg != null,
                    "Editorial flat palette valid: " + ThemeService.ThemeKeys[i]);
            }

            AssertEqual("claude_warm", ThemeService.NormalizeThemeKey("warm_editorial"), "Legacy warm_editorial migration");
            AssertEqual("notion_light", ThemeService.NormalizeThemeKey("frost_glass"), "Legacy frost_glass migration");
            AssertEqual("linear_dark", ThemeService.NormalizeThemeKey("obsidian_glass"), "Legacy obsidian_glass migration");

            for (int q = 0; q <= 4; q++)
            {
                QuadrantVisual qvLight = ThemeService.GetQuadrantVisual(q, false);
                QuadrantVisual qvDark = ThemeService.GetQuadrantVisual(q, true);
                AssertTrue(!string.IsNullOrEmpty(qvLight.Title) && !string.IsNullOrEmpty(qvDark.AccentHex), "Quadrant visual valid: Q" + q);
            }

            UiScaleMetrics mCompact = ThemeService.GetScaleMetrics("compact", 13);
            UiScaleMetrics mLarge = ThemeService.GetScaleMetrics("large", 13);
            AssertTrue(mLarge.ButtonHeight > mCompact.ButtonHeight, "Large UI scale should have taller buttons than compact");

            var spring = new IosSpringEase();
            AssertEqual(0.0, spring.Ease(0.0), "Spring ease(0)==0");
            AssertEqual(1.0, spring.Ease(1.0), "Spring ease(1)==1");
        }

        private static void TestAutoStartRegistryRead()
        {
            bool enabled = Win32Native.IsAutoStartEnabled();
            string cmd = Win32Native.GetAutoStartCommand();
            AssertTrue(!enabled || !string.IsNullOrEmpty(cmd), "AutoStart read consistency");
        }

        private static void TestWpfUiSmokeConstruction()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "DesktopMemo_UiSmoke_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            try
            {
                var storage = new StorageService(tempDir);
                var controller = new TrayAndAppController(storage);

                var dash = new DashboardWindow(controller);
                var sticky = new StickyNoteWindow(controller.Data.StickyNotes[0], controller);
                var dlg = new TaskEditDialog(controller.Data.Tasks[0], false, controller.Data.StickyNotes);
                var chatDlg = new ChatExtractDialog(controller, "");
                var toast = new ReminderToastWindow(controller.Data.Tasks[0], false);
                var undoToast = new UndoToastWindow("测试日程", "claude_warm");

                AssertTrue(dash != null && sticky != null && dlg != null && chatDlg != null && toast != null && undoToast != null, "All WPF windows constructed");
                AssertEqual(System.Windows.WindowStyle.None, dash.WindowStyle, "Dashboard WindowStyle must be None (no Win32 close button)");
                AssertEqual(true, dash.AllowsTransparency, "Dashboard AllowsTransparency must be true");
                AssertEqual(System.Windows.WindowStyle.None, sticky.WindowStyle, "Sticky WindowStyle must be None (no Win32 close button)");
                AssertEqual(true, sticky.AllowsTransparency, "Sticky AllowsTransparency must be true");
                AssertEqual(System.Windows.WindowStyle.None, undoToast.WindowStyle, "UndoToast WindowStyle must be None");
                AssertEqual(true, undoToast.AllowsTransparency, "UndoToast AllowsTransparency must be true");

                for (int i = 0; i < ThemeService.ThemeKeys.Length; i++)
                {
                    controller.Config.PanelTheme = ThemeService.ThemeKeys[i];
                    controller.Config.UiScale = "large";
                    dash.ApplyThemeAndRefreshAll();
                }

                controller.Data.StickyNotes[0].ShowScratchpad = true;
                sticky.ApplyThemeAndContent();

                undoToast.Close();
                toast.Close();
                chatDlg.Close();
                dlg.Close();
                sticky.Close();
                dash.Close();
            }
            finally
            {
                try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
            }
        }
    }
}
