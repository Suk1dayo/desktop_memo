import Foundation

enum APIError: LocalizedError {
    case invalidServerUrl
    case httpError(status: Int, message: String)
    case decodingError(String)
    case networkError(String)

    var errorDescription: String? {
        switch self {
        case .invalidServerUrl:
            return "请先填写有效的服务器地址（以 http:// 或 https:// 开头）"
        case .httpError(_, let message):
            return message
        case .decodingError(let detail):
            return "数据解析失败：\(detail)"
        case .networkError(let detail):
            return "网络请求失败：\(detail)"
        }
    }

    var isUnauthorized: Bool {
        if case .httpError(let status, _) = self {
            return status == 401
        }
        return false
    }
}

struct LoginResponse: Codable {
    let token: String
    let user: AuthUser
}

struct MeResponse: Codable {
    let user: AuthUser
}

struct SyncStateResponse: Codable {
    let syncVersion: Int
    let updatedAtIso: String?
    let data: AppData
    let deletedIds: SyncTombstones?
}

struct CloudConfigResponse: Codable {
    let configVersion: Int
    let hasApiKey: Bool?
    let maskedApiKey: String?
    let config: CloudAiConfig
}

struct CloudConfigUpdateResponse: Codable {
    let configVersion: Int
    let hasApiKey: Bool?
    let maskedApiKey: String?
}

struct AttachmentUploadResponse: Codable {
    let fileName: String
}

struct AiTestResponse: Codable {
    let model: String
    let reply: String
    let latencyMs: Int
}

struct AiExtractResponse: Codable {
    let model: String?
    let latencyMs: Int?
    let reply: String
    let tasks: [ExtractedCandidate]
}

struct AiSortResponse: Codable {
    let assignments: [AiSortAssignment]
}

private struct ErrorEnvelope: Decodable {
    let error: String?
    let message: String?
}

private struct LoginRequestBody: Encodable {
    let email: String
    let password: String
}

private struct NicknameRequestBody: Encodable {
    let nickname: String
}

private struct DeleteAccountRequestBody: Encodable {
    let password: String
}

private struct SyncMergeRequestBody: Encodable {
    let baseSyncVersion: Int
    let data: AppData
    let deletedIds: SyncTombstones
}

private struct CloudConfigUpdateRequestBody: Encodable {
    let config: CloudAiConfig
}

private struct AttachmentUploadRequestBody: Encodable {
    let fileName: String
    let mimeType: String
    let dataBase64: String
}

private struct AiTestRequestBody: Encodable {
    let baseUrl: String?
    let model: String?
    let apiKey: String?
}

private struct AiExtractRequestBody: Encodable {
    let text: String
    let imageBase64: String?
    let mimeType: String?
    let nowIso: String
}

private struct AiSortRequestBody: Encodable {
    let tasks: [MemoTask]
    let nowIso: String
}

enum APIClient {
    private static let syncSession: URLSession = {
        let cfg = URLSessionConfiguration.default
        cfg.timeoutIntervalForRequest = 25
        cfg.timeoutIntervalForResource = 45
        return URLSession(configuration: cfg)
    }()

    private static let aiSession: URLSession = {
        let cfg = URLSessionConfiguration.default
        cfg.timeoutIntervalForRequest = 55
        cfg.timeoutIntervalForResource = 75
        return URLSession(configuration: cfg)
    }()

    static func cleanBaseUrl(_ raw: String) throws -> String {
        let trimmed = raw.trimmingCharacters(in: .whitespacesAndNewlines)
            .trimmingCharacters(in: CharacterSet(charactersIn: "/"))
        let lower = trimmed.lowercased()
        guard lower.hasPrefix("http://") || lower.hasPrefix("https://"), URL(string: trimmed) != nil else {
            throw APIError.invalidServerUrl
        }
        return trimmed
    }

    private static func parseErrorMessage(data: Data, status: Int) -> String {
        if let env = try? JSONDecoder().decode(ErrorEnvelope.self, from: data) {
            if let msg = env.message?.trimmingCharacters(in: .whitespacesAndNewlines), !msg.isEmpty {
                return msg
            }
            if let err = env.error?.trimmingCharacters(in: .whitespacesAndNewlines), !err.isEmpty {
                return err
            }
        }
        return "请求失败 (HTTP \(status))"
    }

    private static func requestJson<Res: Decodable>(
        baseUrl: String,
        path: String,
        method: String = "GET",
        token: String? = nil,
        useAiTimeout: Bool = false
    ) async throws -> Res {
        try await requestJsonWithBody(
            baseUrl: baseUrl,
            path: path,
            method: method,
            token: token,
            bodyData: nil,
            useAiTimeout: useAiTimeout
        )
    }

    private static func requestJson<Req: Encodable, Res: Decodable>(
        baseUrl: String,
        path: String,
        method: String = "POST",
        token: String? = nil,
        body: Req,
        useAiTimeout: Bool = false
    ) async throws -> Res {
        let encoded = try JSONEncoder().encode(body)
        return try await requestJsonWithBody(
            baseUrl: baseUrl,
            path: path,
            method: method,
            token: token,
            bodyData: encoded,
            useAiTimeout: useAiTimeout
        )
    }

    private static func requestJsonWithBody<Res: Decodable>(
        baseUrl: String,
        path: String,
        method: String,
        token: String?,
        bodyData: Data?,
        useAiTimeout: Bool
    ) async throws -> Res {
        let clean = try cleanBaseUrl(baseUrl)
        guard let url = URL(string: clean + path) else {
            throw APIError.invalidServerUrl
        }
        var req = URLRequest(url: url)
        req.httpMethod = method
        req.setValue("application/json", forHTTPHeaderField: "Content-Type")
        if let token, !token.isEmpty {
            req.setValue("Bearer \(token)", forHTTPHeaderField: "Authorization")
        }
        req.httpBody = bodyData

        let session = useAiTimeout ? aiSession : syncSession
        let data: Data
        let response: URLResponse
        do {
            (data, response) = try await session.data(for: req)
        } catch {
            throw APIError.networkError(error.localizedDescription)
        }

        guard let http = response as? HTTPURLResponse else {
            throw APIError.networkError("无效的 HTTP 响应")
        }

        if !(200...299).contains(http.statusCode) {
            throw APIError.httpError(status: http.statusCode, message: parseErrorMessage(data: data, status: http.statusCode))
        }

        do {
            return try JSONDecoder().decode(Res.self, from: data)
        } catch {
            throw APIError.decodingError(error.localizedDescription)
        }
    }

    private static func requestEmptyOk(
        baseUrl: String,
        path: String,
        method: String,
        token: String,
        bodyData: Data? = nil
    ) async throws {
        struct OkEnvelope: Decodable { let ok: Bool? }
        let _: OkEnvelope = try await requestJsonWithBody(
            baseUrl: baseUrl,
            path: path,
            method: method,
            token: token,
            bodyData: bodyData,
            useAiTimeout: false
        )
    }

    static func login(baseUrl: String, email: String, password: String) async throws -> LoginResponse {
        try await requestJson(
            baseUrl: baseUrl,
            path: "/api/auth/login",
            method: "POST",
            body: LoginRequestBody(email: email.trimmingCharacters(in: .whitespacesAndNewlines), password: password)
        )
    }

    static func fetchMe(baseUrl: String, token: String) async throws -> AuthUser {
        let res: MeResponse = try await requestJson(
            baseUrl: baseUrl,
            path: "/api/auth/me",
            method: "GET",
            token: token
        )
        return res.user
    }

    static func updateNickname(baseUrl: String, token: String, nickname: String) async throws -> AuthUser {
        let res: MeResponse = try await requestJson(
            baseUrl: baseUrl,
            path: "/api/auth/profile",
            method: "PATCH",
            token: token,
            body: NicknameRequestBody(nickname: nickname.trimmingCharacters(in: .whitespacesAndNewlines))
        )
        return res.user
    }

    static func deleteAccount(baseUrl: String, token: String, password: String) async throws {
        let encoded = try JSONEncoder().encode(DeleteAccountRequestBody(password: password))
        try await requestEmptyOk(
            baseUrl: baseUrl,
            path: "/api/auth/account",
            method: "DELETE",
            token: token,
            bodyData: encoded
        )
    }

    static func fetchSyncState(baseUrl: String, token: String) async throws -> SyncStateResponse {
        try await requestJson(
            baseUrl: baseUrl,
            path: "/api/sync/state",
            method: "GET",
            token: token
        )
    }

    static func pushSyncMerge(
        baseUrl: String,
        token: String,
        baseSyncVersion: Int,
        data: AppData,
        deletedIds: SyncTombstones
    ) async throws -> SyncStateResponse {
        try await requestJson(
            baseUrl: baseUrl,
            path: "/api/sync/merge",
            method: "POST",
            token: token,
            body: SyncMergeRequestBody(baseSyncVersion: baseSyncVersion, data: data, deletedIds: deletedIds)
        )
    }

    static func fetchCloudConfig(baseUrl: String, token: String) async throws -> CloudConfigResponse {
        try await requestJson(
            baseUrl: baseUrl,
            path: "/api/sync/config?masked=1",
            method: "GET",
            token: token
        )
    }

    static func updateCloudConfig(baseUrl: String, token: String, config: CloudAiConfig) async throws -> CloudConfigUpdateResponse {
        try await requestJson(
            baseUrl: baseUrl,
            path: "/api/sync/config",
            method: "PUT",
            token: token,
            body: CloudConfigUpdateRequestBody(config: config)
        )
    }

    static func uploadAttachment(
        baseUrl: String,
        token: String,
        fileName: String,
        mimeType: String,
        dataBase64: String
    ) async throws -> String {
        let res: AttachmentUploadResponse = try await requestJson(
            baseUrl: baseUrl,
            path: "/api/sync/attachments",
            method: "POST",
            token: token,
            body: AttachmentUploadRequestBody(fileName: fileName, mimeType: mimeType, dataBase64: dataBase64)
        )
        return res.fileName
    }

    static func downloadAttachmentData(baseUrl: String, token: String, fileName: String) async throws -> Data {
        let clean = try cleanBaseUrl(baseUrl)
        let encodedName = fileName.addingPercentEncoding(withAllowedCharacters: .urlPathAllowed) ?? fileName
        guard let url = URL(string: "\(clean)/api/sync/attachments/\(encodedName)") else {
            throw APIError.invalidServerUrl
        }
        var req = URLRequest(url: url)
        req.httpMethod = "GET"
        req.setValue("Bearer \(token)", forHTTPHeaderField: "Authorization")

        let data: Data
        let response: URLResponse
        do {
            (data, response) = try await syncSession.data(for: req)
        } catch {
            throw APIError.networkError(error.localizedDescription)
        }
        guard let http = response as? HTTPURLResponse else {
            throw APIError.networkError("无效的 HTTP 响应")
        }
        if !(200...299).contains(http.statusCode) {
            throw APIError.httpError(status: http.statusCode, message: parseErrorMessage(data: data, status: http.statusCode))
        }
        return data
    }

    static func deleteAttachment(baseUrl: String, token: String, fileName: String) async throws {
        let encodedName = fileName.addingPercentEncoding(withAllowedCharacters: .urlPathAllowed) ?? fileName
        try await requestEmptyOk(
            baseUrl: baseUrl,
            path: "/api/sync/attachments/\(encodedName)",
            method: "DELETE",
            token: token
        )
    }

    static func testCloudAi(
        baseUrl: String,
        token: String,
        overrideBaseUrl: String? = nil,
        overrideModel: String? = nil,
        overrideApiKey: String? = nil
    ) async throws -> AiTestResponse {
        try await requestJson(
            baseUrl: baseUrl,
            path: "/api/ai/test",
            method: "POST",
            token: token,
            body: AiTestRequestBody(baseUrl: overrideBaseUrl, model: overrideModel, apiKey: overrideApiKey),
            useAiTimeout: true
        )
    }

    static func extractTasksWithAi(
        baseUrl: String,
        token: String,
        text: String,
        imageBase64: String? = nil,
        mimeType: String? = nil
    ) async throws -> AiExtractResponse {
        let nowIso = ISO8601DateFormatter().string(from: Date())
        return try await requestJson(
            baseUrl: baseUrl,
            path: "/api/ai/extract",
            method: "POST",
            token: token,
            body: AiExtractRequestBody(text: text, imageBase64: imageBase64, mimeType: mimeType, nowIso: nowIso),
            useAiTimeout: true
        )
    }

    static func sortTasksWithAi(baseUrl: String, token: String, tasks: [MemoTask]) async throws -> [AiSortAssignment] {
        let nowIso = ISO8601DateFormatter().string(from: Date())
        let res: AiSortResponse = try await requestJson(
            baseUrl: baseUrl,
            path: "/api/ai/sort",
            method: "POST",
            token: token,
            body: AiSortRequestBody(tasks: tasks, nowIso: nowIso),
            useAiTimeout: true
        )
        return res.assignments
    }
}
