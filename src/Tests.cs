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
            RunTest("StickyQuickAddAndRemoteSyncMerge", TestStickyQuickAddAndRemoteSyncMerge);
            RunTest("V250PathAttachmentsWeChatImportAndResize", TestV250Features);
            RunTest("DashboardSliderStyleStability", TestDashboardSliderStyleStability);
            RunTest("V21TimetableVoiceAndHotkeys", TestV21Features);

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
                    Title = "桌面便签",
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
                AssertEqual("2.11", AppMeta.Version, "AppMeta.Version must be 2.11");
                AssertEqual("v2.11", AppMeta.VersionTag, "AppMeta.VersionTag must be v2.11");
                AssertEqual(System.Windows.ResizeMode.CanResize, dash.ResizeMode, "Dashboard ResizeMode must be CanResize");
                AssertEqual(System.Windows.ResizeMode.CanResize, sticky.ResizeMode, "Sticky ResizeMode must be CanResize");
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

                object dashBeforeRes = dash.Resources[typeof(System.Windows.Controls.TextBox)];
                dash.ApplyThemeAndRefreshAll();
                object dashAfterRes = dash.Resources[typeof(System.Windows.Controls.TextBox)];
                AssertTrue(object.ReferenceEquals(dashBeforeRes, dashAfterRes),
                    "Unchanged Dashboard theme/scale must reuse cached TextBox Style without rebuilding IME visual tree");

                object stickyBeforeRes = sticky.Resources[typeof(System.Windows.Controls.TextBox)];
                sticky.ApplyThemeAndContent();
                object stickyAfterRes = sticky.Resources[typeof(System.Windows.Controls.TextBox)];
                AssertTrue(object.ReferenceEquals(stickyBeforeRes, stickyAfterRes),
                    "Unchanged StickyNote theme/scale must reuse cached TextBox Style without rebuilding IME visual tree");

                string origNoteTheme = controller.Data.StickyNotes[0].Theme;
                controller.Data.StickyNotes[0].Theme = origNoteTheme == "linear_dark" ? "claude_warm" : "linear_dark";
                sticky.ApplyThemeAndContent();
                object stickyChangedRes = sticky.Resources[typeof(System.Windows.Controls.TextBox)];
                AssertTrue(!object.ReferenceEquals(stickyBeforeRes, stickyChangedRes),
                    "Changing StickyNote theme must refresh TextBox Style");
                controller.Data.StickyNotes[0].Theme = origNoteTheme;

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

        private static void TestStickyQuickAddAndRemoteSyncMerge()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "DesktopMemo_SyncMerge_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            try
            {
                var storage = new StorageService(tempDir);
                var controller = new TrayAndAppController(storage);
                StickyNote localNote = controller.Data.StickyNotes[0];
                localNote.Visible = true;
                localNote.Left = 500;
                localNote.Top = 240;
                localNote.PinMode = "desktop";
                localNote.Theme = "claude_warm";
                localNote.Locked = true;

                var added = new MemoTask
                {
                    Title = "Quick Enter Sticky Item",
                    Quadrant = 0,
                    NoteId = localNote.Id
                };
                controller.AddTask(added);

                AssertTrue(controller.LocalDataVersion > 0, "LocalDataVersion must increment on AddTask");
                AssertEqual(true, controller.HasPendingRemotePush, "HasPendingRemotePush must be true after local AddTask");
                AssertTrue(controller.Data.Tasks.Exists(t => t.Id == added.Id && t.NoteId == localNote.Id), "Task must persist with matching NoteId");

                var remoteNote = new StickyNote
                {
                    Id = localNote.Id,
                    Title = "远程新标题",
                    Content = "远程新内容",
                    ShowScratchpad = true,
                    FilterMode = "custom",
                    Visible = false,
                    Left = 10,
                    Top = 10,
                    Width = 260,
                    Height = 200,
                    PinMode = "normal",
                    Theme = "linear_dark",
                    Locked = false
                };

                List<StickyNote> merged = TrayAndAppController.MergePulledStickyNotes(
                    new List<StickyNote> { localNote },
                    new List<StickyNote> { remoteNote });

                AssertEqual(1, merged.Count, "Merged sticky list should keep count");
                AssertEqual(true, merged[0].Visible, "Merge must preserve desktop Visible state");
                AssertEqual(500.0, merged[0].Left, "Merge must preserve desktop Left position");
                AssertEqual(240.0, merged[0].Top, "Merge must preserve desktop Top position");
                AssertEqual("desktop", merged[0].PinMode, "Merge must preserve desktop PinMode");
                AssertEqual("claude_warm", merged[0].Theme, "Merge must preserve desktop Theme");
                AssertEqual(true, merged[0].Locked, "Merge must preserve desktop Locked state");
                AssertEqual("远程新标题", merged[0].Title, "Merge should update Title from remote");
                AssertEqual("远程新内容", merged[0].Content, "Merge should update Content from remote");

                var stickyWin = new StickyNoteWindow(localNote, controller);
                stickyWin.RebindNote(merged[0]);
                stickyWin.ApplyThemeAndContent();
                stickyWin.Close();
            }
            finally
            {
                try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
            }
        }

        private static void TestV250Features()
        {
            string tempRoot = Path.Combine(Path.GetTempPath(), "DesktopMemo_V250_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            try
            {
                string explicitData = Path.Combine(tempRoot, "custom_cfg");
                string resolvedExplicit = StorageService.ResolveDataDirectory(null, explicitData);
                AssertEqual(Path.GetFullPath(explicitData), resolvedExplicit, "Explicit data dir should take precedence");

                string baseOnly = Path.Combine(tempRoot, "portable_base");
                string resolvedBase = StorageService.ResolveDataDirectory(baseOnly, null);
                AssertEqual(Path.Combine(baseOnly, "data"), resolvedBase, "Base dir resolves to base\\data");

                var storage = new StorageService(null, explicitData);
                AssertTrue(Directory.Exists(storage.AttachmentsDirectory), "Attachments directory should be created automatically");

                string srcPng = Path.Combine(tempRoot, "sample.png");
                using (var bmp = new System.Drawing.Bitmap(24, 18))
                using (var g = System.Drawing.Graphics.FromImage(bmp))
                {
                    g.Clear(System.Drawing.Color.CornflowerBlue);
                    bmp.Save(srcPng, System.Drawing.Imaging.ImageFormat.Png);
                }

                byte[] compressed = ImageAttachmentHelper.CompressImageFileToJpeg(srcPng, 1600, 85);
                AssertTrue(compressed != null && compressed.Length > 0, "Compressed JPEG bytes must be non-empty");
                string dataUrl = ImageAttachmentHelper.ToDataUrl(compressed);
                AssertTrue(dataUrl.StartsWith("data:image/jpeg;base64,", StringComparison.Ordinal), "DataUrl must have JPEG prefix");

                string savedAtt = storage.SaveAttachmentBytes(compressed, ".jpg");
                AssertTrue(!string.IsNullOrEmpty(savedAtt) && storage.AttachmentExists(savedAtt), "Attachment must be stored on disk");
                byte[] readBack = storage.ReadAttachmentBytes(savedAtt);
                AssertEqual(compressed.Length, readBack.Length, "Attachment byte round-trip length match");

                AppData d = storage.LoadData();
                d.Tasks[0].AttachmentFiles.Add(savedAtt);
                storage.SaveData(d);

                AppData reloaded = storage.LoadData();
                AssertTrue(reloaded.Tasks[0].AttachmentFiles != null && reloaded.Tasks[0].AttachmentFiles.Contains(savedAtt),
                    "Task AttachmentFiles must survive JSON round-trip");

                // Chat export .md + image import test
                string wxDir = Path.Combine(tempRoot, "wx_export");
                Directory.CreateDirectory(wxDir);
                File.Copy(srcPng, Path.Combine(wxDir, "0001.jpg"), true);
                string todayStr = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
                string mdContent =
                    "# 课程通知群\r\n" +
                    "2020-01-01 09:00 张三: 很早以前的历史无关消息\r\n" +
                    todayStr + " 李老师: [表情]\r\n" +
                    todayStr + " 班长: 请今天晚上8点前提交实验报告 [图片0001]\r\n";
                File.WriteAllText(Path.Combine(wxDir, "课程通知群.md"), mdContent, Encoding.UTF8);

                WeChatImportResult wxRes = WeChatExportImportService.ImportFromPath(wxDir, 3, 12000);
                AssertTrue(wxRes.FormattedText.IndexOf("实验报告", StringComparison.Ordinal) >= 0, "Recent ChatExport message must be imported");
                AssertTrue(wxRes.FormattedText.IndexOf("很早以前的历史无关消息", StringComparison.Ordinal) < 0, "Old messages must be filtered by 3-day window");
                AssertTrue(wxRes.SkippedNoise >= 1, "Pure [表情] noise must be filtered");
                AssertTrue(wxRes.MatchedImagePaths.Count == 1, "Matched decrypted 0001.jpg image path expected");
            }
            finally
            {
                try { if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true); } catch { }
            }
        }

        private static void TestDashboardSliderStyleStability()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "DesktopMemo_SliderStab_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            try
            {
                var storage = new StorageService(tempDir);
                var controller = new TrayAndAppController(storage);
                controller.Config.PanelTheme = "claude_warm";
                controller.Config.PanelOpacity = 0.96;
                controller.Config.PanelFontSize = 13;

                var dash = new DashboardWindow(controller);
                var sticky = new StickyNoteWindow(controller.Data.StickyNotes[0], controller);

                object origSlider = dash.Resources[typeof(System.Windows.Controls.Slider)];
                object origScroll = dash.Resources[typeof(System.Windows.Controls.Primitives.ScrollBar)];
                object origTb = dash.Resources[typeof(System.Windows.Controls.TextBox)];
                AssertTrue(origSlider != null && origScroll != null && origTb != null, "Dashboard core resource styles must be populated");

                // Changing opacity must NOT rebuild Slider, ScrollBar, or TextBox styles
                controller.Config.PanelOpacity = 0.72;
                dash.ApplyThemeAndRefreshAll();
                AssertTrue(object.ReferenceEquals(origSlider, dash.Resources[typeof(System.Windows.Controls.Slider)]),
                    "Opacity change must preserve Dashboard Slider Style reference");
                AssertTrue(object.ReferenceEquals(origScroll, dash.Resources[typeof(System.Windows.Controls.Primitives.ScrollBar)]),
                    "Opacity change must preserve Dashboard ScrollBar Style reference");
                AssertTrue(object.ReferenceEquals(origTb, dash.Resources[typeof(System.Windows.Controls.TextBox)]),
                    "Opacity change must preserve Dashboard TextBox Style reference");

                // Changing font size must update TextBox style, but NEVER rebuild Slider or ScrollBar styles
                controller.Config.PanelFontSize = 16;
                dash.ApplyThemeAndRefreshAll();
                AssertTrue(object.ReferenceEquals(origSlider, dash.Resources[typeof(System.Windows.Controls.Slider)]),
                    "Font size change must preserve Dashboard Slider Style reference");
                AssertTrue(object.ReferenceEquals(origScroll, dash.Resources[typeof(System.Windows.Controls.Primitives.ScrollBar)]),
                    "Font size change must preserve Dashboard ScrollBar Style reference");
                AssertTrue(!object.ReferenceEquals(origTb, dash.Resources[typeof(System.Windows.Controls.TextBox)]),
                    "Font size change should refresh TextBox Style");

                // Changing theme MUST rebuild Slider style for new theme colors
                controller.Config.PanelTheme = "linear_dark";
                dash.ApplyThemeAndRefreshAll();
                AssertTrue(!object.ReferenceEquals(origSlider, dash.Resources[typeof(System.Windows.Controls.Slider)]),
                    "Theme change should refresh Dashboard Slider Style");

                // StickyNote opacity change must preserve Slider and TextBox styles
                StickyNote note = controller.Data.StickyNotes[0];
                note.Opacity = 0.95;
                sticky.ApplyThemeAndContent();
                object sSlider = sticky.Resources[typeof(System.Windows.Controls.Slider)];
                object sTb = sticky.Resources[typeof(System.Windows.Controls.TextBox)];
                AssertTrue(sSlider != null && sTb != null, "StickyNote resource styles must be populated");

                note.Opacity = 0.75;
                sticky.ApplyThemeAndContent();
                AssertTrue(object.ReferenceEquals(sSlider, sticky.Resources[typeof(System.Windows.Controls.Slider)]),
                    "Opacity change must preserve StickyNote Slider Style reference");
                AssertTrue(object.ReferenceEquals(sTb, sticky.Resources[typeof(System.Windows.Controls.TextBox)]),
                    "Opacity change must preserve StickyNote TextBox Style reference");

                sticky.Close();
                dash.Close();
            }
            finally
            {
                try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
            }
        }

        private static void TestV21Features()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "DesktopMemo_V21_" + Guid.NewGuid().ToString("N").Substring(0, 8));
            try
            {
                // 1. Teaching week & Gregorian alignment
                var tt = new TimetableData
                {
                    SemesterName = "2026秋",
                    SemesterStartMondayIso = "2026-09-07",
                    TotalWeeks = 20
                };
                AssertEqual(1, tt.GetWeekNumberForDate(new DateTime(2026, 9, 7)), "Week 1 Monday check");
                AssertEqual(12, tt.GetWeekNumberForDate(new DateTime(2026, 11, 26)), "Nov 26 2026 should be Week 12");

                DateTime? w14Wed = tt.GetDateForWeekAndDay(14, 3);
                AssertTrue(w14Wed.HasValue && w14Wed.Value == new DateTime(2026, 12, 9), "Week 14 Wednesday must align to 2026-12-09");

                string alignedDdl = tt.AlignWeekTextToDateIso("第14周周三交机器学习实验报告", "", null, null, new DateTime(2026, 11, 26));
                AssertTrue(alignedDdl.StartsWith("2026-12-09", StringComparison.Ordinal), "Chinese week text aligned to 2026-12-09: " + alignedDdl);

                string alignedCnDdl = tt.AlignWeekTextToDateIso("第十四周星期四汇报", "2026-12-01 19:30", null, null, new DateTime(2026, 11, 26));
                AssertEqual("2026-12-10 19:30", alignedCnDdl, "Chinese numeral week + preserved HH:mm alignment");

                var oddCourse = new CourseItem { Name = "编译原理", DayOfWeek = 4, StartSection = 1, EndSection = 2, StartWeek = 1, EndWeek = 16, WeekType = "odd", Location = "教一-102" };
                var evenCourse = new CourseItem { Name = "体育", DayOfWeek = 4, StartSection = 3, EndSection = 4, StartWeek = 1, EndWeek = 16, WeekType = "even", Location = "操场" };
                tt.Courses.Add(oddCourse);
                tt.Courses.Add(evenCourse);

                AssertEqual(true, oddCourse.OccursInWeek(11), "Odd course occurs in Week 11");
                AssertEqual(false, oddCourse.OccursInWeek(12), "Odd course does not occur in Week 12");
                AssertEqual(true, evenCourse.OccursInWeek(12), "Even course occurs in Week 12");

                string aiCtx = tt.BuildAiWeekContext(new DateTime(2026, 11, 26));
                AssertTrue(aiCtx.IndexOf("第12周", StringComparison.Ordinal) >= 0, "AI week context includes current week 12");

                // 2. .ics parsing
                string sampleIcs =
                    "BEGIN:VCALENDAR\r\n" +
                    "BEGIN:VEVENT\r\n" +
                    "SUMMARY:数据结构\r\n" +
                    "LOCATION:二教305\r\n" +
                    "DTSTART:20260908T080000\r\n" +
                    "DTEND:20260908T094000\r\n" +
                    "RRULE:FREQ=WEEKLY;COUNT=16;INTERVAL=1\r\n" +
                    "END:VEVENT\r\n" +
                    "END:VCALENDAR\r\n";
                TimetableData parsedIcs = TimetableService.ParseIcsContent(sampleIcs, new DateTime(2026, 11, 26));
                AssertEqual(1, parsedIcs.Courses.Count, "ICS parsed 1 course");
                AssertEqual("数据结构", parsedIcs.Courses[0].Name, "ICS course summary");
                AssertEqual(2, parsedIcs.Courses[0].DayOfWeek, "Sep 8 2026 is Tuesday (ISO 2)");
                AssertEqual("2026-09-07", parsedIcs.SemesterStartMondayIso, "ICS derived Week 1 Monday");

                // 3. WAV header builder + Hotkey parsing + DeepSeek ASR endpoint
                byte[] wav = VoiceAudioRecorder.BuildWavBytes(new byte[3200], 16000, 16, 1);
                AssertEqual(3244, wav.Length, "WAV header + 3200 PCM bytes must be 3244 bytes");
                AssertTrue(Encoding.ASCII.GetString(wav, 0, 4) == "RIFF" && Encoding.ASCII.GetString(wav, 8, 4) == "WAVE", "WAV RIFF/WAVE header");

                uint mods;
                uint vk;
                string norm;
                AssertEqual(true, Win32Native.ParseHotkeyString("Ctrl+Alt+V", out mods, out vk, out norm), "Parse Ctrl+Alt+V");
                AssertEqual("Ctrl+Alt+V", norm, "Normalized hotkey string");
                AssertEqual(true, Win32Native.ParseHotkeyString("shift+f9", out mods, out vk, out norm), "Parse Shift+F9");
                AssertEqual("Shift+F9", norm, "Normalized Shift+F9");
                AssertEqual(false, Win32Native.ParseHotkeyString("Ctrl", out mods, out vk, out norm), "Bare modifier hotkey must fail");

                bool isDs;
                string asrUrl = AiSortService.NormalizeAsrEndpoint(new AppConfig(), out isDs);
                AssertEqual(true, isDs, "Default preset must route to DeepSeek ASR");
                AssertEqual("https://api.deepseek.com/v1/tools/asr", asrUrl, "DeepSeek ASR endpoint");

                // 3b. Direct HTML timetable parser, week-spec parser & AI JSON section_times
                int pSw, pEw;
                string pWt;
                List<int> pCw;
                AssertEqual(true, CourseItem.TryParseWeekSpec("1-16周(单)", out pSw, out pEw, out pWt, out pCw), "Parse odd week spec");
                AssertEqual("odd", pWt, "Odd week spec resolves to odd");
                var oddProbe = new CourseItem { StartWeek = pSw, EndWeek = pEw, WeekType = pWt, CustomWeeks = pCw };
                AssertEqual(true, oddProbe.OccursInWeek(1), "Odd spec occurs in week 1");
                AssertEqual(false, oddProbe.OccursInWeek(2), "Odd spec skips week 2");

                AssertEqual(true, CourseItem.TryParseWeekSpec("2-16(双)", out pSw, out pEw, out pWt, out pCw), "Parse even week spec");
                AssertEqual("even", pWt, "Even week spec resolves to even");
                var evenProbe = new CourseItem { StartWeek = pSw, EndWeek = pEw, WeekType = pWt, CustomWeeks = pCw };
                AssertEqual(true, evenProbe.OccursInWeek(2), "Even spec occurs in week 2");
                AssertEqual(false, evenProbe.OccursInWeek(3), "Even spec skips week 3");

                AssertEqual(true, CourseItem.TryParseWeekSpec("1-4,8,10-11周", out pSw, out pEw, out pWt, out pCw), "Parse custom weeks");
                AssertEqual(7, pCw.Count, "Custom weeks count=7");
                AssertEqual(8, pCw[4], "Custom weeks includes week 8");

                string sampleHtml =
                    "<html><body><div>当前第12周</div><table>" +
                    "<tr><th>节次</th><th>星期一</th><th>星期二</th><th>星期三</th><th>星期四</th><th>星期五</th></tr>" +
                    "<tr><td>第1节<br>08:00-08:45</td><td rowspan=\"2\"></td><td rowspan=\"2\">编译原理<br>1-16周(单)<br>张教授<br>教一-102<hr>计算机网络<br>2-16周(双)<br>李老师<br>教二-205</td><td rowspan=\"2\"></td><td rowspan=\"2\"></td><td rowspan=\"2\"></td></tr>" +
                    "<tr><td>第2节<br>08:55-09:40</td></tr>" +
                    "<tr><td>第3节<br>10:00-10:45</td><td rowspan=\"2\"></td><td rowspan=\"2\"></td><td rowspan=\"2\"></td><td rowspan=\"2\">软件工程<br>1-16周<br>王老师<br>综合楼A301</td><td rowspan=\"2\"></td></tr>" +
                    "<tr><td>第4节<br>10:55-11:40</td></tr>" +
                    "</table></body></html>";
                DirectTimetableParseResult htmlRes = TimetableService.ParseHtmlTimetable(sampleHtml, new DateTime(2026, 11, 26));
                AssertEqual(true, htmlRes.Success, "Direct HTML timetable parse must succeed");
                AssertEqual(3, htmlRes.Timetable.Courses.Count, "Direct HTML parse extracts 3 courses (odd/even split + Thu)");
                AssertEqual("08:00", htmlRes.Timetable.GetSectionStartTime(1), "Section 1 start time from HTML");
                AssertEqual("08:45", htmlRes.Timetable.GetSectionEndTime(1), "Section 1 end time from HTML");
                AssertEqual("08:55", htmlRes.Timetable.GetSectionStartTime(2), "Section 2 start time from HTML");
                AssertEqual("09:40", htmlRes.Timetable.GetSectionEndTime(2), "Section 2 end time from HTML");

                bool foundTueOdd = false;
                bool foundTueEven = false;
                bool foundThu = false;
                for (int i = 0; i < htmlRes.Timetable.Courses.Count; i++)
                {
                    CourseItem hc = htmlRes.Timetable.Courses[i];
                    if (hc.Name.IndexOf("编译原理", StringComparison.Ordinal) >= 0)
                    {
                        foundTueOdd = (hc.DayOfWeek == 2 && hc.WeekType == "odd" && hc.StartSection == 1 && hc.EndSection == 2);
                    }
                    else if (hc.Name.IndexOf("计算机网络", StringComparison.Ordinal) >= 0)
                    {
                        foundTueEven = (hc.DayOfWeek == 2 && hc.WeekType == "even" && hc.StartSection == 1 && hc.EndSection == 2);
                    }
                    else if (hc.Name.IndexOf("软件工程", StringComparison.Ordinal) >= 0)
                    {
                        foundThu = (hc.DayOfWeek == 4 && hc.StartSection == 3 && hc.EndSection == 4);
                    }
                }
                AssertEqual(true, foundTueOdd, "Tuesday odd course aligned to Day 2 Sec 1-2");
                AssertEqual(true, foundTueEven, "Tuesday even course aligned to Day 2 Sec 1-2");
                AssertEqual(true, foundThu, "Thursday course aligned to Day 4 Sec 3-4");

                string aiTtJson = "{\"semester_name\":\"2026秋\",\"current_week\":12,\"section_times\":[{\"section\":1,\"start_time\":\"8:00\",\"end_time\":\"8:45\"}],\"courses\":[{\"name\":\"体育\",\"day_of_week\":4,\"start_section\":1,\"end_section\":1,\"start_week\":2,\"end_week\":16,\"week_type\":\"双周\"}]}";
                AiTimetableExtractOutcome aiTt = AiSortService.ParseTimetableJson(aiTtJson);
                AssertEqual(true, aiTt.Success, "AI timetable JSON parse");
                AssertEqual("even", aiTt.Courses[0].WeekType, "Chinese week_type 双周 normalized to even");
                AssertEqual("08:00", aiTt.Courses[0].StartTime, "AI timetable course start time backfilled from section_times");
                AssertEqual(0, TimetableService.TryFindAliveDebugPort(Path.Combine(tempDir, "empty_mis_profile")), "Empty MIS profile returns 0 CDP port");

                // 3c. Custom section count, single-week & global drag moves, single-week skip
                var ttCustom = new TimetableData { SectionsPerDay = 8, ShowWeekends = false, RowMinHeight = 42 };
                AssertEqual(8, ttCustom.GetMaxSectionCount(), "Custom SectionsPerDay=8 overrides default row count");
                ttCustom.SectionsPerDay = 0;
                AssertEqual(10, ttCustom.GetMaxSectionCount(), "Auto SectionsPerDay=0 fits default minimum 10");

                var baseCourse = new CourseItem
                {
                    Name = "机器学习",
                    DayOfWeek = 2,
                    StartSection = 1,
                    EndSection = 2,
                    StartWeek = 1,
                    EndWeek = 16,
                    WeekType = "all"
                };
                ttCustom.Courses.Add(baseCourse);
                ttCustom.MoveCourse(baseCourse, 4, 3, 12, true);
                AssertEqual(0, ttCustom.GetCoursesForWeekAndDay(12, 2).Count, "Week 12 Tue slot cleared after single-week move");
                List<CourseItem> w12Thu = ttCustom.GetCoursesForWeekAndDay(12, 4);
                AssertEqual(1, w12Thu.Count, "Week 12 Thu receives single-week moved course");
                AssertEqual(3, w12Thu[0].StartSection, "Single-week moved course starts at Sec 3");
                AssertEqual(4, w12Thu[0].EndSection, "Single-week moved course preserves 2-section span");
                AssertEqual(1, ttCustom.GetCoursesForWeekAndDay(13, 2).Count, "Week 13 Tue retains original course");
                AssertEqual(0, ttCustom.GetCoursesForWeekAndDay(13, 4).Count, "Week 13 Thu does not inherit Week 12 move");

                ttCustom.MoveCourse(baseCourse, 5, 5, 12, false);
                AssertEqual(1, ttCustom.GetCoursesForWeekAndDay(13, 5).Count, "Global move updates all future weeks");
                AssertEqual(5, ttCustom.GetCoursesForWeekAndDay(13, 5)[0].StartSection, "Global move targets Sec 5");

                ttCustom.SkipCourseInWeek(baseCourse, 11);
                AssertEqual(0, ttCustom.GetCoursesForWeekAndDay(11, 5).Count, "Skipped week hides course");
                AssertEqual(1, ttCustom.GetCoursesForWeekAndDay(13, 5).Count, "Non-skipped week retains course");

                // 3d. Anonymized 7-section MIS HTML (course codes, comma odd weeks, sibling div cells, [08:00-09:50] time axis)
                string bjtuHtml =
                    "<html><body><div class=\"widget-header\"><span class=\"widget-title\">" +
                    "<button class=\"btn\">2026-2027-1 学期</button>" +
                    "<ul><li><a>2026-2027-2 学期</a></li><li><a>2026-2027-1 学期</a></li></ul>" +
                    "</span></div>" +
                    "<table class=\"table table-bordered\">" +
                    "<tr><th></th><th>星期一</th><th>星期二</th><th>星期三</th><th>星期四</th><th>星期五</th><th>星期六</th><th>星期日</th></tr>" +
                    "<tr><td>第1节<br><span>[08:00-09:50]</span></td><td></td><td></td><td></td><td><div><!-- comment -->C108020B [02]<br><span>工科数学分析（A）I [本]</span><br><div>第01, 03, 05, 07, 09, 11, 13, 15周 <i>张老师</i></div><span>海淀东校区, 东区一教, DQ210</span></div></td><td></td><td></td><td></td></tr>" +
                    "<tr><td>第2节<br><span>[10:10-12:00]</span></td><td></td><td></td><td></td><td></td><td></td><td></td><td></td></tr>" +
                    "<tr><td>第3节<br><span>[12:10-14:00]</span></td><td></td><td></td><td></td><td></td><td></td><td></td><td></td></tr>" +
                    "<tr><td>第4节<br><span>[14:10-16:00]</span></td><td></td><td></td><td></td><td></td><td></td><td></td><td></td></tr>" +
                    "<tr><td>第5节<br><span>[16:20-18:10]</span></td><td><div>A131008B [02]<br><span>职业生涯规划 [本]</span><br><div>第09-16周 <i>李老师</i></div><span>海淀东校区, DQ202</span></div><div style=\"margin-top:8px;\">A022014B [01]<br><span>大学生心理健康 [本]</span><br><div>第01-08周 <i>王老师</i></div><span>海淀东校区, DQ403</span></div></td><td></td><td></td><td></td><td></td><td></td><td></td></tr>" +
                    "<tr><td>第6节<br><span>[19:00-20:50]</span></td><td></td><td></td><td></td><td></td><td></td><td></td><td></td></tr>" +
                    "<tr><td>第7节<br><span>[21:00-21:50]</span></td><td></td><td></td><td></td><td></td><td></td><td></td><td></td></tr>" +
                    "</table></body></html>";
                DirectTimetableParseResult bjtuRes = TimetableService.ParseHtmlTimetable(bjtuHtml, new DateTime(2026, 11, 27));
                AssertEqual(true, bjtuRes.Success, "BJTU format HTML direct parse succeeds");
                AssertEqual(7, bjtuRes.Timetable.SectionsPerDay, "BJTU 7-row schedule sets SectionsPerDay=7");
                AssertEqual(7, bjtuRes.Timetable.SectionTimes.Count, "BJTU 7 section times captured");
                AssertEqual("08:00", bjtuRes.Timetable.GetSectionStartTime(1), "BJTU Sec 1 start");
                AssertEqual("09:50", bjtuRes.Timetable.GetSectionEndTime(1), "BJTU Sec 1 end");
                AssertEqual("21:00", bjtuRes.Timetable.GetSectionStartTime(7), "BJTU Sec 7 start");
                AssertEqual("21:50", bjtuRes.Timetable.GetSectionEndTime(7), "BJTU Sec 7 end");
                AssertTrue(bjtuRes.Timetable.SemesterName.IndexOf("2026-2027-1", StringComparison.Ordinal) >= 0, "Selected semester button preferred over dropdown: " + bjtuRes.Timetable.SemesterName);
                AssertEqual(3, bjtuRes.Timetable.Courses.Count, "BJTU splits sibling divs and captures all 3 courses");
                CourseItem bjtuOdd = bjtuRes.Timetable.Courses.Find(delegate(CourseItem x) { return x.DayOfWeek == 4 && x.StartSection == 1; });
                AssertTrue(bjtuOdd != null, "BJTU Thursday Sec 1 course exists");
                AssertEqual("工科数学分析（A）I", bjtuOdd.Name, "Course code line and [本] stripped from name");
                AssertEqual("odd", bjtuOdd.WeekType, "Comma-listed odd weeks recognized as odd");
                AssertEqual("张老师", bjtuOdd.Teacher, "Teacher extracted cleanly");

                // 3e. Live CDP JSON payload round-trip (verifies ArrayList/IList deserialization never returns 0 courses)
                var cdpSer = new System.Web.Script.Serialization.JavaScriptSerializer();
                var cdpCells = new List<Dictionary<string, object>>();
                string[] dayHeaders = new string[] { "", "星期一", "星期二", "星期三", "星期四", "星期五", "星期六", "星期日" };
                for (int cIdx = 0; cIdx < dayHeaders.Length; cIdx++)
                {
                    cdpCells.Add(new Dictionary<string, object> { { "r", 0 }, { "c", cIdx }, { "rs", 1 }, { "cs", 1 }, { "text", dayHeaders[cIdx] }, { "blocks", new string[0] } });
                }
                for (int rIdx = 1; rIdx <= 7; rIdx++)
                {
                    string[] slotLabels = new string[] { "第1节\n[08:00-09:50]", "第2节\n[10:10-12:00]", "第3节\n[12:10-14:00]", "第4节\n[14:10-16:00]", "第5节\n[16:20-18:10]", "第6节\n[19:00-20:50]", "第7节\n[21:00-21:50]" };
                    cdpCells.Add(new Dictionary<string, object> { { "r", rIdx }, { "c", 0 }, { "rs", 1 }, { "cs", 1 }, { "text", slotLabels[rIdx - 1] }, { "blocks", new string[0] } });
                    if (rIdx == 1)
                    {
                        string cellTxt = "C108020B [02]\n工科数学分析（A）I [本]\n第01, 03, 05, 07, 09, 11, 13, 15周 张老师\n海淀东校区, 东区一教, DQ210";
                        cdpCells.Add(new Dictionary<string, object> { { "r", rIdx }, { "c", 4 }, { "rs", 1 }, { "cs", 1 }, { "text", cellTxt }, { "blocks", new string[] { cellTxt } } });
                    }
                }
                var cdpPayload = new Dictionary<string, object>
                {
                    { "hints", new string[] { "2026-2027-1 学期" } },
                    { "tables", new object[] { new Dictionary<string, object> { { "rows", 8 }, { "cols", 8 }, { "wdHits", 7 }, { "nonEmpty", 16 }, { "cells", cdpCells } } } }
                };
                var roundTripInfo = cdpSer.Deserialize<Dictionary<string, object>>(cdpSer.Serialize(cdpPayload));
                System.Collections.IList rtTables = roundTripInfo["tables"] as System.Collections.IList;
                AssertTrue(rtTables != null && rtTables.Count == 1, "JavaScriptSerializer nested array casts to IList");
                var liveCap = new MisPageCapture
                {
                    Success = true,
                    StructuredTablesJson = cdpSer.Serialize(rtTables),
                    SelectedHints = new List<string> { "2026-2027-1 学期" }
                };
                DirectTimetableParseResult liveRes = TimetableService.ParseCapturedTimetable(liveCap, new DateTime(2026, 11, 27));
                AssertEqual(true, liveRes.Success, "Live CDP StructuredTablesJson parses directly");
                AssertEqual(7, liveRes.Timetable.SectionsPerDay, "Live CDP sets SectionsPerDay=7");
                AssertEqual(1, liveRes.Timetable.Courses.Count, "Live CDP yields 1 course");
                AssertEqual("工科数学分析（A）I", liveRes.Timetable.Courses[0].Name, "Live CDP extracts course name");
                AssertEqual("odd", liveRes.Timetable.Courses[0].WeekType, "Live CDP keeps odd week type");

                // 3f. Timetable drag edge auto-scroll math
                double scrollDown = DashboardWindow.ComputeEdgeAutoScrollDelta(390.0, 400.0, 20.0, 180.0);
                AssertTrue(scrollDown > 0.0, "Cursor near bottom edge must produce positive scroll delta: " + scrollDown);
                double scrollUp = DashboardWindow.ComputeEdgeAutoScrollDelta(10.0, 400.0, 50.0, 180.0);
                AssertTrue(scrollUp < 0.0, "Cursor near top edge must produce negative scroll delta: " + scrollUp);
                double scrollMid = DashboardWindow.ComputeEdgeAutoScrollDelta(200.0, 400.0, 50.0, 180.0);
                AssertEqual(0.0, scrollMid, "Cursor in center viewport must not auto-scroll");
                double scrollBottomBound = DashboardWindow.ComputeEdgeAutoScrollDelta(395.0, 400.0, 180.0, 180.0);
                AssertEqual(0.0, scrollBottomBound, "Already at bottom bound returns 0");

                tt.SectionsPerDay = 11;
                tt.ShowWeekends = false;
                tt.RowMinHeight = 64;
                tt.Courses[0].ExcludedWeeks.Add(15);

                // 4. Storage round-trip & WPF UI smoke for MIS + Timetable Sticky + Voice Overlay
                var storage = new StorageService(tempDir);
                var controller = new TrayAndAppController(storage);
                controller.Config.MisEnabled = true;
                controller.Data.Timetable = tt;
                storage.SaveData(controller.Data);
                AppData reloaded = storage.LoadData();
                AssertEqual(2, reloaded.Timetable.Courses.Count, "Timetable courses survive JSON roundtrip");
                AssertEqual(11, reloaded.Timetable.SectionsPerDay, "SectionsPerDay survives roundtrip");
                AssertEqual(false, reloaded.Timetable.ShowWeekends, "ShowWeekends survives roundtrip");
                AssertEqual(64, reloaded.Timetable.RowMinHeight, "RowMinHeight survives roundtrip");
                AssertEqual(true, reloaded.Timetable.Courses[0].ExcludedWeeks.Contains(15), "ExcludedWeeks survives roundtrip");

                var dash = new DashboardWindow(controller);
                dash.UpdateMisTabVisibility();
                dash.ApplyThemeAndRefreshAll();

                var ttNote = new StickyNote { Title = "课程表", FilterMode = "timetable" };
                var stickyWin = new StickyNoteWindow(ttNote, controller);
                stickyWin.ApplyThemeAndContent();

                var misDlg = new MisImportDialog(controller);
                var overlay = new VoiceMemoOverlayWindow("claude_warm");
                overlay.SetRecordingHint("Ctrl+Alt+V", true);
                overlay.UpdateAudioLevel(0.65);
                overlay.SetProcessingState("正在转文字…", "test");

                overlay.Close();
                misDlg.Close();
                stickyWin.Close();
                dash.Close();
            }
            finally
            {
                try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
            }
        }
    }
}
