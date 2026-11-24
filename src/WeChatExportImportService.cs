using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace DesktopMemo
{
    public class WeChatImportResult
    {
        public string FormattedText { get; set; }
        public List<string> MatchedImagePaths { get; set; }
        public string SourceSummary { get; set; }
        public int KeptMessages { get; set; }
        public int SkippedNoise { get; set; }

        public WeChatImportResult()
        {
            FormattedText = "";
            MatchedImagePaths = new List<string>();
            SourceSummary = "";
        }
    }

    public static class WeChatExportImportService
    {
        private static readonly Regex DateRegex = new Regex(
            @"(?<!\d)(20\d{2}[-/]\d{1,2}[-/]\d{1,2})(?:\s+(\d{1,2}:\d{2})(?::\d{2})?)?",
            RegexOptions.Compiled);

        private static readonly Regex ImgTagRegex = new Regex(
            @"\[图片\s*(\d{1,6})\]",
            RegexOptions.Compiled);

        private static readonly Regex MdImgRegex = new Regex(
            @"!\[[^\]]*\]\(([^)]+)\)",
            RegexOptions.Compiled);

        public static string ResolveWeChatExportExe(AppConfig cfg)
        {
            if (cfg != null && !string.IsNullOrWhiteSpace(cfg.WeChatExportExePath))
            {
                string explicitPath = cfg.WeChatExportExePath.Trim().Trim('"');
                if (File.Exists(explicitPath)) return explicitPath;
            }

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string direct = Path.Combine(baseDir, @"plugins\WeChatExportPlus\WeChatExportPlus.exe");
            if (File.Exists(direct)) return direct;

            string pluginRoot = Path.Combine(baseDir, @"plugins\WeChatExportPlus");
            if (Directory.Exists(pluginRoot))
            {
                try
                {
                    string[] hits = Directory.GetFiles(pluginRoot, "WeChatExportPlus.exe", SearchOption.AllDirectories);
                    if (hits != null && hits.Length > 0) return hits[0];
                }
                catch { }
            }
            return "";
        }

        public static string ResolveDefaultExportSearchRoot(AppConfig cfg)
        {
            if (cfg != null && !string.IsNullOrWhiteSpace(cfg.WeChatExportOutputDir))
            {
                string explicitDir = cfg.WeChatExportOutputDir.Trim().Trim('"');
                if (Directory.Exists(explicitDir)) return explicitDir;
            }

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string[] candidates = new string[]
            {
                Path.Combine(baseDir, @"plugins\WeChatExportPlus\exports"),
                Path.Combine(baseDir, @"plugins\WeChatExportPlus\output"),
                Path.Combine(baseDir, @"plugins\WeChatExportPlus\导出"),
                Path.Combine(baseDir, @"plugins\WeChatExportPlus")
            };
            for (int i = 0; i < candidates.Length; i++)
            {
                if (Directory.Exists(candidates[i])) return candidates[i];
            }
            return "";
        }

        public static string FindLatestExportDirectory(string baseDir)
        {
            if (string.IsNullOrWhiteSpace(baseDir) || !Directory.Exists(baseDir)) return "";
            try
            {
                string bestDir = "";
                DateTime bestTime = DateTime.MinValue;
                var allFiles = Directory.GetFiles(baseDir, "*.*", SearchOption.AllDirectories);
                for (int i = 0; i < allFiles.Length; i++)
                {
                    string f = allFiles[i];
                    if (!IsSupportedExportFile(f)) continue;
                    DateTime wt = File.GetLastWriteTimeUtc(f);
                    if (wt > bestTime)
                    {
                        bestTime = wt;
                        bestDir = Path.GetDirectoryName(f);
                    }
                }
                return bestDir;
            }
            catch
            {
                return "";
            }
        }

        public static bool IsSupportedExportFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return false;
            string name = Path.GetFileName(filePath);
            if (name.Equals("README.md", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("使用说明.md", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            string ext = Path.GetExtension(name).ToLowerInvariant();
            return ext == ".md" || ext == ".jsonl" || ext == ".txt";
        }

        public static WeChatImportResult ImportFromPath(string fileOrDirPath, int recentDays, int maxChars)
        {
            var result = new WeChatImportResult();
            if (string.IsNullOrWhiteSpace(fileOrDirPath)) return result;

            var sourceFiles = new List<string>();
            if (File.Exists(fileOrDirPath))
            {
                if (IsSupportedExportFile(fileOrDirPath)) sourceFiles.Add(fileOrDirPath);
            }
            else if (Directory.Exists(fileOrDirPath))
            {
                string[] files = Directory.GetFiles(fileOrDirPath, "*.*", SearchOption.AllDirectories);
                var jsonls = new List<string>();
                var mds = new List<string>();
                var txts = new List<string>();
                for (int i = 0; i < files.Length; i++)
                {
                    string f = files[i];
                    if (!IsSupportedExportFile(f)) continue;
                    string ext = Path.GetExtension(f).ToLowerInvariant();
                    if (ext == ".jsonl") jsonls.Add(f);
                    else if (ext == ".md") mds.Add(f);
                    else if (ext == ".txt" && !Path.GetFileName(f).Equals("AI提示词.txt", StringComparison.OrdinalIgnoreCase)) txts.Add(f);
                }
                // Prefer structured .jsonl per folder, else .md, else .txt
                if (mds.Count > 0) sourceFiles.AddRange(mds);
                else if (jsonls.Count > 0) sourceFiles.AddRange(jsonls);
                else sourceFiles.AddRange(txts);
            }

            if (sourceFiles.Count == 0)
            {
                result.SourceSummary = "未找到可导入的 .md / .jsonl / .txt 导出文件";
                return result;
            }

            DateTime? cutoff = null;
            if (recentDays > 0)
            {
                cutoff = DateTime.Today.AddDays(-(recentDays - 1));
            }

            var sb = new StringBuilder();
            var matchedImgs = new List<string>();
            var imgSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int maxFiles = Math.Min(12, sourceFiles.Count);

            for (int fIdx = 0; fIdx < maxFiles; fIdx++)
            {
                string file = sourceFiles[fIdx];
                string dir = Path.GetDirectoryName(file);
                string ext = Path.GetExtension(file).ToLowerInvariant();
                string sessionLabel = Path.GetFileNameWithoutExtension(file);

                var fileLines = new List<string>();
                if (ext == ".jsonl")
                {
                    ParseJsonlFile(file, dir, cutoff, fileLines, matchedImgs, imgSeen, result);
                }
                else
                {
                    ParseMarkdownOrTextFile(file, dir, cutoff, fileLines, matchedImgs, imgSeen, result);
                }

                if (fileLines.Count > 0)
                {
                    sb.Append("=== 会话：").Append(sessionLabel).Append(" ===\r\n");
                    for (int k = 0; k < fileLines.Count; k++)
                    {
                        sb.Append(fileLines[k]).Append("\r\n");
                    }
                    sb.Append("\r\n");
                }
            }

            string text = sb.ToString().Trim();
            if (maxChars > 0 && text.Length > maxChars)
            {
                int tailStart = text.Length - maxChars;
                int lineBreak = text.IndexOf('\n', tailStart);
                if (lineBreak > 0 && lineBreak < text.Length - 20) tailStart = lineBreak + 1;
                text = "[...仅保留最近聊天记录...]\r\n" + text.Substring(tailStart);
            }

            // Keep most recent images up to 12
            if (matchedImgs.Count > 12)
            {
                matchedImgs = matchedImgs.GetRange(matchedImgs.Count - 12, 12);
            }

            result.FormattedText = text;
            result.MatchedImagePaths = matchedImgs;
            result.SourceSummary = string.Format("已读取 {0} 个会话文件，保留 {1} 条有效消息，过滤 {2} 条噪音，匹配 {3} 张图片",
                maxFiles, result.KeptMessages, result.SkippedNoise, matchedImgs.Count);
            return result;
        }

        private static void ParseJsonlFile(
            string filePath,
            string dir,
            DateTime? cutoff,
            List<string> outLines,
            List<string> matchedImgs,
            HashSet<string> imgSeen,
            WeChatImportResult stats)
        {
            var ser = new JavaScriptSerializer();
            string[] lines = File.ReadAllLines(filePath, Encoding.UTF8);
            for (int i = 0; i < lines.Length; i++)
            {
                string raw = (lines[i] ?? "").Trim();
                if (string.IsNullOrEmpty(raw)) continue;

                Dictionary<string, object> d = null;
                try { d = ser.Deserialize<Dictionary<string, object>>(raw); }
                catch { d = null; }
                if (d == null) continue;

                string timeStr = PickStr(d, "time", "timestamp", "create_time", "datetime", "date");
                DateTime? msgTime = TryParseTimeString(timeStr);
                if (cutoff.HasValue && msgTime.HasValue && msgTime.Value < cutoff.Value) continue;

                string sender = PickStr(d, "sender_name", "display_name", "sender", "nickname", "from", "wxid");
                string msgType = PickStr(d, "type", "msg_type", "kind");
                string content = PickStr(d, "content", "text", "message", "body");
                string imgField = PickStr(d, "image", "image_path", "media_path", "file", "local_path");

                bool hasImgInLine = CollectImagesFromTextAndField(content, imgField, dir, matchedImgs, imgSeen);
                if (IsNoiseMessage(content, msgType, hasImgInLine))
                {
                    stats.SkippedNoise++;
                    continue;
                }

                stats.KeptMessages++;
                var lineSb = new StringBuilder();
                if (!string.IsNullOrEmpty(timeStr)) lineSb.Append("[").Append(timeStr).Append("] ");
                if (!string.IsNullOrEmpty(sender)) lineSb.Append(sender).Append(": ");
                lineSb.Append(content.Trim());
                outLines.Add(lineSb.ToString());
            }
        }

        private static void ParseMarkdownOrTextFile(
            string filePath,
            string dir,
            DateTime? cutoff,
            List<string> outLines,
            List<string> matchedImgs,
            HashSet<string> imgSeen,
            WeChatImportResult stats)
        {
            string[] lines = File.ReadAllLines(filePath, Encoding.UTF8);
            DateTime? currentDate = null;

            for (int i = 0; i < lines.Length; i++)
            {
                string line = (lines[i] ?? "").Trim();
                if (string.IsNullOrEmpty(line)) continue;
                if (line.StartsWith("<!--") || line == "---") continue;

                Match mDate = DateRegex.Match(line);
                if (mDate.Success)
                {
                    DateTime parsed;
                    string norm = mDate.Groups[1].Value.Replace('/', '-');
                    if (DateTime.TryParseExact(norm, new[] { "yyyy-MM-dd", "yyyy-M-d" }, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out parsed))
                    {
                        currentDate = parsed;
                    }
                }

                if (cutoff.HasValue && currentDate.HasValue && currentDate.Value < cutoff.Value.Date)
                {
                    continue;
                }

                bool hasImgInLine = CollectImagesFromTextAndField(line, "", dir, matchedImgs, imgSeen);
                if (IsNoiseMessage(line, "", hasImgInLine))
                {
                    stats.SkippedNoise++;
                    continue;
                }

                stats.KeptMessages++;
                outLines.Add(line);
            }
        }

        private static bool CollectImagesFromTextAndField(
            string text,
            string explicitField,
            string baseDir,
            List<string> matchedImgs,
            HashSet<string> imgSeen)
        {
            bool found = false;
            if (!string.IsNullOrWhiteSpace(explicitField))
            {
                string resolved = ResolveImageCandidate(baseDir, explicitField);
                if (!string.IsNullOrEmpty(resolved) && imgSeen.Add(resolved))
                {
                    matchedImgs.Add(resolved);
                    found = true;
                }
            }

            if (!string.IsNullOrWhiteSpace(text))
            {
                MatchCollection mdMatches = MdImgRegex.Matches(text);
                for (int i = 0; i < mdMatches.Count; i++)
                {
                    string rel = mdMatches[i].Groups[1].Value.Trim().Trim('"');
                    string resolved = ResolveImageCandidate(baseDir, rel);
                    if (!string.IsNullOrEmpty(resolved) && imgSeen.Add(resolved))
                    {
                        matchedImgs.Add(resolved);
                        found = true;
                    }
                }

                MatchCollection tagMatches = ImgTagRegex.Matches(text);
                for (int i = 0; i < tagMatches.Count; i++)
                {
                    string num = tagMatches[i].Groups[1].Value;
                    string padded = num.PadLeft(4, '0');
                    string[] exts = new string[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" };
                    for (int e = 0; e < exts.Length; e++)
                    {
                        string resolved = ResolveImageCandidate(baseDir, padded + exts[e]);
                        if (string.IsNullOrEmpty(resolved)) resolved = ResolveImageCandidate(baseDir, num + exts[e]);
                        if (!string.IsNullOrEmpty(resolved) && imgSeen.Add(resolved))
                        {
                            matchedImgs.Add(resolved);
                            found = true;
                            break;
                        }
                    }
                }
            }

            return found;
        }

        private static string ResolveImageCandidate(string baseDir, string candidate)
        {
            if (string.IsNullOrWhiteSpace(candidate)) return "";
            string clean = candidate.Trim().Trim('"').Replace('/', Path.DirectorySeparatorChar);
            if (Path.IsPathRooted(clean) && File.Exists(clean) && ImageAttachmentHelper.IsSupportedImageExtension(clean))
            {
                return clean;
            }
            if (string.IsNullOrWhiteSpace(baseDir)) return "";

            string[] subDirs = new string[] { "", "images", "media", "pics", "图片", "files" };
            string fileNameOnly = Path.GetFileName(clean);
            for (int i = 0; i < subDirs.Length; i++)
            {
                string dir = string.IsNullOrEmpty(subDirs[i]) ? baseDir : Path.Combine(baseDir, subDirs[i]);
                if (!Directory.Exists(dir)) continue;

                string p1 = Path.Combine(dir, clean);
                if (File.Exists(p1) && ImageAttachmentHelper.IsSupportedImageExtension(p1)) return p1;

                if (!string.IsNullOrEmpty(fileNameOnly) && fileNameOnly != clean)
                {
                    string p2 = Path.Combine(dir, fileNameOnly);
                    if (File.Exists(p2) && ImageAttachmentHelper.IsSupportedImageExtension(p2)) return p2;
                }
            }
            return "";
        }

        private static bool IsNoiseMessage(string content, string msgType, bool hasMatchedImage)
        {
            if (hasMatchedImage) return false;
            string t = (content ?? "").Trim();
            if (string.IsNullOrEmpty(t)) return true;

            if (t == "[表情]" || t == "[动画表情]" || t == "[语音]" || t == "[视频]" || t == "[图片]") return true;
            if (t.IndexOf("撤回了一条消息", StringComparison.Ordinal) >= 0) return true;
            if (t.IndexOf("拍了拍", StringComparison.Ordinal) >= 0 && t.Length <= 24) return true;
            if (t.EndsWith(": [表情]") || t.EndsWith("：[表情]") || t.EndsWith(": [动画表情]") || t.EndsWith("：[动画表情]")) return true;
            if (t.EndsWith(": [语音]") || t.EndsWith("：[语音]")) return true;

            string mt = (msgType ?? "").ToLowerInvariant();
            if ((mt == "emoji" || mt == "sticker" || mt == "voice" || mt == "revoke") && t.Length <= 16) return true;
            return false;
        }

        private static DateTime? TryParseTimeString(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            DateTime dt;
            if (DateTime.TryParse(s.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out dt)) return dt;
            if (DateTime.TryParse(s.Trim(), CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out dt)) return dt;
            long unix;
            if (long.TryParse(s.Trim(), out unix) && unix > 1000000000L)
            {
                DateTime epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                return unix > 99999999999L
                    ? epoch.AddMilliseconds(unix).ToLocalTime()
                    : epoch.AddSeconds(unix).ToLocalTime();
            }
            return null;
        }

        private static string PickStr(Dictionary<string, object> d, params string[] keys)
        {
            for (int i = 0; i < keys.Length; i++)
            {
                string k = keys[i];
                if (d.ContainsKey(k) && d[k] != null)
                {
                    string v = d[k].ToString();
                    if (!string.IsNullOrWhiteSpace(v)) return v;
                }
            }
            return "";
        }
    }
}
