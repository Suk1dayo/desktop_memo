using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace DesktopMemo
{
    public static class ServerProgram
    {
        private static StorageService _storage;
        private static readonly object _stateLock = new object();
        private static string _hmacSecret;
        private static string _webRoot;
        private static int _port = 8765;
        private static string _cliPasswordOverride = null;

        public static int Main(string[] args)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | (SecurityProtocolType)3072;

            string dataRoot = null;
            if (args != null)
            {
                for (int i = 0; i < args.Length; i++)
                {
                    string a = args[i] ?? "";
                    if (a.StartsWith("--port=", StringComparison.OrdinalIgnoreCase))
                    {
                        int p;
                        if (int.TryParse(a.Substring("--port=".Length), out p) && p >= 1 && p <= 65535) _port = p;
                    }
                    else if (a.StartsWith("--password=", StringComparison.OrdinalIgnoreCase))
                    {
                        _cliPasswordOverride = a.Substring("--password=".Length);
                    }
                    else if (a.StartsWith("--data-root=", StringComparison.OrdinalIgnoreCase))
                    {
                        dataRoot = a.Substring("--data-root=".Length).Trim('"');
                    }
                }
            }

            _storage = new StorageService(dataRoot);
            AppConfig cfg = _storage.LoadConfig();

            bool portSpecifiedOnCli = args != null && args.Any(x => (x ?? "").StartsWith("--port=", StringComparison.OrdinalIgnoreCase));
            if (!portSpecifiedOnCli && cfg.ServerPort >= 1024 && cfg.ServerPort <= 65535)
            {
                _port = cfg.ServerPort;
            }

            if (!string.IsNullOrEmpty(_cliPasswordOverride))
            {
                cfg.ServerPassword = _cliPasswordOverride;
                _storage.SaveConfig(cfg);
            }
            else if (string.IsNullOrWhiteSpace(cfg.ServerPassword))
            {
                cfg.ServerPassword = GenerateRandomCode(8);
                _storage.SaveConfig(cfg);
            }

            _hmacSecret = "DM_" + cfg.ServerPassword + "_" + Environment.MachineName;
            _webRoot = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "web"));

            TcpListener listener = new TcpListener(IPAddress.Any, _port);
            listener.Start();

            Console.OutputEncoding = Encoding.UTF8;
            Console.WriteLine("==================================================");
            Console.WriteLine("  DesktopMemo Web Server (Claude-Style UI)");
            Console.WriteLine("  Local URL  : http://127.0.0.1:" + _port + "/");
            Console.WriteLine("  Network URL: http://0.0.0.0:" + _port + "/");
            Console.WriteLine("  Data Dir   : " + _storage.DataDirectory);
            Console.WriteLine("  Password   : " + cfg.ServerPassword);
            Console.WriteLine("  Press Ctrl+C to stop.");
            Console.WriteLine("==================================================");

            while (true)
            {
                try
                {
                    TcpClient client = listener.AcceptTcpClient();
                    ThreadPool.QueueUserWorkItem(delegate(object state)
                    {
                        HandleClient((TcpClient)state);
                    }, client);
                }
                catch (Exception ex)
                {
                    _storage.LogError("AcceptTcpClient", ex);
                    Thread.Sleep(100);
                }
            }
        }

        private static void HandleClient(TcpClient client)
        {
            using (client)
            {
                try
                {
                    client.ReceiveTimeout = 15000;
                    client.SendTimeout = 15000;
                    using (NetworkStream stream = client.GetStream())
                    {
                        HttpRequest req = ReadHttpRequest(stream);
                        if (req == null) return;

                        if (req.Method == "OPTIONS")
                        {
                            WriteResponse(stream, 204, "No Content", "text/plain", new byte[0]);
                            return;
                        }

                        if (req.Path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
                        {
                            HandleApiRequest(stream, req);
                        }
                        else
                        {
                            HandleStaticFile(stream, req.Path);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _storage.LogError("HandleClient", ex);
                }
            }
        }

        private class HttpRequest
        {
            public string Method;
            public string Path;
            public Dictionary<string, string> Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public string Body = "";
        }

        private static HttpRequest ReadHttpRequest(NetworkStream stream)
        {
            var buf = new byte[8192];
            var ms = new MemoryStream();
            int headerEnd = -1;

            while (ms.Length < 1024 * 1024)
            {
                int read = stream.Read(buf, 0, buf.Length);
                if (read <= 0) break;
                ms.Write(buf, 0, read);
                byte[] arr = ms.ToArray();
                headerEnd = IndexOfBytes(arr, new byte[] { 13, 10, 13, 10 });
                if (headerEnd >= 0) break;
            }

            if (headerEnd < 0) return null;

            byte[] allBytes = ms.ToArray();
            string headerText = Encoding.ASCII.GetString(allBytes, 0, headerEnd);
            string[] lines = headerText.Split(new string[] { "\r\n" }, StringSplitOptions.None);
            if (lines.Length == 0) return null;

            string[] parts = lines[0].Split(' ');
            if (parts.Length < 2) return null;

            var req = new HttpRequest();
            req.Method = parts[0].ToUpperInvariant();
            string rawUrl = parts[1];
            int qIdx = rawUrl.IndexOf('?');
            req.Path = qIdx >= 0 ? rawUrl.Substring(0, qIdx) : rawUrl;

            for (int i = 1; i < lines.Length; i++)
            {
                int colon = lines[i].IndexOf(':');
                if (colon > 0)
                {
                    string k = lines[i].Substring(0, colon).Trim();
                    string v = lines[i].Substring(colon + 1).Trim();
                    req.Headers[k] = v;
                }
            }

            int contentLength = 0;
            if (req.Headers.ContainsKey("Content-Length"))
            {
                int.TryParse(req.Headers["Content-Length"], out contentLength);
            }

            int bodyStart = headerEnd + 4;
            int alreadyHave = allBytes.Length - bodyStart;
            if (contentLength > 0 && contentLength <= 16 * 1024 * 1024)
            {
                var bodyMs = new MemoryStream();
                if (alreadyHave > 0)
                {
                    bodyMs.Write(allBytes, bodyStart, Math.Min(alreadyHave, contentLength));
                }
                while (bodyMs.Length < contentLength)
                {
                    int need = (int)Math.Min(buf.Length, contentLength - bodyMs.Length);
                    int r = stream.Read(buf, 0, need);
                    if (r <= 0) break;
                    bodyMs.Write(buf, 0, r);
                }
                req.Body = Encoding.UTF8.GetString(bodyMs.ToArray());
            }

            return req;
        }

        private static void HandleApiRequest(NetworkStream stream, HttpRequest req)
        {
            var ser = new JavaScriptSerializer { MaxJsonLength = 32 * 1024 * 1024 };

            if (req.Method == "GET" && req.Path == "/api/health")
            {
                WriteJson(stream, 200, new Dictionary<string, object>
                {
                    { "ok", true },
                    { "version", "2.01" },
                    { "time", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") }
                });
                return;
            }

            if (req.Method == "POST" && req.Path == "/api/login")
            {
                var bodyDict = SafeDeserializeDict(ser, req.Body);
                string pass = ReadDictStr(bodyDict, "password");
                AppConfig cfg;
                lock (_stateLock) { cfg = _storage.LoadConfig(); }
                if (!string.Equals(pass, cfg.ServerPassword ?? "", StringComparison.Ordinal))
                {
                    WriteJsonError(stream, 401, "访问口令不正确。");
                    return;
                }

                string token = IssueToken();
                WriteJson(stream, 200, new Dictionary<string, object>
                {
                    { "ok", true },
                    { "token", token }
                });
                return;
            }

            if (!VerifyRequestAuth(req))
            {
                WriteJsonError(stream, 401, "未登录或登录已过期，请先输入访问口令。");
                return;
            }

            if (req.Method == "GET" && req.Path == "/api/state")
            {
                AppData data;
                AppConfig cfg;
                lock (_stateLock)
                {
                    data = _storage.LoadData();
                    cfg = _storage.LoadConfig();
                }

                WriteJson(stream, 200, new Dictionary<string, object>
                {
                    { "tasks", data.Tasks },
                    { "stickyNotes", data.StickyNotes },
                    { "serverTime", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") },
                    { "ai", new Dictionary<string, object>
                        {
                            { "preset", cfg.AiProviderPreset },
                            { "baseUrl", cfg.AiBaseUrl },
                            { "model", cfg.AiModel },
                            { "customInstruction", cfg.AiCustomInstruction },
                            { "hasApiKey", !string.IsNullOrWhiteSpace(cfg.AiApiKey) }
                        }
                    }
                });
                return;
            }

            if (req.Method == "POST" && req.Path == "/api/tasks")
            {
                var d = SafeDeserializeDict(ser, req.Body);
                string title = ReadDictStr(d, "title").Trim();
                if (string.IsNullOrEmpty(title))
                {
                    WriteJsonError(stream, 400, "标题不能为空。");
                    return;
                }
                string singleReason = ReadDictStr(d, "aiReason");
                if (string.IsNullOrEmpty(singleReason)) singleReason = ReadDictStr(d, "reason");
                var t = new MemoTask
                {
                    Title = title,
                    Notes = ReadDictStr(d, "notes").Trim(),
                    DeadlineIso = MemoTask.FormatIso(MemoTask.ParseIsoDateTime(ReadDictStr(d, "deadlineIso"))),
                    RemindAtIso = MemoTask.FormatIso(MemoTask.ParseIsoDateTime(ReadDictStr(d, "remindAtIso"))),
                    Quadrant = MemoTask.NormalizeQuadrant(ReadDictInt(d, "quadrant", 0)),
                    NoteId = ReadDictStr(d, "noteId"),
                    AiReason = singleReason
                };
                lock (_stateLock)
                {
                    AppData data = _storage.LoadData();
                    data.Tasks.Add(t);
                    _storage.SaveData(data);
                }
                WriteJson(stream, 200, new Dictionary<string, object> { { "ok", true }, { "task", t } });
                return;
            }

            if (req.Method == "POST" && req.Path == "/api/tasks/batch")
            {
                var d = SafeDeserializeDict(ser, req.Body);
                List<Dictionary<string, object>> items = ExtractDictList(d, "tasks");
                if (items == null)
                {
                    WriteJsonError(stream, 400, "缺少 tasks 数组。");
                    return;
                }

                var added = new List<MemoTask>();
                lock (_stateLock)
                {
                    AppData data = _storage.LoadData();
                    for (int i = 0; i < items.Count; i++)
                    {
                        var row = items[i];
                        if (row == null) continue;
                        string title = ReadDictStr(row, "title").Trim();
                        if (string.IsNullOrEmpty(title)) continue;
                        string reason = ReadDictStr(row, "aiReason");
                        if (string.IsNullOrEmpty(reason)) reason = ReadDictStr(row, "reason");

                        var t = new MemoTask
                        {
                            Title = title,
                            Notes = ReadDictStr(row, "notes").Trim(),
                            DeadlineIso = MemoTask.FormatIso(MemoTask.ParseIsoDateTime(ReadDictStr(row, "deadlineIso"))),
                            RemindAtIso = MemoTask.FormatIso(MemoTask.ParseIsoDateTime(ReadDictStr(row, "remindAtIso"))),
                            Quadrant = MemoTask.NormalizeQuadrant(ReadDictInt(row, "quadrant", 0)),
                            AiReason = reason,
                            NoteId = ReadDictStr(row, "noteId")
                        };
                        data.Tasks.Add(t);
                        added.Add(t);
                    }
                    _storage.SaveData(data);
                }
                WriteJson(stream, 200, new Dictionary<string, object> { { "ok", true }, { "addedCount", added.Count } });
                return;
            }

            if (req.Method == "PUT" && req.Path.StartsWith("/api/tasks/", StringComparison.OrdinalIgnoreCase))
            {
                string id = req.Path.Substring("/api/tasks/".Length).Trim('/');
                var d = SafeDeserializeDict(ser, req.Body);
                MemoTask target = null;
                lock (_stateLock)
                {
                    AppData data = _storage.LoadData();
                    target = data.Tasks.FirstOrDefault(x => x.Id == id);
                    if (target == null)
                    {
                        WriteJsonError(stream, 404, "未找到该任务。");
                        return;
                    }
                    if (d.ContainsKey("title")) target.Title = ReadDictStr(d, "title").Trim();
                    if (d.ContainsKey("notes")) target.Notes = ReadDictStr(d, "notes").Trim();
                    if (d.ContainsKey("quadrant")) target.Quadrant = MemoTask.NormalizeQuadrant(ReadDictInt(d, "quadrant", target.Quadrant));
                    if (d.ContainsKey("deadlineIso")) target.DeadlineIso = MemoTask.FormatIso(MemoTask.ParseIsoDateTime(ReadDictStr(d, "deadlineIso")));
                    if (d.ContainsKey("remindAtIso"))
                    {
                        string remIso = MemoTask.FormatIso(MemoTask.ParseIsoDateTime(ReadDictStr(d, "remindAtIso")));
                        if (remIso != target.RemindAtIso) target.Reminded = false;
                        target.RemindAtIso = remIso;
                    }
                    if (d.ContainsKey("completed"))
                    {
                        bool comp = ReadDictBool(d, "completed", target.Completed);
                        target.Completed = comp;
                        target.CompletedAtIso = comp ? DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") : "";
                    }
                    _storage.SaveData(data);
                }
                WriteJson(stream, 200, new Dictionary<string, object> { { "ok", true }, { "task", target } });
                return;
            }

            if (req.Method == "DELETE" && req.Path.StartsWith("/api/tasks/", StringComparison.OrdinalIgnoreCase))
            {
                string id = req.Path.Substring("/api/tasks/".Length).Trim('/');
                lock (_stateLock)
                {
                    AppData data = _storage.LoadData();
                    int removed = data.Tasks.RemoveAll(x => x.Id == id);
                    if (removed == 0)
                    {
                        WriteJsonError(stream, 404, "未找到该任务。");
                        return;
                    }
                    _storage.SaveData(data);
                }
                WriteJson(stream, 200, new Dictionary<string, object> { { "ok", true } });
                return;
            }

            if (req.Method == "POST" && req.Path == "/api/notes")
            {
                var d = SafeDeserializeDict(ser, req.Body);
                var note = new StickyNote
                {
                    Title = string.IsNullOrWhiteSpace(ReadDictStr(d, "title")) ? "新建便签" : ReadDictStr(d, "title").Trim(),
                    Content = ReadDictStr(d, "content"),
                    ShowScratchpad = ReadDictBool(d, "showScratchpad", false),
                    FilterMode = string.IsNullOrWhiteSpace(ReadDictStr(d, "filterMode")) ? "custom" : ReadDictStr(d, "filterMode"),
                    Theme = "frost_glass"
                };
                lock (_stateLock)
                {
                    AppData data = _storage.LoadData();
                    data.StickyNotes.Add(note);
                    _storage.SaveData(data);
                }
                WriteJson(stream, 200, new Dictionary<string, object> { { "ok", true }, { "note", note } });
                return;
            }

            if (req.Method == "PUT" && req.Path.StartsWith("/api/notes/", StringComparison.OrdinalIgnoreCase))
            {
                string id = req.Path.Substring("/api/notes/".Length).Trim('/');
                var d = SafeDeserializeDict(ser, req.Body);
                StickyNote note = null;
                lock (_stateLock)
                {
                    AppData data = _storage.LoadData();
                    note = data.StickyNotes.FirstOrDefault(x => x.Id == id);
                    if (note == null)
                    {
                        WriteJsonError(stream, 404, "未找到便签。");
                        return;
                    }
                    if (d.ContainsKey("title")) note.Title = ReadDictStr(d, "title");
                    if (d.ContainsKey("content")) note.Content = ReadDictStr(d, "content");
                    if (d.ContainsKey("showScratchpad")) note.ShowScratchpad = ReadDictBool(d, "showScratchpad", note.ShowScratchpad);
                    _storage.SaveData(data);
                }
                WriteJson(stream, 200, new Dictionary<string, object> { { "ok", true }, { "note", note } });
                return;
            }

            if (req.Method == "DELETE" && req.Path.StartsWith("/api/notes/", StringComparison.OrdinalIgnoreCase))
            {
                string id = req.Path.Substring("/api/notes/".Length).Trim('/');
                lock (_stateLock)
                {
                    AppData data = _storage.LoadData();
                    data.StickyNotes.RemoveAll(x => x.Id == id);
                    data.Tasks.RemoveAll(x => x.NoteId == id);
                    _storage.SaveData(data);
                }
                WriteJson(stream, 200, new Dictionary<string, object> { { "ok", true } });
                return;
            }

            if (req.Method == "POST" && req.Path == "/api/config/ai")
            {
                var d = SafeDeserializeDict(ser, req.Body);
                lock (_stateLock)
                {
                    AppConfig cfg = _storage.LoadConfig();
                    if (d.ContainsKey("preset")) cfg.AiProviderPreset = ReadDictStr(d, "preset");
                    if (d.ContainsKey("baseUrl")) cfg.AiBaseUrl = ReadDictStr(d, "baseUrl").Trim();
                    if (d.ContainsKey("model")) cfg.AiModel = ReadDictStr(d, "model").Trim();
                    if (d.ContainsKey("customInstruction")) cfg.AiCustomInstruction = ReadDictStr(d, "customInstruction").Trim();
                    string newKey = ReadDictStr(d, "apiKey").Trim();
                    if (!string.IsNullOrEmpty(newKey)) cfg.AiApiKey = newKey;
                    _storage.SaveConfig(cfg);
                }
                WriteJson(stream, 200, new Dictionary<string, object> { { "ok", true } });
                return;
            }

            if (req.Method == "POST" && req.Path == "/api/ai/sort")
            {
                AppData data;
                AppConfig cfg;
                lock (_stateLock)
                {
                    data = _storage.LoadData();
                    cfg = _storage.LoadConfig();
                }

                List<MemoTask> active = data.Tasks.Where(t => !t.Completed).ToList();
                AiSortOutcome outcome = AiSortService.SortTasksAsync(cfg, active, DateTime.Now).GetAwaiter().GetResult();
                if (!outcome.Success)
                {
                    WriteJsonError(stream, 400, outcome.ErrorMessage);
                    return;
                }

                lock (_stateLock)
                {
                    AppData fresh = _storage.LoadData();
                    var byId = fresh.Tasks.ToDictionary(t => t.Id, t => t);
                    for (int i = 0; i < outcome.Items.Count; i++)
                    {
                        AiSortItem item = outcome.Items[i];
                        MemoTask target;
                        if (byId.TryGetValue(item.Id, out target))
                        {
                            target.Quadrant = item.Quadrant;
                            target.AiScore = item.Score;
                            target.AiReason = item.Reason ?? "";
                        }
                    }
                    _storage.SaveData(fresh);
                }

                WriteJson(stream, 200, new Dictionary<string, object>
                {
                    { "ok", true },
                    { "updatedCount", outcome.Items.Count }
                });
                return;
            }

            if (req.Method == "POST" && req.Path == "/api/ai/parse-chat")
            {
                var d = SafeDeserializeDict(ser, req.Body);
                string text = ReadDictStr(d, "text");
                AppConfig cfg;
                lock (_stateLock) { cfg = _storage.LoadConfig(); }

                AiChatExtractOutcome outcome = AiSortService.ExtractTasksFromChatAsync(cfg, text, DateTime.Now).GetAwaiter().GetResult();
                if (!outcome.Success)
                {
                    WriteJsonError(stream, 400, outcome.ErrorMessage);
                    return;
                }

                WriteJson(stream, 200, new Dictionary<string, object>
                {
                    { "ok", true },
                    { "tasks", outcome.Tasks }
                });
                return;
            }

            if (req.Method == "POST" && req.Path == "/api/sync/replace")
            {
                AppData incoming = ser.Deserialize<AppData>(req.Body);
                if (incoming == null || incoming.Tasks == null)
                {
                    WriteJsonError(stream, 400, "无效的同步数据结构。");
                    return;
                }
                if (incoming.StickyNotes == null) incoming.StickyNotes = new List<StickyNote>();
                lock (_stateLock)
                {
                    _storage.SaveData(incoming);
                }
                WriteJson(stream, 200, new Dictionary<string, object> { { "ok", true }, { "count", incoming.Tasks.Count } });
                return;
            }

            WriteJsonError(stream, 404, "API 路由不存在：" + req.Path);
        }

        private static void HandleStaticFile(NetworkStream stream, string reqPath)
        {
            string rel = (reqPath ?? "/").TrimStart('/');
            if (string.IsNullOrEmpty(rel)) rel = "index.html";
            rel = rel.Replace('/', Path.DirectorySeparatorChar);

            string candidate = Path.GetFullPath(Path.Combine(_webRoot, rel));
            if (!candidate.StartsWith(_webRoot, StringComparison.OrdinalIgnoreCase) || !File.Exists(candidate))
            {
                byte[] notFound = Encoding.UTF8.GetBytes("404 Not Found");
                WriteResponse(stream, 404, "Not Found", "text/plain; charset=utf-8", notFound);
                return;
            }

            string ext = Path.GetExtension(candidate).ToLowerInvariant();
            string mime = "application/octet-stream";
            if (ext == ".html") mime = "text/html; charset=utf-8";
            else if (ext == ".css") mime = "text/css; charset=utf-8";
            else if (ext == ".js") mime = "application/javascript; charset=utf-8";
            else if (ext == ".json") mime = "application/json; charset=utf-8";
            else if (ext == ".svg") mime = "image/svg+xml";
            else if (ext == ".ico") mime = "image/x-icon";

            byte[] bytes = File.ReadAllBytes(candidate);
            WriteResponse(stream, 200, "OK", mime, bytes);
        }

        private static string IssueToken()
        {
            long exp = DateTimeOffset.UtcNow.AddDays(14).ToUnixTimeSeconds();
            string payload = exp.ToString();
            string sig = ComputeHmac(payload);
            return payload + "." + sig;
        }

        private static bool VerifyRequestAuth(HttpRequest req)
        {
            string auth;
            if (!req.Headers.TryGetValue("Authorization", out auth) || string.IsNullOrWhiteSpace(auth)) return false;
            if (!auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return false;
            string token = auth.Substring("Bearer ".Length).Trim();
            int dot = token.IndexOf('.');
            if (dot <= 0) return false;

            string payload = token.Substring(0, dot);
            string sig = token.Substring(dot + 1);
            long exp;
            if (!long.TryParse(payload, out exp)) return false;
            if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > exp) return false;
            return string.Equals(sig, ComputeHmac(payload), StringComparison.Ordinal);
        }

        private static string ComputeHmac(string payload)
        {
            using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_hmacSecret)))
            {
                byte[] hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }

        private static void WriteJson(NetworkStream stream, int statusCode, object obj)
        {
            var ser = new JavaScriptSerializer { MaxJsonLength = 32 * 1024 * 1024 };
            byte[] data = Encoding.UTF8.GetBytes(ser.Serialize(obj));
            string statusText = statusCode == 200 ? "OK" : (statusCode == 401 ? "Unauthorized" : "Bad Request");
            WriteResponse(stream, statusCode, statusText, "application/json; charset=utf-8", data);
        }

        private static void WriteJsonError(NetworkStream stream, int statusCode, string error)
        {
            WriteJson(stream, statusCode, new Dictionary<string, object> { { "ok", false }, { "error", error } });
        }

        private static void WriteResponse(NetworkStream stream, int statusCode, string statusText, string contentType, byte[] body)
        {
            var sb = new StringBuilder();
            sb.AppendFormat("HTTP/1.1 {0} {1}\r\n", statusCode, statusText);
            sb.AppendFormat("Content-Type: {0}\r\n", contentType);
            sb.AppendFormat("Content-Length: {0}\r\n", body.Length);
            sb.Append("Access-Control-Allow-Origin: *\r\n");
            sb.Append("Access-Control-Allow-Headers: Content-Type, Authorization\r\n");
            sb.Append("Access-Control-Allow-Methods: GET, POST, PUT, DELETE, OPTIONS\r\n");
            sb.Append("Cache-Control: no-store\r\n");
            sb.Append("Connection: close\r\n\r\n");

            byte[] headerBytes = Encoding.ASCII.GetBytes(sb.ToString());
            stream.Write(headerBytes, 0, headerBytes.Length);
            if (body.Length > 0) stream.Write(body, 0, body.Length);
            stream.Flush();
        }

        private static int IndexOfBytes(byte[] haystack, byte[] needle)
        {
            for (int i = 0; i <= haystack.Length - needle.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < needle.Length; j++)
                {
                    if (haystack[i + j] != needle[j]) { match = false; break; }
                }
                if (match) return i;
            }
            return -1;
        }

        private static Dictionary<string, object> SafeDeserializeDict(JavaScriptSerializer ser, string body)
        {
            if (string.IsNullOrWhiteSpace(body)) return new Dictionary<string, object>();
            try
            {
                var d = ser.Deserialize<Dictionary<string, object>>(body);
                return d ?? new Dictionary<string, object>();
            }
            catch
            {
                return new Dictionary<string, object>();
            }
        }

        private static List<Dictionary<string, object>> ExtractDictList(Dictionary<string, object> d, string key)
        {
            if (d == null || !d.ContainsKey(key) || d[key] == null) return null;
            object raw = d[key];
            if (raw is string || raw is IDictionary) return null;
            var seq = raw as IEnumerable;
            if (seq == null) return null;
            var result = new List<Dictionary<string, object>>();
            foreach (object elem in seq)
            {
                var row = elem as Dictionary<string, object>;
                if (row != null) result.Add(row);
            }
            return result;
        }

        private static string ReadDictStr(Dictionary<string, object> d, string key)
        {
            if (d == null || !d.ContainsKey(key) || d[key] == null) return "";
            return d[key].ToString();
        }

        private static int ReadDictInt(Dictionary<string, object> d, string key, int fallback)
        {
            if (d == null || !d.ContainsKey(key) || d[key] == null) return fallback;
            int v;
            if (int.TryParse(d[key].ToString(), out v)) return v;
            return fallback;
        }

        private static bool ReadDictBool(Dictionary<string, object> d, string key, bool fallback)
        {
            if (d == null || !d.ContainsKey(key) || d[key] == null) return fallback;
            bool b;
            if (bool.TryParse(d[key].ToString(), out b)) return b;
            return fallback;
        }

        private static string GenerateRandomCode(int len)
        {
            const string chars = "abcdefghjkmnpqrstuvwxyz23456789";
            var rng = new RNGCryptoServiceProvider();
            var data = new byte[len];
            rng.GetBytes(data);
            var sb = new StringBuilder(len);
            for (int i = 0; i < len; i++) sb.Append(chars[data[i] % chars.Length]);
            return sb.ToString();
        }
    }
}
