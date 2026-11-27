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

    public class AiTranscriptionOutcome
    {
        public bool Success { get; set; }
        public string Text { get; set; }
        public string ErrorMessage { get; set; }

        public AiTranscriptionOutcome()
        {
            Text = "";
            ErrorMessage = "";
        }
    }

    public class AiTimetableExtractOutcome
    {
        public bool Success { get; set; }
        public string SemesterName { get; set; }
        public int CurrentWeek { get; set; }
        public List<CourseItem> Courses { get; set; }
        public List<SectionTimeSlot> SectionTimes { get; set; }
        public string ErrorMessage { get; set; }

        public AiTimetableExtractOutcome()
        {
            SemesterName = "";
            CurrentWeek = 0;
            Courses = new List<CourseItem>();
            SectionTimes = new List<SectionTimeSlot>();
            ErrorMessage = "";
        }
    }

    public static class AiSortService
    {
        public static readonly AiPreset[] Presets = new AiPreset[]
        {
            new AiPreset("deepseek_flash", "DeepSeek Flash (deepseek-flash · 支持识图)", "https://api.deepseek.com/v1", "deepseek-flash"),
            new AiPreset("deepseek", "DeepSeek (deepseek-chat · 纯文本)", "https://api.deepseek.com/v1", "deepseek-chat"),
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

        public static Task<AiSortOutcome> SortTasksAsync(AppConfig config, List<MemoTask> activeTasks, DateTime now)
        {
            return SortTasksAsync(config, activeTasks, now, null);
        }

        public static async Task<AiSortOutcome> SortTasksAsync(AppConfig config, List<MemoTask> activeTasks, DateTime now, TimetableData timetable)
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
                "你是时间管理与四象限（艾森豪威尔矩阵）专家。请根据当前时间、DDL截止紧迫度、教学周/今日课程安排、任务重要性以及用户偏好，为每个未完成任务（包括 current_quadrant 为 0 的未分类独立便签任务）分配最合适的四象限(1-4)和优先级分数。\n" +
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
            if (timetable != null)
            {
                string weekCtx = timetable.BuildAiWeekContext(now);
                if (!string.IsNullOrEmpty(weekCtx))
                {
                    userSb.Append(weekCtx).Append("\n");
                }
            }
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

        public static Task<AiChatExtractOutcome> ExtractTasksFromChatAsync(AppConfig config, string rawChatText, DateTime now)
        {
            return ExtractTasksFromMultimodalAsync(config, rawChatText, null, now, null);
        }

        public static Task<AiChatExtractOutcome> ExtractTasksFromMultimodalAsync(
            AppConfig config,
            string rawChatText,
            List<string> imageDataUrls,
            DateTime now)
        {
            return ExtractTasksFromMultimodalAsync(config, rawChatText, imageDataUrls, now, null);
        }

        public static async Task<AiChatExtractOutcome> ExtractTasksFromMultimodalAsync(
            AppConfig config,
            string rawChatText,
            List<string> imageDataUrls,
            DateTime now,
            TimetableData timetable)
        {
            var outcome = new AiChatExtractOutcome();
            if (config == null || string.IsNullOrWhiteSpace(config.AiApiKey))
            {
                outcome.Success = false;
                outcome.ErrorMessage = "尚未配置 API Key，请先到「AI 与设置」中填入 DeepSeek 或兼容接口的 Key。";
                return outcome;
            }

            bool hasText = !string.IsNullOrWhiteSpace(rawChatText);
            bool hasImages = imageDataUrls != null && imageDataUrls.Count > 0;
            if (!hasText && !hasImages)
            {
                outcome.Success = false;
                outcome.ErrorMessage = "请先粘贴聊天内容或添加至少一张截图/图片。";
                return outcome;
            }

            string endpoint = NormalizeChatEndpoint(config.AiBaseUrl);
            string model = string.IsNullOrWhiteSpace(config.AiModel) ? "deepseek-flash" : config.AiModel.Trim();

            string systemPrompt =
                "你是智能日程助手。用户会发给你微信聊天记录、语音转写文字、课程通知、小组分工、会议纪要，或者相关通知/日程截图。\n" +
                "请你综合文字与图片中的所有信息，提取出具体的待办任务（去掉寒暄和无关水话），并结合当前日期与校历教学周对照推算具体公历 DDL 时间和重要紧急四象限。\n" +
                "若内容提及「第X周」或「第X周周Y」（如第14周周三），请结合提供的校历换算成公历 yyyy-MM-dd HH:mm，同时可附带 \"teaching_week\":14,\"weekday\":3（1=周一..7=周日）。\n" +
                "四象限：1=重要且紧急，2=重要不紧急，3=紧急不重要，4=不重要不紧急。\n" +
                "日期时间格式严格为 \"yyyy-MM-dd HH:mm\"（如 \"2026-11-24 23:59\"），若完全没提时间则留空 \"\"；如可合理建议提前提醒，可填 remind_at（同格式，否则 \"\"）。\n" +
                "请严格只输出纯 JSON，不要任何解释文字：\n" +
                "{\"tasks\":[{\"title\":\"简明任务标题(25字内)\",\"notes\":\"补充细节/提交要求/地点\",\"deadline\":\"yyyy-MM-dd HH:mm\",\"remind_at\":\"\",\"quadrant\":1,\"teaching_week\":0,\"weekday\":0,\"reason\":\"归类简述(18字内)\"}]}";

            var userSb = new StringBuilder();
            userSb.Append("当前本地时间：").Append(now.ToString("yyyy-MM-dd HH:mm")).Append(" ").Append(GetChineseWeekday(now)).Append("\n");
            if (timetable != null)
            {
                string weekCtx = timetable.BuildAiWeekContext(now);
                if (!string.IsNullOrEmpty(weekCtx))
                {
                    userSb.Append(weekCtx).Append("\n");
                }
            }
            if (!string.IsNullOrWhiteSpace(config.AiCustomInstruction))
            {
                userSb.Append("用户个人优先级偏好：").Append(config.AiCustomInstruction.Trim()).Append("\n");
            }
            if (hasText)
            {
                userSb.Append("内容原文：\n").Append(rawChatText.Trim());
            }
            else
            {
                userSb.Append("请仔细识别图片中的日程安排、会议时间、课程作业、活动地点与截止日期（DDL），整理成待办任务。");
            }

            object userContent;
            if (hasImages)
            {
                var contentBlocks = new List<object>();
                contentBlocks.Add(new Dictionary<string, object>
                {
                    { "type", "text" },
                    { "text", userSb.ToString() }
                });
                for (int i = 0; i < imageDataUrls.Count; i++)
                {
                    string url = imageDataUrls[i];
                    if (string.IsNullOrWhiteSpace(url)) continue;
                    contentBlocks.Add(new Dictionary<string, object>
                    {
                        { "type", "image_url" },
                        { "image_url", new Dictionary<string, object> { { "url", url } } }
                    });
                }
                userContent = contentBlocks.ToArray();
            }
            else
            {
                userContent = userSb.ToString();
            }

            var payload = new Dictionary<string, object>();
            payload["model"] = model;
            if (model.IndexOf("reasoner", StringComparison.OrdinalIgnoreCase) < 0)
            {
                payload["temperature"] = 0.2;
            }
            payload["messages"] = new object[]
            {
                new Dictionary<string, object> { { "role", "system" }, { "content", systemPrompt } },
                new Dictionary<string, object> { { "role", "user" }, { "content", userContent } }
            };

            try
            {
                string rawContent = await SendChatRequestAsync(endpoint, config.AiApiKey.Trim(), payload, hasImages ? 90 : 65);
                List<AiExtractedTask> parsed = ParseExtractedTasksJson(rawContent);
                if (timetable != null)
                {
                    for (int i = 0; i < parsed.Count; i++)
                    {
                        AiExtractedTask t = parsed[i];
                        string combined = ((rawChatText ?? "") + " " + (t.Title ?? "") + " " + (t.Notes ?? "")).Trim();
                        string alignedDdl = timetable.AlignWeekTextToDateIso(
                            combined,
                            t.DeadlineIso,
                            t.TeachingWeek > 0 ? (int?)t.TeachingWeek : null,
                            t.TeachingWeekday > 0 ? (int?)t.TeachingWeekday : null,
                            now);
                        if (!string.IsNullOrEmpty(alignedDdl))
                        {
                            t.DeadlineIso = alignedDdl;
                        }
                    }
                }
                if (parsed.Count == 0)
                {
                    outcome.Success = false;
                    outcome.ErrorMessage = "未能在内容中识别出明确的待办事项（AI 原始片段：" + Truncate(rawContent, 100) + "）。";
                    return outcome;
                }

                outcome.Success = true;
                outcome.Tasks = parsed;
                return outcome;
            }
            catch (Exception ex)
            {
                string msg = ex.Message ?? "";
                if (hasImages && (msg.IndexOf("image_url", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    msg.IndexOf("vision", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    msg.IndexOf("multimodal", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    msg.IndexOf("unknown variant", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    msg += "（提示：当前模型可能不支持识图，请在「AI 与设置」将模型切换为 deepseek-flash）";
                }
                outcome.Success = false;
                outcome.ErrorMessage = "AI 识别提取失败：" + msg;
                return outcome;
            }
        }

        public static string NormalizeAsrEndpoint(AppConfig config, out bool isDeepSeek)
        {
            string baseUrl = config != null && !string.IsNullOrWhiteSpace(config.AiBaseUrl)
                ? config.AiBaseUrl.Trim().TrimEnd('/')
                : "https://api.deepseek.com/v1";
            string preset = config != null && config.AiProviderPreset != null ? config.AiProviderPreset : "";
            isDeepSeek = baseUrl.IndexOf("deepseek", StringComparison.OrdinalIgnoreCase) >= 0
                || preset.StartsWith("deepseek", StringComparison.OrdinalIgnoreCase);

            if (baseUrl.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
            {
                baseUrl = baseUrl.Substring(0, baseUrl.Length - "/chat/completions".Length);
            }
            if (!baseUrl.EndsWith("/v1", StringComparison.OrdinalIgnoreCase) &&
                !Regex.IsMatch(baseUrl, @"/v\d+$", RegexOptions.IgnoreCase))
            {
                baseUrl = baseUrl + "/v1";
            }
            return isDeepSeek ? (baseUrl + "/tools/asr") : (baseUrl + "/audio/transcriptions");
        }

        public static async Task<AiTranscriptionOutcome> TranscribeAudioAsync(AppConfig config, byte[] wavBytes)
        {
            var outcome = new AiTranscriptionOutcome();
            if (config == null || string.IsNullOrWhiteSpace(config.AiApiKey))
            {
                outcome.Success = false;
                outcome.ErrorMessage = "尚未配置 API Key，无法进行语音转文字。";
                return outcome;
            }
            if (wavBytes == null || wavBytes.Length <= 44)
            {
                outcome.Success = false;
                outcome.ErrorMessage = "录音时长过短或未采集到声音。";
                return outcome;
            }

            bool isDeepSeek;
            string endpoint = NormalizeAsrEndpoint(config, out isDeepSeek);

            try
            {
                using (var client = new HttpClient())
                using (var form = new MultipartFormDataContent())
                {
                    client.Timeout = TimeSpan.FromSeconds(60);
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", config.AiApiKey.Trim());

                    var fileContent = new ByteArrayContent(wavBytes);
                    fileContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
                    form.Add(fileContent, "file", "voice_memo.wav");

                    if (!isDeepSeek)
                    {
                        form.Add(new StringContent("whisper-1"), "model");
                    }

                    using (HttpResponseMessage resp = await client.PostAsync(endpoint, form))
                    {
                        string respBody = await resp.Content.ReadAsStringAsync();
                        if (!resp.IsSuccessStatusCode)
                        {
                            outcome.Success = false;
                            outcome.ErrorMessage = string.Format("语音转写接口返回 HTTP {0}: {1}", (int)resp.StatusCode, Truncate(respBody, 180));
                            return outcome;
                        }

                        string text = ExtractTranscriptText(respBody);
                        if (string.IsNullOrWhiteSpace(text))
                        {
                            outcome.Success = false;
                            outcome.ErrorMessage = "未能识别出有效语音内容。";
                            return outcome;
                        }

                        outcome.Success = true;
                        outcome.Text = text.Trim();
                        return outcome;
                    }
                }
            }
            catch (Exception ex)
            {
                outcome.Success = false;
                outcome.ErrorMessage = "语音转写失败：" + ex.Message;
                return outcome;
            }
        }

        private static string ExtractTranscriptText(string respBody)
        {
            if (string.IsNullOrWhiteSpace(respBody)) return "";
            string raw = respBody.Trim();
            if (!raw.StartsWith("{")) return raw;

            try
            {
                var ser = new JavaScriptSerializer();
                var dict = ser.Deserialize<Dictionary<string, object>>(raw);
                if (dict == null) return raw;
                string direct = ReadString(dict, "text");
                if (!string.IsNullOrWhiteSpace(direct)) return direct;
                direct = ReadString(dict, "transcript");
                if (!string.IsNullOrWhiteSpace(direct)) return direct;

                if (dict.ContainsKey("data") && dict["data"] is Dictionary<string, object>)
                {
                    var sub = (Dictionary<string, object>)dict["data"];
                    direct = ReadString(sub, "text");
                    if (!string.IsNullOrWhiteSpace(direct)) return direct;
                }
                if (dict.ContainsKey("result") && dict["result"] is Dictionary<string, object>)
                {
                    var sub = (Dictionary<string, object>)dict["result"];
                    direct = ReadString(sub, "text");
                    if (!string.IsNullOrWhiteSpace(direct)) return direct;
                }
            }
            catch { }
            return "";
        }

        public static async Task<AiTimetableExtractOutcome> ExtractTimetableAsync(
            AppConfig config,
            string pageText,
            string screenshotDataUrl,
            DateTime now)
        {
            var outcome = new AiTimetableExtractOutcome();
            if (config == null || string.IsNullOrWhiteSpace(config.AiApiKey))
            {
                outcome.Success = false;
                outcome.ErrorMessage = "尚未配置 API Key，无法使用 AI 解析课表；你也可以使用下方「导入 .ics / .html」或手动加课。";
                return outcome;
            }

            bool hasText = !string.IsNullOrWhiteSpace(pageText);
            bool hasImage = !string.IsNullOrWhiteSpace(screenshotDataUrl);
            if (!hasText && !hasImage)
            {
                outcome.Success = false;
                outcome.ErrorMessage = "未检测到课表文本或截图内容。";
                return outcome;
            }

            string endpoint = NormalizeChatEndpoint(config.AiBaseUrl);
            string model = string.IsNullOrWhiteSpace(config.AiModel) ? "deepseek-flash" : config.AiModel.Trim();

            string trimmedText = hasText ? pageText.Trim() : "";
            if (trimmedText.Length > 18000) trimmedText = trimmedText.Substring(0, 18000);

            string systemPrompt =
                "你是高校教务课表解析专家。用户会提供教务/MIS系统的结构化课表文本或课表截图（常见为正方、强智、URP、树维或高校自研MIS）。\n" +
                "请严格遵守以下规则并输出纯 JSON：\n" +
                "1. 若页面或截图中可见左侧每节课的具体起止时间（如第1节 08:00-08:45），务必提取到 section_times 数组：[{\"section\":1,\"start_time\":\"08:00\",\"end_time\":\"08:45\"}, ...]。\n" +
                "2. 严格对齐星期列（day_of_week 为 1-7，1=周一..7=周日）与节次（start_section/end_section 为 1-16）。如果输入文本中已经标注了 [周X | 第A-B节 | HH:mm-HH:mm] 坐标，绝对禁止篡改对应星期或节次！\n" +
                "3. 严格区分单双周与不连续周：含「单」「(单)」「单周」则 week_type 必须为 \"odd\"；含「双」「(双)」「双周」则必须为 \"even\"；其余连续周为 \"all\"。若是非连续周次（如 1-4,8,10-12周），在 custom_weeks 填入具体周数数组 [1,2,3,4,8,10,11,12]。\n" +
                "4. 若同一格子内叠放多门课（例如单周A课+双周B课），必须在 courses 中拆分成多条独立对象。\n" +
                "严格只输出纯 JSON，不要任何说明文字：\n" +
                "{\"semester_name\":\"2026秋\",\"current_week\":12,\"section_times\":[{\"section\":1,\"start_time\":\"08:00\",\"end_time\":\"08:45\"}],\"courses\":[{\"name\":\"课程名\",\"teacher\":\"教师\",\"location\":\"教室\",\"day_of_week\":1,\"start_section\":1,\"end_section\":2,\"start_time\":\"08:00\",\"end_time\":\"09:40\",\"start_week\":1,\"end_week\":16,\"week_type\":\"all\",\"custom_weeks\":[]}]}";

            var userSb = new StringBuilder();
            userSb.Append("当前本地日期：").Append(now.ToString("yyyy-MM-dd")).Append(" ").Append(GetChineseWeekday(now)).Append("\n");
            if (!string.IsNullOrEmpty(trimmedText))
            {
                userSb.Append("教务系统课表页面数据：\n").Append(trimmedText);
            }
            else
            {
                userSb.Append("请仔细识别图片中的左侧作息时间列、星期列对齐、课程表格子、课程名、周次范围、单双周、节次与教室/教师信息。");
            }

            object userContent;
            if (hasImage)
            {
                userContent = new object[]
                {
                    new Dictionary<string, object> { { "type", "text" }, { "text", userSb.ToString() } },
                    new Dictionary<string, object> { { "type", "image_url" }, { "image_url", new Dictionary<string, object> { { "url", screenshotDataUrl } } } }
                };
            }
            else
            {
                userContent = userSb.ToString();
            }

            var payload = new Dictionary<string, object>();
            payload["model"] = model;
            if (model.IndexOf("reasoner", StringComparison.OrdinalIgnoreCase) < 0)
            {
                payload["temperature"] = 0.1;
            }
            payload["messages"] = new object[]
            {
                new Dictionary<string, object> { { "role", "system" }, { "content", systemPrompt } },
                new Dictionary<string, object> { { "role", "user" }, { "content", userContent } }
            };

            try
            {
                string rawContent = await SendChatRequestAsync(endpoint, config.AiApiKey.Trim(), payload, 90);
                AiTimetableExtractOutcome parsed = ParseTimetableJson(rawContent);
                if (parsed.Courses.Count == 0)
                {
                    parsed.Success = false;
                    parsed.ErrorMessage = "未能在当前页面或截图中识别到课程安排（请确认已进入个人课表页，或尝试直接贴课表截图）。";
                }
                return parsed;
            }
            catch (Exception ex)
            {
                outcome.Success = false;
                outcome.ErrorMessage = "AI 解析课表失败：" + ex.Message;
                return outcome;
            }
        }

        public static AiTimetableExtractOutcome ParseTimetableJson(string rawContent)
        {
            var outcome = new AiTimetableExtractOutcome();
            string jsonSlice = ExtractCleanedJson(rawContent);
            if (string.IsNullOrWhiteSpace(jsonSlice)) return outcome;

            try
            {
                var ser = new JavaScriptSerializer();
                ser.MaxJsonLength = 8 * 1024 * 1024;
                object parsed = ser.DeserializeObject(jsonSlice);
                IList courseArray = null;
                var rootDict = parsed as Dictionary<string, object>;
                if (rootDict != null)
                {
                    outcome.SemesterName = Truncate(ReadString(rootDict, "semester_name"), 40);
                    outcome.CurrentWeek = ReadInt(rootDict, "current_week", 0);
                    IList stArr = rootDict.ContainsKey("section_times") ? rootDict["section_times"] as IList : null;
                    if (stArr != null)
                    {
                        for (int s = 0; s < stArr.Count; s++)
                        {
                            var stRow = stArr[s] as Dictionary<string, object>;
                            if (stRow == null) continue;
                            int sec = ReadInt(stRow, "section", 0);
                            string stStart = CourseItem.NormalizeTimeToken(ReadString(stRow, "start_time"));
                            string stEnd = CourseItem.NormalizeTimeToken(ReadString(stRow, "end_time"));
                            if (sec >= 1 && sec <= 16 && (!string.IsNullOrEmpty(stStart) || !string.IsNullOrEmpty(stEnd)))
                            {
                                outcome.SectionTimes.Add(new SectionTimeSlot
                                {
                                    Section = sec,
                                    StartTime = stStart,
                                    EndTime = stEnd
                                });
                            }
                        }
                    }
                    if (rootDict.ContainsKey("courses")) courseArray = rootDict["courses"] as IList;
                }
                else
                {
                    courseArray = parsed as IList;
                }

                if (courseArray == null) return outcome;

                for (int i = 0; i < courseArray.Count; i++)
                {
                    var row = courseArray[i] as Dictionary<string, object>;
                    if (row == null) continue;
                    string name = Truncate(ReadString(row, "name"), 50);
                    if (string.IsNullOrWhiteSpace(name)) continue;

                    var c = new CourseItem();
                    c.Name = name;
                    c.Teacher = Truncate(ReadString(row, "teacher"), 30);
                    c.Location = Truncate(ReadString(row, "location"), 40);
                    c.DayOfWeek = Math.Max(1, Math.Min(7, ReadInt(row, "day_of_week", 1)));
                    c.StartSection = Math.Max(1, Math.Min(16, ReadInt(row, "start_section", 1)));
                    c.EndSection = Math.Max(c.StartSection, Math.Min(16, ReadInt(row, "end_section", c.StartSection)));
                    c.StartTime = CourseItem.NormalizeTimeToken(ReadString(row, "start_time"));
                    c.EndTime = CourseItem.NormalizeTimeToken(ReadString(row, "end_time"));
                    c.StartWeek = Math.Max(1, ReadInt(row, "start_week", 1));
                    c.EndWeek = Math.Max(c.StartWeek, Math.Min(30, ReadInt(row, "end_week", 16)));
                    c.WeekType = CourseItem.NormalizeWeekType(ReadString(row, "week_type"));

                    // Recover week ranges / odd-even markers if AI put them in helper fields or course name
                    string weekHint = (ReadString(row, "weeks") + " " + ReadString(row, "week_text") + " " + ReadString(row, "raw_weeks")).Trim();
                    if (!string.IsNullOrWhiteSpace(weekHint))
                    {
                        int pSw, pEw;
                        string pWt;
                        List<int> pCw;
                        if (CourseItem.TryParseWeekSpec(weekHint, out pSw, out pEw, out pWt, out pCw))
                        {
                            c.StartWeek = pSw;
                            c.EndWeek = pEw;
                            if (pWt != "all" || c.WeekType == "all") c.WeekType = pWt;
                            if (pCw != null && pCw.Count > 0) c.CustomWeeks = pCw;
                        }
                    }
                    if (c.WeekType == "all")
                    {
                        string wtHint = CourseItem.NormalizeWeekType(name + " " + weekHint);
                        if (wtHint != "all") c.WeekType = wtHint;
                    }

                    IList cwArr = row.ContainsKey("custom_weeks") ? row["custom_weeks"] as IList : null;
                    if (cwArr != null)
                    {
                        for (int k = 0; k < cwArr.Count; k++)
                        {
                            if (cwArr[k] == null) continue;
                            int wn;
                            if (int.TryParse(cwArr[k].ToString(), out wn) && wn >= 1 && wn <= 30 && !c.CustomWeeks.Contains(wn))
                            {
                                c.CustomWeeks.Add(wn);
                            }
                        }
                        if (c.CustomWeeks.Count > 0)
                        {
                            c.CustomWeeks.Sort();
                            c.StartWeek = c.CustomWeeks[0];
                            c.EndWeek = c.CustomWeeks[c.CustomWeeks.Count - 1];
                        }
                    }

                    c.ColorHex = PickCoursePaletteHex(c.Name);
                    outcome.Courses.Add(c);
                }

                // Backfill missing start/end times on parsed courses from section_times
                if (outcome.SectionTimes.Count > 0)
                {
                    var slotBySec = new Dictionary<int, SectionTimeSlot>();
                    for (int s = 0; s < outcome.SectionTimes.Count; s++)
                    {
                        slotBySec[outcome.SectionTimes[s].Section] = outcome.SectionTimes[s];
                    }
                    for (int i = 0; i < outcome.Courses.Count; i++)
                    {
                        CourseItem c = outcome.Courses[i];
                        SectionTimeSlot sSlot, eSlot;
                        if (string.IsNullOrEmpty(c.StartTime) && slotBySec.TryGetValue(c.StartSection, out sSlot) && !string.IsNullOrEmpty(sSlot.StartTime))
                        {
                            c.StartTime = sSlot.StartTime;
                        }
                        if (string.IsNullOrEmpty(c.EndTime) && slotBySec.TryGetValue(c.EndSection, out eSlot) && !string.IsNullOrEmpty(eSlot.EndTime))
                        {
                            c.EndTime = eSlot.EndTime;
                        }
                    }
                }

                outcome.Success = outcome.Courses.Count > 0;
                return outcome;
            }
            catch
            {
                return outcome;
            }
        }

        private static string PickCoursePaletteHex(string courseName)
        {
            string[] palette = new string[]
            {
                "#2563EB", "#D97706", "#059669", "#7C3AED",
                "#DC2626", "#0891B2", "#BE4B62", "#4F46E5"
            };
            if (string.IsNullOrEmpty(courseName)) return palette[0];
            int hash = 17;
            for (int i = 0; i < courseName.Length; i++)
            {
                hash = unchecked(hash * 31 + courseName[i]);
            }
            int idx = (hash & 0x7FFFFFFF) % palette.Length;
            return palette[idx];
        }

        private static async Task<string> SendChatRequestAsync(string endpoint, string apiKey, Dictionary<string, object> payload, int timeoutSeconds)
        {
            var ser = new JavaScriptSerializer();
            ser.MaxJsonLength = 32 * 1024 * 1024;
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
                    item.TeachingWeek = ReadInt(row, "teaching_week", 0);
                    item.TeachingWeekday = ReadInt(row, "weekday", 0);
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
