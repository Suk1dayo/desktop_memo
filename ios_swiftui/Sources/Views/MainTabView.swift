import SwiftUI

struct MainTabView: View {
    @EnvironmentObject private var store: AppStore
    @State private var selectedTab: Int = 0

    var body: some View {
        TabView(selection: $selectedTab) {
            TasksView()
                .tabItem {
                    Label("日程", systemImage: "checklist")
                }
                .tag(0)

            TimetableView()
                .tabItem {
                    Label("课表", systemImage: "calendar")
                }
                .tag(1)

            StickiesView()
                .tabItem {
                    Label("便签", systemImage: "note.text")
                }
                .tag(2)

            ProfileView()
                .tabItem {
                    Label("我的", systemImage: "person.crop.circle")
                }
                .tag(3)
        }
        .overlay(alignment: .bottom) {
            floatingOverlays
        }
        .alert(
            "登录状态提醒",
            isPresented: Binding(
                get: { store.sessionExpiredNotice != nil },
                set: { if !$0 { store.sessionExpiredNotice = nil } }
            )
        ) {
            Button("重新登录") {
                store.signOut()
            }
            Button("稍后再说", role: .cancel) {}
        } message: {
            Text(store.sessionExpiredNotice ?? "会话已过期")
        }
    }

    @ViewBuilder
    private var floatingOverlays: some View {
        VStack(spacing: 8) {
            if let undo = store.undoPayload {
                HStack(spacing: 12) {
                    Text(undo.label)
                        .font(.subheadline)
                        .foregroundStyle(Theme.ink)
                        .lineLimit(1)
                    Spacer(minLength: 0)
                    Button("撤销") {
                        _Concurrency.Task {
                            await store.performUndo()
                        }
                    }
                    .font(.subheadline.weight(.semibold))
                    .foregroundStyle(Theme.accent)
                }
                .padding(.horizontal, 14)
                .padding(.vertical, 10)
                .background(.ultraThinMaterial)
                .clipShape(RoundedRectangle(cornerRadius: 14, style: .continuous))
                .overlay(
                    RoundedRectangle(cornerRadius: 14, style: .continuous)
                        .stroke(Theme.border, lineWidth: 1)
                )
                .shadow(color: .black.opacity(0.08), radius: 8, y: 4)
            }

            if let toast = store.toastMessage {
                HStack(spacing: 8) {
                    Image(systemName: toast.isError ? "xmark.circle.fill" : "checkmark.circle.fill")
                        .foregroundStyle(toast.isError ? Theme.danger : Theme.success)
                    Text(toast.text)
                        .font(.footnote.weight(.medium))
                        .foregroundStyle(Theme.ink)
                        .lineLimit(2)
                }
                .padding(.horizontal, 14)
                .padding(.vertical, 9)
                .background(.ultraThinMaterial)
                .clipShape(Capsule())
                .overlay(
                    Capsule()
                        .stroke(Theme.border, lineWidth: 1)
                )
                .shadow(color: .black.opacity(0.08), radius: 8, y: 4)
            }
        }
        .padding(.horizontal, 16)
        .padding(.bottom, 64)
        .allowsHitTesting(store.undoPayload != nil)
    }
}
