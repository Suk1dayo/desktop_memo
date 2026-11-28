import SwiftUI

@main
struct DesktopMemoApp: App {
    @StateObject private var store = AppStore()
    @Environment(\.scenePhase) private var scenePhase

    var body: some Scene {
        WindowGroup {
            Group {
                if store.token != nil {
                    MainTabView()
                } else {
                    AuthView()
                }
            }
            .environmentObject(store)
            .tint(Theme.accent)
            .preferredColorScheme(store.themePreference.colorScheme)
            .onChange(of: scenePhase) { _, newPhase in
                if newPhase == .active, store.token != nil, !store.syncing {
                    _Concurrency.Task {
                        await store.pullRemote(silent: true)
                    }
                }
            }
        }
    }
}
