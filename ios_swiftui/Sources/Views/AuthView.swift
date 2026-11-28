import SwiftUI

struct AuthView: View {
    @EnvironmentObject private var store: AppStore
    @Environment(\.openURL) private var openURL

    @State private var serverUrlInput: String = ""
    @State private var email: String = ""
    @State private var password: String = ""
    @State private var isSubmitting: Bool = false
    @State private var errorMessage: String?

    var body: some View {
        NavigationStack {
            ScrollView {
                VStack(spacing: 22) {
                    headerSection

                    VStack(spacing: 14) {
                        fieldBlock(title: "云端服务器地址") {
                            TextField("例如 https://memo.yourdomain.com", text: $serverUrlInput)
                                .keyboardType(.URL)
                                .textInputAutocapitalization(.never)
                                .autocorrectionDisabled()
                        }

                        Text("支持带 HTTPS 域名或自建局域网 http://IP:端口")
                            .font(.caption)
                            .foregroundStyle(Theme.secondaryInk)
                            .frame(maxWidth: .infinity, alignment: .leading)

                        fieldBlock(title: "邮箱账号") {
                            TextField("name@example.com", text: $email)
                                .keyboardType(.emailAddress)
                                .textContentType(.username)
                                .textInputAutocapitalization(.never)
                                .autocorrectionDisabled()
                        }

                        fieldBlock(title: "登录密码") {
                            SecureField("请输入密码", text: $password)
                                .textContentType(.password)
                        }
                    }
                    .warmCard(padding: 18, cornerRadius: 18)

                    if let errorMessage {
                        HStack(spacing: 8) {
                            Image(systemName: "exclamationmark.triangle.fill")
                                .foregroundStyle(Theme.danger)
                            Text(errorMessage)
                                .font(.footnote)
                                .foregroundStyle(Theme.danger)
                            Spacer(minLength: 0)
                        }
                        .padding(12)
                        .background(Theme.danger.opacity(0.1))
                        .clipShape(RoundedRectangle(cornerRadius: 12, style: .continuous))
                    }

                    Button {
                        submitLogin()
                    } label: {
                        HStack(spacing: 8) {
                            if isSubmitting {
                                ProgressView()
                                    .tint(.white)
                            }
                            Text(isSubmitting ? "正在登录..." : "登录并同步云端数据")
                                .font(.headline)
                        }
                        .frame(maxWidth: .infinity)
                        .padding(.vertical, 14)
                        .background(Theme.accent)
                        .foregroundStyle(.white)
                        .clipShape(RoundedRectangle(cornerRadius: 14, style: .continuous))
                    }
                    .disabled(isSubmitting || email.trimmingCharacters(in: .whitespaces).isEmpty || password.isEmpty)

                    webPortalFooter
                }
                .padding(.horizontal, 20)
                .padding(.vertical, 28)
                .frame(maxWidth: 480)
                .frame(maxWidth: .infinity)
            }
            .background(Theme.canvas.ignoresSafeArea())
            .navigationBarHidden(true)
            .onAppear {
                if serverUrlInput.isEmpty {
                    serverUrlInput = store.serverUrl
                }
            }
        }
    }

    private var headerSection: some View {
        VStack(spacing: 10) {
            ZStack {
                RoundedRectangle(cornerRadius: 20, style: .continuous)
                    .fill(Theme.accent)
                    .frame(width: 68, height: 68)
                Image(systemName: "checkmark.rectangle.stack.fill")
                    .font(.system(size: 30, weight: .semibold))
                    .foregroundStyle(.white)
            }
            Text("DesktopMemo")
                .font(.system(size: 28, weight: .bold, design: .serif))
                .foregroundStyle(Theme.ink)
            Text("云端四象限日程 · 课表 · AI 识图助手")
                .font(.subheadline)
                .foregroundStyle(Theme.secondaryInk)
        }
        .padding(.top, 12)
    }

    private func fieldBlock<Content: View>(title: String, @ViewBuilder content: () -> Content) -> some View {
        VStack(alignment: .leading, spacing: 6) {
            Text(title)
                .font(.caption.weight(.semibold))
                .foregroundStyle(Theme.secondaryInk)
            content()
                .padding(.horizontal, 12)
                .padding(.vertical, 10)
                .background(Theme.subtle)
                .clipShape(RoundedRectangle(cornerRadius: 10, style: .continuous))
        }
    }

    @ViewBuilder
    private var webPortalFooter: some View {
        let trimmed = serverUrlInput.trimmingCharacters(in: .whitespacesAndNewlines).trimmingCharacters(in: CharacterSet(charactersIn: "/"))
        if let url = URL(string: trimmed), (trimmed.lowercased().hasPrefix("http://") || trimmed.lowercased().hasPrefix("https://")) {
            Button {
                openURL(url)
            } label: {
                Text("还没有账号或忘记密码？前往网页端注册 / 重置")
                    .font(.footnote)
                    .foregroundStyle(Theme.accent)
            }
            .padding(.top, 4)
        } else {
            Text("首次注册账号请填写服务器地址后点击或前往网页端完成邮箱验证")
                .font(.caption)
                .foregroundStyle(Theme.secondaryInk)
                .multilineTextAlignment(.center)
        }
    }

    private func submitLogin() {
        errorMessage = nil
        isSubmitting = true
        _Concurrency.Task {
            defer { isSubmitting = false }
            do {
                try await store.signIn(serverUrl: serverUrlInput, email: email, password: password)
            } catch {
                errorMessage = error.localizedDescription
            }
        }
    }
}
