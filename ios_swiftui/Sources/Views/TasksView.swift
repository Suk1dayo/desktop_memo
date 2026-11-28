import PhotosUI
import SwiftUI
import UIKit

private enum TaskFilter: String, CaseIterable, Identifiable {
    case active = "全部待办"
    case q1 = "Q1 立刻"
    case q2 = "Q2 计划"
    case q3 = "Q3 尽快"
    case q4 = "Q4 稍后"
    case completed = "已完成"

    var id: String { rawValue }
}

struct TasksView: View {
    @EnvironmentObject private var store: AppStore

    @State private var filter: TaskFilter = .active
    @State private var searchText: String = ""
    @State private var editingTask: MemoTask?
    @State private var showingNewTask: Bool = false
    @State private var showingAiExtract: Bool = false
    @State private var previewFileName: String?
    @State private var isAiSorting: Bool = false

    private var filteredTasks: [MemoTask] {
        let q = searchText.trimmingCharacters(in: .whitespacesAndNewlines).lowercased()
        let base = store.data.Tasks.filter { task in
            let matchesFilter: Bool
            switch filter {
            case .active: matchesFilter = !task.Completed
            case .q1: matchesFilter = !task.Completed && task.Quadrant == 1
            case .q2: matchesFilter = !task.Completed && task.Quadrant == 2
            case .q3: matchesFilter = !task.Completed && task.Quadrant == 3
            case .q4: matchesFilter = !task.Completed && task.Quadrant == 4
            case .completed: matchesFilter = task.Completed
            }
            guard matchesFilter else { return false }
            if q.isEmpty { return true }
            return task.Title.lowercased().contains(q)
                || task.Notes.lowercased().contains(q)
                || task.AiReason.lowercased().contains(q)
        }
        return base.sorted { lhs, rhs in
            if lhs.Completed != rhs.Completed { return !lhs.Completed }
            let ld = DateHelper.parseIsoDate(lhs.DeadlineIso)
            let rd = DateHelper.parseIsoDate(rhs.DeadlineIso)
            switch (ld, rd) {
            case let (l?, r?): return l < r
            case (_?, nil): return true
            case (nil, _?): return false
            case (nil, nil):
                if lhs.Quadrant != rhs.Quadrant {
                    return (lhs.Quadrant == 0 ? 9 : lhs.Quadrant) < (rhs.Quadrant == 0 ? 9 : rhs.Quadrant)
                }
                return lhs.CreatedAtIso > rhs.CreatedAtIso
            }
        }
    }

    private func countFor(_ item: TaskFilter) -> Int {
        switch item {
        case .active: return store.data.Tasks.filter { !$0.Completed }.count
        case .q1: return store.data.Tasks.filter { !$0.Completed && $0.Quadrant == 1 }.count
        case .q2: return store.data.Tasks.filter { !$0.Completed && $0.Quadrant == 2 }.count
        case .q3: return store.data.Tasks.filter { !$0.Completed && $0.Quadrant == 3 }.count
        case .q4: return store.data.Tasks.filter { !$0.Completed && $0.Quadrant == 4 }.count
        case .completed: return store.data.Tasks.filter { $0.Completed }.count
        }
    }

    var body: some View {
        NavigationStack {
            VStack(spacing: 0) {
                filterBar
                todayCoursesStrip

                if filteredTasks.isEmpty {
                    ContentUnavailableView {
                        Label(searchText.isEmpty ? "暂无日程任务" : "没有匹配的任务", systemImage: "checklist")
                    } description: {
                        Text(searchText.isEmpty ? "点击右上角 + 新建日程，或使用 AI 识图一键提取通知" : "尝试更换搜索关键词或切换筛选分组")
                    } actions: {
                        if searchText.isEmpty {
                            Button("新建日程") { showingNewTask = true }
                                .buttonStyle(.borderedProminent)
                        }
                    }
                    .frame(maxWidth: .infinity, maxHeight: .infinity)
                    .background(Theme.canvas)
                } else {
                    List {
                        ForEach(filteredTasks) { task in
                            TaskRowView(
                                task: task,
                                onToggleComplete: {
                                    _Concurrency.Task { await store.toggleTaskComplete(id: task.Id) }
                                },
                                onTapAttachment: { fileName in
                                    previewFileName = fileName
                                }
                            )
                            .contentShape(Rectangle())
                            .onTapGesture {
                                editingTask = task
                            }
                            .swipeActions(edge: .leading, allowsFullSwipe: true) {
                                Button {
                                    _Concurrency.Task { await store.toggleTaskComplete(id: task.Id) }
                                } label: {
                                    Label(task.Completed ? "恢复" : "完成", systemImage: task.Completed ? "arrow.uturn.backward" : "checkmark")
                                }
                                .tint(Theme.success)
                            }
                            .swipeActions(edge: .trailing, allowsFullSwipe: true) {
                                Button(role: .destructive) {
                                    _Concurrency.Task { await store.deleteTask(id: task.Id) }
                                } label: {
                                    Label("删除", systemImage: "trash")
                                }
                            }
                        }
                    }
                    .listStyle(.insetGrouped)
                    .scrollContentBackground(.hidden)
                    .background(Theme.canvas)
                    .refreshable {
                        await store.pullRemote()
                    }
                }
            }
            .background(Theme.canvas)
            .navigationTitle("日程")
            .searchable(text: $searchText, prompt: "搜索日程标题或备注")
            .toolbar {
                ToolbarItem(placement: .topBarLeading) {
                    Button {
                        _Concurrency.Task { await store.pullRemote() }
                    } label: {
                        HStack(spacing: 4) {
                            if store.syncing {
                                ProgressView()
                                    .controlSize(.small)
                            } else {
                                Image(systemName: "arrow.triangle.2.circlepath")
                            }
                            Text("v\(store.syncVersion)")
                                .font(.caption.monospacedDigit())
                        }
                        .foregroundStyle(Theme.secondaryInk)
                    }
                    .disabled(store.syncing)
                }

                ToolbarItemGroup(placement: .topBarTrailing) {
                    Button {
                        showingAiExtract = true
                    } label: {
                        Image(systemName: "sparkles")
                    }
                    .accessibilityLabel("AI 提取日程")

                    Button {
                        triggerAiSort()
                    } label: {
                        if isAiSorting {
                            ProgressView().controlSize(.small)
                        } else {
                            Image(systemName: "wand.and.stars")
                        }
                    }
                    .disabled(isAiSorting)
                    .accessibilityLabel("AI 四象限整理")

                    Button {
                        showingNewTask = true
                    } label: {
                        Image(systemName: "plus")
                    }
                    .accessibilityLabel("新建日程")
                }
            }
            .sheet(isPresented: $showingNewTask) {
                TaskEditorSheet(task: nil)
            }
            .sheet(item: $editingTask) { task in
                TaskEditorSheet(task: task)
            }
            .sheet(isPresented: $showingAiExtract) {
                AiExtractSheet()
            }
            .sheet(
                item: Binding(
                    get: { previewFileName.map { PreviewItem(fileName: $0) } },
                    set: { previewFileName = $0?.fileName }
                )
            ) { item in
                AttachmentPreviewSheet(fileName: item.fileName)
            }
        }
    }

    private var filterBar: some View {
        ScrollView(.horizontal, showsIndicators: false) {
            HStack(spacing: 8) {
                ForEach(TaskFilter.allCases) { item in
                    let active = filter == item
                    Button {
                        withAnimation(.easeInOut(duration: 0.15)) {
                            filter = item
                        }
                    } label: {
                        HStack(spacing: 4) {
                            Text(item.rawValue)
                            Text("\(countFor(item))")
                                .font(.caption2.monospacedDigit())
                                .opacity(0.8)
                        }
                        .font(.footnote.weight(active ? .semibold : .medium))
                        .padding(.horizontal, 12)
                        .padding(.vertical, 7)
                        .background(active ? Theme.accent : Theme.surface)
                        .foregroundStyle(active ? .white : Theme.ink)
                        .clipShape(Capsule())
                        .overlay(
                            Capsule()
                                .stroke(active ? Theme.accent : Theme.border, lineWidth: 1)
                        )
                    }
                }
            }
            .padding(.horizontal, 16)
            .padding(.vertical, 10)
        }
        .background(Theme.canvas)
    }

    @ViewBuilder
    private var todayCoursesStrip: some View {
        let today = store.data.Timetable.todayCourses()
        if !today.isEmpty {
            ScrollView(.horizontal, showsIndicators: false) {
                HStack(spacing: 8) {
                    Label("今日课程", systemImage: "book.closed")
                        .font(.caption.weight(.semibold))
                        .foregroundStyle(Theme.secondaryInk)
                    ForEach(today) { course in
                        HStack(spacing: 6) {
                            Circle()
                                .fill(Color(hex: course.ColorHex))
                                .frame(width: 7, height: 7)
                            Text(course.Name)
                                .font(.caption.weight(.medium))
                                .foregroundStyle(Theme.ink)
                            Text(course.effectiveStartTime(using: store.data.Timetable.SectionTimes))
                                .font(.caption2.monospacedDigit())
                                .foregroundStyle(Theme.secondaryInk)
                        }
                        .padding(.horizontal, 10)
                        .padding(.vertical, 5)
                        .background(Theme.surface)
                        .clipShape(Capsule())
                        .overlay(Capsule().stroke(Theme.border, lineWidth: 1))
                    }
                }
                .padding(.horizontal, 16)
                .padding(.bottom, 6)
            }
            .background(Theme.canvas)
        }
    }

    private func triggerAiSort() {
        isAiSorting = true
        _Concurrency.Task {
            defer { isAiSorting = false }
            do {
                _ = try await store.runAiSort()
            } catch {
                store.showToast(error.localizedDescription, isError: true)
            }
        }
    }
}

private struct PreviewItem: Identifiable {
    let fileName: String
    var id: String { fileName }
}

private struct TaskRowView: View {
    let task: MemoTask
    let onToggleComplete: () -> Void
    let onTapAttachment: (String) -> Void

    var body: some View {
        HStack(alignment: .top, spacing: 12) {
            Button(action: onToggleComplete) {
                Image(systemName: task.Completed ? "checkmark.circle.fill" : "circle")
                    .font(.system(size: 22))
                    .foregroundStyle(task.Completed ? Theme.success : Theme.quadrantColor(task.Quadrant))
                    .padding(.top, 2)
            }
            .buttonStyle(.plain)

            VStack(alignment: .leading, spacing: 6) {
                Text(task.Title.isEmpty ? "(无标题)" : task.Title)
                    .font(.body.weight(.medium))
                    .foregroundStyle(task.Completed ? Theme.secondaryInk : Theme.ink)
                    .strikethrough(task.Completed, color: Theme.secondaryInk)

                if !task.Notes.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
                    Text(task.Notes)
                        .font(.subheadline)
                        .foregroundStyle(Theme.secondaryInk)
                        .lineLimit(2)
                }

                HStack(spacing: 6) {
                    Text(Theme.quadrantShortName(task.Quadrant))
                        .font(.caption2.weight(.semibold))
                        .padding(.horizontal, 7)
                        .padding(.vertical, 3)
                        .background(Theme.quadrantColor(task.Quadrant).opacity(0.14))
                        .foregroundStyle(Theme.quadrantColor(task.Quadrant))
                        .clipShape(Capsule())

                    if let ddl = DateHelper.deadlineDisplay(iso: task.DeadlineIso, completed: task.Completed) {
                        Text(ddl.text)
                            .font(.caption2.weight(.medium))
                            .padding(.horizontal, 7)
                            .padding(.vertical, 3)
                            .background(ddl.tintColor.opacity(0.12))
                            .foregroundStyle(ddl.tintColor)
                            .clipShape(Capsule())
                    }

                    if !task.AttachmentFiles.isEmpty {
                        Label("\(task.AttachmentFiles.count)", systemImage: "paperclip")
                            .font(.caption2)
                            .foregroundStyle(Theme.secondaryInk)
                    }
                }

                if !task.AiReason.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
                    Text("AI: \(task.AiReason)")
                        .font(.caption2)
                        .foregroundStyle(Theme.secondaryInk)
                        .lineLimit(1)
                }

                if !task.AttachmentFiles.isEmpty {
                    ScrollView(.horizontal, showsIndicators: false) {
                        HStack(spacing: 6) {
                            ForEach(task.AttachmentFiles, id: \.self) { file in
                                Button {
                                    onTapAttachment(file)
                                } label: {
                                    Label(file, systemImage: "photo")
                                        .font(.caption2)
                                        .padding(.horizontal, 8)
                                        .padding(.vertical, 4)
                                        .background(Theme.subtle)
                                        .foregroundStyle(Theme.ink)
                                        .clipShape(RoundedRectangle(cornerRadius: 6, style: .continuous))
                                }
                                .buttonStyle(.plain)
                            }
                        }
                    }
                }
            }
        }
        .padding(.vertical, 4)
        .listRowBackground(Theme.surface)
    }
}

struct TaskEditorSheet: View {
    @EnvironmentObject private var store: AppStore
    @Environment(\.dismiss) private var dismiss

    let task: MemoTask?

    @State private var title: String = ""
    @State private var notes: String = ""
    @State private var quadrant: Int = 2
    @State private var hasDeadline: Bool = false
    @State private var deadlineDate: Date = Date().addingTimeInterval(3600 * 4)
    @State private var attachmentFiles: [String] = []
    @State private var selectedPhotoItem: PhotosPickerItem?
    @State private var isUploadingAttachment: Bool = false
    @State private var isSaving: Bool = false

    var body: some View {
        NavigationStack {
            Form {
                Section("基本信息") {
                    TextField("待办事项标题", text: $title)
                    Picker("四象限优先级", selection: $quadrant) {
                        Text("Q1 重要且紧急").tag(1)
                        Text("Q2 重要不紧急").tag(2)
                        Text("Q3 紧急不重要").tag(3)
                        Text("Q4 不重要不紧急").tag(4)
                        Text("未分类").tag(0)
                    }
                }

                Section("截止时间 (DDL)") {
                    Toggle("设置截止时间", isOn: $hasDeadline)
                    if hasDeadline {
                        DatePicker(
                            "截止时间",
                            selection: $deadlineDate,
                            displayedComponents: [.date, .hourAndMinute]
                        )
                        .datePickerStyle(.compact)
                    }
                }

                Section("详细备注") {
                    TextEditor(text: $notes)
                        .frame(minHeight: 110)
                }

                Section("图片附件") {
                    PhotosPicker(selection: $selectedPhotoItem, matching: .images) {
                        HStack {
                            Label(isUploadingAttachment ? "正在上传..." : "从相册上传图片附件", systemImage: "photo.badge.plus")
                            Spacer()
                            if isUploadingAttachment {
                                ProgressView().controlSize(.small)
                            }
                        }
                    }
                    .disabled(isUploadingAttachment)

                    ForEach(attachmentFiles, id: \.self) { fileName in
                        HStack {
                            Label(fileName, systemImage: "paperclip")
                                .font(.footnote)
                                .lineLimit(1)
                            Spacer()
                            Button(role: .destructive) {
                                attachmentFiles.removeAll { $0 == fileName }
                            } label: {
                                Image(systemName: "xmark.circle.fill")
                                    .foregroundStyle(Theme.secondaryInk)
                            }
                            .buttonStyle(.plain)
                        }
                    }
                }
            }
            .scrollContentBackground(.hidden)
            .background(Theme.canvas)
            .navigationTitle(task == nil ? "新建日程" : "编辑日程")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("取消") { dismiss() }
                }
                ToolbarItem(placement: .confirmationAction) {
                    Button(isSaving ? "保存中..." : "保存") {
                        saveTask()
                    }
                    .disabled(isSaving || title.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty)
                }
            }
            .onAppear {
                if let task {
                    title = task.Title
                    notes = task.Notes
                    quadrant = task.Quadrant
                    if let parsed = DateHelper.parseIsoDate(task.DeadlineIso) {
                        hasDeadline = true
                        deadlineDate = parsed
                    }
                    attachmentFiles = task.AttachmentFiles
                }
            }
            .onChange(of: selectedPhotoItem) { _, newItem in
                guard let newItem else { return }
                uploadPickedPhoto(newItem)
            }
        }
    }

    private func uploadPickedPhoto(_ item: PhotosPickerItem) {
        isUploadingAttachment = true
        _Concurrency.Task {
            defer {
                isUploadingAttachment = false
                selectedPhotoItem = nil
            }
            do {
                guard let data = try await item.loadTransferable(type: Data.self),
                      let uiImage = UIImage(data: data) else {
                    store.showToast("无法读取所选图片", isError: true)
                    return
                }
                let fileName = try await store.uploadImageAttachment(uiImage)
                if !attachmentFiles.contains(fileName) {
                    attachmentFiles.append(fileName)
                }
                store.showToast("附件上传成功")
            } catch {
                store.showToast(error.localizedDescription, isError: true)
            }
        }
    }

    private func saveTask() {
        isSaving = true
        let cleanTitle = title.trimmingCharacters(in: .whitespacesAndNewlines)
        var draft = task ?? MemoTask()
        draft.Title = cleanTitle
        draft.Notes = notes
        draft.Quadrant = quadrant
        draft.DeadlineIso = hasDeadline ? DateHelper.formatDeadlineIso(deadlineDate) : ""
        draft.AttachmentFiles = attachmentFiles

        _Concurrency.Task {
            await store.upsertTask(draft)
            isSaving = false
            dismiss()
        }
    }
}

struct AiExtractSheet: View {
    @EnvironmentObject private var store: AppStore
    @Environment(\.dismiss) private var dismiss

    @State private var inputText: String = ""
    @State private var pickedItem: PhotosPickerItem?
    @State private var pickedImage: UIImage?
    @State private var isExtracting: Bool = false
    @State private var replySummary: String = ""
    @State private var candidates: [ExtractedCandidate] = []

    var body: some View {
        NavigationStack {
            Form {
                Section("输入文本或通知截图") {
                    TextEditor(text: $inputText)
                        .frame(minHeight: 110)

                    HStack(spacing: 12) {
                        PhotosPicker(selection: $pickedItem, matching: .images) {
                            Label("选择截图", systemImage: "photo")
                        }
                        .buttonStyle(.bordered)

                        Button {
                            if let pasteImg = UIPasteboard.general.image {
                                pickedImage = pasteImg
                            } else {
                                store.showToast("剪贴板中暂无图片", isError: true)
                            }
                        } label: {
                            Label("粘贴图片", systemImage: "doc.on.clipboard")
                        }
                        .buttonStyle(.bordered)
                    }

                    if let pickedImage {
                        VStack(alignment: .leading, spacing: 8) {
                            Image(uiImage: pickedImage)
                                .resizable()
                                .scaledToFit()
                                .frame(maxHeight: 180)
                                .clipShape(RoundedRectangle(cornerRadius: 10, style: .continuous))
                            Button("移除图片", role: .destructive) {
                                self.pickedImage = nil
                            }
                            .font(.caption)
                        }
                    }

                    Button {
                        runExtract()
                    } label: {
                        HStack {
                            if isExtracting {
                                ProgressView().controlSize(.small)
                            }
                            Text(isExtracting ? "云端 AI 正在解析..." : "开始 AI 提取日程")
                                .fontWeight(.semibold)
                        }
                        .frame(maxWidth: .infinity)
                    }
                    .buttonStyle(.borderedProminent)
                    .disabled(isExtracting || (inputText.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty && pickedImage == nil))
                }

                if !replySummary.isEmpty {
                    Section("AI 识别摘要") {
                        Text(replySummary)
                            .font(.subheadline)
                            .foregroundStyle(Theme.ink)
                    }
                }

                if !candidates.isEmpty {
                    Section("提取结果（点击勾选需要导入的条目）") {
                        ForEach($candidates) { $item in
                            Button {
                                item.selected.toggle()
                            } label: {
                                HStack(alignment: .top, spacing: 10) {
                                    Image(systemName: item.selected ? "checkmark.square.fill" : "square")
                                        .foregroundStyle(item.selected ? Theme.accent : Theme.secondaryInk)
                                        .font(.title3)
                                    VStack(alignment: .leading, spacing: 4) {
                                        Text(item.title)
                                            .font(.subheadline.weight(.semibold))
                                            .foregroundStyle(Theme.ink)
                                        HStack(spacing: 6) {
                                            Text(Theme.quadrantShortName(item.quadrant))
                                                .font(.caption2)
                                                .foregroundStyle(Theme.quadrantColor(item.quadrant))
                                            if !item.deadlineIso.isEmpty {
                                                Text(item.deadlineIso)
                                                    .font(.caption2)
                                                    .foregroundStyle(Theme.secondaryInk)
                                            }
                                        }
                                        if !item.notes.isEmpty {
                                            Text(item.notes)
                                                .font(.caption)
                                                .foregroundStyle(Theme.secondaryInk)
                                        }
                                    }
                                }
                            }
                            .buttonStyle(.plain)
                        }

                        Button("批量导入选中项") {
                            _Concurrency.Task {
                                _ = await store.importExtractedTasks(candidates)
                                dismiss()
                            }
                        }
                        .buttonStyle(.borderedProminent)
                    }
                }
            }
            .scrollContentBackground(.hidden)
            .background(Theme.canvas)
            .navigationTitle("AI 对话/识图提取")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("关闭") { dismiss() }
                }
            }
            .onChange(of: pickedItem) { _, newItem in
                guard let newItem else { return }
                _Concurrency.Task {
                    if let data = try? await newItem.loadTransferable(type: Data.self),
                       let img = UIImage(data: data) {
                        pickedImage = img
                    }
                    pickedItem = nil
                }
            }
        }
    }

    private func runExtract() {
        isExtracting = true
        _Concurrency.Task {
            defer { isExtracting = false }
            do {
                let res = try await store.extractWithAi(text: inputText, image: pickedImage)
                replySummary = res.reply
                candidates = res.tasks
            } catch {
                store.showToast(error.localizedDescription, isError: true)
            }
        }
    }
}

struct AttachmentPreviewSheet: View {
    @EnvironmentObject private var store: AppStore
    @Environment(\.dismiss) private var dismiss

    let fileName: String

    @State private var loadedImage: UIImage?
    @State private var errorText: String?

    var body: some View {
        NavigationStack {
            ZStack {
                Theme.canvas.ignoresSafeArea()
                if let loadedImage {
                    Image(uiImage: loadedImage)
                        .resizable()
                        .scaledToFit()
                        .padding()
                } else if let errorText {
                    ContentUnavailableView("无法加载附件", systemImage: "exclamationmark.triangle", description: Text(errorText))
                } else {
                    ProgressView("正在加载附件...")
                }
            }
            .navigationTitle(fileName)
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("关闭") { dismiss() }
                }
            }
            .task {
                do {
                    loadedImage = try await store.loadAttachmentImage(fileName: fileName)
                } catch {
                    errorText = error.localizedDescription
                }
            }
        }
    }
}
