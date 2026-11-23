# DesktopMemo

轻量、原生、Claude / Notion / Linear 极简扁平风格的 **Windows 桌面备忘录**，附带独立网页后端（类 Claude 极简界面）。

纯 C# / WPF + Win32 原生实现，无第三方运行时依赖，单文件 `.exe` 开箱即用。

![status](https://img.shields.io/badge/build-csc.exe%20%C2%B7%20no%20SDK-blue)
![platform](https://img.shields.io/badge/platform-Windows%2010%2F11-lightgrey)
![lang](https://img.shields.io/badge/C%23-.NET%20Framework%204.x-239120)

---

## 下载

不想编译？直接取预构建版本：见本仓库右侧 **Releases** 页面的 `v2.02`。

| 文件 | 说明 |
| --- | --- |
| `DesktopMemo.exe` | 桌面端，双击即用 |
| `DesktopMemoServer.exe` | 网页后端，需与 `web/`、`data/` 同目录运行 |

---

## 功能亮点 (v2.02)

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
├── tools/
│   └── DataVault.ps1           # data/ 加密保险箱（AES-256 + HMAC）
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

## 数据加密（DataVault）

`data/` 里的 `config.json` 含 AI Key 与服务器口令，属于明文。需要备份、上传网盘或同步到 GitHub 时，用 `tools/DataVault.ps1` 先加密：

```powershell
# 口令模式：AES-256-CBC + PBKDF2-SHA256(200k) + HMAC-SHA256，跨设备可还原
.\tools\DataVault.ps1 lock -Passphrase "你的长口令"

# 本机模式：DPAPI 绑定当前 Windows 用户，不用记口令，但换机/换用户无法解密
.\tools\DataVault.ps1 lock -Dpapi

# 查看已有保险箱
.\tools\DataVault.ps1 list

# 还原
.\tools\DataVault.ps1 unlock -VaultFile .\vault\xxx.vault -Passphrase "你的长口令" -OutDir .\restore
```

- 产物是单个 `vault\desktop_memo_data_<时间戳>.vault`；加密后会立即解密回读并校验 SHA-256，`Verify: OK` 才算成功。
- 采用 **Encrypt-then-MAC**：口令错误或文件被改动都会直接报 MAC 校验失败，不会解出半截脏数据。
- `vault/` 默认被 `.gitignore` 忽略。确实要把加密备份传到 GitHub 时，用 `git add -f vault/xxx.vault` 显式添加；公开仓库里放密文可以，但请确保口令足够强（≥16 位随机）。
- 加密只保护"静止数据"（备份文件）。程序运行时仍需读取 `data/` 明文，所以本机安全要靠 Windows 账户密码与磁盘加密（BitLocker）。

### 关于本仓库的同步方式

**本项目不是实时同步。** `git` 只在执行 `git commit` + `git push` 时才把内容推到 GitHub；在文件夹里编辑文件、新增文件都只留在本地，`git status` 能看到 `modified` / `untracked`，但远程仓库毫无变化。`.gitignore` 中的 `data/`、`vault/`、`restore/`、`*.exe` 即使被改动也永远不会被提交。

---

## 公网访问：域名 + 自动 HTTPS

### 前置条件

1. **域名已完成 ICP 备案**（国内服务器强制）。后缀必须在工信部白名单内：`.com` `.net` `.cn` `.top` `.xyz` `.club` `.site` 等可以，`.win` `.dev` 之类不行。
2. 域名 A 记录指向服务器公网 IP，80 与 443 能从公网访问。

### 一键配置

以**管理员**身份运行：

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\Install-Caddy.ps1 -Domain memo.example.com -Email you@example.com
```

脚本会：

| 步骤 | 动作 |
| --- | --- |
| 1 | 准备 `caddy.exe`（缺失时自动下载） |
| 2 | 生成 `Caddyfile`，`caddy fmt` 规范化 + `validate` 校验 |
| 3 | 防火墙放行 80/443，**封禁 8765 公网入站**（Caddy 走本机回环不受影响） |
| 4 | 注册 `DesktopMemoCaddy` 开机自启任务（SYSTEM），启动并验证进程 |

最终效果：

```
https://memo.example.com  ──443(TLS)──>  Caddy  ──127.0.0.1:8765──>  DesktopMemoServer
```

- 证书由 Caddy 自动申请并续期（Let's Encrypt），无需手动操作
- HTTP 自动 301 跳 HTTPS，附带 HSTS
- 8765 不再对公网暴露，外部只能访问 443

### 备案要点（个人）

- 需要包年包月服务器（剩余 ≥3 个月）才能申请备案授权码
- 材料：身份证正反面、本人手机号、邮箱、人脸核验（腾讯云 APP）
- 网站名称要体现个人性质（如「XX的备忘录」），不要含「平台/系统/官网」等经营性或机构性词汇
- 备案期间域名**不要解析到服务器**，否则会被判定「未备案先上线」而驳回
- 周期：接入商初审 1–2 工作日 + 管局审核 5–20 工作日，之后完成工信部短信核验
- 备案通过后 30 日内，部分省份还需做**公安联网备案**

---

## 许可证

MIT License
