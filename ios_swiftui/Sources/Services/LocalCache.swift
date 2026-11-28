import Foundation

struct CachedSnapshot: Codable {
    var user: AuthUser?
    var syncVersion: Int
    var data: AppData
    var deletedIds: SyncTombstones
}

enum LocalCache {
    private static let serverUrlKey = "dm_server_url_v3"
    static let defaultServerPlaceholder = "https://your-server.example.com"

    private static var cacheFileUrl: URL? {
        guard let dir = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first else {
            return nil
        }
        return dir
            .appendingPathComponent("DesktopMemo", isDirectory: true)
            .appendingPathComponent("cache_v3.json", isDirectory: false)
    }

    static func loadServerUrl() -> String {
        let saved = UserDefaults.standard.string(forKey: serverUrlKey)?.trimmingCharacters(in: .whitespacesAndNewlines) ?? ""
        return saved.trimmingCharacters(in: CharacterSet(charactersIn: "/"))
    }

    static func saveServerUrl(_ url: String) {
        let clean = url.trimmingCharacters(in: .whitespacesAndNewlines)
            .trimmingCharacters(in: CharacterSet(charactersIn: "/"))
        UserDefaults.standard.set(clean, forKey: serverUrlKey)
    }

    static func loadSnapshot() -> CachedSnapshot? {
        guard let url = cacheFileUrl, FileManager.default.fileExists(atPath: url.path) else {
            return nil
        }
        do {
            let raw = try Data(contentsOf: url)
            return try JSONDecoder().decode(CachedSnapshot.self, from: raw)
        } catch {
            return nil
        }
    }

    static func saveSnapshot(_ snapshot: CachedSnapshot) {
        guard let url = cacheFileUrl else { return }
        do {
            let folder = url.deletingLastPathComponent()
            try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
            let encoded = try JSONEncoder().encode(snapshot)
            try encoded.write(to: url, options: .atomic)
        } catch {
            // Ignore cache write errors silently
        }
    }

    static func clearSessionAndCache() {
        KeychainHelper.deleteToken()
        if let url = cacheFileUrl {
            try? FileManager.default.removeItem(at: url)
        }
    }
}
