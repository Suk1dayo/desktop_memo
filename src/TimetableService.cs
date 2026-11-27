using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace DesktopMemo
{
    public class MisPageCapture
    {
        public bool Success { get; set; }
        public string PageTitle { get; set; }
        public string PageUrl { get; set; }
        public string ExtractedText { get; set; }
        public string StructuredTablesJson { get; set; }
        public string CapturedTablesHtml { get; set; }
        public int CapturedTableCount { get; set; }
        public string StructuredGridText { get; set; }
        public List<string> SelectedHints { get; set; }
        public string ScreenshotDataUrl { get; set; }
        public string ErrorMessage { get; set; }

        public MisPageCapture()
        {
            PageTitle = "";
            PageUrl = "";
            ExtractedText = "";
            StructuredTablesJson = "";
            CapturedTablesHtml = "";
            CapturedTableCount = 0;
            StructuredGridText = "";
            SelectedHints = new List<string>();
            ScreenshotDataUrl = "";
            ErrorMessage = "";
        }
    }

    public class DirectTimetableParseResult
    {
        public bool Success { get; set; }
        public TimetableData Timetable { get; set; }
        public int CurrentWeek { get; set; }
        public string SourceNote { get; set; }
        public string StructuredGridText { get; set; }

        public DirectTimetableParseResult()
        {
            Timetable = new TimetableData();
            CurrentWeek = 0;
            SourceNote = "";
            StructuredGridText = "";
        }
    }

    public static class TimetableService
    {
        private class GridCell
        {
            public int Row;
            public int Col;
            public int RowSpan;
            public int ColSpan;
            public string Text;
            public List<string> Blocks;

            public GridCell()
            {
                RowSpan = 1;
                ColSpan = 1;
                Text = "";
                Blocks = new List<string>();
            }
        }

        private class GridTable
        {
            public int RowCount;
            public int ColCount;
            public int WeekdayHits;
            public List<GridCell> Cells = new List<GridCell>();
        }

        public static string FindChromiumBrowserExe()
        {
            string[] candidates = new string[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Microsoft\Edge\Application\msedge.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Microsoft\Edge\Application\msedge.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Google\Chrome\Application\chrome.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Google\Chrome\Application\chrome.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\Edge\Application\msedge.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Google\Chrome\Application\chrome.exe")
            };
            for (int i = 0; i < candidates.Length; i++)
            {
                try
                {
                    if (!string.IsNullOrEmpty(candidates[i]) && File.Exists(candidates[i]))
                    {
                        return candidates[i];
                    }
                }
                catch { }
            }
            return "";
        }

        private static int FindFreeLoopbackPort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        public static string NormalizeUrl(string rawUrl)
        {
            string u = (rawUrl ?? "").Trim();
            if (string.IsNullOrEmpty(u)) return "";
            if (!u.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !u.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                u = "https://" + u;
            }
            return u;
        }

        private static HttpClient CreateLoopbackHttpClient(TimeSpan timeout)
        {
            var handler = new HttpClientHandler
            {
                UseProxy = false
            };
            var client = new HttpClient(handler, true);
            client.Timeout = timeout;
            return client;
        }

        public static bool IsCdpPortAlive(int port)
        {
            if (port <= 0) return false;
            try
            {
                using (HttpClient http = CreateLoopbackHttpClient(TimeSpan.FromMilliseconds(1200)))
                {
                    string body = http.GetStringAsync(string.Format("http://127.0.0.1:{0}/json/version", port)).GetAwaiter().GetResult();
                    return !string.IsNullOrEmpty(body) && body.IndexOf("webSocketDebuggerUrl", StringComparison.OrdinalIgnoreCase) >= 0;
                }
            }
            catch
            {
                return false;
            }
        }

        private static bool IsProfileDirLocked(string profileDir)
        {
            if (string.IsNullOrEmpty(profileDir)) return false;
            string lockPath = Path.Combine(profileDir, "lockfile");
            if (!File.Exists(lockPath)) return false;
            try
            {
                using (FileStream fs = File.Open(lockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    return false;
                }
            }
            catch (IOException)
            {
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static int ReadDevToolsActivePort(string profileDir)
        {
            if (string.IsNullOrEmpty(profileDir)) return 0;
            string path = Path.Combine(profileDir, "DevToolsActivePort");
            if (!File.Exists(path)) return 0;
            try
            {
                string[] lines = File.ReadAllLines(path);
                int port;
                if (lines.Length > 0 && int.TryParse((lines[0] ?? "").Trim(), out port) && port > 0) return port;
            }
            catch { }
            return 0;
        }

        private static int ReadSavedCdpPort(string profileDir)
        {
            if (string.IsNullOrEmpty(profileDir)) return 0;
            string path = Path.Combine(profileDir, ".cdp_port");
            if (!File.Exists(path)) return 0;
            try
            {
                int port;
                if (int.TryParse(File.ReadAllText(path).Trim(), out port) && port > 0) return port;
            }
            catch { }
            return 0;
        }

        public static int TryFindAliveDebugPort(string baseProfileDir)
        {
            if (string.IsNullOrWhiteSpace(baseProfileDir)) return 0;

            var candidateDirs = new List<string>();
            candidateDirs.Add(baseProfileDir);
            for (int idx = 2; idx <= 12; idx++)
            {
                candidateDirs.Add(baseProfileDir + "_s" + idx);
            }

            var checkedPorts = new HashSet<int>();
            for (int i = 0; i < candidateDirs.Count; i++)
            {
                string dir = candidateDirs[i];
                if (!Directory.Exists(dir)) continue;

                int[] ports = new int[] { ReadSavedCdpPort(dir), ReadDevToolsActivePort(dir) };
                for (int p = 0; p < ports.Length; p++)
                {
                    int port = ports[p];
                    if (port > 0 && checkedPorts.Add(port) && IsCdpPortAlive(port))
                    {
                        return port;
                    }
                }
            }
            return 0;
        }

        public static async Task<bool> WaitForCdpReadyAsync(int debugPort, int maxWaitMs)
        {
            if (debugPort <= 0) return false;
            int waited = 0;
            while (waited <= maxWaitMs)
            {
                bool alive = await Task.Run(delegate { return IsCdpPortAlive(debugPort); });
                if (alive) return true;
                await Task.Delay(200);
                waited += 200;
            }
            return false;
        }

        private static bool TryOpenUrlInExistingCdp(int port, string targetUrl)
        {
            if (port <= 0 || string.IsNullOrWhiteSpace(targetUrl)) return false;
            try
            {
                using (HttpClient http = CreateLoopbackHttpClient(TimeSpan.FromMilliseconds(2000)))
                {
                    string reqUrl = string.Format("http://127.0.0.1:{0}/json/new?{1}", port, Uri.EscapeDataString(targetUrl));
                    using (var req = new HttpRequestMessage(HttpMethod.Put, reqUrl))
                    {
                        HttpResponseMessage resp = http.SendAsync(req).GetAwaiter().GetResult();
                        if (resp.IsSuccessStatusCode) return true;
                    }
                    HttpResponseMessage getResp = http.GetAsync(reqUrl).GetAwaiter().GetResult();
                    return getResp.IsSuccessStatusCode;
                }
            }
            catch
            {
                return false;
            }
        }

        public static bool LaunchMisBrowser(string url, string profileDir, out int debugPort, out Process proc, out string error)
        {
            debugPort = 0;
            proc = null;
            error = "";

            string targetUrl = NormalizeUrl(url);
            if (string.IsNullOrEmpty(targetUrl))
            {
                error = "请先填写学校 MIS / 教务系统网址。";
                return false;
            }

            int alivePort = TryFindAliveDebugPort(profileDir);
            if (alivePort > 0)
            {
                TryOpenUrlInExistingCdp(alivePort, targetUrl);
                debugPort = alivePort;
                return true;
            }

            string browser = FindChromiumBrowserExe();
            if (!string.IsNullOrEmpty(browser))
            {
                try
                {
                    string launchProfileDir = profileDir;
                    if (!string.IsNullOrEmpty(profileDir))
                    {
                        if (!Directory.Exists(profileDir))
                        {
                            Directory.CreateDirectory(profileDir);
                        }
                        else if (IsProfileDirLocked(profileDir))
                        {
                            // An orphaned Edge process holds lockfile without reachable CDP;
                            // pick an unlocked sibling profile dir so --remote-debugging-port actually binds.
                            for (int idx = 2; idx <= 12; idx++)
                            {
                                string candidate = profileDir + "_s" + idx;
                                if (!Directory.Exists(candidate) || !IsProfileDirLocked(candidate))
                                {
                                    launchProfileDir = candidate;
                                    break;
                                }
                            }
                        }
                        if (!Directory.Exists(launchProfileDir))
                        {
                            Directory.CreateDirectory(launchProfileDir);
                        }
                    }

                    int port = FindFreeLoopbackPort();
                    // Use --new-window with isolated user-data-dir so secondary SSO/MIS links
                    // stay inside this window while --remote-debugging-port binds cleanly.
                    string args = string.Format(
                        "--new-window --user-data-dir=\"{1}\" --remote-debugging-port={2} --remote-allow-origins=* --window-size=1220,820 --no-first-run --no-default-browser-check --no-service-autorun --disable-background-mode \"{0}\"",
                        targetUrl, launchProfileDir, port);

                    proc = Process.Start(new ProcessStartInfo
                    {
                        FileName = browser,
                        Arguments = args,
                        UseShellExecute = false
                    });
                    debugPort = port;
                    if (!string.IsNullOrEmpty(profileDir))
                    {
                        try { File.WriteAllText(Path.Combine(profileDir, ".cdp_port"), port.ToString(CultureInfo.InvariantCulture)); } catch { }
                    }
                    if (!string.IsNullOrEmpty(launchProfileDir) && !string.Equals(launchProfileDir, profileDir, StringComparison.OrdinalIgnoreCase))
                    {
                        try { File.WriteAllText(Path.Combine(launchProfileDir, ".cdp_port"), port.ToString(CultureInfo.InvariantCulture)); } catch { }
                    }
                    return true;
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }
            }

            try
            {
                proc = Process.Start(new ProcessStartInfo { FileName = targetUrl, UseShellExecute = true });
                return true;
            }
            catch (Exception ex)
            {
                error = "打开浏览器失败：" + ex.Message;
                return false;
            }
        }

        private class TabInspectResult
        {
            public string TargetType = "page";
            public string WsUrl = "";
            public string Title = "";
            public string Url = "";
            public string Text = "";
            public string TablesJson = "";
            public string TablesHtml = "";
            public int TableCount = 0;
            public List<string> Hints = new List<string>();
            public bool HasFocus;
            public int Score;
        }

        public static async Task<MisPageCapture> CaptureActiveMisPageAsync(int debugPort)
        {
            var result = new MisPageCapture();
            if (debugPort <= 0)
            {
                result.Success = false;
                result.ErrorMessage = "当前浏览器未开启自动读取通道，请点击「🌐 打开登录窗口」启动专用窗口，或直接截图贴入下方。";
                return result;
            }

            try
            {
                string jsonList;
                using (HttpClient http = CreateLoopbackHttpClient(TimeSpan.FromSeconds(5)))
                {
                    jsonList = await http.GetStringAsync(string.Format("http://127.0.0.1:{0}/json/list", debugPort));
                }

                var ser = new JavaScriptSerializer { MaxJsonLength = 32 * 1024 * 1024 };
                IList targets = ser.DeserializeObject(jsonList) as IList;
                if (targets == null || targets.Count == 0)
                {
                    result.Success = false;
                    result.ErrorMessage = "未检测到已打开的 MIS 窗口，请先点击「🌐 打开登录窗口」并完成登录。";
                    return result;
                }

                var candidateTabs = new List<TabInspectResult>();
                for (int i = 0; i < targets.Count; i++)
                {
                    var row = targets[i] as Dictionary<string, object>;
                    if (row == null) continue;
                    string type = row.ContainsKey("type") && row["type"] != null ? row["type"].ToString() : "";
                    string url = row.ContainsKey("url") && row["url"] != null ? row["url"].ToString() : "";
                    string candidateWs = row.ContainsKey("webSocketDebuggerUrl") && row["webSocketDebuggerUrl"] != null ? row["webSocketDebuggerUrl"].ToString() : "";
                    if ((type == "page" || type == "iframe") && !string.IsNullOrEmpty(candidateWs) &&
                        !url.StartsWith("devtools://", StringComparison.OrdinalIgnoreCase) &&
                        !url.StartsWith("chrome-extension://", StringComparison.OrdinalIgnoreCase) &&
                        !url.StartsWith("edge://", StringComparison.OrdinalIgnoreCase) &&
                        !url.Equals("about:blank", StringComparison.OrdinalIgnoreCase))
                    {
                        candidateTabs.Add(new TabInspectResult
                        {
                            TargetType = type,
                            WsUrl = candidateWs,
                            Url = url,
                            Title = row.ContainsKey("title") && row["title"] != null ? row["title"].ToString() : ""
                        });
                    }
                }

                if (candidateTabs.Count == 0)
                {
                    result.Success = false;
                    result.ErrorMessage = "未找到可读取的 MIS 标签页，请确认窗口尚未关闭。";
                    return result;
                }

                string jsExtract =
                    "(function(){" +
                    "var texts=[];var hints=[];var tables=[];var tablesHtml=[];var hasFocus=false;" +
                    "try{hasFocus=!!document.hasFocus();}catch(e){}" +
                    "function normTxt(s){return (s||'').replace(/\\r\\n/g,'\\n').replace(/\\u00a0/g,' ').replace(/[ \\t]+\\n/g,'\\n').replace(/\\n{3,}/g,'\\n\\n').trim();}" +
                    "function cellTextWithBreaks(el){" +
                    "  if(!el)return '';" +
                    "  try{" +
                    "    var clone=el.cloneNode(true);" +
                    "    var w=document.createTreeWalker(clone,4,null,false);var tn;" +
                    "    while((tn=w.nextNode())){tn.nodeValue=(tn.nodeValue||'').replace(/[\\r\\n\\t]+/g,' ');}" +
                    "    var brs=clone.querySelectorAll('br');" +
                    "    for(var i=0;i<brs.length;i++) brs[i].parentNode.replaceChild(document.createTextNode('\\n'),brs[i]);" +
                    "    var hrs=clone.querySelectorAll('hr');" +
                    "    for(var i=0;i<hrs.length;i++) hrs[i].parentNode.replaceChild(document.createTextNode('\\n---BLOCK---\\n'),hrs[i]);" +
                    "    var blks=clone.querySelectorAll('div,p,li,h1,h2,h3,h4');" +
                    "    for(var i=0;i<blks.length;i++) blks[i].appendChild(document.createTextNode('\\n'));" +
                    "    return normTxt(clone.textContent||'');" +
                    "  }catch(e){return normTxt(el.innerText||el.textContent||'');}" +
                    "}" +
                    "function extractBlocks(td){" +
                    "  var out=[];" +
                    "  try{" +
                    "    var directDivs=[];" +
                    "    if(td.children){" +
                    "      for(var i=0;i<td.children.length;i++){" +
                    "        if(td.children[i].tagName==='DIV'){" +
                    "          var dt=cellTextWithBreaks(td.children[i]).replace(/---BLOCK---/g,'').trim();" +
                    "          if(dt)directDivs.push(dt);" +
                    "        }" +
                    "      }" +
                    "    }" +
                    "    var wholeLen=(td.textContent||'').replace(/\\s+/g,'').length;" +
                    "    if(directDivs.length>1||(directDivs.length===1&&wholeLen<=directDivs[0].replace(/\\s+/g,'').length+20))return directDivs;" +
                    "    var subs=td.querySelectorAll('div.kbcontent,div.timetable_con,div[class*=\"course\"],li');" +
                    "    if(subs&&subs.length>1){" +
                    "      for(var i=0;i<subs.length;i++){" +
                    "        var st=cellTextWithBreaks(subs[i]).replace(/---BLOCK---/g,'').trim();" +
                    "        if(st)out.push(st);" +
                    "      }" +
                    "      if(out.length>0)return out;" +
                    "    }" +
                    "  }catch(e){}" +
                    "  var raw=cellTextWithBreaks(td);" +
                    "  if(!raw)return out;" +
                    "  var parts=raw.split(/\\n?---BLOCK---\\n?|\\n{2,}/);" +
                    "  for(var i=0;i<parts.length;i++){var p=parts[i].trim();if(p)out.push(p);}" +
                    "  return out;" +
                    "}" +
                    "function extractTable(tbl){" +
                    "  var trs=tbl.rows;if(!trs||trs.length<2)return null;" +
                    "  var occ=[];var cells=[];var maxCols=0;var wdSet={};var nonEmpty=0;" +
                    "  for(var r=0;r<trs.length;r++){" +
                    "    if(!occ[r])occ[r]=[];" +
                    "    var c=0;var rowCells=trs[r].cells;" +
                    "    for(var j=0;j<rowCells.length;j++){" +
                    "      while(occ[r][c])c++;" +
                    "      var td=rowCells[j];" +
                    "      var rs=Math.max(1,parseInt(td.rowSpan||td.getAttribute('rowspan')||'1',10)||1);" +
                    "      var cs=Math.max(1,parseInt(td.colSpan||td.getAttribute('colspan')||'1',10)||1);" +
                    "      for(var dr=0;dr<rs;dr++){if(!occ[r+dr])occ[r+dr]=[];for(var dc=0;dc<cs;dc++)occ[r+dr][c+dc]=true;}" +
                    "      var txt=cellTextWithBreaks(td).replace(/---BLOCK---/g,'\\n').trim();" +
                    "      var blks=extractBlocks(td);" +
                    "      if(txt)nonEmpty++;" +
                    "      var hm=txt.match(/(?:星期|周)[一二三四五六日天]|^(?:一|二|三|四|五|六|日|Mon|Tue|Wed|Thu|Fri|Sat|Sun)$/m);" +
                    "      if(hm)wdSet[hm[0]]=true;" +
                    "      cells.push({r:r,c:c,rs:rs,cs:cs,text:txt,blocks:blks});" +
                    "      if(c+cs>maxCols)maxCols=c+cs;" +
                    "      c+=cs;" +
                    "    }" +
                    "  }" +
                    "  var wdCount=Object.keys(wdSet).length;" +
                    "  return {rows:trs.length,cols:maxCols,wdHits:wdCount,nonEmpty:nonEmpty,cells:cells};" +
                    "}" +
                    "function collect(doc,depth){" +
                    "  if(!doc||depth>4)return;" +
                    "  try{" +
                    "    if(doc.hasFocus&&doc.hasFocus())hasFocus=true;" +
                    "    if(doc.body&&doc.body.innerText)texts.push(doc.body.innerText);" +
                    "    var sels=doc.querySelectorAll('select');" +
                    "    for(var i=0;i<sels.length;i++){" +
                    "      var opt=sels[i].options&&sels[i].selectedIndex>=0?sels[i].options[sels[i].selectedIndex]:null;" +
                    "      if(opt&&opt.text&&opt.text.trim())hints.push(opt.text.trim());" +
                    "    }" +
                    "    var els=doc.querySelectorAll('h1,h2,h3,h4,.title,.widget-title button,.btn,[class*=\"week\"],[class*=\"xq\"],[id*=\"week\"],[id*=\"xq\"]');" +
                    "    for(var i=0;i<els.length;i++){" +
                    "      var t=(els[i].innerText||'').trim();" +
                    "      if(t&&t.length<80&&(t.indexOf('周')>=0||t.indexOf('学期')>=0||t.indexOf('学年')>=0))hints.push(t);" +
                    "    }" +
                    "    var tbls=doc.querySelectorAll('table');" +
                    "    for(var i=0;i<tbls.length;i++){" +
                    "      var parsed=extractTable(tbls[i]);" +
                    "      if(parsed&&parsed.cols>=5&&parsed.nonEmpty>=4){" +
                    "        tables.push(parsed);" +
                    "        try{if(tbls[i].outerHTML&&tbls[i].outerHTML.length<400000)tablesHtml.push(tbls[i].outerHTML);}catch(e){}" +
                    "      }" +
                    "    }" +
                    "    var frames=doc.querySelectorAll('iframe,frame');" +
                    "    for(var i=0;i<frames.length;i++){" +
                    "      try{if(frames[i].contentDocument)collect(frames[i].contentDocument,depth+1);}catch(e){}" +
                    "    }" +
                    "  }catch(e){}" +
                    "}" +
                    "collect(document,0);" +
                    "return JSON.stringify({title:document.title||'',url:location.href||'',hasFocus:hasFocus,text:texts.join('\\n---\\n'),hints:hints,tables:tables,tablesHtml:tablesHtml.join('\\n')});" +
                    "})()";

                TabInspectResult bestTab = null;
                TabInspectResult bestPageTab = null;
                for (int i = 0; i < candidateTabs.Count; i++)
                {
                    TabInspectResult tab = candidateTabs[i];
                    try
                    {
                        using (var ws = new ClientWebSocket())
                        using (var cts = new CancellationTokenSource(10000))
                        {
                            ws.Options.Proxy = null;
                            await ws.ConnectAsync(new Uri(tab.WsUrl), cts.Token);
                            var evalCmd = new Dictionary<string, object>
                            {
                                { "id", 1 },
                                { "method", "Runtime.evaluate" },
                                { "params", new Dictionary<string, object> { { "expression", jsExtract }, { "returnByValue", true } } }
                            };
                            Dictionary<string, object> evalResp = await SendCdpCommandAsync(ws, ser.Serialize(evalCmd), 1, cts.Token);
                            if (evalResp != null && evalResp.ContainsKey("result"))
                            {
                                var r1 = evalResp["result"] as Dictionary<string, object>;
                                var valObj = r1 != null && r1.ContainsKey("result") ? r1["result"] as Dictionary<string, object> : null;
                                string jsonStr = valObj != null && valObj.ContainsKey("value") && valObj["value"] != null ? valObj["value"].ToString() : "";
                                if (!string.IsNullOrWhiteSpace(jsonStr))
                                {
                                    var info = ser.DeserializeObject(jsonStr) as Dictionary<string, object>;
                                    if (info != null)
                                    {
                                        if (info.ContainsKey("title") && info["title"] != null) tab.Title = info["title"].ToString();
                                        if (info.ContainsKey("url") && info["url"] != null) tab.Url = info["url"].ToString();
                                        if (info.ContainsKey("text") && info["text"] != null) tab.Text = info["text"].ToString();
                                        if (info.ContainsKey("tablesHtml") && info["tablesHtml"] != null) tab.TablesHtml = info["tablesHtml"].ToString();
                                        if (info.ContainsKey("hasFocus") && info["hasFocus"] is bool) tab.HasFocus = (bool)info["hasFocus"];
                                        IList hList = info.ContainsKey("hints") ? info["hints"] as IList : null;
                                        if (hList != null)
                                        {
                                            for (int h = 0; h < hList.Count; h++)
                                            {
                                                if (hList[h] != null)
                                                {
                                                    string hs = hList[h].ToString().Trim();
                                                    if (!string.IsNullOrEmpty(hs) && !tab.Hints.Contains(hs)) tab.Hints.Add(hs);
                                                }
                                            }
                                        }
                                        IList tblList = info.ContainsKey("tables") ? info["tables"] as IList : null;
                                        if (tblList != null)
                                        {
                                            tab.TableCount = tblList.Count;
                                            tab.TablesJson = ser.Serialize(tblList);
                                            int maxWd = 0;
                                            int maxNonEmpty = 0;
                                            for (int t = 0; t < tblList.Count; t++)
                                            {
                                                var td = tblList[t] as Dictionary<string, object>;
                                                if (td == null) continue;
                                                int wdh = td.ContainsKey("wdHits") ? Convert.ToInt32(td["wdHits"]) : 0;
                                                int ne = td.ContainsKey("nonEmpty") ? Convert.ToInt32(td["nonEmpty"]) : 0;
                                                if (wdh > maxWd) maxWd = wdh;
                                                if (ne > maxNonEmpty) maxNonEmpty = ne;
                                            }
                                            tab.Score += maxWd * 1000 + Math.Min(400, maxNonEmpty * 4);
                                        }
                                    }
                                }
                            }

                            if (tab.HasFocus) tab.Score += 80;
                            string lowerUrl = (tab.Url ?? "").ToLowerInvariant();
                            string lowerTitle = (tab.Title ?? "").ToLowerInvariant();
                            if (lowerUrl.Contains("kb") || lowerUrl.Contains("kcb") || lowerUrl.Contains("schedule") || lowerTitle.Contains("课表"))
                            {
                                tab.Score += 180;
                            }
                            tab.Score += i; // prefer latest tab on ties

                            if (ws.State == WebSocketState.Open)
                            {
                                try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None); } catch { }
                            }
                        }
                    }
                    catch { }

                    if (bestTab == null || tab.Score > bestTab.Score) bestTab = tab;
                    if (tab.TargetType == "page" && (bestPageTab == null || tab.Score > bestPageTab.Score)) bestPageTab = tab;
                }

                if (bestTab == null)
                {
                    result.Success = false;
                    result.ErrorMessage = "未能连接到任何有效的 MIS 标签页。";
                    return result;
                }

                result.PageTitle = bestTab.Title;
                result.PageUrl = bestTab.Url;
                result.ExtractedText = bestTab.Text;
                result.StructuredTablesJson = bestTab.TablesJson;
                result.CapturedTablesHtml = bestTab.TablesHtml;
                result.CapturedTableCount = bestTab.TableCount;
                result.SelectedHints = bestTab.Hints;

                // Capture screenshot from winning tab (or parent page tab if winning target was a cross-origin iframe)
                TabInspectResult shotTab = (bestTab.TargetType == "page" || bestPageTab == null) ? bestTab : bestPageTab;
                try
                {
                    using (var ws = new ClientWebSocket())
                    using (var cts = new CancellationTokenSource(8000))
                    {
                        ws.Options.Proxy = null;
                        await ws.ConnectAsync(new Uri(shotTab.WsUrl), cts.Token);
                        var shotCmd = new Dictionary<string, object>
                        {
                            { "id", 2 },
                            { "method", "Page.captureScreenshot" },
                            { "params", new Dictionary<string, object> { { "format", "jpeg" }, { "quality", 82 } } }
                        };
                        Dictionary<string, object> shotResp = await SendCdpCommandAsync(ws, ser.Serialize(shotCmd), 2, cts.Token);
                        if (shotResp != null && shotResp.ContainsKey("result"))
                        {
                            var r2 = shotResp["result"] as Dictionary<string, object>;
                            if (r2 != null && r2.ContainsKey("data") && r2["data"] != null)
                            {
                                result.ScreenshotDataUrl = "data:image/jpeg;base64," + r2["data"].ToString();
                            }
                        }
                        if (ws.State == WebSocketState.Open)
                        {
                            try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None); } catch { }
                        }
                    }
                }
                catch { }

                DirectTimetableParseResult pre = ParseCapturedTimetable(result, DateTime.Now);
                result.StructuredGridText = pre.StructuredGridText ?? "";

                result.Success = !string.IsNullOrWhiteSpace(result.ExtractedText) ||
                                 !string.IsNullOrWhiteSpace(result.StructuredTablesJson) ||
                                 !string.IsNullOrWhiteSpace(result.ScreenshotDataUrl);
                if (!result.Success) result.ErrorMessage = "页面内容为空，请确认已打开课程表页。";
                return result;
            }
            catch (Exception ex)
            {
                Exception root = ex;
                while (root.InnerException != null) root = root.InnerException;
                string detail = string.Equals(root.Message, ex.Message, StringComparison.Ordinal)
                    ? ex.Message
                    : (ex.Message + " (" + root.Message + ")");
                result.Success = false;
                result.ErrorMessage = "读取 MIS 窗口失败：" + detail + "（也可以直接截图并 Ctrl+V 粘贴到这里识别）";
                return result;
            }
        }

        private static async Task<Dictionary<string, object>> SendCdpCommandAsync(
            ClientWebSocket ws,
            string jsonPayload,
            int expectedId,
            CancellationToken token)
        {
            byte[] outBytes = Encoding.UTF8.GetBytes(jsonPayload);
            await ws.SendAsync(new ArraySegment<byte>(outBytes), WebSocketMessageType.Text, true, token);

            var ser = new JavaScriptSerializer { MaxJsonLength = 32 * 1024 * 1024 };
            byte[] buf = new byte[65536];
            while (ws.State == WebSocketState.Open)
            {
                using (var ms = new MemoryStream())
                {
                    WebSocketReceiveResult recv;
                    do
                    {
                        recv = await ws.ReceiveAsync(new ArraySegment<byte>(buf), token);
                        if (recv.MessageType == WebSocketMessageType.Close) return null;
                        if (recv.Count > 0) ms.Write(buf, 0, recv.Count);
                    } while (!recv.EndOfMessage);

                    string msg = Encoding.UTF8.GetString(ms.ToArray());
                    var dict = ser.DeserializeObject(msg) as Dictionary<string, object>;
                    if (dict != null && dict.ContainsKey("id") && dict["id"] != null)
                    {
                        int idVal;
                        if (int.TryParse(dict["id"].ToString(), out idVal) && idVal == expectedId)
                        {
                            return dict;
                        }
                    }
                }
            }
            return null;
        }

        public static DirectTimetableParseResult ParseCapturedTimetable(MisPageCapture cap, DateTime now)
        {
            if (cap == null) return new DirectTimetableParseResult();
            DirectTimetableParseResult res = ParseStructuredTablesJson(cap.StructuredTablesJson, cap.ExtractedText, cap.SelectedHints, now);
            if (!res.Success && !string.IsNullOrWhiteSpace(cap.CapturedTablesHtml))
            {
                DirectTimetableParseResult htmlRes = ParseHtmlTimetable(cap.CapturedTablesHtml, now);
                if (htmlRes.Success && htmlRes.Timetable != null && htmlRes.Timetable.Courses.Count > 0)
                {
                    if (string.IsNullOrEmpty(htmlRes.Timetable.SemesterName) || htmlRes.CurrentWeek <= 0)
                    {
                        DetectSemesterAndWeekHints(cap.ExtractedText, cap.SelectedHints, now, htmlRes);
                    }
                    htmlRes.SourceNote = "MIS 网页 HTML 直解析";
                    return htmlRes;
                }
            }
            return res;
        }

        public static DirectTimetableParseResult ParseStructuredTablesJson(
            string tablesJson,
            string pageText,
            List<string> selectedHints,
            DateTime now)
        {
            var tables = new List<GridTable>();
            if (!string.IsNullOrWhiteSpace(tablesJson))
            {
                try
                {
                    var ser = new JavaScriptSerializer { MaxJsonLength = 32 * 1024 * 1024 };
                    IList arr = ser.DeserializeObject(tablesJson) as IList;
                    if (arr != null)
                    {
                        for (int i = 0; i < arr.Count; i++)
                        {
                            var td = arr[i] as Dictionary<string, object>;
                            if (td == null) continue;
                            var gt = new GridTable();
                            gt.RowCount = td.ContainsKey("rows") ? Convert.ToInt32(td["rows"]) : 0;
                            gt.ColCount = td.ContainsKey("cols") ? Convert.ToInt32(td["cols"]) : 0;
                            gt.WeekdayHits = td.ContainsKey("wdHits") ? Convert.ToInt32(td["wdHits"]) : 0;
                            IList cellsArr = td.ContainsKey("cells") ? td["cells"] as IList : null;
                            if (cellsArr != null)
                            {
                                for (int j = 0; j < cellsArr.Count; j++)
                                {
                                    var cd = cellsArr[j] as Dictionary<string, object>;
                                    if (cd == null) continue;
                                    var gc = new GridCell();
                                    gc.Row = cd.ContainsKey("r") ? Convert.ToInt32(cd["r"]) : 0;
                                    gc.Col = cd.ContainsKey("c") ? Convert.ToInt32(cd["c"]) : 0;
                                    gc.RowSpan = Math.Max(1, cd.ContainsKey("rs") ? Convert.ToInt32(cd["rs"]) : 1);
                                    gc.ColSpan = Math.Max(1, cd.ContainsKey("cs") ? Convert.ToInt32(cd["cs"]) : 1);
                                    gc.Text = cd.ContainsKey("text") && cd["text"] != null ? cd["text"].ToString() : "";
                                    IList bArr = cd.ContainsKey("blocks") ? cd["blocks"] as IList : null;
                                    if (bArr != null)
                                    {
                                        for (int b = 0; b < bArr.Count; b++)
                                        {
                                            if (bArr[b] != null)
                                            {
                                                string bs = bArr[b].ToString().Trim();
                                                if (!string.IsNullOrEmpty(bs)) gc.Blocks.Add(bs);
                                            }
                                        }
                                    }
                                    gt.Cells.Add(gc);
                                }
                            }
                            tables.Add(gt);
                        }
                    }
                }
                catch { }
            }
            return ParseGridTables(tables, pageText, selectedHints, now, "MIS 网页 DOM 直解析");
        }

        public static DirectTimetableParseResult ParseHtmlTimetable(string html, DateTime now)
        {
            var tables = new List<GridTable>();
            if (string.IsNullOrWhiteSpace(html)) return new DirectTimetableParseResult();

            string cleaned = Regex.Replace(html, @"<script[\s\S]*?</script>", " ", RegexOptions.IgnoreCase);
            cleaned = Regex.Replace(cleaned, @"<style[\s\S]*?</style>", " ", RegexOptions.IgnoreCase);

            MatchCollection tableMatches = Regex.Matches(cleaned, @"<table\b[^>]*>([\s\S]*?)</table>", RegexOptions.IgnoreCase);
            for (int t = 0; t < tableMatches.Count; t++)
            {
                string tHtml = tableMatches[t].Groups[1].Value;
                MatchCollection trMatches = Regex.Matches(tHtml, @"<tr\b[^>]*>([\s\S]*?)</tr>", RegexOptions.IgnoreCase);
                if (trMatches.Count < 2) continue;

                var gt = new GridTable();
                var occupied = new Dictionary<string, bool>();
                int maxCols = 0;
                var wdHits = new HashSet<int>();

                for (int r = 0; r < trMatches.Count; r++)
                {
                    int c = 0;
                    string rowHtml = trMatches[r].Groups[1].Value;
                    MatchCollection tdMatches = Regex.Matches(rowHtml, @"<(td|th)\b([^>]*)>([\s\S]*?)</\1>", RegexOptions.IgnoreCase);
                    for (int j = 0; j < tdMatches.Count; j++)
                    {
                        while (occupied.ContainsKey(r + ":" + c)) c++;
                        string attrs = tdMatches[j].Groups[2].Value;
                        string inner = tdMatches[j].Groups[3].Value;

                        int rs = ReadHtmlSpanAttr(attrs, "rowspan");
                        int cs = ReadHtmlSpanAttr(attrs, "colspan");
                        for (int dr = 0; dr < rs; dr++)
                        {
                            for (int dc = 0; dc < cs; dc++)
                            {
                                occupied[(r + dr) + ":" + (c + dc)] = true;
                            }
                        }

                        List<string> blocks;
                        string text = ExtractHtmlCellText(inner, out blocks);
                        int wd = ParseWeekdayHeaderCell(text);
                        if (wd >= 1 && wd <= 7) wdHits.Add(wd);

                        var gc = new GridCell
                        {
                            Row = r,
                            Col = c,
                            RowSpan = rs,
                            ColSpan = cs,
                            Text = text,
                            Blocks = blocks
                        };
                        gt.Cells.Add(gc);
                        if (c + cs > maxCols) maxCols = c + cs;
                        c += cs;
                    }
                }

                gt.RowCount = trMatches.Count;
                gt.ColCount = maxCols;
                gt.WeekdayHits = wdHits.Count;
                if (gt.ColCount >= 5 && gt.Cells.Count >= 6) tables.Add(gt);
            }

            var htmlHints = new List<string>();
            MatchCollection btnMatches = Regex.Matches(cleaned, @"<(?:button|h1|h2|h3|h4|option)\b[^>]*>([\s\S]*?)</(?:button|h1|h2|h3|h4|option)>", RegexOptions.IgnoreCase);
            for (int i = 0; i < btnMatches.Count; i++)
            {
                string tagWhole = btnMatches[i].Value;
                bool isOption = tagWhole.StartsWith("<option", StringComparison.OrdinalIgnoreCase);
                if (isOption && tagWhole.IndexOf("selected", StringComparison.OrdinalIgnoreCase) < 0) continue;
                string txt = Regex.Replace(btnMatches[i].Groups[1].Value, @"<[^>]+>", " ");
                txt = WebUtility.HtmlDecode(txt).Replace('\r', ' ').Replace('\n', ' ').Trim();
                txt = Regex.Replace(txt, @"\s+", " ");
                if (!string.IsNullOrEmpty(txt) && txt.Length < 80 && (txt.Contains("学期") || txt.Contains("学年") || txt.Contains("周")))
                {
                    htmlHints.Add(txt);
                }
            }

            string plain = Regex.Replace(cleaned, @"<!--[\s\S]*?-->", " ");
            plain = Regex.Replace(plain, @"<[^>]+>", " ");
            plain = WebUtility.HtmlDecode(plain);
            return ParseGridTables(tables, plain, htmlHints, now, "HTML 课表直解析");
        }

        private static int ReadHtmlSpanAttr(string attrs, string attrName)
        {
            if (string.IsNullOrEmpty(attrs)) return 1;
            Match m = Regex.Match(attrs, attrName + @"\s*=\s*[""']?\s*(\d{1,2})", RegexOptions.IgnoreCase);
            int v;
            if (m.Success && int.TryParse(m.Groups[1].Value, out v) && v >= 1 && v <= 20) return v;
            return 1;
        }

        private static List<string> SplitTopLevelDivHtmlBlocks(string html)
        {
            var topDivs = new List<string>();
            if (string.IsNullOrWhiteSpace(html)) return topDivs;

            MatchCollection tags = Regex.Matches(html, @"</?div\b[^>]*>", RegexOptions.IgnoreCase);
            if (tags.Count < 2) return topDivs;

            int depth = 0;
            int blockStart = -1;
            for (int i = 0; i < tags.Count; i++)
            {
                Match m = tags[i];
                bool isClose = m.Value.StartsWith("</", StringComparison.Ordinal);
                if (!isClose)
                {
                    if (depth == 0) blockStart = m.Index;
                    depth++;
                }
                else
                {
                    if (depth > 0) depth--;
                    if (depth == 0 && blockStart >= 0)
                    {
                        string slice = html.Substring(blockStart, (m.Index + m.Length) - blockStart);
                        topDivs.Add(slice);
                        blockStart = -1;
                    }
                }
            }
            return topDivs;
        }

        private static string HtmlFragmentToLines(string frag)
        {
            if (string.IsNullOrWhiteSpace(frag)) return "";
            string marked = Regex.Replace(frag, @"<!--[\s\S]*?-->", " ");
            marked = Regex.Replace(marked, @"[\r\n\t]+", " ");
            marked = Regex.Replace(marked, @"<hr\b[^>]*>", "\u001E", RegexOptions.IgnoreCase);
            marked = Regex.Replace(marked, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
            marked = Regex.Replace(marked, @"</(?:div|p|li|tr|h\d)>", "\n", RegexOptions.IgnoreCase);
            marked = Regex.Replace(marked, @"<[^>]+>", "");
            marked = WebUtility.HtmlDecode(marked).Replace('\u00A0', ' ');
            return NormalizeCellLines(marked.Replace("\u001E", "\n"));
        }

        private static string ExtractHtmlCellText(string innerHtml, out List<string> blocks)
        {
            blocks = new List<string>();
            if (string.IsNullOrWhiteSpace(innerHtml)) return "";

            string cleanHtml = Regex.Replace(innerHtml, @"<!--[\s\S]*?-->", " ");
            cleanHtml = Regex.Replace(cleanHtml, @"[\r\n\t]+", " ");
            List<string> topDivs = SplitTopLevelDivHtmlBlocks(cleanHtml);
            if (topDivs.Count >= 1)
            {
                var nonEmptyBlocks = new List<string>();
                for (int i = 0; i < topDivs.Count; i++)
                {
                    string bt = HtmlFragmentToLines(topDivs[i]);
                    if (!string.IsNullOrEmpty(bt)) nonEmptyBlocks.Add(bt);
                }
                if (nonEmptyBlocks.Count >= 2 || (nonEmptyBlocks.Count == 1 && Regex.Replace(cleanHtml, @"<[^>]+>", "").Trim().Length <= nonEmptyBlocks[0].Length + 20))
                {
                    blocks.AddRange(nonEmptyBlocks);
                    return string.Join("\n", nonEmptyBlocks.ToArray());
                }
            }

            string marked = Regex.Replace(cleanHtml, @"<hr\b[^>]*>", "\u001E", RegexOptions.IgnoreCase);
            marked = Regex.Replace(marked, @"<div\b[^>]*class\s*=\s*[""'][^""']*(?:kbcontent|timetable_con|course)[^""']*[""'][^>]*>", "\u001E", RegexOptions.IgnoreCase);
            marked = Regex.Replace(marked, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
            marked = Regex.Replace(marked, @"</(?:div|p|li|tr|h\d)>", "\n", RegexOptions.IgnoreCase);
            marked = Regex.Replace(marked, @"<[^>]+>", "");
            marked = WebUtility.HtmlDecode(marked).Replace('\u00A0', ' ');

            string[] rawBlocks = marked.Split(new char[] { '\u001E' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < rawBlocks.Length; i++)
            {
                string[] doubleSplit = Regex.Split(rawBlocks[i], @"\n{2,}");
                for (int j = 0; j < doubleSplit.Length; j++)
                {
                    string clean = NormalizeCellLines(doubleSplit[j]);
                    if (!string.IsNullOrEmpty(clean)) blocks.Add(clean);
                }
            }

            return NormalizeCellLines(marked.Replace("\u001E", "\n"));
        }

        private static string NormalizeCellLines(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            string[] lines = raw.Split(new char[] { '\n' });
            var sb = new StringBuilder();
            for (int i = 0; i < lines.Length; i++)
            {
                string ln = Regex.Replace(lines[i] ?? "", @"[ \t]+", " ").Trim();
                if (ln.Length == 0) continue;
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(ln);
            }
            return sb.ToString();
        }

        private static DirectTimetableParseResult ParseGridTables(
            List<GridTable> tables,
            string pageText,
            List<string> selectedHints,
            DateTime now,
            string sourceLabel)
        {
            var res = new DirectTimetableParseResult();
            res.SourceNote = sourceLabel;
            DetectSemesterAndWeekHints(pageText, selectedHints, now, res);

            GridTable best = null;
            int bestScore = -1;
            int headerRow = -1;
            Dictionary<int, int> colToDay = null;

            for (int i = 0; i < tables.Count; i++)
            {
                GridTable candidate = tables[i];
                int hRow;
                Dictionary<int, int> map = FindWeekdayHeaderMap(candidate, out hRow);
                if (map == null || map.Count < 4)
                {
                    // Try transposed timetable (weekdays along column 0..1, sections along header row)
                    GridTable transposed = TryTransposeTable(candidate);
                    if (transposed != null)
                    {
                        int thRow;
                        Dictionary<int, int> tMap = FindWeekdayHeaderMap(transposed, out thRow);
                        if (tMap != null && tMap.Count >= 4)
                        {
                            candidate = transposed;
                            hRow = thRow;
                            map = tMap;
                        }
                    }
                }

                if (map == null || map.Count < 4) continue;
                int nonEmpty = 0;
                for (int c = 0; c < candidate.Cells.Count; c++)
                {
                    if (!string.IsNullOrWhiteSpace(candidate.Cells[c].Text)) nonEmpty++;
                }
                int sc = map.Count * 500 + nonEmpty;
                if (sc > bestScore)
                {
                    bestScore = sc;
                    best = candidate;
                    headerRow = hRow;
                    colToDay = map;
                }
            }

            if (best == null || colToDay == null || headerRow < 0)
            {
                return res;
            }

            int minWeekdayCol = int.MaxValue;
            foreach (KeyValuePair<int, int> kv in colToDay)
            {
                if (kv.Key < minWeekdayCol) minWeekdayCol = kv.Key;
            }

            // Collect left-hand axis text per row (r > headerRow)
            var leftCellsByRow = new Dictionary<int, List<GridCell>>();
            var allRowCells = new Dictionary<int, List<GridCell>>();
            for (int i = 0; i < best.Cells.Count; i++)
            {
                GridCell cell = best.Cells[i];
                if (!allRowCells.ContainsKey(cell.Row)) allRowCells[cell.Row] = new List<GridCell>();
                allRowCells[cell.Row].Add(cell);
                if (cell.Col < minWeekdayCol && cell.Row > headerRow)
                {
                    for (int dr = 0; dr < cell.RowSpan; dr++)
                    {
                        int targetRow = cell.Row + dr;
                        if (!leftCellsByRow.ContainsKey(targetRow)) leftCellsByRow[targetRow] = new List<GridCell>();
                        leftCellsByRow[targetRow].Add(cell);
                    }
                }
            }

            var rowStartSec = new Dictionary<int, int>();
            var rowEndSec = new Dictionary<int, int>();
            var rowSkip = new HashSet<int>();
            int nextSeqSection = 1;
            int maxTableSection = 0;

            for (int r = headerRow + 1; r < best.RowCount; r++)
            {
                string rowAllText = "";
                if (allRowCells.ContainsKey(r))
                {
                    var parts = new List<string>();
                    for (int j = 0; j < allRowCells[r].Count; j++)
                    {
                        if (!string.IsNullOrWhiteSpace(allRowCells[r][j].Text)) parts.Add(allRowCells[r][j].Text);
                    }
                    rowAllText = string.Join(" ", parts.ToArray());
                }

                var leftTextBuilder = new StringBuilder();
                if (leftCellsByRow.ContainsKey(r))
                {
                    for (int j = 0; j < leftCellsByRow[r].Count; j++)
                    {
                        GridCell lc = leftCellsByRow[r][j];
                        if (lc.Row != r && lc.RowSpan > 1 && IsCoarsePeriodOnly(lc.Text)) continue;
                        if (leftTextBuilder.Length > 0) leftTextBuilder.Append("\n");
                        leftTextBuilder.Append(lc.Text);
                    }
                }
                string leftText = leftTextBuilder.ToString().Trim();

                if (IsSeparatorRow(leftText, rowAllText))
                {
                    rowSkip.Add(r);
                    continue;
                }

                int s1, s2;
                if (!TryParseSectionRange(leftText, out s1, out s2))
                {
                    // Skip purely blank rows
                    if (string.IsNullOrWhiteSpace(rowAllText))
                    {
                        rowSkip.Add(r);
                        continue;
                    }
                    s1 = nextSeqSection;
                    s2 = nextSeqSection;
                }
                if (s1 < 1) s1 = 1;
                if (s2 < s1) s2 = s1;
                if (s2 > 16) s2 = 16;

                rowStartSec[r] = s1;
                rowEndSec[r] = s2;
                if (s2 > maxTableSection) maxTableSection = s2;
                if (s2 >= nextSeqSection) nextSeqSection = s2 + 1;

                // Extract left-column concrete clock times for section(s)
                List<string[]> timePairs = ExtractTimePairs(leftText);
                if (timePairs.Count > 0)
                {
                    int secCount = s2 - s1 + 1;
                    if (secCount == 1)
                    {
                        res.Timetable.SetSectionTime(s1, timePairs[0][0], timePairs[0][1]);
                    }
                    else if (timePairs.Count >= secCount)
                    {
                        for (int k = 0; k < secCount; k++)
                        {
                            res.Timetable.SetSectionTime(s1 + k, timePairs[k][0], timePairs[k][1]);
                        }
                    }
                    else
                    {
                        res.Timetable.SetSectionTime(s1, timePairs[0][0], "");
                        res.Timetable.SetSectionTime(s2, "", timePairs[timePairs.Count - 1][1]);
                    }
                }
            }

            // Build structured text summary for transparency & AI fallback & extract courses deterministically
            var gridSb = new StringBuilder();
            if (res.Timetable.SectionTimes.Count > 0)
            {
                gridSb.Append("【左侧识别到的作息时间】\n");
                for (int i = 0; i < res.Timetable.SectionTimes.Count; i++)
                {
                    SectionTimeSlot st = res.Timetable.SectionTimes[i];
                    gridSb.AppendFormat("第{0}节: {1}-{2}\n", st.Section,
                        string.IsNullOrEmpty(st.StartTime) ? "??" : st.StartTime,
                        string.IsNullOrEmpty(st.EndTime) ? "??" : st.EndTime);
                }
            }

            var dedup = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < best.Cells.Count; i++)
            {
                GridCell cell = best.Cells[i];
                if (cell.Row <= headerRow) continue;
                if (rowSkip.Contains(cell.Row)) continue;
                int day;
                if (!colToDay.TryGetValue(cell.Col, out day)) continue;
                if (string.IsNullOrWhiteSpace(cell.Text)) continue;

                int cStartSec = int.MaxValue;
                int cEndSec = 0;
                for (int rr = cell.Row; rr < cell.Row + cell.RowSpan; rr++)
                {
                    int rsVal, reVal;
                    if (rowStartSec.TryGetValue(rr, out rsVal) && rsVal < cStartSec) cStartSec = rsVal;
                    if (rowEndSec.TryGetValue(rr, out reVal) && reVal > cEndSec) cEndSec = reVal;
                }
                if (cStartSec == int.MaxValue) cStartSec = 1;
                if (cEndSec < cStartSec) cEndSec = cStartSec;

                string resolvedStart = res.Timetable.GetSectionStartTime(cStartSec);
                string resolvedEnd = res.Timetable.GetSectionEndTime(cEndSec);

                gridSb.AppendFormat("[{0} | 第{1}-{2}节 | {3}-{4}]\n{5}\n",
                    CourseItem.GetWeekdayName(day), cStartSec, cEndSec, resolvedStart, resolvedEnd, cell.Text);

                List<CourseItem> parsedCourses = ParseCoursesFromCell(cell, day, cStartSec, cEndSec, res.Timetable);
                for (int k = 0; k < parsedCourses.Count; k++)
                {
                    CourseItem c = parsedCourses[k];
                    string key = string.Format(CultureInfo.InvariantCulture, "{0}|{1}|{2}|{3}|{4}|{5}|{6}|{7}",
                        c.Name, c.DayOfWeek, c.StartSection, c.EndSection, c.StartWeek, c.EndWeek, c.WeekType,
                        c.CustomWeeks != null ? string.Join(",", c.CustomWeeks) : "");
                    if (dedup.Add(key))
                    {
                        res.Timetable.Courses.Add(c);
                    }
                }
            }

            if (maxTableSection >= 4 && maxTableSection <= 16 && res.Timetable.SectionTimes.Count >= 4)
            {
                res.Timetable.SectionsPerDay = maxTableSection;
            }
            res.StructuredGridText = gridSb.ToString().Trim();
            res.Timetable.BackfillCourseTimesFromSections();
            res.Success = res.Timetable.Courses.Count > 0;
            return res;
        }

        private static Dictionary<int, int> FindWeekdayHeaderMap(GridTable table, out int headerRow)
        {
            headerRow = -1;
            Dictionary<int, int> bestMap = null;
            int maxScanRow = Math.Min(4, table.RowCount);
            for (int r = 0; r < maxScanRow; r++)
            {
                var map = new Dictionary<int, int>();
                var seenDays = new HashSet<int>();
                for (int i = 0; i < table.Cells.Count; i++)
                {
                    GridCell cell = table.Cells[i];
                    if (cell.Row != r) continue;
                    int wd = ParseWeekdayHeaderCell(cell.Text);
                    if (wd >= 1 && wd <= 7 && seenDays.Add(wd))
                    {
                        map[cell.Col] = wd;
                    }
                }
                if (bestMap == null || map.Count > bestMap.Count)
                {
                    bestMap = map;
                    headerRow = r;
                }
            }
            return bestMap;
        }

        private static GridTable TryTransposeTable(GridTable src)
        {
            if (src == null || src.RowCount < 5 || src.ColCount < 4) return null;
            int colWdHits = 0;
            for (int i = 0; i < src.Cells.Count; i++)
            {
                GridCell cell = src.Cells[i];
                if (cell.Col <= 1 && ParseWeekdayHeaderCell(cell.Text) >= 1) colWdHits++;
            }
            if (colWdHits < 5) return null;

            var tr = new GridTable
            {
                RowCount = src.ColCount,
                ColCount = src.RowCount
            };
            for (int i = 0; i < src.Cells.Count; i++)
            {
                GridCell c = src.Cells[i];
                tr.Cells.Add(new GridCell
                {
                    Row = c.Col,
                    Col = c.Row,
                    RowSpan = c.ColSpan,
                    ColSpan = c.RowSpan,
                    Text = c.Text,
                    Blocks = new List<string>(c.Blocks)
                });
            }
            return tr;
        }

        private static int ParseWeekdayHeaderCell(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            string s = text.Trim();
            if (Regex.IsMatch(s, @"(?:星期一|周一|\bMon(?:day)?\b|^一$)", RegexOptions.IgnoreCase | RegexOptions.Multiline)) return 1;
            if (Regex.IsMatch(s, @"(?:星期二|周二|\bTue(?:sday)?\b|^二$)", RegexOptions.IgnoreCase | RegexOptions.Multiline)) return 2;
            if (Regex.IsMatch(s, @"(?:星期三|周三|\bWed(?:nesday)?\b|^三$)", RegexOptions.IgnoreCase | RegexOptions.Multiline)) return 3;
            if (Regex.IsMatch(s, @"(?:星期四|周四|\bThu(?:rsday)?\b|^四$)", RegexOptions.IgnoreCase | RegexOptions.Multiline)) return 4;
            if (Regex.IsMatch(s, @"(?:星期五|周五|\bFri(?:day)?\b|^五$)", RegexOptions.IgnoreCase | RegexOptions.Multiline)) return 5;
            if (Regex.IsMatch(s, @"(?:星期六|周六|\bSat(?:urday)?\b|^六$)", RegexOptions.IgnoreCase | RegexOptions.Multiline)) return 6;
            if (Regex.IsMatch(s, @"(?:星期日|星期天|周日|周天|\bSun(?:day)?\b|^日$|^天$)", RegexOptions.IgnoreCase | RegexOptions.Multiline)) return 7;
            return 0;
        }

        private static bool IsCoarsePeriodOnly(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return true;
            string t = text.Trim();
            return Regex.IsMatch(t, @"^(?:上午|下午|晚上|晚间|中午|早自习)$");
        }

        private static bool IsSeparatorRow(string leftText, string rowAllText)
        {
            string probe = string.IsNullOrWhiteSpace(leftText) ? rowAllText : leftText;
            if (string.IsNullOrWhiteSpace(probe)) return true;
            if (Regex.IsMatch(probe, @"午休|午间|休息时间|其它课程|其他课程|实践环节|备注\s*[:：]"))
            {
                if (!Regex.IsMatch(probe, @"\d{1,2}\s*节") && !Regex.IsMatch(probe, @"\d{1,2}[:：]\d{2}")) return true;
            }
            return false;
        }

        private static bool TryParseSectionRange(string leftText, out int startSec, out int endSec)
        {
            startSec = 0;
            endSec = 0;
            if (string.IsNullOrWhiteSpace(leftText)) return false;

            // Remove HH:mm clock times so numbers like 08 and 45 are never mistaken for section numbers
            string work = Regex.Replace(leftText, @"\d{1,2}[:：]\d{2}", " ");
            work = work.Replace('—', '-').Replace('~', '-').Replace('–', '-').Replace('至', '-').Replace('到', '-');

            Match mRange = Regex.Match(work, @"第?\s*0?(\d{1,2})\s*[-,]\s*0?(\d{1,2})\s*节?");
            if (mRange.Success)
            {
                int a, b;
                if (int.TryParse(mRange.Groups[1].Value, out a) && int.TryParse(mRange.Groups[2].Value, out b))
                {
                    if (a >= 1 && a <= 16 && b >= a && b <= 16)
                    {
                        startSec = a;
                        endSec = b;
                        return true;
                    }
                }
            }

            Match mLinePair = Regex.Match(work, @"\b0?(\d{1,2})\s*\r?\n\s*0?(\d{1,2})\b");
            if (mLinePair.Success)
            {
                int a, b;
                if (int.TryParse(mLinePair.Groups[1].Value, out a) && int.TryParse(mLinePair.Groups[2].Value, out b))
                {
                    if (a >= 1 && a <= 16 && b == a + 1)
                    {
                        startSec = a;
                        endSec = b;
                        return true;
                    }
                }
            }

            Match mSingle = Regex.Match(work, @"(?:第\s*0?(\d{1,2})\s*节|^\s*0?(\d{1,2})\s*$)", RegexOptions.Multiline);
            if (mSingle.Success)
            {
                string tok = mSingle.Groups[1].Success ? mSingle.Groups[1].Value : mSingle.Groups[2].Value;
                int s;
                if (int.TryParse(tok, out s) && s >= 1 && s <= 16)
                {
                    startSec = s;
                    endSec = s;
                    return true;
                }
            }

            Match mCn = Regex.Match(work, @"第\s*([一二三四五六七八九十]+)\s*节");
            if (mCn.Success)
            {
                int cn = ParseSmallChineseNum(mCn.Groups[1].Value);
                if (cn >= 1 && cn <= 16)
                {
                    startSec = cn;
                    endSec = cn;
                    return true;
                }
            }
            return false;
        }

        private static int ParseSmallChineseNum(string s)
        {
            if (string.IsNullOrEmpty(s)) return 0;
            if (s == "十") return 10;
            if (s.StartsWith("十") && s.Length == 2) return 10 + ParseSmallChineseDigit(s[1]);
            if (s.Length == 1) return ParseSmallChineseDigit(s[0]);
            return 0;
        }

        private static int ParseSmallChineseDigit(char c)
        {
            switch (c)
            {
                case '一': return 1;
                case '二': return 2;
                case '三': return 3;
                case '四': return 4;
                case '五': return 5;
                case '六': return 6;
                case '七': return 7;
                case '八': return 8;
                case '九': return 9;
                default: return 0;
            }
        }

        private static List<string[]> ExtractTimePairs(string text)
        {
            var pairs = new List<string[]>();
            if (string.IsNullOrWhiteSpace(text)) return pairs;

            MatchCollection rangeMatches = Regex.Matches(text, @"(\d{1,2}[:：]\d{2})\s*[-~—至到\r\n]+\s*(\d{1,2}[:：]\d{2})");
            for (int i = 0; i < rangeMatches.Count; i++)
            {
                string s = CourseItem.NormalizeTimeToken(rangeMatches[i].Groups[1].Value);
                string e = CourseItem.NormalizeTimeToken(rangeMatches[i].Groups[2].Value);
                if (!string.IsNullOrEmpty(s) && !string.IsNullOrEmpty(e))
                {
                    pairs.Add(new string[] { s, e });
                }
            }

            if (pairs.Count == 0)
            {
                MatchCollection singles = Regex.Matches(text, @"\b(\d{1,2}[:：]\d{2})\b");
                for (int i = 0; i + 1 < singles.Count; i += 2)
                {
                    string s = CourseItem.NormalizeTimeToken(singles[i].Groups[1].Value);
                    string e = CourseItem.NormalizeTimeToken(singles[i + 1].Groups[1].Value);
                    if (!string.IsNullOrEmpty(s) && !string.IsNullOrEmpty(e))
                    {
                        pairs.Add(new string[] { s, e });
                    }
                }
            }

            return pairs;
        }

        private static List<CourseItem> ParseCoursesFromCell(
            GridCell cell,
            int day,
            int cellStartSec,
            int cellEndSec,
            TimetableData tt)
        {
            var results = new List<CourseItem>();
            var rawBlocks = new List<string>();
            if (cell.Blocks != null && cell.Blocks.Count > 0)
            {
                rawBlocks.AddRange(cell.Blocks);
            }
            else if (!string.IsNullOrWhiteSpace(cell.Text))
            {
                rawBlocks.Add(cell.Text);
            }

            var subBlocks = new List<string>();
            for (int i = 0; i < rawBlocks.Count; i++)
            {
                List<string> split = SplitSubCourseBlocks(rawBlocks[i]);
                subBlocks.AddRange(split);
            }

            for (int i = 0; i < subBlocks.Count; i++)
            {
                CourseItem item = ParseSingleCourseBlock(subBlocks[i], day, cellStartSec, cellEndSec, tt);
                if (item != null && !string.IsNullOrWhiteSpace(item.Name))
                {
                    results.Add(item);
                }
            }
            return results;
        }

        private static List<string> SplitSubCourseBlocks(string rawBlock)
        {
            var blocks = new List<string>();
            if (string.IsNullOrWhiteSpace(rawBlock)) return blocks;

            // Split by visual divider lines or decorative course bullet symbols if repeated
            string[] sepChunks = Regex.Split(rawBlock, @"(?:^|\n)\s*[-=_*·]{3,}\s*(?:\n|$)");
            for (int c = 0; c < sepChunks.Length; c++)
            {
                string chunk = (sepChunks[c] ?? "").Trim();
                if (string.IsNullOrEmpty(chunk)) continue;

                string[] lines = chunk.Split(new char[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                var cleanedLines = new List<string>();
                for (int i = 0; i < lines.Length; i++)
                {
                    string ln = lines[i].Trim();
                    if (!string.IsNullOrEmpty(ln)) cleanedLines.Add(ln);
                }
                if (cleanedLines.Count == 0) continue;

                // Split stacked courses sharing one cell without <hr> (e.g. odd course lines followed by even course lines)
                int startIdx = 0;
                int lastWeekLineIdx = -1;
                for (int i = 0; i < cleanedLines.Count; i++)
                {
                    int sw, ew;
                    string wt;
                    List<int> cw;
                    bool hasWeek = CourseItem.TryParseWeekSpec(cleanedLines[i], out sw, out ew, out wt, out cw);
                    if (hasWeek)
                    {
                        if (lastWeekLineIdx >= 0)
                        {
                            // Find the first non-metadata line after lastWeekLineIdx (likely the second course's title)
                            int splitAt = i;
                            for (int k = lastWeekLineIdx + 1; k < i; k++)
                            {
                                string probe = cleanedLines[k];
                                if (!LooksLikeLocationLine(probe) && !LooksLikeTeacherLine(probe) && !LooksLikeTimeOrSectionLine(probe))
                                {
                                    splitAt = k;
                                    break;
                                }
                            }
                            if (splitAt > startIdx)
                            {
                                blocks.Add(string.Join("\n", cleanedLines.GetRange(startIdx, splitAt - startIdx).ToArray()));
                                startIdx = splitAt;
                            }
                        }
                        lastWeekLineIdx = i;
                    }
                }
                if (startIdx < cleanedLines.Count)
                {
                    blocks.Add(string.Join("\n", cleanedLines.GetRange(startIdx, cleanedLines.Count - startIdx).ToArray()));
                }
            }
            return blocks;
        }

        private static CourseItem ParseSingleCourseBlock(
            string blockText,
            int day,
            int cellStartSec,
            int cellEndSec,
            TimetableData tt)
        {
            if (string.IsNullOrWhiteSpace(blockText)) return null;

            string[] rawLines = blockText.Split(new char[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var lines = new List<string>();
            for (int i = 0; i < rawLines.Length; i++)
            {
                string ln = Regex.Replace(rawLines[i] ?? "", @"^[◆◇★☆●•\-\s]+", "").Trim();
                if (!string.IsNullOrEmpty(ln)) lines.Add(ln);
            }
            if (lines.Count == 0) return null;

            var c = new CourseItem();
            c.DayOfWeek = Math.Max(1, Math.Min(7, day));
            c.StartSection = Math.Max(1, Math.Min(16, cellStartSec));
            c.EndSection = Math.Max(c.StartSection, Math.Min(16, cellEndSec));

            // 1. Parse inline clock time if explicitly present inside cell
            List<string[]> inlineTimes = ExtractTimePairs(blockText);
            if (inlineTimes.Count > 0)
            {
                c.StartTime = inlineTimes[0][0];
                c.EndTime = inlineTimes[0][1];
            }

            // 2. Parse inline explicit section override ONLY when row grid wasn't fine-grained or inline specifies tighter range
            Match secMatch = Regex.Match(blockText, @"[\(\[]?\s*(?:第\s*)?(\d{1,2})\s*[-~—至]\s*(\d{1,2})\s*节\s*[\)\]]?");
            if (secMatch.Success)
            {
                int ss, es;
                if (int.TryParse(secMatch.Groups[1].Value, out ss) && int.TryParse(secMatch.Groups[2].Value, out es) && ss >= 1 && es >= ss && es <= 16)
                {
                    if (c.EndSection - c.StartSection > 3 || (ss >= c.StartSection && es <= c.EndSection))
                    {
                        c.StartSection = ss;
                        c.EndSection = es;
                    }
                }
            }

            // 3. Parse weeks + odd/even
            int pSw, pEw;
            string pWt;
            List<int> pCw;
            if (CourseItem.TryParseWeekSpec(blockText, out pSw, out pEw, out pWt, out pCw))
            {
                c.StartWeek = pSw;
                c.EndWeek = pEw;
                c.WeekType = pWt;
                c.CustomWeeks = pCw ?? new List<int>();
            }
            // Fallback explicit marker if page wrote e.g. "(单)" separately from "1-16周"
            if (c.WeekType == "all")
            {
                if (Regex.IsMatch(blockText, @"[\(\[（]单周?[\)\]）]|单周")) c.WeekType = "odd";
                else if (Regex.IsMatch(blockText, @"[\(\[（]双周?[\)\]）]|双周")) c.WeekType = "even";
            }

            // 4. Identify course name, location, teacher across lines
            var titleCandidates = new List<string>();
            for (int i = 0; i < lines.Count; i++)
            {
                string ln = lines[i];

                // Inline labelled attributes separated by slash/space like "1-16周(单) / 张三 / 教一-102"
                string stripped = StripInlineWeekAndSectionTokens(ln);
                if (string.IsNullOrWhiteSpace(stripped)) continue;

                // Skip standalone course codes / class numbers (e.g. C108020B [02])
                if (IsCourseCodeLine(stripped)) continue;

                // Explicit location label (require separator after label so phrases like "地点见..." stay intact)
                Match locLbl = Regex.Match(stripped, @"(?:@|教室\s*[:：\s]|地点\s*[:：]|上课地点\s*[:：\s]|\[教室\])\s*(.+)$");
                if (locLbl.Success && string.IsNullOrEmpty(c.Location))
                {
                    string locVal = CleanLocationText(locLbl.Groups[1].Value.Trim());
                    string beforeLoc = stripped.Substring(0, locLbl.Index).Trim();
                    c.Location = TruncateText(locVal, 48);
                    if (!string.IsNullOrWhiteSpace(beforeLoc)) titleCandidates.Add(beforeLoc);
                    continue;
                }

                // Explicit teacher label
                Match tchLbl = Regex.Match(stripped, @"(?:教师[:：]?|老师[:：]?|任课教师[:：]?|\[教师\])\s*([^\s,，;/]+)");
                if (tchLbl.Success && string.IsNullOrEmpty(c.Teacher))
                {
                    c.Teacher = TruncateText(tchLbl.Groups[1].Value.Trim(), 30);
                    string rem = (stripped.Substring(0, tchLbl.Index) + " " + stripped.Substring(tchLbl.Index + tchLbl.Length)).Trim();
                    if (!string.IsNullOrWhiteSpace(rem) && !IsMetadataOnlyLine(rem) && !IsCourseCodeLine(rem)) titleCandidates.Add(rem);
                    continue;
                }

                if (IsMetadataOnlyLine(stripped)) continue;

                string cleanedLoc = CleanLocationText(stripped);
                if (i > 0 && string.IsNullOrEmpty(c.Location) && LooksLikeLocationLine(cleanedLoc))
                {
                    c.Location = TruncateText(cleanedLoc, 48);
                    continue;
                }

                if (i > 0 && string.IsNullOrEmpty(c.Teacher) && LooksLikeTeacherLine(stripped))
                {
                    c.Teacher = TruncateText(stripped, 30);
                    continue;
                }

                titleCandidates.Add(stripped);
            }

            if (titleCandidates.Count == 0 && lines.Count > 0)
            {
                for (int i = 0; i < lines.Count; i++)
                {
                    string fallback = StripInlineWeekAndSectionTokens(lines[i]);
                    if (!string.IsNullOrEmpty(fallback) && !IsCourseCodeLine(fallback) && !IsMetadataOnlyLine(fallback))
                    {
                        titleCandidates.Add(fallback);
                        break;
                    }
                }
            }

            if (titleCandidates.Count == 0) return null;

            string nameCandidate = titleCandidates[0];
            nameCandidate = Regex.Replace(nameCandidate, @"\s*[\[\(（【]\s*(?:本|研|主修|辅修|选修|必修|通识)\s*[\]\)）】]\s*$", "");
            nameCandidate = Regex.Replace(nameCandidate, @"\s+", " ").Trim();
            if (nameCandidate.Length <= 1) return null;
            c.Name = TruncateText(nameCandidate, 50);

            // Second title candidate might be a combined Teacher Location line (e.g. "张教授 教一-102")
            for (int i = 1; i < titleCandidates.Count; i++)
            {
                string extra = titleCandidates[i];
                string extraLoc = CleanLocationText(extra);
                if (string.IsNullOrEmpty(c.Location) && LooksLikeLocationLine(extraLoc))
                {
                    c.Location = TruncateText(extraLoc, 48);
                }
                else if (string.IsNullOrEmpty(c.Teacher) && LooksLikeTeacherLine(extra))
                {
                    c.Teacher = TruncateText(extra, 30);
                }
            }

            c.ColorHex = PickCourseColorHex(c.Name);
            return c;
        }

        private static bool IsCourseCodeLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return false;
            string s = line.Trim();
            return Regex.IsMatch(s, @"^[A-Za-z]{0,4}\d{4,10}[A-Za-z0-9\-]*(?:\s*[\[\(（【]\s*[A-Za-z0-9]{1,4}\s*[\]\)）】])?$");
        }

        private static string CleanLocationText(string loc)
        {
            if (string.IsNullOrWhiteSpace(loc)) return "";
            string s = Regex.Replace(loc.Trim(), @"\s*[,，]\s*-\s*[,，]\s*", ", ");
            s = Regex.Replace(s, @"^\s*[-,，]+\s*|\s*[-,，]+\s*$", "");
            s = Regex.Replace(s, @"\s*[,，]\s*[,，]+\s*", ", ");
            s = Regex.Replace(s, @"\s+", " ");
            return s.Trim();
        }

        private static string StripInlineWeekAndSectionTokens(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return "";
            string s = line.Trim();
            s = Regex.Replace(s, @"\d{1,2}[:：]\d{2}\s*[-~—至到]\s*\d{1,2}[:：]\d{2}", " ");
            s = Regex.Replace(s, @"[\(\[（【]?\s*(?:第\s*)?\d{1,2}(?:\s*[-~,，、至到]\s*\d{1,2})*\s*节\s*[\)\]）】]?", " ");
            s = Regex.Replace(s, @"[\(\[（【]?\s*(?:第\s*)?\d{1,2}(?:\s*[-~,，、至到]\s*\d{1,2})*\s*周(?:\s*[\(\[（]?(?:单周?|双周?|odd|even)[\)\]）]?)?\s*[\)\]）】]?", " ", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"[\(\[（【]\s*(?:单周?|双周?|odd|even)\s*[\)\]）】]", " ", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"[\(\[（【]\s*[\)\]）】]", " ");
            s = Regex.Replace(s, @"^[/\\|·,;，；\-]+|[/\\|·,;，；\-]+$", "");
            return Regex.Replace(s, @"\s+", " ").Trim();
        }

        private static bool LooksLikeTimeOrSectionLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return true;
            string s = StripInlineWeekAndSectionTokens(line);
            return string.IsNullOrWhiteSpace(s);
        }

        private static bool LooksLikeLocationLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return false;
            string s = line.Trim();
            if (s.Length > 48) return false;
            if (Regex.IsMatch(s, @"楼|室|馆|厅|场|区|机房|实验室|实训|中心|校区|阶梯|教学楼|教[一二三四五六七八九\d]|东区|西区|南区|北区|线上|网课|网络|地点见"))
                return true;
            if (Regex.IsMatch(s, @"^[A-Za-z]{1,4}[-_]?\d{3,4}[A-Za-z]?$")) return true;
            if (Regex.IsMatch(s, @"^\d{1,2}-\d{3,4}$")) return true;
            return false;
        }

        private static bool LooksLikeTeacherLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return false;
            string s = line.Trim();
            if (Regex.IsMatch(s, @"^[\u4e00-\u9fa5]{2,4}(?:\s*[,，、/]\s*[\u4e00-\u9fa5]{2,4}){0,2}$")) return true;
            return false;
        }

        private static bool IsMetadataOnlyLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return true;
            string s = line.Trim();
            if (Regex.IsMatch(s, @"^(?:必修|选修|公选|限选|任选|考查|考试|学分[:：]?\s*\d+(?:\.\d+)?|\d+(?:\.\d+)?学分|周学时[:：]?\s*\d+)$"))
                return true;
            if (Regex.IsMatch(s, @"^(?:单周|双周|单|双|全周)$")) return true;
            return false;
        }

        private static void DetectSemesterAndWeekHints(
            string pageText,
            List<string> selectedHints,
            DateTime now,
            DirectTimetableParseResult res)
        {
            var pool = new List<string>();
            if (selectedHints != null) pool.AddRange(selectedHints);
            if (!string.IsNullOrWhiteSpace(pageText)) pool.Add(pageText);

            for (int i = 0; i < pool.Count; i++)
            {
                string src = pool[i];
                if (string.IsNullOrWhiteSpace(src)) continue;

                if (string.IsNullOrEmpty(res.Timetable.SemesterName))
                {
                    Match sem = Regex.Match(src, @"(20\d{2}\s*[-~—]\s*20\d{2}\s*-\s*[12]\s*学期?|20\d{2}\s*[-~—]\s*20\d{2}\s*学年?(?:第?[12一二]学期)?|20\d{2}\s*[春夏秋冬](?:季)?(?:学期)?)");
                    if (sem.Success)
                    {
                        res.Timetable.SemesterName = TruncateText(sem.Groups[1].Value, 32);
                    }
                }

                if (res.CurrentWeek <= 0)
                {
                    Match curW = Regex.Match(src, @"(?:当前(?:为|是|教学)?(?:第)?\s*|本周(?:为|是)?\s*第\s*|^\s*第\s*)(\d{1,2})\s*周", RegexOptions.Multiline);
                    int cw;
                    if (curW.Success && int.TryParse(curW.Groups[1].Value, out cw) && cw >= 1 && cw <= 30)
                    {
                        res.CurrentWeek = cw;
                        res.Timetable.SetCurrentWeekForDate(cw, now);
                    }
                }
            }

            if (res.CurrentWeek <= 0 && !string.IsNullOrWhiteSpace(pageText))
            {
                Match wm = Regex.Match(pageText, @"第\s*(\d{1,2})\s*周");
                int cw;
                if (wm.Success && int.TryParse(wm.Groups[1].Value, out cw) && cw >= 1 && cw <= 25)
                {
                    res.CurrentWeek = cw;
                    res.Timetable.SetCurrentWeekForDate(cw, now);
                }
            }
        }

        private static string TruncateText(string s, int maxLen)
        {
            if (string.IsNullOrEmpty(s)) return "";
            string clean = s.Replace("\r", " ").Replace("\n", " ").Trim();
            if (clean.Length <= maxLen) return clean;
            return clean.Substring(0, maxLen);
        }

        public static TimetableData ParseIcsContent(string icsText, DateTime now)
        {
            var tt = new TimetableData();
            if (string.IsNullOrWhiteSpace(icsText)) return tt;

            string unfolded = Regex.Replace(icsText, @"\r?\n[ \t]", "");
            string[] lines = unfolded.Split(new string[] { "\r\n", "\n" }, StringSplitOptions.None);

            bool inEvent = false;
            string summary = "";
            string location = "";
            string description = "";
            DateTime? dtStart = null;
            DateTime? dtEnd = null;
            int count = 16;
            int interval = 1;
            DateTime? minCourseDate = null;

            for (int i = 0; i < lines.Length; i++)
            {
                string line = (lines[i] ?? "").Trim();
                if (line.Equals("BEGIN:VEVENT", StringComparison.OrdinalIgnoreCase))
                {
                    inEvent = true;
                    summary = "";
                    location = "";
                    description = "";
                    dtStart = null;
                    dtEnd = null;
                    count = 16;
                    interval = 1;
                    continue;
                }
                if (line.Equals("END:VEVENT", StringComparison.OrdinalIgnoreCase))
                {
                    if (inEvent && !string.IsNullOrWhiteSpace(summary) && dtStart.HasValue)
                    {
                        if (!minCourseDate.HasValue || dtStart.Value.Date < minCourseDate.Value)
                        {
                            minCourseDate = dtStart.Value.Date;
                        }
                        var c = new CourseItem();
                        c.Name = summary.Trim();
                        c.Location = location.Trim();
                        c.Teacher = ExtractTeacherFromDesc(description);
                        c.DayOfWeek = TimetableData.ToIsoWeekday(dtStart.Value.DayOfWeek);
                        c.StartTime = dtStart.Value.ToString("HH:mm", CultureInfo.InvariantCulture);
                        c.EndTime = dtEnd.HasValue ? dtEnd.Value.ToString("HH:mm", CultureInfo.InvariantCulture) : c.StartTime;
                        c.StartSection = InferSectionFromTime(dtStart.Value.Hour, dtStart.Value.Minute);
                        c.EndSection = dtEnd.HasValue
                            ? Math.Max(c.StartSection, InferSectionFromTime(dtEnd.Value.Hour, dtEnd.Value.Minute))
                            : Math.Min(12, c.StartSection + 1);
                        c.StartWeek = 1;
                        c.EndWeek = Math.Max(1, Math.Min(30, count * interval));
                        c.WeekType = interval == 2 ? "odd" : "all";
                        c.ColorHex = PickCourseColorHex(c.Name);
                        tt.Courses.Add(c);
                    }
                    inEvent = false;
                    continue;
                }
                if (!inEvent) continue;

                int colon = line.IndexOf(':');
                if (colon <= 0) continue;
                string keyPart = line.Substring(0, colon).ToUpperInvariant();
                string valPart = line.Substring(colon + 1).Trim();
                int semi = keyPart.IndexOf(';');
                string baseKey = semi > 0 ? keyPart.Substring(0, semi) : keyPart;

                if (baseKey == "SUMMARY") summary = UnescapeIcs(valPart);
                else if (baseKey == "LOCATION") location = UnescapeIcs(valPart);
                else if (baseKey == "DESCRIPTION") description = UnescapeIcs(valPart);
                else if (baseKey == "DTSTART") dtStart = ParseIcsDateTime(valPart);
                else if (baseKey == "DTEND") dtEnd = ParseIcsDateTime(valPart);
                else if (baseKey == "RRULE")
                {
                    Match mCount = Regex.Match(valPart, @"COUNT=(\d+)", RegexOptions.IgnoreCase);
                    if (mCount.Success) int.TryParse(mCount.Groups[1].Value, out count);
                    Match mInt = Regex.Match(valPart, @"INTERVAL=(\d+)", RegexOptions.IgnoreCase);
                    if (mInt.Success) int.TryParse(mInt.Groups[1].Value, out interval);
                }
            }

            if (minCourseDate.HasValue)
            {
                int delta = ((int)minCourseDate.Value.DayOfWeek + 6) % 7;
                tt.SemesterStartMondayIso = minCourseDate.Value.AddDays(-delta).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                tt.TotalWeeks = 20;
            }
            tt.BackfillCourseTimesFromSections();
            return tt;
        }

        public static TimetableData ParseFallbackTimetableText(string text, DateTime now)
        {
            var tt = new TimetableData();
            if (string.IsNullOrWhiteSpace(text)) return tt;

            string plain = Regex.Replace(text, @"<script[\s\S]*?</script>", " ", RegexOptions.IgnoreCase);
            plain = Regex.Replace(plain, @"<style[\s\S]*?</style>", " ", RegexOptions.IgnoreCase);
            plain = Regex.Replace(plain, @"<[^>]+>", " ");
            plain = WebUtility.HtmlDecode(plain);

            Match wm = Regex.Match(plain, @"第\s*(\d{1,2})\s*周");
            if (wm.Success)
            {
                int cw;
                if (int.TryParse(wm.Groups[1].Value, out cw) && cw >= 1 && cw <= 25)
                {
                    tt.SetCurrentWeekForDate(cw, now);
                }
            }
            return tt;
        }

        private static string UnescapeIcs(string v)
        {
            return (v ?? "").Replace("\\n", " ").Replace("\\,", ",").Replace("\\;", ";").Replace("\\\\", "\\");
        }

        private static string ExtractTeacherFromDesc(string desc)
        {
            if (string.IsNullOrWhiteSpace(desc)) return "";
            Match m = Regex.Match(desc, @"(?:教师|老师|Teacher)[:：\s]+([^\s,;，；]+)", RegexOptions.IgnoreCase);
            return m.Success ? m.Groups[1].Value.Trim() : "";
        }

        private static DateTime? ParseIcsDateTime(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            string s = raw.Trim().TrimEnd('Z');
            DateTime dt;
            if (DateTime.TryParseExact(s, "yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out dt)) return dt;
            if (DateTime.TryParseExact(s, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out dt)) return dt;
            return null;
        }

        private static int InferSectionFromTime(int hour, int minute)
        {
            int mins = hour * 60 + minute;
            if (mins < 8 * 60 + 50) return 1;
            if (mins < 9 * 60 + 50) return 2;
            if (mins < 10 * 60 + 50) return 3;
            if (mins < 12 * 60 + 15) return 4;
            if (mins < 14 * 60 + 50) return 5;
            if (mins < 15 * 60 + 50) return 6;
            if (mins < 16 * 60 + 50) return 7;
            if (mins < 18 * 60 + 30) return 8;
            if (mins < 19 * 60 + 50) return 9;
            if (mins < 20 * 60 + 45) return 10;
            if (mins < 21 * 60 + 38) return 11;
            return 12;
        }

        public static string PickCourseColorHex(string name)
        {
            string[] palette = new string[]
            {
                "#2563EB", "#D97706", "#059669", "#7C3AED",
                "#DC2626", "#0891B2", "#BE4B62", "#4F46E5"
            };
            if (string.IsNullOrEmpty(name)) return palette[0];
            int hash = 17;
            for (int i = 0; i < name.Length; i++) hash = unchecked(hash * 31 + name[i]);
            return palette[(hash & 0x7FFFFFFF) % palette.Length];
        }
    }
}
