import Foundation
import Security

enum KeychainHelper {
    private static let account = "dm_auth_token_v3"
    private static let fallbackDefaultsKey = "dm_auth_token_fallback_v3"

    private static var service: String {
        Bundle.main.bundleIdentifier ?? "com.gyer.desktopmemo"
    }

    static func saveToken(_ token: String?) {
        guard let token, !token.isEmpty else {
            deleteToken()
            return
        }
        guard let data = token.data(using: .utf8) else { return }

        let query: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecAttrAccount as String: account
        ]
        SecItemDelete(query as CFDictionary)

        let attributes: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecAttrAccount as String: account,
            kSecAttrAccessible as String: kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly,
            kSecValueData as String: data
        ]

        let status = SecItemAdd(attributes as CFDictionary, nil)
        if status == errSecMissingEntitlement {
            UserDefaults.standard.set(token, forKey: fallbackDefaultsKey)
        } else if status == errSecSuccess {
            UserDefaults.standard.removeObject(forKey: fallbackDefaultsKey)
        }
    }

    static func loadToken() -> String? {
        let query: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecAttrAccount as String: account,
            kSecReturnData as String: true,
            kSecMatchLimit as String: kSecMatchLimitOne
        ]

        var item: CFTypeRef?
        let status = SecItemCopyMatching(query as CFDictionary, &item)
        if status == errSecSuccess, let data = item as? Data, let token = String(data: data, encoding: .utf8) {
            return token
        }
        if status == errSecMissingEntitlement {
            return UserDefaults.standard.string(forKey: fallbackDefaultsKey)
        }
        return nil
    }

    static func deleteToken() {
        let query: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecAttrAccount as String: account
        ]
        SecItemDelete(query as CFDictionary)
        UserDefaults.standard.removeObject(forKey: fallbackDefaultsKey)
    }
}
