# DesktopMemo iOS 原生版（SwiftUI）免费编译与自签安装指南

本目录是 **100% 原生 Swift / SwiftUI（iOS 17+，适配 iPhone 与 iPad）** 客户端源码。配合仓库根目录下的 GitHub Actions 工作流，可在 **没有 Mac、没有 \$99 付费苹果开发者账号** 的情况下，免费由云端 macOS 构建机编译出未签名 `.ipa`，再用 Windows 自签工具安装到你的 iPhone / iPad。

---

## 第一步：通过 GitHub Actions 免费生成 `.ipa`

1. 将代码推送（push）到你的 GitHub 仓库（`.github/workflows/build-ios-ipa.yml` 已配置好触发规则，仓库根目录需包含 `.github/workflows/build-ios-ipa.yml` 与 `ios_swiftui/`）。
2. 打开 GitHub 仓库网页 → 顶部 **Actions** → 左侧选择 **Build iOS Unsigned IPA (SwiftUI)**。
   - 若刚推送完且改动在 `ios_swiftui/**` 下，工作流会自动开始运行；
   - 也可以随时点右侧 **Run workflow** 手动触发。
3. 等待约 2～4 分钟构建完成后，点击进入该次运行记录，在底部 **Artifacts** 区域下载 **`DesktopMemo-iOS-unsigned-ipa`**，解压后即可得到 `DesktopMemo-iOS-unsigned.ipa`。

> 额度说明：GitHub **公开仓库（Public）** 运行 `macos-latest` Actions **完全免费、不计分钟数**；若是 **私有仓库（Private）**，GitHub 免费账号每月有 2000 分钟标准额度，macOS 构建机按 10 倍分钟数折算（单次约消耗 20～30 分钟额度，每月约可免费构建 60+ 次）。

---

## 第二步：在 Windows 上用免费 Apple ID 签名并安装到手机

### 方式 A：Sideloadly（最简单，3 分钟装好）

1. 在 Windows 上安装 **iTunes（Apple 官网普通版，非微软商店版）** 和 **Sideloadly**（官网 `sideloadly.io` 免费下载）。
2. 用数据线将 iPhone / iPad 连接到电脑，在手机上点「信任此电脑」。
3. 打开 Sideloadly：
   - 把下载好的 `DesktopMemo-iOS-unsigned.ipa` 拖进软件左侧 IPA 图标框；
   - **Apple ID** 填你的普通免费 Apple ID 邮箱（首次会要求输入密码及手机端两步验证码）；
   - 直接点击 **Start**，等待安装完成，桌面就会出现 **DesktopMemo** 图标。

### 方式 B：SideStore（支持在手机上免电脑自动 7 天续签，适合长期使用）

- 初次按 SideStore 官网教程用 Windows 的 AltServer/SideStore 装好引导后，之后每次快到 7 天时，手机连着 Wi-Fi 打开 SideStore 即可直接在手机端一键续签（或配合 iOS 快捷指令每周自动续签），不再需要每次插电脑。

---

## 第三步：iPhone / iPad 首次打开授权

安装完成后第一次点击图标，系统会提示「未受信任的开发者」或需要开发者模式：

1. **信任签名证书**：打开系统「**设置 → 通用 → VPN 与设备管理**」→ 找到你的 Apple ID → 点击 **信任**。
2. **开启开发者模式（iOS 16+ 要求）**：打开「**设置 → 隐私与安全性 → 开发者模式**」打开并按提示重启一次设备即可正常运行。

---

## 免费账号限制须知

- 苹果官方规定，**普通免费 Apple ID** 自签安装的应用证书有效期为 **7 天**，到期前用 Sideloadly 再点一次 Start（覆盖安装，**不会丢失登录状态与本地缓存**）或用 SideStore 刷新即可继续使用。
- 单个免费 Apple ID 同时在设备上的有效自签 App 数量上限为 **3 个**。
- 本客户端已开启 `NSAllowsArbitraryLoads`，同时支持 `https://` 域名与局域网/公网 `http://IP:端口` 直连你的 DesktopMemo v3 服务端。
