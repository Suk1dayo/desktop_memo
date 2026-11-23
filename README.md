# DesktopMemo

轻量、原生、Claude / Notion / Linear 极简扁平风格的 **Windows 桌面备忘录**，附带独立网页后端（类 Claude 极简界面）。

纯 C# / WPF + Win32 原生实现，无第三方运行时依赖，单文件 `.exe` 开箱即用。

---

## 功能亮点 (v2.0)

### 极简扁平编辑风 + 丝滑弹簧动效
- 内置 6 套高质感克制扁平主题：**Claude 暖杏极简**（默认）、**Notion 纯净白**、**Linear 深空暗黑**、**Nord 静谧灰蓝**、**青岚护眼浅绿**、**莫兰迪暖灰粉**，支持 60%–100% 窗口不透明度调节。
- 顶部采用手机桌面式悬浮分段胶囊导航，页面切换、胶囊滑移与按钮交互均配有阻尼弹簧物理动效。

### 全局尺度与极简交互
- 在「**AI 与设置**」中可自由切换按钮与界面尺度（`紧凑 S` / `标准 M` / `大按钮 L`）、全局字号与不透明度。
- 独立便签中新建的任务默认保持「**未分类**」，不会强行塞入 Q2；仅在你主动点击 **AI 智能排序** 或手动选择时才归入四象限。
- 独立便签右上角 `⚙` 可自由开关「上方随手记文字区」；「独立便利贴」页中的锁定按钮仅锁定便签，不再误锁大面板。

### 微信聊天 / 通知 AI 智能提取
- 大面板和每张小便签右上角均提供 `💬` 提取入口：粘贴一段微信群通知、作业要求或会议分工，AI 自动拆解任务、推算 DDL 和四象限，经你预览勾选确认后再写入。

### 四象限任务管理
- 重要 / 紧急四象限视图、独立便签悬浮窗、DDL 倒计时、桌面提醒弹窗与提示音。
- 撤销 Toast（Undo）、系统托盘菜单、全局热键、开机自启。

### 类 Claude 网页端 + Windows Server 一键部署
- 手机或电脑浏览器通过口令登录使用，与桌面端共享同一份 `data/` 数据。
- 桌面端「AI 与设置」中可填写远程 Server 地址实现同步。

---

## 快速开始

### 桌面端
直接双击 `DesktopMemo.exe` 即可贴在桌面上运行。

### 网页端（本地或 Windows Server）
双击 `start_server.bat` 启动 `DesktopMemoServer.exe`；或指定端口：

```bat
start_server.bat 8765
```

### Windows Server 一键部署

以管理员身份运行 PowerShell：

```powershell
powershell -ExecutionPolicy Bypass -File .\setup_windows_server.ps1 -Port 8765 -Password "你的访问口令"
```

脚本会自动完成：

1. 放行 Windows 防火墙入站端口；
2. 注册开机后台自启计划任务并启动服务；
3. 输出本机 / 局域网访问地址与访问口令。

随后即可在手机或电脑浏览器通过口令登录使用。

---

## 从源码编译

依赖：Windows 10/11 自带的 .NET Framework 4.x（`csc.exe`），**无需安装 Visual Studio**。

```bat
build.bat
```

脚本会同时产出：

| 产物 | 说明 |
| --- | --- |
| `DesktopMemo.exe` | WPF 桌面端（`/target:winexe`） |
| `DesktopMemoServer.exe` | 网页后端（`/target:exe`，复用 `Models.cs` / `StorageService.cs` / `AiSortService.cs`） |

---

## 目录结构

```
desktop_memo/
├── src/                        # 桌面端源码（WPF，C#）
│   ├── Program.cs              # 入口
│   ├── DashboardWindow.cs      # 大面板主界面
│   ├── StickyNoteWindow.cs     # 独立便签窗口
│   ├── TaskEditDialog.cs       # 任务编辑弹窗
│   ├── ChatExtractDialog.cs    # 聊天记录 AI 提取弹窗
│   ├── TrayAndAppController.cs # 托盘、热键、窗口调度
│   ├── ThemeService.cs         # 6 套主题与尺度度量
│   ├── GlassControlStyles.cs   # 玻璃质感控件样式
│   ├── SpringAnimationHelper.cs# iOS 风格弹簧动效
│   ├── ReminderService.cs      # DDL 提醒调度
│   ├── ReminderToastWindow.cs  # 提醒弹窗
│   ├── UndoToastWindow.cs      # 撤销提示
│   ├── AiSortService.cs        # AI 排序 / 提取（DeepSeek 等 OpenAI 兼容接口）
│   ├── StorageService.cs       # data/ 持久化
│   ├── Models.cs               # 数据模型
│   ├── Win32Native.cs          # 贴桌面 / 无边框 / 毛玻璃原生调用
│   ├── Tests.cs                # 内置自检
│   ├── app.manifest            # DPI 感知与 UAC 声明
│   └── app.ico
├── server_src/
│   └── ServerProgram.cs        # HttpListener 网页后端 + 口令鉴权 + JSON API
├── web/                        # 网页端前端
│   ├── index.html
│   ├── style.css
│   └── app.js
├── data/                       # 运行期数据（已被 .gitignore 排除）
├── build.bat                   # 一键编译桌面端 + 服务端
├── start_server.bat            # 启动网页后端起服务
├── setup_windows_server.ps1    # Windows Server 一键部署
└── README.md
```

---

## 数据与安全

- 所有任务便签数据保存在 `data/data.json`，配置（主题、位置、AI Key、服务器口令）保存在 `data/config.json`。
- `data/` 目录已被 `.gitignore` 排除：**不要**把含 API Key 或访问口令的配置文件提交到公开仓库。
- AI 功能使用 OpenAI 兼容接口（默认 DeepSeek），API Key 仅存本地，请自行在设置页填写。
- 网页后端为口令登录，公网部署请务必设置强口令，并建议前置 HTTPS 反向代理。

---

## 许可证

MIT License
