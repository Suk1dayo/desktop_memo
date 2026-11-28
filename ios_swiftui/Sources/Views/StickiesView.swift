import SwiftUI

struct StickiesView: View {
    @EnvironmentObject private var store: AppStore

    @State private var editingNote: StickyNote?
    @State private var showingNewNote: Bool = false

    private let columns = [
        GridItem(.adaptive(minimum: 165), spacing: 12)
    ]

    var body: some View {
        NavigationStack {
            Group {
                if store.data.StickyNotes.isEmpty {
                    ContentUnavailableView {
                        Label("暂无云端便签", systemImage: "note.text")
                    } description: {
                        Text("随时记录灵感、零碎事项或会议纪要，跨设备自动同步")
                    } actions: {
                        Button("新建便签") { showingNewNote = true }
                            .buttonStyle(.borderedProminent)
                    }
                    .frame(maxWidth: .infinity, maxHeight: .infinity)
                    .background(Theme.canvas)
                } else {
                    ScrollView {
                        LazyVGrid(columns: columns, spacing: 12) {
                            ForEach(store.data.StickyNotes) { note in
                                StickyCardView(note: note)
                                    .onTapGesture {
                                        editingNote = note
                                    }
                                    .contextMenu {
                                        Button {
                                            editingNote = note
                                        } label: {
                                            Label("编辑", systemImage: "pencil")
                                        }
                                        Button(role: .destructive) {
                                            _Concurrency.Task { await store.deleteNote(id: note.Id) }
                                        } label: {
                                            Label("删除", systemImage: "trash")
                                        }
                                    }
                            }
                        }
                        .padding(16)
                    }
                    .background(Theme.canvas)
                    .refreshable {
                        await store.pullRemote()
                    }
                }
            }
            .navigationTitle("便签")
            .toolbar {
                ToolbarItem(placement: .topBarTrailing) {
                    Button {
                        showingNewNote = true
                    } label: {
                        Image(systemName: "plus")
                    }
                    .accessibilityLabel("新建便签")
                }
            }
            .sheet(isPresented: $showingNewNote) {
                StickyEditorSheet(note: nil)
            }
            .sheet(item: $editingNote) { note in
                StickyEditorSheet(note: note)
            }
        }
    }
}

private struct StickyCardView: View {
    let note: StickyNote

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack {
                Text(note.Title.isEmpty ? "无标题便签" : note.Title)
                    .font(.headline)
                    .foregroundStyle(Theme.ink)
                    .lineLimit(1)
                Spacer(minLength: 4)
            }

            Text(note.filterLabel)
                .font(.caption2.weight(.semibold))
                .padding(.horizontal, 8)
                .padding(.vertical, 3)
                .background(Theme.accent.opacity(0.12))
                .foregroundStyle(Theme.accent)
                .clipShape(Capsule())

            Text(note.Content.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty ? "（空白内容）" : note.Content)
                .font(.subheadline)
                .foregroundStyle(Theme.secondaryInk)
                .lineLimit(6)
                .multilineTextAlignment(.leading)

            Spacer(minLength: 0)
        }
        .frame(maxWidth: .infinity, minHeight: 140, alignment: .topLeading)
        .warmCard(padding: 14, cornerRadius: 16)
    }
}

struct StickyEditorSheet: View {
    @EnvironmentObject private var store: AppStore
    @Environment(\.dismiss) private var dismiss

    let note: StickyNote?

    @State private var title: String = ""
    @State private var content: String = ""
    @State private var filterMode: String = "custom"

    var body: some View {
        NavigationStack {
            Form {
                Section("便签标题与类型") {
                    TextField("标题", text: $title)
                    Picker("桌面展示模式", selection: $filterMode) {
                        Text("独立便签（自由文本）").tag("custom")
                        Text("全部日程").tag("all")
                        Text("今日课表").tag("timetable")
                        Text("Q1 重要且紧急").tag("q1")
                        Text("Q2 重要不紧急").tag("q2")
                        Text("Q3 紧急不重要").tag("q3")
                        Text("Q4 不重要不紧急").tag("q4")
                    }
                }

                Section("正文内容") {
                    TextEditor(text: $content)
                        .frame(minHeight: 220)
                }

                if let note {
                    Section {
                        Button("删除此便签", role: .destructive) {
                            _Concurrency.Task {
                                await store.deleteNote(id: note.Id)
                                dismiss()
                            }
                        }
                    }
                }
            }
            .scrollContentBackground(.hidden)
            .background(Theme.canvas)
            .navigationTitle(note == nil ? "新建便签" : "编辑便签")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("取消") { dismiss() }
                }
                ToolbarItem(placement: .confirmationAction) {
                    Button("保存") {
                        var draft = note ?? StickyNote()
                        let cleanTitle = title.trimmingCharacters(in: .whitespacesAndNewlines)
                        draft.Title = cleanTitle.isEmpty ? "随手便签" : cleanTitle
                        draft.Content = content
                        draft.FilterMode = filterMode
                        _Concurrency.Task {
                            await store.upsertNote(draft)
                            dismiss()
                        }
                    }
                }
            }
            .onAppear {
                if let note {
                    title = note.Title
                    content = note.Content
                    filterMode = note.FilterMode
                }
            }
        }
    }
}
