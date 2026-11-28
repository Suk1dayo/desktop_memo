import Foundation
import SwiftUI
import UIKit

struct ToastMessage: Identifiable, Equatable {
    let id = UUID()
    let text: String
    let isError: Bool
}

struct UndoPayload: Identifiable, Equatable {
    let id = UUID()
    let label: String
    let snapshotData: AppData
    let snapshotDeleted: SyncTombstones
}

@MainActor
final class AppStore: ObservableObject {
    @AppStorage("dm_theme_preference_v3") var themePreferenceRaw: String = ThemePreference.system.rawValue

    var themePreference: ThemePreference {
        get { ThemePreference(rawValue: themePreferenceRaw) ?? .system }
        set { themePreferenceRaw = newValue.rawValue }
    }

    @Published var syncing: Bool = false
    @Published var serverUrl: String
    @Published var token: String?
    @Published var user: AuthUser?
    @Published var syncVersion: Int = 0
    @Published var data: AppData = AppData()
    @Published var deletedIds: SyncTombstones = SyncTombstones()
    @Published var aiConfig: CloudAiConfig = CloudAiConfig()
    @Published var hasApiKey: Bool = false
    @Published var maskedApiKey: String = ""
    @Published var lastSyncText: String = "尚未同步"
    @Published var sessionExpiredNotice: String?
    @Published var toastMessage: ToastMessage?
    @Published var undoPayload: UndoPayload?

    private var attachmentCache: [String: UIImage] = [:]
    private var toastDismissTask: _Concurrency.Task<Void, Never>?
    private var undoDismissTask: _Concurrency.Task<Void, Never>?

    init() {
        self.serverUrl = LocalCache.loadServerUrl()
        self.token = KeychainHelper.loadToken()
        if let snap = LocalCache.loadSnapshot() {
            self.user = snap.user
            self.syncVersion = snap.syncVersion
            self.data = snap.data
            self.deletedIds = snap.deletedIds
            if snap.syncVersion > 0 {
                self.lastSyncText = "本地缓存 · v\(snap.syncVersion)"
            }
        }
        if self.token != nil && !self.serverUrl.isEmpty {
            _Concurrency.Task {
                await self.pullRemote(silent: true)
            }
        }
    }

    func showToast(_ text: String, isError: Bool = false) {
        toastDismissTask?.cancel()
        withAnimation(.easeInOut(duration: 0.2)) {
            toastMessage = ToastMessage(text: text, isError: isError)
        }
        toastDismissTask = _Concurrency.Task {
            try? await _Concurrency.Task.sleep(for: .seconds(3.2))
            guard !_Concurrency.Task.isCancelled else { return }
            withAnimation(.easeInOut(duration: 0.2)) {
                self.toastMessage = nil
            }
        }
    }

    private func scheduleUndo(label: String, snapshotData: AppData, snapshotDeleted: SyncTombstones) {
        undoDismissTask?.cancel()
        withAnimation(.spring(response: 0.3, dampingFraction: 0.85)) {
            undoPayload = UndoPayload(
                label: label,
                snapshotData: snapshotData,
                snapshotDeleted: snapshotDeleted
            )
        }
        undoDismissTask = _Concurrency.Task {
            try? await _Concurrency.Task.sleep(for: .seconds(7.5))
            guard !_Concurrency.Task.isCancelled else { return }
            withAnimation(.easeInOut(duration: 0.2)) {
                self.undoPayload = nil
            }
        }
    }

    func dismissUndo() {
        undoDismissTask?.cancel()
        withAnimation(.easeInOut(duration: 0.2)) {
            undoPayload = nil
        }
    }

    func performUndo() async {
        guard let payload = undoPayload else { return }
        dismissUndo()
        await pushMerge(
            nextData: payload.snapshotData,
            nextDeleted: payload.snapshotDeleted,
            toastText: "已撤销删除"
        )
    }

    func signIn(serverUrl: String, email: String, password: String) async throws {
        let cleanUrl = try APIClient.cleanBaseUrl(serverUrl)
        let res = try await APIClient.login(baseUrl: cleanUrl, email: email, password: password)
        LocalCache.saveServerUrl(cleanUrl)
        KeychainHelper.saveToken(res.token)
        self.serverUrl = cleanUrl
        self.token = res.token
        self.user = res.user
        self.sessionExpiredNotice = nil
        await pullRemote(silent: false)
    }

    func saveServerUrlAndSync(_ newUrl: String) async throws {
        let cleanUrl = try APIClient.cleanBaseUrl(newUrl)
        LocalCache.saveServerUrl(cleanUrl)
        self.serverUrl = cleanUrl
        await pullRemote(silent: false)
    }

    func pullRemote(silent: Bool = false) async {
        guard let token, !token.isEmpty, !serverUrl.isEmpty else { return }
        if syncing { return }
        syncing = true
        defer { syncing = false }

        do {
            async let meReq = APIClient.fetchMe(baseUrl: serverUrl, token: token)
            async let syncReq = APIClient.fetchSyncState(baseUrl: serverUrl, token: token)
            async let cfgReq = APIClient.fetchCloudConfig(baseUrl: serverUrl, token: token)

            let (me, syncRes, cfgRes) = try await (meReq, syncReq, cfgReq)
            let tombstones = syncRes.deletedIds ?? SyncTombstones()

            self.user = me
            self.syncVersion = syncRes.syncVersion
            self.data = syncRes.data
            self.deletedIds = tombstones
            self.aiConfig = cfgRes.config
            self.hasApiKey = cfgRes.hasApiKey ?? false
            self.maskedApiKey = cfgRes.maskedApiKey ?? ""
            self.sessionExpiredNotice = nil
            self.lastSyncText = "已同步 · v\(syncRes.syncVersion)"

            LocalCache.saveSnapshot(
                CachedSnapshot(user: me, syncVersion: syncRes.syncVersion, data: syncRes.data, deletedIds: tombstones)
            )
            if !silent {
                showToast("云端数据已同步 (v\(syncRes.syncVersion))")
            }
        } catch let apiErr as APIError {
            if apiErr.isUnauthorized {
                self.sessionExpiredNotice = "登录会话已失效，请重新登录"
            }
            if !silent {
                showToast(apiErr.localizedDescription, isError: true)
            }
        } catch {
            if !silent {
                showToast(error.localizedDescription, isError: true)
            }
        }
    }

    private func pushMerge(nextData: AppData, nextDeleted: SyncTombstones, toastText: String? = nil) async {
        var stamped = nextData
        stamped.LastModifiedIso = DateHelper.nowIsoLocal()
        self.data = stamped
        self.deletedIds = nextDeleted

        LocalCache.saveSnapshot(
            CachedSnapshot(user: user, syncVersion: syncVersion, data: stamped, deletedIds: nextDeleted)
        )

        guard let token, !token.isEmpty, !serverUrl.isEmpty else {
            if let toastText { showToast(toastText) }
            return
        }

        syncing = true
        defer { syncing = false }
        do {
            let res = try await APIClient.pushSyncMerge(
                baseUrl: serverUrl,
                token: token,
                baseSyncVersion: syncVersion,
                data: stamped,
                deletedIds: nextDeleted
            )
            let mergedDeleted = res.deletedIds ?? nextDeleted
            self.syncVersion = res.syncVersion
            self.data = res.data
            self.deletedIds = mergedDeleted
            self.lastSyncText = "已同步 · v\(res.syncVersion)"

            LocalCache.saveSnapshot(
                CachedSnapshot(user: user, syncVersion: res.syncVersion, data: res.data, deletedIds: mergedDeleted)
            )
            triggerLightHaptic()
            if let toastText { showToast(toastText) }
        } catch {
            self.lastSyncText = "待同步（本地已保存）"
            showToast("同步失败，已暂存本地：\(error.localizedDescription)", isError: true)
        }
    }

    func upsertTask(_ task: MemoTask) async {
        var next = data
        var item = task
        item.UpdatedAtIso = DateHelper.nowIsoFull()
        if let idx = next.Tasks.firstIndex(where: { $0.Id == item.Id }) {
            next.Tasks[idx] = item
        } else {
            next.Tasks.insert(item, at: 0)
        }
        await pushMerge(nextData: next, nextDeleted: deletedIds, toastText: "日程已保存并同步")
    }

    func toggleTaskComplete(id: String) async {
        var next = data
        guard let idx = next.Tasks.firstIndex(where: { $0.Id == id }) else { return }
        let newDone = !next.Tasks[idx].Completed
        next.Tasks[idx].Completed = newDone
        next.Tasks[idx].CompletedAtIso = newDone ? DateHelper.nowIsoFull() : ""
        next.Tasks[idx].UpdatedAtIso = DateHelper.nowIsoFull()
        triggerLightHaptic()
        await pushMerge(
            nextData: next,
            nextDeleted: deletedIds,
            toastText: newDone ? "已完成任务" : "已恢复为待办"
        )
    }

    func deleteTask(id: String) async {
        let snapshotData = data
        let snapshotDeleted = deletedIds
        guard let victim = data.Tasks.first(where: { $0.Id == id }) else { return }

        var next = data
        next.Tasks.removeAll { $0.Id == id }
        var nextDel = deletedIds
        nextDel.Tasks[id] = DateHelper.nowUnixMs()

        scheduleUndo(label: "已删除「\(victim.Title)」", snapshotData: snapshotData, snapshotDeleted: snapshotDeleted)
        await pushMerge(nextData: next, nextDeleted: nextDel)
    }

    func upsertCourse(_ course: CourseItem) async {
        var next = data
        var item = course
        item.UpdatedAtIso = DateHelper.nowIsoFull()
        if let idx = next.Timetable.Courses.firstIndex(where: { $0.Id == item.Id }) {
            next.Timetable.Courses[idx] = item
        } else {
            next.Timetable.Courses.append(item)
        }
        await pushMerge(nextData: next, nextDeleted: deletedIds, toastText: "课程已保存")
    }

    func deleteCourse(id: String) async {
        let snapshotData = data
        let snapshotDeleted = deletedIds
        guard let victim = data.Timetable.Courses.first(where: { $0.Id == id }) else { return }

        var next = data
        next.Timetable.Courses.removeAll { $0.Id == id }
        var nextDel = deletedIds
        nextDel.Courses[id] = DateHelper.nowUnixMs()

        scheduleUndo(label: "已删除课程「\(victim.Name)」", snapshotData: snapshotData, snapshotDeleted: snapshotDeleted)
        await pushMerge(nextData: next, nextDeleted: nextDel)
    }

    func updateTimetableSettings(
        semesterName: String,
        startMondayIso: String,
        totalWeeks: Int,
        showWeekends: Bool
    ) async {
        var next = data
        next.Timetable.SemesterName = semesterName.trimmingCharacters(in: .whitespacesAndNewlines)
        next.Timetable.SemesterStartMondayIso = startMondayIso
        next.Timetable.TotalWeeks = max(1, min(30, totalWeeks))
        next.Timetable.ShowWeekends = showWeekends
        await pushMerge(nextData: next, nextDeleted: deletedIds, toastText: "学期设置已更新")
    }

    func upsertNote(_ note: StickyNote) async {
        var next = data
        var item = note
        item.UpdatedAtIso = DateHelper.nowIsoFull()
        if let idx = next.StickyNotes.firstIndex(where: { $0.Id == item.Id }) {
            next.StickyNotes[idx] = item
        } else {
            next.StickyNotes.insert(item, at: 0)
        }
        await pushMerge(nextData: next, nextDeleted: deletedIds, toastText: "便签已保存")
    }

    func deleteNote(id: String) async {
        let snapshotData = data
        let snapshotDeleted = deletedIds
        guard let victim = data.StickyNotes.first(where: { $0.Id == id }) else { return }

        var next = data
        next.StickyNotes.removeAll { $0.Id == id }
        var nextDel = deletedIds
        nextDel.StickyNotes[id] = DateHelper.nowUnixMs()

        scheduleUndo(label: "已删除便签「\(victim.Title)」", snapshotData: snapshotData, snapshotDeleted: snapshotDeleted)
        await pushMerge(nextData: next, nextDeleted: nextDel)
    }

    // MARK: - Attachments & Image Compression

    func compressImageToJpeg(_ image: UIImage, maxDimension: CGFloat = 1600, quality: CGFloat = 0.78) -> Data? {
        let size = image.size
        guard size.width > 0, size.height > 0 else { return nil }
        let longest = max(size.width, size.height)
        let scale = longest > maxDimension ? (maxDimension / longest) : 1.0
        let targetSize = CGSize(width: floor(size.width * scale), height: floor(size.height * scale))
        let format = UIGraphicsImageRendererFormat()
        format.scale = 1.0
        let renderer = UIGraphicsImageRenderer(size: targetSize, format: format)
        let resized = renderer.image { _ in
            image.draw(in: CGRect(origin: .zero, size: targetSize))
        }
        return resized.jpegData(compressionQuality: quality)
    }

    func uploadImageAttachment(_ image: UIImage) async throws -> String {
        guard let token, !token.isEmpty else {
            throw APIError.httpError(status: 401, message: "请先登录云端账号")
        }
        guard let jpegData = compressImageToJpeg(image) else {
            throw APIError.decodingError("无法压缩所选图片")
        }
        let stamp = Int(Date().timeIntervalSince1970 * 1000)
        let fileName = "ios_\(stamp).jpg"
        let b64 = jpegData.base64EncodedString()
        let savedName = try await APIClient.uploadAttachment(
            baseUrl: serverUrl,
            token: token,
            fileName: fileName,
            mimeType: "image/jpeg",
            dataBase64: b64
        )
        attachmentCache[savedName] = UIImage(data: jpegData)
        return savedName
    }

    func loadAttachmentImage(fileName: String) async throws -> UIImage {
        if let cached = attachmentCache[fileName] {
            return cached
        }
        guard let token, !token.isEmpty else {
            throw APIError.httpError(status: 401, message: "请先登录云端账号")
        }
        let bytes = try await APIClient.downloadAttachmentData(baseUrl: serverUrl, token: token, fileName: fileName)
        guard let image = UIImage(data: bytes) else {
            throw APIError.decodingError("该附件不是有效的图片格式")
        }
        attachmentCache[fileName] = image
        return image
    }

    // MARK: - Cloud AI

    func runAiSort() async throws -> Int {
        guard let token, !token.isEmpty else {
            throw APIError.httpError(status: 401, message: "请先登录云端账号")
        }
        let active = data.Tasks.filter { !$0.Completed }
        let assignments = try await APIClient.sortTasksWithAi(baseUrl: serverUrl, token: token, tasks: active)
        let map = Dictionary(uniqueKeysWithValues: assignments.map { ($0.id, $0) })

        var next = data
        var updatedCount = 0
        for idx in next.Tasks.indices {
            if let hit = map[next.Tasks[idx].Id] {
                next.Tasks[idx].Quadrant = hit.quadrant
                next.Tasks[idx].AiScore = hit.aiScore
                next.Tasks[idx].AiReason = hit.aiReason
                next.Tasks[idx].UpdatedAtIso = DateHelper.nowIsoFull()
                updatedCount += 1
            }
        }
        await pushMerge(nextData: next, nextDeleted: deletedIds, toastText: "AI 已完成 \(updatedCount) 条任务分类")
        return updatedCount
    }

    func extractWithAi(text: String, image: UIImage?) async throws -> AiExtractResponse {
        guard let token, !token.isEmpty else {
            throw APIError.httpError(status: 401, message: "请先登录云端账号")
        }
        var b64: String?
        if let image, let jpeg = compressImageToJpeg(image, maxDimension: 1500, quality: 0.78) {
            b64 = jpeg.base64EncodedString()
        }
        return try await APIClient.extractTasksWithAi(
            baseUrl: serverUrl,
            token: token,
            text: text,
            imageBase64: b64,
            mimeType: b64 != nil ? "image/jpeg" : nil
        )
    }

    func importExtractedTasks(_ candidates: [ExtractedCandidate]) async -> Int {
        let chosen = candidates.filter { $0.selected && !$0.title.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty }
        guard !chosen.isEmpty else { return 0 }
        var next = data
        let nowStr = DateHelper.nowIsoFull()
        for item in chosen.reversed() {
            let task = MemoTask(
                Title: item.title.trimmingCharacters(in: .whitespacesAndNewlines),
                Notes: item.notes,
                DeadlineIso: item.deadlineIso,
                Quadrant: (1...4).contains(item.quadrant) ? item.quadrant : 2,
                AiScore: 70,
                AiReason: item.aiReason.isEmpty ? "AI 提取" : item.aiReason,
                CreatedAtIso: nowStr,
                UpdatedAtIso: nowStr
            )
            next.Tasks.insert(task, at: 0)
        }
        await pushMerge(nextData: next, nextDeleted: deletedIds, toastText: "已导入 \(chosen.count) 条日程")
        return chosen.count
    }

    func saveCloudAiConfig(_ cfg: CloudAiConfig, newApiKey: String) async throws {
        guard let token, !token.isEmpty else {
            throw APIError.httpError(status: 401, message: "请先登录云端账号")
        }
        var payload = cfg
        let trimmedKey = newApiKey.trimmingCharacters(in: .whitespacesAndNewlines)
        payload.AiApiKey = trimmedKey.isEmpty ? CloudAiConfig.keepExistingSentinel : trimmedKey
        let res = try await APIClient.updateCloudConfig(baseUrl: serverUrl, token: token, config: payload)
        self.aiConfig = cfg
        self.hasApiKey = res.hasApiKey ?? (!trimmedKey.isEmpty || self.hasApiKey)
        self.maskedApiKey = res.maskedApiKey ?? self.maskedApiKey
        showToast("云端 AI 配置已加密保存")
    }

    func testCloudAi(baseUrl: String, model: String, apiKey: String) async throws -> AiTestResponse {
        guard let token, !token.isEmpty else {
            throw APIError.httpError(status: 401, message: "请先登录云端账号")
        }
        let trimmedKey = apiKey.trimmingCharacters(in: .whitespacesAndNewlines)
        return try await APIClient.testCloudAi(
            baseUrl: serverUrl,
            token: token,
            overrideBaseUrl: baseUrl.trimmingCharacters(in: .whitespacesAndNewlines),
            overrideModel: model.trimmingCharacters(in: .whitespacesAndNewlines),
            overrideApiKey: trimmedKey.isEmpty ? nil : trimmedKey
        )
    }

    // MARK: - Account Management

    func updateNickname(_ nickname: String) async throws {
        guard let token, !token.isEmpty else { return }
        let updated = try await APIClient.updateNickname(baseUrl: serverUrl, token: token, nickname: nickname)
        self.user = updated
        LocalCache.saveSnapshot(
            CachedSnapshot(user: updated, syncVersion: syncVersion, data: data, deletedIds: deletedIds)
        )
        showToast("昵称已更新")
    }

    func signOut() {
        dismissUndo()
        attachmentCache.removeAll()
        LocalCache.clearSessionAndCache()
        self.token = nil
        self.user = nil
        self.syncVersion = 0
        self.data = AppData()
        self.deletedIds = SyncTombstones()
        self.aiConfig = CloudAiConfig()
        self.hasApiKey = false
        self.maskedApiKey = ""
        self.lastSyncText = "尚未同步"
        self.sessionExpiredNotice = nil
    }

    func deleteAccount(password: String) async throws {
        guard let token, !token.isEmpty else {
            throw APIError.httpError(status: 401, message: "请先登录")
        }
        try await APIClient.deleteAccount(baseUrl: serverUrl, token: token, password: password)
        signOut()
    }

    private func triggerLightHaptic() {
        let generator = UIImpactFeedbackGenerator(style: .light)
        generator.impactOccurred()
    }
}
