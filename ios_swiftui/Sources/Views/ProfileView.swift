import SwiftUI

private struct AiPresetOption {
    let id: String
    let label: String
    let baseUrl: String
    let model: String
}

private let aiPresets: [AiPresetOption] = [
    AiPresetOption(id: "deepseek", label: "DeepSeek Chat", baseUrl: "https://api.deepseek.com", model: "deepseek-chat"),
    AiPresetOption(id: "deepseek_reasoner", label: "DeepSeek R1", baseUrl: "https://api.deepseek.com", model: "deepseek-reasoner"),
    AiPresetOption(id: "openai", label: "OpenAI", baseUrl: "https://api.openai.com/v1", model: "gpt-4o-mini"),
    AiPresetOption(id: "siliconflow", label: "硅基流动 SiliconFlow", baseUrl: "https://api.siliconflow.cn/v1", model: "Qwen/Qwen2.5-72B-Instruct"),
    AiPresetOption(id: "qwen", label: "通义千问 DashScope", baseUrl: "https://dashscope.aliyuncs.com/compatible-mode/v1", model: "qwen-plus"),
    AiPresetOption(id: "custom", label: "自定义 OpenAI 兼容端点", baseUrl: "", model: "")
]

struct ProfileView: View {
    @EnvironmentObject private var store: AppStore
    @Environment(\.openURL) private var openURL

    @State private var serverUrlDraft: String = ""
    @State private var aiPreset: String = "deepseek"
    @State private var aiBaseUrl: String = ""
    @State private var aiModel: String = ""
    @State private var aiApiKeyInput: String = ""
    @State private var aiTestStatus: String = ""
    @State private var isTestingAi: Bool = false
    @State private var isSavingAi: Bool = false

    @State private var showingNicknameAlert: Bool = false
    @State private var nicknameDraft: String = ""
    @State private var showingLogoutConfirm: Bool = false
    @State private var showingDeleteSheet: Bool = false

    var body: some View {
        NavigationStack {
            Form {
                accountSection
                themeSection
                serverSection
                aiSection
                securitySection
            }
            .scrollContentBackground(.hidden)
            .background(Theme.canvas)
            .navigationTitle("我的")
            .onAppear {
                syncDraftsFromStore()
            }
            .onChange(of: store.aiConfig) { _, _ in
                syncDraftsFromStore()
            }
            .alert("修改昵称", isPresented: $showingNicknameAlert) {
                TextField("新昵称", text: $nicknameDraft)
                Button("取消", role: .cancel) {}
                Button("保存") {
                    let clean = nicknameDraft.trimmingCharacters(in: .whitespacesAndNewlines)
                    guard !clean.isEmpty else { return }
                    _Concurrency.Task {
                        do {
                            try await store.updateNickname(clean)
                        } catch {
                            store.showToast(error.localizedDescription, isError: true)
                        }
                    }
                }
            }
            .confirmationDialog("确定要退出登录吗？", isPresented: $showingLogoutConfirm, titleVisibility: .visible) {
                Button("退出登录", role: .destructive) {
                    store.signOut()
                }
                Button("取消", role: .cancel) {}
            } message: {
                Text("本地缓存也会一并清理，云端数据保留在服务器中。")
            }
            .sheet(isPresented: $showingDeleteSheet) {
                DeleteAccountSheet()
            }
        }
    }

    private var accountSection: some View {
        Section("云端账号") {
            HStack(spacing: 14) {
                ZStack {
                    Circle()
                        .fill(Theme.accent.opacity(0.16))
                        .frame(width: 48, height: 48)
                    Text(String((store.user?.nickname ?? "U").prefix(1)).uppercased())
                        .font(.title3.weight(.bold))
                        .foregroundStyle(Theme.accent)
                }

                VStack(alignment: .leading, spacing: 3) {
                    HStack(spacing: 6) {
                        Text(store.user?.nickname ?? "已登录用户")
                            .font(.headline)
                            .foregroundStyle(Theme.ink)
                        Button {
                            nicknameDraft = store.user?.nickname ?? ""
                            showingNicknameAlert = true
                        } label: {
                            Image(systemName: "pencil")
                                .font(.caption)
                                .foregroundStyle(Theme.secondaryInk)
                        }
                        .buttonStyle(.plain)
                    }
                    Text(store.user?.email ?? "")
                        .font(.caption)
                        .foregroundStyle(Theme.secondaryInk)
                    Text(store.lastSyncText)
                        .font(.caption2)
                        .foregroundStyle(Theme.secondaryInk)
                }
            }
            .padding(.vertical, 4)

            Button {
                _Concurrency.Task { await store.pullRemote() }
            } label: {
                HStack {
                    Label("立即与云端同步", systemImage: "arrow.triangle.2.circlepath")
                    Spacer()
                    if store.syncing {
                        ProgressView().controlSize(.small)
                    }
                }
            }
            .disabled(store.syncing)
        }
    }

    private var themeSection: some View {
        Section("外观主题") {
            Picker("配色模式", selection: Binding(
                get: { store.themePreference },
                set: { store.themePreference = $0 }
            )) {
                ForEach(ThemePreference.allCases) { mode in
                    Text(mode.label).tag(mode)
                }
            }
            .pickerStyle(.segmented)
        }
    }

    private var serverSection: some View {
        Section("服务器连接") {
            TextField("服务器地址", text: $serverUrlDraft)
                .keyboardType(.URL)
                .textInputAutocapitalization(.never)
                .autocorrectionDisabled()

            Button("保存服务器地址并同步") {
                _Concurrency.Task {
                    do {
                        try await store.saveServerUrlAndSync(serverUrlDraft)
                    } catch {
                        store.showToast(error.localizedDescription, isError: true)
                    }
                }
            }

            if let workspaceUrl = URL(string: store.serverUrl + "/app"),
               store.serverUrl.lowercased().hasPrefix("http") {
                Button {
                    openURL(workspaceUrl)
                } label: {
                    Label("在浏览器中打开网页工作台", systemImage: "safari")
                }
            }
        }
    }

    private var aiSection: some View {
        Section {
            Picker("模型预设", selection: $aiPreset) {
                ForEach(aiPresets, id: \.id) { preset in
                    Text(preset.label).tag(preset.id)
                }
            }
            .onChange(of: aiPreset) { _, newId in
                if let found = aiPresets.first(where: { $0.id == newId }), newId != "custom" {
                    aiBaseUrl = found.baseUrl
                    aiModel = found.model
                }
            }

            TextField("API Base URL", text: $aiBaseUrl)
                .keyboardType(.URL)
                .textInputAutocapitalization(.never)
                .autocorrectionDisabled()

            TextField("模型名称 (Model)", text: $aiModel)
                .textInputAutocapitalization(.never)
                .autocorrectionDisabled()

            SecureField(
                store.hasApiKey
                    ? "已保存 \(store.maskedApiKey)（留空保持不变）"
                    : "输入云端 AI API Key",
                text: $aiApiKeyInput
            )

            Button {
                saveAi()
            } label: {
                HStack {
                    Label("加密保存云端 AI 配置", systemImage: "lock.shield")
                    Spacer()
                    if isSavingAi {
                        ProgressView().controlSize(.small)
                    }
                }
            }
            .disabled(isSavingAi)

            Button {
                testAi()
            } label: {
                HStack {
                    Label("测试云端 AI 连通性", systemImage: "bolt.horizontal")
                    Spacer()
                    if isTestingAi {
                        ProgressView().controlSize(.small)
                    }
                }
            }
            .disabled(isTestingAi)

            if !aiTestStatus.isEmpty {
                Text(aiTestStatus)
                    .font(.caption)
                    .foregroundStyle(aiTestStatus.hasPrefix("✓") ? Theme.success : Theme.danger)
            }
        } header: {
            Text("云端 AI 助手配置")
        } footer: {
            Text("API Key 使用 AES-256-GCM 加密保存在您的云端个人空间，AI 请求由您的服务端代为转发。")
        }
    }

    private var securitySection: some View {
        Section {
            Button("退出登录", role: .destructive) {
                showingLogoutConfirm = true
            }
            Button("永久注销账号", role: .destructive) {
                showingDeleteSheet = true
            }
        } header: {
            Text("安全与账号")
        } footer: {
            Text("DesktopMemo v3.0 · Native SwiftUI for iPhone & iPad")
        }
    }

    private func syncDraftsFromStore() {
        serverUrlDraft = store.serverUrl
        let rawPreset = store.aiConfig.AiProviderPreset
        if aiPresets.contains(where: { $0.id == rawPreset }) {
            aiPreset = rawPreset
        } else if rawPreset.contains("deepseek") {
            aiPreset = "deepseek"
        } else {
            aiPreset = "custom"
        }
        aiBaseUrl = store.aiConfig.AiBaseUrl
        aiModel = store.aiConfig.AiModel
    }

    private func saveAi() {
        isSavingAi = true
        _Concurrency.Task {
            defer { isSavingAi = false }
            var next = store.aiConfig
            next.AiProviderPreset = aiPreset
            next.AiBaseUrl = aiBaseUrl.trimmingCharacters(in: .whitespacesAndNewlines)
            next.AiModel = aiModel.trimmingCharacters(in: .whitespacesAndNewlines)
            do {
                try await store.saveCloudAiConfig(next, newApiKey: aiApiKeyInput)
                aiApiKeyInput = ""
            } catch {
                store.showToast(error.localizedDescription, isError: true)
            }
        }
    }

    private func testAi() {
        isTestingAi = true
        aiTestStatus = ""
        _Concurrency.Task {
            defer { isTestingAi = false }
            do {
                let res = try await store.testCloudAi(
                    baseUrl: aiBaseUrl,
                    model: aiModel,
                    apiKey: aiApiKeyInput
                )
                aiTestStatus = "✓ \(res.model) 响应：\(res.reply) (\(res.latencyMs)ms)"
            } catch {
                aiTestStatus = "✕ \(error.localizedDescription)"
            }
        }
    }
}

private struct DeleteAccountSheet: View {
    @EnvironmentObject private var store: AppStore
    @Environment(\.dismiss) private var dismiss

    @State private var password: String = ""
    @State private var isDeleting: Bool = false
    @State private var errorText: String?

    var body: some View {
        NavigationStack {
            Form {
                Section {
                    Text("注销账号会从服务器永久删除您的所有待办日程、课程表、便签、图片附件和云端 AI 配置，且不可恢复。")
                        .font(.subheadline)
                        .foregroundStyle(Theme.danger)
                }

                Section("确认当前密码") {
                    SecureField("请输入登录密码以确认", text: $password)
                }

                if let errorText {
                    Section {
                        Text(errorText)
                            .font(.footnote)
                            .foregroundStyle(Theme.danger)
                    }
                }

                Section {
                    Button(isDeleting ? "正在永久删除..." : "确认永久注销账号", role: .destructive) {
                        confirmDelete()
                    }
                    .disabled(isDeleting || password.isEmpty)
                }
            }
            .scrollContentBackground(.hidden)
            .background(Theme.canvas)
            .navigationTitle("永久注销账号")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("取消") { dismiss() }
                }
            }
        }
    }

    private func confirmDelete() {
        errorText = nil
        isDeleting = true
        _Concurrency.Task {
            defer { isDeleting = false }
            do {
                try await store.deleteAccount(password: password)
                dismiss()
            } catch {
                errorText = error.localizedDescription
            }
        }
    }
}
