using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace DesktopMemo
{
    public class AiPreset
    {
        public string Key { get; set; }
        public string DisplayName { get; set; }
        public string BaseUrl { get; set; }
        public string DefaultModel { get; set; }

        public AiPreset(string key, string displayName, string baseUrl, string defaultModel)
        {
            Key = key;
            DisplayName = displayName;
            BaseUrl = baseUrl;
            DefaultModel = defaultModel;
        }
    }

    public class AiSortOutcome
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public List<AiSortItem> Items { get; set; }

        public AiSortOutcome()
        {
            Items = new List<AiSortItem>();
            ErrorMessage = "";
        }
    }

    public class AiChatExtractOutcome
    {
        public bool Success { get; set; }
        public string ErrorMessage { get; set; }
        public List<AiExtractedTask> Tasks { get; set; }

        public AiChatExtractOutcome()
        {
            Tasks = new List<AiExtractedTask>();
            ErrorMessage = "";
        }
    }

    public class AiConnectionTestOutcome
    {
        public bool Success { get; set; }
        public string Message { get; set; }
    }

    public static class AiSortService
    {
        public static readonly AiPreset[] Presets = new AiPreset[]
        {
            new AiPreset("deepseek", "DeepSeek (deepseek-chat)", "https://api.deepseek.com/v1", "deepseek-chat"),
            new AiPreset("deepseek_reasoner", "DeepSeek Reasoner (R1)", "https://api.deepseek.com/v1", "deepseek-reasoner"),
            new AiPreset("openai", "OpenAI (gpt-4o-mini)", "https://api.openai.com/v1", "gpt-4o-mini"),
            new AiPreset("siliconflow", "硅基流动 SiliconFlow", "https://api.siliconflow.cn/v1", "deepseek-ai/DeepSeek-V3"),
            new AiPreset("qwen", "阿里通义千问 (兼容接口)", "https://dashscope.aliyuncs.com/compatible-mode/v1", "qwen-plus"),
            new AiPreset("custom", "自定义 OpenAI 兼容接口", "", "")
        };

        public static AiPreset FindPreset(string key)
        {
            for (int i = 0; i < Presets.Length; i++)
            {
                if (Presets[i].Key == key) return Presets[i];
            }
            return Presets[0];
        }

        public static string NormalizeChatEndpoint(string baseUrl)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                return "https://api.deepseek.com/v1/chat/completions";
            }

            string trimmed = baseUrl.Trim().TrimEnd('/');
            if (trimmed.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed;
            }
            if (trimmed.EndsWith("/v1", StringComparison.OrdinalIgnoreCase) ||
                trimmed.EndsWith("/compatible-mode/v1", StringComparison.OrdinalIgnoreCase) ||
                Regex.IsMatch(trimmed, @"/v\d+$", RegexOptions.IgnoreCase))
            {
                return trimmed + "/chat/completions";
            }
            return trimmed + "/v1/chat/completions";
        }

        public static async Task<AiConnectionTestOutcome> TestConnectionAsync(AppConfig config)
        {
            var res = new AiConnectionTestOutcome();
            if (config == null || string.IsNullOrWhiteSpace(config.AiApiKey))
            {
                res.Success = false;
                res.Message = "请先填写 API Key。";
                return res;
            }

            string endpoint = NormalizeChatEndpoint(config.AiBaseUrl);
            string model = string.IsNullOrWhiteSpace(config.AiModel) ? "deepseek-chat" : config.AiModel.Trim();

            try
            {
                var payload = new Dictionary<string, object>();
                payload["model"] = model;
                if (model.IndexOf("reasoner", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    payload["temperature"] = 0.1;
                }
                payload["max_tokens"] = 24;
                payload["messages"] = new object[]
                {
                    new Dictionary<string, object> { { "role", "user" }, { "content", "Reply strictly with: OK" } }
                };

                string content = await SendChatRequestAsync(endpoint, config.AiApiKey.Trim(), payload, 25);
                res.Success = true;
                res.Message = "连接成功！模型返回：" + Truncate(content, 40);
                return res;
            }
            catch (Exception ex)
            {
                res.Success = false;
                res.Message = "连接失败：" + ex.Message;
                return res;
            }
        }

        public static async Task<AiSortOutcome> SortTasksAsync(AppConfig config, List<MemoTask> activeTasks, DateTime now)
        {
            var outcome = new AiSortOutcome();
            if (config == null || string.IsNullOrWhiteSpace(config.AiApiKey))
            {
                outcome.Success = false;
                outcome.ErrorMessage = "尚未配置 API Key，请先到「AI 与设置」中填入 DeepSeek 或兼容接口的 Key。";
                return outcome;
            }

            if (activeTasks == null || activeTasks.Count == 0)
            {
                outcome.Success = false;
                outcome.ErrorMessage = "当前没有未完成的待办事项需要排序。";
                return outcome;
            }

            string endpoint = NormalizeChatEndpoint(config.AiBaseUrl);
            string model = string.IsNullOrWhiteSpace(config.AiModel) ? "deepseek-chat" : config.AiModel.Trim();

            var ser = new JavaScriptSerializer();
            var taskPayloadList = new List<Dictionary<string, object>>();
            for (int i = 0; i < activeTasks.Count; i++)
            {
                MemoTask t = activeTasks[i];
                var item = new Dictionary<string, object>();
                item["id"] = t.Id;
                item["title"] = t.Title;
                if (!string.IsNullOrWhiteSpace(t.Notes)) item["notes"] = Truncate(t.Notes, 120);
                if (!string.IsNullOrWhiteSpace(t.DeadlineIso)) item["deadline"] = t.DeadlineIso;
                item["current_quadrant"] = t.Quadrant;
                taskPayloadList.Add(item);
            }

            string systemPrompt =
                "你是时间管理与四象限（艾森豪威尔矩阵）专家。请根据当前时间、DDL截止紧迫度、任务重要性以及用户偏好，为每个未完成任务（包括 current_quadrant 为 0 的未分类独立便签任务）分配最合适的四象限(1-4)和优先级分数。\n" +
                "四象限定义：\n" +
                "1 = 重要且紧急（马上做：临近或已逾期DDL、高影响核心紧急事务）\n" +
                "2 = 重要不紧急（计划做：高价值学习/研究/长期核心目标，DDL尚充裕）\n" +
                "3 = 紧急不重要（快做/琐事：有时间限制但价值较低的日常事务）\n" +
                "4 = 不重要不紧急（稍后做：无紧迫期限且收益低）\n" +
                "评分规则：score 为 1-100 整数，分数越高越应该先做（建议 Q1: 75-100, Q2: 50-85, Q3: 35-65, Q4: 1-40，并综合DDL先后微调）。\n" +
                "请严格只输出纯 JSON（不要输出解释、Markdown代码块外文字），格式如下：\n" +
                "{\"items\":[{\"id\":\"任务id\",\"quadrant\":1,\"score\":95,\"reason\":\"不超过20字的中文理由\"}]}";

            var userSb = new StringBuilder();
            userSb.Append("当前本地时间：").Append(now.ToString("yyyy-MM-dd HH:mm")).Append(" ").Append(GetChineseWeekday(now)).Append("\n");
            if (!string.IsNullOrWhiteSpace(config.AiCustomInstruction))
            {
                userSb.Append("用户个人优先级偏好：").Append(config.AiCustomInstruction.Trim()).Append("\n");
            }
            userSb.Append("待评估任务列表(JSON)：\n").Append(ser.Serialize(taskPayloadList));

            var payload = new Dictionary<string, object>();
            payload["model"] = model;
            if (model.IndexOf("reasoner", StringComparison.OrdinalIgnoreCase) < 0)
            {
                payload["temperature"] = 0.2;
            }
            payload["messages"] = new object[]
            {
                new Dictionary<string, object> { { "role", "system" }, { "content", systemPrompt } },
                new Dictionary<string, object> { { "role", "user" }, { "content", userSb.ToString() } }
            };

            try
            {
                string rawContent = await SendChatRequestAsync(endpoint, config.AiApiKey.Trim(), payload, 60);
                List<AiSortItem> parsed = ParseAiSortJson(rawContent);
                if (parsed.Count == 0)
                {
                    outcome.Success = false;
                    outcome.ErrorMessage = "AI 返回格式未能解析出排序结果，原始内容片段：" + Truncate(rawContent, 120);
                    return outcome;
                }

                outcome.Success = true;
                outcome.Items = parsed;
                return outcome;
            }
            catch (Exception ex)
            {
                outcome.Success = false;
                outcome.ErrorMessage = "AI 排序请求失败：" + ex.Message;
                return outcome;
            }
        }

        public static async Task<AiChatExtractOutcome> ExtractTasksFromChatAsync(AppConfig config, string rawChatText, DateTime now)
        {
            var outcome = new AiChatExtractOutcome();
            if (config == null || string.IsNullOrWhiteSpace(config.AiApiKey))
            {
                outcome.Success = false;
                outcome.ErrorMessage = "尚未配置 API Key，请先到「AI 与设置」中填入 DeepSeek 或兼容接口的 Key。";
                return outcome;
            }
            if (string.IsNullOrWhiteSpace(rawChatText))
            {
                outcome.Success = false;
                outcome.ErrorMessage = "请先粘贴需要识别提取的聊天记录或通知文本。";
                return outcome;
            }

            string endpoint = NormalizeChatEndpoint(config.AiBaseUrl);
            string model = string.IsNullOrWhiteSpace(config.AiModel) ? "deepseek-chat" : config.AiModel.Trim();

            string systemPrompt =
                "你是智能日程助手。用户会发给你一段微信聊天记录、课程通知、小组分工或会议纪要。\n" +
                "请你从中提取出所有具体的待办任务（去掉寒暄和无关水话），并结合当前日期推算具体 DDL 时间和重要紧急四象限。\n" +
                "四象限：1=重要且紧急，2=重要不紧急，3=紧急不重要，4=不重要不紧急。\n" +
                "日期时间格式严格为 \"yyyy-MM-dd HH:mm\"（如 \"2026-11-24 23:59\"），若文本完全没提时间则留空 \"\"；如可合理建议提前提醒，可填 remind_at（同格式，否则 \"\"）。\n" +
                "请严格只输出纯 JSON，不要任何解释文字：\n" +
                "{\"tasks\":[{\"title\":\"简明任务标题(25字内)\",\"notes\":\"补充细节/提交要求/负责人\",\"deadline\":\"yyyy-MM-dd HH:mm\",\"remind_at\":\"\",\"quadrant\":1,\"reason\":\"归类简述(18字内)\"}]}";

            var userSb = new StringBuilder();
            userSb.Append("当前本地时间：").Append(now.ToString("yyyy-MM-dd HH:mm")).Append(" ").Append(GetChineseWeekday(now)).Append("\n");
            if (!string.IsNullOrWhiteSpace(config.AiCustomInstruction))
            {
                userSb.Append("用户个人优先级偏好：").Append(config.AiCustomInstruction.Trim()).Append("\n");
            }
            userSb.Append("聊天/通知原文：\n").Append(rawChatText.Trim());

            var payload = new Dictionary<string, object>();
            payload["model"] = model;
            if (model.IndexOf("reasoner", StringComparison.OrdinalIgnoreCase) < 0)
            {
                payload["temperature"] = 0.2;
            }
            payload["messages"] = new object[]
            {
                new Dictionary<string, object> { { "role", "system" }, { "content", systemPrompt } },
                new Dictionary<string, object> { { "role", "user" }, { "content", userSb.ToString() } }
            };

            try
            {
                string rawContent = await SendChatRequestAsync(endpoint, config.AiApiKey.Trim(), payload, 65);
                List<AiExtractedTask> parsed = ParseExtractedTasksJson(rawContent);
                if (parsed.Count == 0)
                {
                    outcome.Success = false;
                    outcome.ErrorMessage = "未能在该段文本中识别出明确的待办事项（AI 原始片段：" + Truncate(rawContent, 100) + "）。";
                    return outcome;
                }

                outcome.Success = true;
                outcome.Tasks = parsed;
                return outcome;
            }
            catch (Exception ex)
            {
                outcome.Success = false;
                outcome.ErrorMessage = "AI 识别提取失败：" + ex.Message;
                return outcome;
            }
        }

        private static async Task<string> SendChatRequestAsync(string endpoint, string apiKey, Dictionary<string, object> payload, int timeoutSeconds)
        {
            var ser = new JavaScriptSerializer();
            ser.MaxJsonLength = 8 * 1024 * 1024;
            string jsonBody = ser.Serialize(payload);

            using (var client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

                using (var content = new StringContent(jsonBody, Encoding.UTF8, "application/json"))
                using (HttpResponseMessage resp = await client.PostAsync(endpoint, content))
                {
                    string respBody = await resp.Content.ReadAsStringAsync();
                    if (!resp.IsSuccessStatusCode)
                    {
                        throw new InvalidOperationException(string.Format("HTTP {0}: {1}", (int)resp.StatusCode, Truncate(respBody, 220)));
                    }

                    return ExtractChatCompletionContent(respBody);
                }
            }
        }

        public static string ExtractChatCompletionContent(string respBody)
        {
            var ser = new JavaScriptSerializer();
            ser.MaxJsonLength = 8 * 1024 * 1024;
            Dictionary<string, object> root = ser.Deserialize<Dictionary<string, object>>(respBody);
            if (root == null || !root.ContainsKey("choices"))
            {
                throw new InvalidOperationException("响应缺少 choices 字段。");
            }

            IList choices = root["choices"] as IList;
            if (choices == null || choices.Count == 0)
            {
                throw new InvalidOperationException("响应 choices 为空。");
            }

            Dictionary<string, object> first = choices[0] as Dictionary<string, object>;
            if (first == null || !first.ContainsKey("message"))
            {
                throw new InvalidOperationException("响应缺少 message 字段。");
            }

            Dictionary<string, object> msg = first["message"] as Dictionary<string, object>;
            if (msg == null || !msg.ContainsKey("content") || msg["content"] == null)
            {
                throw new InvalidOperationException("响应 message.content 为空。");
            }

            return msg["content"].ToString();
        }

        public static List<AiSortItem> ParseAiSortJson(string rawContent)
        {
            var result = new List<AiSortItem>();
            string jsonSlice = ExtractCleanedJson(rawContent);
            if (string.IsNullOrWhiteSpace(jsonSlice)) return result;

            try
            {
                var ser = new JavaScriptSerializer();
                object parsed = ser.DeserializeObject(jsonSlice);

                object[] itemsArray = null;
                var dict = parsed as Dictionary<string, object>;
                if (dict != null)
                {
                    if (dict.ContainsKey("items")) itemsArray = dict["items"] as object[];
                    else if (dict.ContainsKey("tasks")) itemsArray = dict["tasks"] as object[];
                }
                else
                {
                    itemsArray = parsed as object[];
                }

                if (itemsArray == null) return result;

                for (int i = 0; i < itemsArray.Length; i++)
                {
                    var row = itemsArray[i] as Dictionary<string, object>;
                    if (row == null || !row.ContainsKey("id") || row["id"] == null) continue;

                    var item = new AiSortItem();
                    item.Id = row["id"].ToString().Trim();
                    item.Quadrant = MemoTask.ClampQuadrant(ReadInt(row, "quadrant", 2));
                    int score = ReadInt(row, "score", 50);
                    if (score < 1) score = 1;
                    if (score > 100) score = 100;
                    item.Score = score;
                    item.Reason = Truncate(ReadString(row, "reason"), 42);
                    result.Add(item);
                }
            }
            catch
            {
                return new List<AiSortItem>();
            }

            return result;
        }

        public static List<AiExtractedTask> ParseExtractedTasksJson(string rawContent)
        {
            var result = new List<AiExtractedTask>();
            string jsonSlice = ExtractCleanedJson(rawContent);
            if (string.IsNullOrWhiteSpace(jsonSlice)) return result;

            try
            {
                var ser = new JavaScriptSerializer();
                object parsed = ser.DeserializeObject(jsonSlice);

                object[] tasksArray = null;
                var dict = parsed as Dictionary<string, object>;
                if (dict != null)
                {
                    if (dict.ContainsKey("tasks")) tasksArray = dict["tasks"] as object[];
                    else if (dict.ContainsKey("items")) tasksArray = dict["items"] as object[];
                }
                else
                {
                    tasksArray = parsed as object[];
                }

                if (tasksArray == null) return result;

                for (int i = 0; i < tasksArray.Length; i++)
                {
                    var row = tasksArray[i] as Dictionary<string, object>;
                    if (row == null) continue;

                    string title = ReadString(row, "title").Trim();
                    if (string.IsNullOrEmpty(title)) continue;

                    var item = new AiExtractedTask();
                    item.Title = Truncate(title, 80);
                    item.Notes = ReadString(row, "notes").Trim();
                    item.Quadrant = MemoTask.NormalizeQuadrant(ReadInt(row, "quadrant", 1));
                    item.Reason = Truncate(ReadString(row, "reason").Trim(), 42);
                    item.Selected = true;

                    DateTime? ddl = MemoTask.ParseIsoDateTime(ReadString(row, "deadline"));
                    if (ddl.HasValue) item.DeadlineIso = MemoTask.FormatIso(ddl);

                    DateTime? rem = MemoTask.ParseIsoDateTime(ReadString(row, "remind_at"));
                    if (rem.HasValue) item.RemindAtIso = MemoTask.FormatIso(rem);

                    result.Add(item);
                }
            }
            catch
            {
                return new List<AiExtractedTask>();
            }

            return result;
        }

        private static string ExtractCleanedJson(string rawContent)
        {
            if (string.IsNullOrWhiteSpace(rawContent)) return "";
            string cleaned = Regex.Replace(rawContent, @"<think>[\s\S]*?</think>", "", RegexOptions.IgnoreCase).Trim();
            Match fenceMatch = Regex.Match(cleaned, @"```(?:json)?\s*([\s\S]*?)\s*```", RegexOptions.IgnoreCase);
            if (fenceMatch.Success)
            {
                cleaned = fenceMatch.Groups[1].Value.Trim();
            }
            return ExtractOutermostJson(cleaned);
        }

        private static string ExtractOutermostJson(string text)
        {
            int objStart = text.IndexOf('{');
            int arrStart = text.IndexOf('[');
            int start;
            char openChar;
            char closeChar;

            if (objStart >= 0 && (arrStart < 0 || objStart < arrStart))
            {
                start = objStart;
                openChar = '{';
                closeChar = '}';
            }
            else if (arrStart >= 0)
            {
                start = arrStart;
                openChar = '[';
                closeChar = ']';
            }
            else
            {
                return "";
            }

            int depth = 0;
            bool inString = false;
            bool escaped = false;
            for (int i = start; i < text.Length; i++)
            {
                char ch = text[i];
                if (inString)
                {
                    if (escaped) escaped = false;
                    else if (ch == '\\') escaped = true;
                    else if (ch == '"') inString = false;
                    continue;
                }

                if (ch == '"')
                {
                    inString = true;
                }
                else if (ch == openChar)
                {
                    depth++;
                }
                else if (ch == closeChar)
                {
                    depth--;
                    if (depth == 0)
                    {
                        return text.Substring(start, i - start + 1);
                    }
                }
            }
            return text.Substring(start);
        }

        private static string ReadString(Dictionary<string, object> row, string key)
        {
            if (row == null || !row.ContainsKey(key) || row[key] == null) return "";
            return row[key].ToString();
        }

        private static int ReadInt(Dictionary<string, object> row, string key, int fallback)
        {
            if (!row.ContainsKey(key) || row[key] == null) return fallback;
            int val;
            if (int.TryParse(row[key].ToString(), out val)) return val;
            double dval;
            if (double.TryParse(row[key].ToString(), out dval)) return (int)Math.Round(dval);
            return fallback;
        }

        private static string GetChineseWeekday(DateTime dt)
        {
            string[] names = new string[] { "星期日", "星期一", "星期二", "星期三", "星期四", "星期五", "星期六" };
            return names[(int)dt.DayOfWeek];
        }

        private static string Truncate(string s, int maxLen)
        {
            if (string.IsNullOrEmpty(s)) return "";
            string singleLine = s.Replace("\r", " ").Replace("\n", " ").Trim();
            if (singleLine.Length <= maxLen) return singleLine;
            return singleLine.Substring(0, maxLen) + "...";
        }
    }
}
