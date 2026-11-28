import SwiftUI

struct TimetableView: View {
    @EnvironmentObject private var store: AppStore

    @State private var selectedWeek: Int = 1
    @State private var selectedDay: Int = DateHelper.isoWeekday()
    @State private var initializedWeek: Bool = false
    @State private var editingCourse: CourseItem?
    @State private var showingNewCourse: Bool = false
    @State private var showingSemesterSettings: Bool = false

    private var timetable: TimetableData {
        store.data.Timetable
    }

    private var visibleDays: [Int] {
        timetable.ShowWeekends ? Array(1...7) : Array(1...5)
    }

    private var dayCourses: [CourseItem] {
        timetable.courses(forWeek: selectedWeek, dayOfWeek: selectedDay)
    }

    var body: some View {
        NavigationStack {
            VStack(spacing: 0) {
                weekSelectorBar
                daySelectorBar

                if dayCourses.isEmpty {
                    ContentUnavailableView {
                        Label("本日暂无课程", systemImage: "calendar")
                    } description: {
                        Text("第 \(selectedWeek) 周 \(DateHelper.weekdayName(selectedDay)) 没有排课，可点击右上角 + 添加课程")
                    } actions: {
                        Button("添加课程") { showingNewCourse = true }
                            .buttonStyle(.borderedProminent)
                    }
                    .frame(maxWidth: .infinity, maxHeight: .infinity)
                    .background(Theme.canvas)
                } else {
                    List {
                        ForEach(dayCourses) { course in
                            CourseCardRow(
                                course: course,
                                sectionTimes: timetable.SectionTimes
                            )
                            .contentShape(Rectangle())
                            .onTapGesture {
                                editingCourse = course
                            }
                            .swipeActions(edge: .trailing, allowsFullSwipe: true) {
                                Button(role: .destructive) {
                                    _Concurrency.Task { await store.deleteCourse(id: course.Id) }
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
            .navigationTitle("课表")
            .toolbar {
                ToolbarItem(placement: .topBarLeading) {
                    Button {
                        showingSemesterSettings = true
                    } label: {
                        Image(systemName: "calendar.badge.clock")
                    }
                    .accessibilityLabel("学期设置")
                }
                ToolbarItem(placement: .topBarTrailing) {
                    Button {
                        showingNewCourse = true
                    } label: {
                        Image(systemName: "plus")
                    }
                    .accessibilityLabel("添加课程")
                }
            }
            .onAppear {
                if !initializedWeek {
                    selectedWeek = timetable.computeCurrentWeek()
                    let today = DateHelper.isoWeekday()
                    selectedDay = visibleDays.contains(today) ? today : 1
                    initializedWeek = true
                }
            }
            .sheet(isPresented: $showingNewCourse) {
                CourseEditorSheet(course: nil, defaultDay: selectedDay)
            }
            .sheet(item: $editingCourse) { course in
                CourseEditorSheet(course: course, defaultDay: course.DayOfWeek)
            }
            .sheet(isPresented: $showingSemesterSettings) {
                SemesterSettingsSheet()
            }
        }
    }

    private var weekSelectorBar: some View {
        let total = max(1, timetable.TotalWeeks)
        let currentWeek = timetable.computeCurrentWeek()
        return HStack(spacing: 12) {
            Button {
                selectedWeek = max(1, selectedWeek - 1)
            } label: {
                Image(systemName: "chevron.left")
                    .font(.subheadline.weight(.semibold))
                    .frame(width: 32, height: 32)
                    .background(Theme.surface)
                    .clipShape(Circle())
                    .overlay(Circle().stroke(Theme.border, lineWidth: 1))
            }
            .disabled(selectedWeek <= 1)

            VStack(spacing: 2) {
                Text("第 \(selectedWeek) 周 / 共 \(total) 周")
                    .font(.headline)
                    .foregroundStyle(Theme.ink)
                Text(timetable.SemesterName.isEmpty ? "当前学期" : timetable.SemesterName)
                    .font(.caption)
                    .foregroundStyle(Theme.secondaryInk)
            }

            Button {
                selectedWeek = min(total, selectedWeek + 1)
            } label: {
                Image(systemName: "chevron.right")
                    .font(.subheadline.weight(.semibold))
                    .frame(width: 32, height: 32)
                    .background(Theme.surface)
                    .clipShape(Circle())
                    .overlay(Circle().stroke(Theme.border, lineWidth: 1))
            }
            .disabled(selectedWeek >= total)

            Spacer()

            if selectedWeek != currentWeek {
                Button("本周") {
                    withAnimation {
                        selectedWeek = currentWeek
                        let today = DateHelper.isoWeekday()
                        if visibleDays.contains(today) || today <= 7 {
                            selectedDay = today
                        }
                    }
                }
                .font(.caption.weight(.semibold))
                .padding(.horizontal, 10)
                .padding(.vertical, 6)
                .background(Theme.accent.opacity(0.14))
                .foregroundStyle(Theme.accent)
                .clipShape(Capsule())
            }
        }
        .padding(.horizontal, 16)
        .padding(.vertical, 10)
        .background(Theme.canvas)
    }

    private var daySelectorBar: some View {
        let todayDay = DateHelper.isoWeekday()
        let isCurrentWeek = selectedWeek == timetable.computeCurrentWeek()
        return HStack(spacing: 8) {
            ForEach(1...7, id: \.self) { day in
                let isSelected = selectedDay == day
                let hasClasses = !timetable.courses(forWeek: selectedWeek, dayOfWeek: day).isEmpty
                let isToday = isCurrentWeek && day == todayDay
                Button {
                    withAnimation(.easeInOut(duration: 0.15)) {
                        selectedDay = day
                    }
                } label: {
                    VStack(spacing: 4) {
                        Text(DateHelper.weekdayShort(day))
                            .font(.subheadline.weight(isSelected ? .bold : .medium))
                        Circle()
                            .fill(isSelected ? Color.white : (hasClasses ? Theme.accent : (isToday ? Theme.secondaryInk : Color.clear)))
                            .frame(width: 5, height: 5)
                    }
                    .frame(maxWidth: .infinity)
                    .padding(.vertical, 8)
                    .background(isSelected ? Theme.accent : Theme.surface)
                    .foregroundStyle(isSelected ? .white : (isToday ? Theme.accent : Theme.ink))
                    .clipShape(RoundedRectangle(cornerRadius: 12, style: .continuous))
                    .overlay(
                        RoundedRectangle(cornerRadius: 12, style: .continuous)
                            .stroke(isSelected ? Theme.accent : Theme.border, lineWidth: 1)
                    )
                }
            }
        }
        .padding(.horizontal, 16)
        .padding(.bottom, 10)
        .background(Theme.canvas)
    }
}

private struct CourseCardRow: View {
    let course: CourseItem
    let sectionTimes: [SectionTimeSlot]

    var body: some View {
        HStack(spacing: 12) {
            RoundedRectangle(cornerRadius: 3, style: .continuous)
                .fill(Color(hex: course.ColorHex))
                .frame(width: 5)
                .padding(.vertical, 4)

            VStack(alignment: .leading, spacing: 6) {
                HStack {
                    Text(course.Name.isEmpty ? "(未命名课程)" : course.Name)
                        .font(.headline)
                        .foregroundStyle(Theme.ink)
                    Spacer()
                    Text(course.sectionLabel)
                        .font(.caption.weight(.semibold))
                        .padding(.horizontal, 8)
                        .padding(.vertical, 3)
                        .background(Color(hex: course.ColorHex).opacity(0.14))
                        .foregroundStyle(Color(hex: course.ColorHex))
                        .clipShape(Capsule())
                }

                HStack(spacing: 12) {
                    Label("\(course.effectiveStartTime(using: sectionTimes)) - \(course.effectiveEndTime(using: sectionTimes))", systemImage: "clock")
                    if !course.Location.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
                        Label(course.Location, systemImage: "mappin.and.ellipse")
                    }
                }
                .font(.caption)
                .foregroundStyle(Theme.secondaryInk)

                HStack(spacing: 12) {
                    if !course.Teacher.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
                        Label(course.Teacher, systemImage: "person")
                    }
                    Label(course.weeksLabel, systemImage: "repeat")
                }
                .font(.caption2)
                .foregroundStyle(Theme.secondaryInk)
            }
        }
        .padding(.vertical, 4)
        .listRowBackground(Theme.surface)
    }
}

struct CourseEditorSheet: View {
    @EnvironmentObject private var store: AppStore
    @Environment(\.dismiss) private var dismiss

    let course: CourseItem?
    let defaultDay: Int

    @State private var name: String = ""
    @State private var location: String = ""
    @State private var teacher: String = ""
    @State private var dayOfWeek: Int = 1
    @State private var startSection: Int = 1
    @State private var endSection: Int = 2
    @State private var startWeek: Int = 1
    @State private var endWeek: Int = 16
    @State private var weekType: String = "all"
    @State private var colorHex: String = "#C96442"

    var body: some View {
        NavigationStack {
            Form {
                Section("课程信息") {
                    TextField("课程名称", text: $name)
                    TextField("教室地点", text: $location)
                    TextField("授课教师", text: $teacher)
                }

                Section("上课时间") {
                    Picker("星期", selection: $dayOfWeek) {
                        ForEach(1...7, id: \.self) { day in
                            Text(DateHelper.weekdayName(day)).tag(day)
                        }
                    }

                    Stepper("开始节次：第 \(startSection) 节", value: $startSection, in: 1...14)
                        .onChange(of: startSection) { _, newVal in
                            if endSection < newVal { endSection = newVal }
                        }
                    Stepper("结束节次：第 \(endSection) 节", value: $endSection, in: startSection...14)
                }

                Section("教学周次") {
                    Stepper("起始周：第 \(startWeek) 周", value: $startWeek, in: 1...30)
                        .onChange(of: startWeek) { _, newVal in
                            if endWeek < newVal { endWeek = newVal }
                        }
                    Stepper("结束周：第 \(endWeek) 周", value: $endWeek, in: startWeek...30)

                    Picker("单双周", selection: $weekType) {
                        Text("全周").tag("all")
                        Text("单周").tag("odd")
                        Text("双周").tag("even")
                    }
                    .pickerStyle(.segmented)
                }

                Section("卡片颜色") {
                    LazyVGrid(columns: [GridItem(.adaptive(minimum: 36), spacing: 12)], spacing: 12) {
                        ForEach(Theme.coursePalette, id: \.self) { hex in
                            Button {
                                colorHex = hex
                            } label: {
                                ZStack {
                                    Circle()
                                        .fill(Color(hex: hex))
                                        .frame(width: 32, height: 32)
                                    if colorHex == hex {
                                        Image(systemName: "checkmark")
                                            .font(.caption.bold())
                                            .foregroundStyle(.white)
                                    }
                                }
                            }
                            .buttonStyle(.plain)
                        }
                    }
                    .padding(.vertical, 4)
                }
            }
            .scrollContentBackground(.hidden)
            .background(Theme.canvas)
            .navigationTitle(course == nil ? "添加课程" : "编辑课程")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("取消") { dismiss() }
                }
                ToolbarItem(placement: .confirmationAction) {
                    Button("保存") {
                        saveCourse()
                    }
                    .disabled(name.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty)
                }
            }
            .onAppear {
                if let course {
                    name = course.Name
                    location = course.Location
                    teacher = course.Teacher
                    dayOfWeek = course.DayOfWeek
                    startSection = course.StartSection
                    endSection = course.EndSection
                    startWeek = course.StartWeek
                    endWeek = course.EndWeek
                    weekType = course.WeekType
                    colorHex = course.ColorHex
                } else {
                    dayOfWeek = defaultDay
                }
            }
        }
    }

    private func saveCourse() {
        var draft = course ?? CourseItem()
        draft.Name = name.trimmingCharacters(in: .whitespacesAndNewlines)
        draft.Location = location.trimmingCharacters(in: .whitespacesAndNewlines)
        draft.Teacher = teacher.trimmingCharacters(in: .whitespacesAndNewlines)
        draft.DayOfWeek = dayOfWeek
        draft.StartSection = startSection
        draft.EndSection = max(startSection, endSection)
        draft.StartWeek = startWeek
        draft.EndWeek = max(startWeek, endWeek)
        draft.WeekType = weekType
        draft.ColorHex = colorHex

        _Concurrency.Task {
            await store.upsertCourse(draft)
            dismiss()
        }
    }
}

struct SemesterSettingsSheet: View {
    @EnvironmentObject private var store: AppStore
    @Environment(\.dismiss) private var dismiss

    @State private var semesterName: String = ""
    @State private var startDate: Date = Date()
    @State private var totalWeeks: Int = 20
    @State private var showWeekends: Bool = false

    var body: some View {
        NavigationStack {
            Form {
                Section("学期参数") {
                    TextField("学期名称（如 2026 春季）", text: $semesterName)
                    DatePicker("第 1 周日期", selection: $startDate, displayedComponents: .date)
                    Stepper("总周数：\(totalWeeks) 周", value: $totalWeeks, in: 1...30)
                    Toggle("桌面/总览默认显示周末", isOn: $showWeekends)
                }
                Section {
                    Text("系统会自动将所选日期对齐到所在周的周一，用于计算当前处于第几教学周。")
                        .font(.caption)
                        .foregroundStyle(Theme.secondaryInk)
                }
            }
            .scrollContentBackground(.hidden)
            .background(Theme.canvas)
            .navigationTitle("学期设置")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("取消") { dismiss() }
                }
                ToolbarItem(placement: .confirmationAction) {
                    Button("保存") {
                        let mondayIso = DateHelper.mondayYmd(for: startDate)
                        _Concurrency.Task {
                            await store.updateTimetableSettings(
                                semesterName: semesterName,
                                startMondayIso: mondayIso,
                                totalWeeks: totalWeeks,
                                showWeekends: showWeekends
                            )
                            dismiss()
                        }
                    }
                }
            }
            .onAppear {
                let tt = store.data.Timetable
                semesterName = tt.SemesterName
                totalWeeks = max(1, tt.TotalWeeks)
                showWeekends = tt.ShowWeekends
                if let parsed = DateHelper.parseIsoDate(tt.SemesterStartMondayIso) {
                    startDate = parsed
                }
            }
        }
    }
}
