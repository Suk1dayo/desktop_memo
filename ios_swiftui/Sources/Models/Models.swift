import Foundation
import SwiftUI

struct DeadlineBadge: Equatable {
    enum Tone: Equatable {
        case overdue
        case soon
        case normal
        case completed
    }

    let text: String
    let tone: Tone

    var tintColor: Color {
        switch tone {
        case .overdue: return Theme.danger
        case .soon: return Theme.warning
        case .normal: return Theme.secondaryInk
        case .completed: return Theme.success
        }
    }
}

enum DateHelper {
    private static let localShortFormatter: DateFormatter = {
        let f = DateFormatter()
        f.locale = Locale(identifier: "en_US_POSIX")
        f.dateFormat = "yyyy-MM-dd HH:mm"
        return f
    }()

    private static let localFullFormatter: DateFormatter = {
        let f = DateFormatter()
        f.locale = Locale(identifier: "en_US_POSIX")
        f.dateFormat = "yyyy-MM-dd HH:mm:ss"
        return f
    }()

    private static let ymdFormatter: DateFormatter = {
        let f = DateFormatter()
        f.locale = Locale(identifier: "en_US_POSIX")
        f.dateFormat = "yyyy-MM-dd"
        return f
    }()

    private static let monthDayTimeFormatter: DateFormatter = {
        let f = DateFormatter()
        f.locale = Locale(identifier: "en_US_POSIX")
        f.dateFormat = "MM-dd HH:mm"
        return f
    }()

    static func nowIsoLocal() -> String {
        localShortFormatter.string(from: Date())
    }

    static func nowIsoFull() -> String {
        localFullFormatter.string(from: Date())
    }

    static func nowUnixMs() -> Int64 {
        Int64(Date().timeIntervalSince1970 * 1000)
    }

    static func formatDeadlineIso(_ date: Date) -> String {
        localShortFormatter.string(from: date)
    }

    static func formatYmd(_ date: Date) -> String {
        ymdFormatter.string(from: date)
    }

    static func parseIsoDate(_ raw: String?) -> Date? {
        guard let trimmed = raw?.trimmingCharacters(in: .whitespacesAndNewlines), !trimmed.isEmpty else {
            return nil
        }
        let formats = [
            "yyyy-MM-dd HH:mm",
            "yyyy-MM-dd HH:mm:ss",
            "yyyy-MM-dd'T'HH:mm",
            "yyyy-MM-dd'T'HH:mm:ss",
            "yyyy-MM-dd"
        ]
        for fmt in formats {
            let f = DateFormatter()
            f.locale = Locale(identifier: "en_US_POSIX")
            f.dateFormat = fmt
            if let d = f.date(from: trimmed) {
                return d
            }
        }
        let iso = ISO8601DateFormatter()
        iso.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        if let d = iso.date(from: trimmed) {
            return d
        }
        iso.formatOptions = [.withInternetDateTime]
        return iso.date(from: trimmed)
    }

    static func mondayYmd(for date: Date = Date()) -> String {
        let cal = Calendar(identifier: .gregorian)
        let startOfDay = cal.startOfDay(for: date)
        let weekday = cal.component(.weekday, from: startOfDay) // 1=Sun, 2=Mon..7=Sat
        let delta = weekday == 1 ? -6 : (2 - weekday)
        let monday = cal.date(byAdding: .day, value: delta, to: startOfDay) ?? startOfDay
        return ymdFormatter.string(from: monday)
    }

    static func isoWeekday(for date: Date = Date()) -> Int {
        let cal = Calendar(identifier: .gregorian)
        let w = cal.component(.weekday, from: date)
        return w == 1 ? 7 : (w - 1)
    }

    static func weekdayName(_ dayOfWeek: Int) -> String {
        switch dayOfWeek {
        case 1: return "周一"
        case 2: return "周二"
        case 3: return "周三"
        case 4: return "周四"
        case 5: return "周五"
        case 6: return "周六"
        case 7: return "周日"
        default: return "周一"
        }
    }

    static func weekdayShort(_ dayOfWeek: Int) -> String {
        switch dayOfWeek {
        case 1: return "一"
        case 2: return "二"
        case 3: return "三"
        case 4: return "四"
        case 5: return "五"
        case 6: return "六"
        case 7: return "日"
        default: return "一"
        }
    }

    static func deadlineDisplay(iso: String, completed: Bool, now: Date = Date()) -> DeadlineBadge? {
        let trimmed = iso.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmed.isEmpty else { return nil }
        guard let target = parseIsoDate(trimmed) else {
            return DeadlineBadge(text: trimmed, tone: completed ? .completed : .normal)
        }
        let shortLabel = monthDayTimeFormatter.string(from: target)
        if completed {
            return DeadlineBadge(text: "已完成 · \(shortLabel)", tone: .completed)
        }
        let diff = target.timeIntervalSince(now)
        if diff < 0 {
            let absSec = abs(diff)
            if absSec < 3600 {
                let mins = max(1, Int(ceil(absSec / 60)))
                return DeadlineBadge(text: "已逾期 \(mins)分钟 · \(shortLabel)", tone: .overdue)
            } else if absSec < 86400 {
                let hrs = Int(floor(absSec / 3600))
                return DeadlineBadge(text: "已逾期 \(hrs)小时 · \(shortLabel)", tone: .overdue)
            } else {
                let days = Int(floor(absSec / 86400))
                return DeadlineBadge(text: "已逾期 \(days)天 · \(shortLabel)", tone: .overdue)
            }
        }
        if diff < 3600 {
            let mins = max(1, Int(ceil(diff / 60)))
            return DeadlineBadge(text: "剩 \(mins)分钟 · \(shortLabel)", tone: .soon)
        }
        if diff < 86400 {
            let hrs = Int(floor(diff / 3600))
            return DeadlineBadge(text: "剩 \(hrs)小时 · \(shortLabel)", tone: .soon)
        }
        let days = Int(floor(diff / 86400))
        return DeadlineBadge(text: "剩 \(days)天 · \(shortLabel)", tone: days <= 2 ? .soon : .normal)
    }
}

struct MemoTask: Identifiable, Codable, Equatable {
    var Id: String
    var Title: String
    var Notes: String
    var DeadlineIso: String
    var RemindAtIso: String
    var Reminded: Bool
    var Quadrant: Int
    var AiScore: Int
    var AiReason: String
    var Completed: Bool
    var CompletedAtIso: String
    var CreatedAtIso: String
    var UpdatedAtIso: String?
    var NoteId: String
    var AttachmentFiles: [String]

    var id: String { Id }

    init(
        Id: String = UUID().uuidString.replacingOccurrences(of: "-", with: "").prefix(12).lowercased(),
        Title: String = "",
        Notes: String = "",
        DeadlineIso: String = "",
        RemindAtIso: String = "",
        Reminded: Bool = false,
        Quadrant: Int = 2,
        AiScore: Int = 0,
        AiReason: String = "",
        Completed: Bool = false,
        CompletedAtIso: String = "",
        CreatedAtIso: String = DateHelper.nowIsoFull(),
        UpdatedAtIso: String? = DateHelper.nowIsoFull(),
        NoteId: String = "",
        AttachmentFiles: [String] = []
    ) {
        self.Id = Id
        self.Title = Title
        self.Notes = Notes
        self.DeadlineIso = DeadlineIso
        self.RemindAtIso = RemindAtIso
        self.Reminded = Reminded
        self.Quadrant = Quadrant
        self.AiScore = AiScore
        self.AiReason = AiReason
        self.Completed = Completed
        self.CompletedAtIso = CompletedAtIso
        self.CreatedAtIso = CreatedAtIso
        self.UpdatedAtIso = UpdatedAtIso
        self.NoteId = NoteId
        self.AttachmentFiles = AttachmentFiles
    }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        self.Id = try c.decodeIfPresent(String.self, forKey: .Id) ?? UUID().uuidString
        self.Title = try c.decodeIfPresent(String.self, forKey: .Title) ?? ""
        self.Notes = try c.decodeIfPresent(String.self, forKey: .Notes) ?? ""
        self.DeadlineIso = try c.decodeIfPresent(String.self, forKey: .DeadlineIso) ?? ""
        self.RemindAtIso = try c.decodeIfPresent(String.self, forKey: .RemindAtIso) ?? ""
        self.Reminded = try c.decodeIfPresent(Bool.self, forKey: .Reminded) ?? false
        self.Quadrant = try c.decodeIfPresent(Int.self, forKey: .Quadrant) ?? 0
        self.AiScore = try c.decodeIfPresent(Int.self, forKey: .AiScore) ?? 0
        self.AiReason = try c.decodeIfPresent(String.self, forKey: .AiReason) ?? ""
        self.Completed = try c.decodeIfPresent(Bool.self, forKey: .Completed) ?? false
        self.CompletedAtIso = try c.decodeIfPresent(String.self, forKey: .CompletedAtIso) ?? ""
        self.CreatedAtIso = try c.decodeIfPresent(String.self, forKey: .CreatedAtIso) ?? ""
        self.UpdatedAtIso = try c.decodeIfPresent(String.self, forKey: .UpdatedAtIso)
        self.NoteId = try c.decodeIfPresent(String.self, forKey: .NoteId) ?? ""
        self.AttachmentFiles = try c.decodeIfPresent([String].self, forKey: .AttachmentFiles) ?? []
    }
}

struct StickyNote: Identifiable, Codable, Equatable {
    var Id: String
    var Title: String
    var Content: String
    var ShowScratchpad: Bool
    var FilterMode: String
    var Theme: String
    var Opacity: Double
    var GlassTransparency: Double
    var FontSize: Double
    var Left: Double
    var Top: Double
    var Width: Double
    var Height: Double
    var PinMode: String
    var Locked: Bool
    var Collapsed: Bool
    var Visible: Bool
    var UpdatedAtIso: String?

    var id: String { Id }

    init(
        Id: String = String(UUID().uuidString.replacingOccurrences(of: "-", with: "").prefix(12)).lowercased(),
        Title: String = "随手便签",
        Content: String = "",
        ShowScratchpad: Bool = false,
        FilterMode: String = "custom",
        Theme: String = "claude_warm",
        Opacity: Double = 0.96,
        GlassTransparency: Double = 0.96,
        FontSize: Double = 13,
        Left: Double = 60,
        Top: Double = 60,
        Width: Double = 320,
        Height: Double = 360,
        PinMode: String = "desktop",
        Locked: Bool = false,
        Collapsed: Bool = false,
        Visible: Bool = true,
        UpdatedAtIso: String? = DateHelper.nowIsoFull()
    ) {
        self.Id = Id
        self.Title = Title
        self.Content = Content
        self.ShowScratchpad = ShowScratchpad
        self.FilterMode = FilterMode
        self.Theme = Theme
        self.Opacity = Opacity
        self.GlassTransparency = GlassTransparency
        self.FontSize = FontSize
        self.Left = Left
        self.Top = Top
        self.Width = Width
        self.Height = Height
        self.PinMode = PinMode
        self.Locked = Locked
        self.Collapsed = Collapsed
        self.Visible = Visible
        self.UpdatedAtIso = UpdatedAtIso
    }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        self.Id = try c.decodeIfPresent(String.self, forKey: .Id) ?? UUID().uuidString
        self.Title = try c.decodeIfPresent(String.self, forKey: .Title) ?? "桌面便签"
        self.Content = try c.decodeIfPresent(String.self, forKey: .Content) ?? ""
        self.ShowScratchpad = try c.decodeIfPresent(Bool.self, forKey: .ShowScratchpad) ?? false
        self.FilterMode = try c.decodeIfPresent(String.self, forKey: .FilterMode) ?? "custom"
        self.Theme = try c.decodeIfPresent(String.self, forKey: .Theme) ?? "claude_warm"
        self.Opacity = try c.decodeIfPresent(Double.self, forKey: .Opacity) ?? 0.96
        self.GlassTransparency = try c.decodeIfPresent(Double.self, forKey: .GlassTransparency) ?? 0.96
        self.FontSize = try c.decodeIfPresent(Double.self, forKey: .FontSize) ?? 13
        self.Left = try c.decodeIfPresent(Double.self, forKey: .Left) ?? 60
        self.Top = try c.decodeIfPresent(Double.self, forKey: .Top) ?? 60
        self.Width = try c.decodeIfPresent(Double.self, forKey: .Width) ?? 320
        self.Height = try c.decodeIfPresent(Double.self, forKey: .Height) ?? 360
        self.PinMode = try c.decodeIfPresent(String.self, forKey: .PinMode) ?? "desktop"
        self.Locked = try c.decodeIfPresent(Bool.self, forKey: .Locked) ?? false
        self.Collapsed = try c.decodeIfPresent(Bool.self, forKey: .Collapsed) ?? false
        self.Visible = try c.decodeIfPresent(Bool.self, forKey: .Visible) ?? true
        self.UpdatedAtIso = try c.decodeIfPresent(String.self, forKey: .UpdatedAtIso)
    }

    var filterLabel: String {
        switch FilterMode {
        case "all": return "全部日程"
        case "timetable": return "今日课表"
        case "q1": return "Q1 重要紧急"
        case "q2": return "Q2 重要不紧急"
        case "q3": return "Q3 紧急不重要"
        case "q4": return "Q4 不重要不紧急"
        default: return "独立便签"
        }
    }
}

struct SectionTimeSlot: Identifiable, Codable, Equatable {
    var Section: Int
    var StartTime: String
    var EndTime: String

    var id: Int { Section }

    init(Section: Int, StartTime: String, EndTime: String) {
        self.Section = Section
        self.StartTime = StartTime
        self.EndTime = EndTime
    }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        self.Section = try c.decodeIfPresent(Int.self, forKey: .Section) ?? 1
        self.StartTime = try c.decodeIfPresent(String.self, forKey: .StartTime) ?? ""
        self.EndTime = try c.decodeIfPresent(String.self, forKey: .EndTime) ?? ""
    }
}

struct CourseItem: Identifiable, Codable, Equatable {
    var Id: String
    var Name: String
    var Teacher: String
    var Location: String
    var DayOfWeek: Int
    var StartSection: Int
    var EndSection: Int
    var StartTime: String
    var EndTime: String
    var StartWeek: Int
    var EndWeek: Int
    var WeekType: String
    var CustomWeeks: [Int]
    var ExcludedWeeks: [Int]
    var ColorHex: String
    var UpdatedAtIso: String?

    var id: String { Id }

    init(
        Id: String = String(UUID().uuidString.replacingOccurrences(of: "-", with: "").prefix(12)).lowercased(),
        Name: String = "",
        Teacher: String = "",
        Location: String = "",
        DayOfWeek: Int = 1,
        StartSection: Int = 1,
        EndSection: Int = 2,
        StartTime: String = "",
        EndTime: String = "",
        StartWeek: Int = 1,
        EndWeek: Int = 16,
        WeekType: String = "all",
        CustomWeeks: [Int] = [],
        ExcludedWeeks: [Int] = [],
        ColorHex: String = "#C96442",
        UpdatedAtIso: String? = DateHelper.nowIsoFull()
    ) {
        self.Id = Id
        self.Name = Name
        self.Teacher = Teacher
        self.Location = Location
        self.DayOfWeek = DayOfWeek
        self.StartSection = StartSection
        self.EndSection = EndSection
        self.StartTime = StartTime
        self.EndTime = EndTime
        self.StartWeek = StartWeek
        self.EndWeek = EndWeek
        self.WeekType = WeekType
        self.CustomWeeks = CustomWeeks
        self.ExcludedWeeks = ExcludedWeeks
        self.ColorHex = ColorHex
        self.UpdatedAtIso = UpdatedAtIso
    }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        self.Id = try c.decodeIfPresent(String.self, forKey: .Id) ?? UUID().uuidString
        self.Name = try c.decodeIfPresent(String.self, forKey: .Name) ?? ""
        self.Teacher = try c.decodeIfPresent(String.self, forKey: .Teacher) ?? ""
        self.Location = try c.decodeIfPresent(String.self, forKey: .Location) ?? ""
        self.DayOfWeek = try c.decodeIfPresent(Int.self, forKey: .DayOfWeek) ?? 1
        self.StartSection = try c.decodeIfPresent(Int.self, forKey: .StartSection) ?? 1
        self.EndSection = try c.decodeIfPresent(Int.self, forKey: .EndSection) ?? 2
        self.StartTime = try c.decodeIfPresent(String.self, forKey: .StartTime) ?? ""
        self.EndTime = try c.decodeIfPresent(String.self, forKey: .EndTime) ?? ""
        self.StartWeek = try c.decodeIfPresent(Int.self, forKey: .StartWeek) ?? 1
        self.EndWeek = try c.decodeIfPresent(Int.self, forKey: .EndWeek) ?? 16
        self.WeekType = try c.decodeIfPresent(String.self, forKey: .WeekType) ?? "all"
        self.CustomWeeks = try c.decodeIfPresent([Int].self, forKey: .CustomWeeks) ?? []
        self.ExcludedWeeks = try c.decodeIfPresent([Int].self, forKey: .ExcludedWeeks) ?? []
        self.ColorHex = try c.decodeIfPresent(String.self, forKey: .ColorHex) ?? "#C96442"
        self.UpdatedAtIso = try c.decodeIfPresent(String.self, forKey: .UpdatedAtIso)
    }

    func occursInWeek(_ week: Int) -> Bool {
        if week < 1 { return false }
        if ExcludedWeeks.contains(week) { return false }
        if !CustomWeeks.isEmpty {
            return CustomWeeks.contains(week)
        }
        let sw = max(1, StartWeek)
        let ew = max(sw, EndWeek)
        if week < sw || week > ew { return false }
        let wt = WeekType.lowercased()
        if wt == "odd" { return week % 2 == 1 }
        if wt == "even" { return week % 2 == 0 }
        return true
    }

    func effectiveStartTime(using slots: [SectionTimeSlot]) -> String {
        let explicit = StartTime.trimmingCharacters(in: .whitespacesAndNewlines)
        if !explicit.isEmpty { return explicit }
        let table = slots.isEmpty ? TimetableData.defaultSectionTimes : slots
        if let found = table.first(where: { $0.Section == StartSection }), !found.StartTime.isEmpty {
            return found.StartTime
        }
        return TimetableData.defaultStartTime(for: StartSection)
    }

    func effectiveEndTime(using slots: [SectionTimeSlot]) -> String {
        let explicit = EndTime.trimmingCharacters(in: .whitespacesAndNewlines)
        if !explicit.isEmpty { return explicit }
        let target = max(StartSection, EndSection)
        let table = slots.isEmpty ? TimetableData.defaultSectionTimes : slots
        if let found = table.first(where: { $0.Section == target }), !found.EndTime.isEmpty {
            return found.EndTime
        }
        return TimetableData.defaultEndTime(for: target)
    }

    var sectionLabel: String {
        let s = max(1, StartSection)
        let e = max(s, EndSection)
        return s == e ? "第\(s)节" : "第\(s)-\(e)节"
    }

    var weeksLabel: String {
        if !CustomWeeks.isEmpty {
            return CustomWeeks.map(String.init).joined(separator: ",") + "周"
        }
        let suffix: String
        switch WeekType.lowercased() {
        case "odd": suffix = "(单)"
        case "even": suffix = "(双)"
        default: suffix = ""
        }
        if StartWeek == EndWeek {
            return "第\(StartWeek)周\(suffix)"
        }
        return "\(StartWeek)-\(EndWeek)周\(suffix)"
    }
}

struct TimetableData: Codable, Equatable {
    var SemesterName: String
    var SemesterStartMondayIso: String
    var TotalWeeks: Int
    var SectionsPerDay: Int
    var ShowWeekends: Bool
    var ShowTimeOnCard: Bool
    var ShowLocationOnCard: Bool
    var ShowTeacherOnCard: Bool
    var RowMinHeight: Double
    var Courses: [CourseItem]
    var SectionTimes: [SectionTimeSlot]
    var LastSyncIso: String

    static let defaultSectionTimes: [SectionTimeSlot] = [
        SectionTimeSlot(Section: 1, StartTime: "08:00", EndTime: "08:45"),
        SectionTimeSlot(Section: 2, StartTime: "08:55", EndTime: "09:40"),
        SectionTimeSlot(Section: 3, StartTime: "10:00", EndTime: "10:45"),
        SectionTimeSlot(Section: 4, StartTime: "10:55", EndTime: "11:40"),
        SectionTimeSlot(Section: 5, StartTime: "14:00", EndTime: "14:45"),
        SectionTimeSlot(Section: 6, StartTime: "14:55", EndTime: "15:40"),
        SectionTimeSlot(Section: 7, StartTime: "16:00", EndTime: "16:45"),
        SectionTimeSlot(Section: 8, StartTime: "16:55", EndTime: "17:40"),
        SectionTimeSlot(Section: 9, StartTime: "19:00", EndTime: "19:45"),
        SectionTimeSlot(Section: 10, StartTime: "19:55", EndTime: "20:40"),
        SectionTimeSlot(Section: 11, StartTime: "20:50", EndTime: "21:35"),
        SectionTimeSlot(Section: 12, StartTime: "21:40", EndTime: "22:25"),
        SectionTimeSlot(Section: 13, StartTime: "22:30", EndTime: "23:15"),
        SectionTimeSlot(Section: 14, StartTime: "23:20", EndTime: "23:55")
    ]

    static func defaultStartTime(for section: Int) -> String {
        defaultSectionTimes.first(where: { $0.Section == section })?.StartTime ?? "08:00"
    }

    static func defaultEndTime(for section: Int) -> String {
        defaultSectionTimes.first(where: { $0.Section == section })?.EndTime ?? "09:40"
    }

    init(
        SemesterName: String = "当前学期",
        SemesterStartMondayIso: String = DateHelper.mondayYmd(),
        TotalWeeks: Int = 20,
        SectionsPerDay: Int = 0,
        ShowWeekends: Bool = false,
        ShowTimeOnCard: Bool = true,
        ShowLocationOnCard: Bool = true,
        ShowTeacherOnCard: Bool = false,
        RowMinHeight: Double = 52,
        Courses: [CourseItem] = [],
        SectionTimes: [SectionTimeSlot] = TimetableData.defaultSectionTimes,
        LastSyncIso: String = ""
    ) {
        self.SemesterName = SemesterName
        self.SemesterStartMondayIso = SemesterStartMondayIso
        self.TotalWeeks = TotalWeeks
        self.SectionsPerDay = SectionsPerDay
        self.ShowWeekends = ShowWeekends
        self.ShowTimeOnCard = ShowTimeOnCard
        self.ShowLocationOnCard = ShowLocationOnCard
        self.ShowTeacherOnCard = ShowTeacherOnCard
        self.RowMinHeight = RowMinHeight
        self.Courses = Courses
        self.SectionTimes = SectionTimes
        self.LastSyncIso = LastSyncIso
    }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        self.SemesterName = try c.decodeIfPresent(String.self, forKey: .SemesterName) ?? "当前学期"
        let startRaw = try c.decodeIfPresent(String.self, forKey: .SemesterStartMondayIso) ?? ""
        self.SemesterStartMondayIso = startRaw.isEmpty ? DateHelper.mondayYmd() : startRaw
        self.TotalWeeks = max(1, try c.decodeIfPresent(Int.self, forKey: .TotalWeeks) ?? 20)
        self.SectionsPerDay = try c.decodeIfPresent(Int.self, forKey: .SectionsPerDay) ?? 0
        self.ShowWeekends = try c.decodeIfPresent(Bool.self, forKey: .ShowWeekends) ?? false
        self.ShowTimeOnCard = try c.decodeIfPresent(Bool.self, forKey: .ShowTimeOnCard) ?? true
        self.ShowLocationOnCard = try c.decodeIfPresent(Bool.self, forKey: .ShowLocationOnCard) ?? true
        self.ShowTeacherOnCard = try c.decodeIfPresent(Bool.self, forKey: .ShowTeacherOnCard) ?? false
        self.RowMinHeight = try c.decodeIfPresent(Double.self, forKey: .RowMinHeight) ?? 52
        self.Courses = try c.decodeIfPresent([CourseItem].self, forKey: .Courses) ?? []
        let slots = try c.decodeIfPresent([SectionTimeSlot].self, forKey: .SectionTimes) ?? []
        self.SectionTimes = slots.isEmpty ? TimetableData.defaultSectionTimes : slots
        self.LastSyncIso = try c.decodeIfPresent(String.self, forKey: .LastSyncIso) ?? ""
    }

    func computeCurrentWeek(now: Date = Date()) -> Int {
        guard let start = DateHelper.parseIsoDate(SemesterStartMondayIso) else { return 1 }
        let cal = Calendar(identifier: .gregorian)
        let startDay = cal.startOfDay(for: start)
        let today = cal.startOfDay(for: now)
        let diffDays = cal.dateComponents([.day], from: startDay, to: today).day ?? 0
        let week = Int(floor(Double(diffDays) / 7.0)) + 1
        return max(1, min(max(1, TotalWeeks), week))
    }

    func courses(forWeek week: Int, dayOfWeek: Int) -> [CourseItem] {
        Courses
            .filter { $0.DayOfWeek == dayOfWeek && $0.occursInWeek(week) }
            .sorted {
                if $0.StartSection != $1.StartSection { return $0.StartSection < $1.StartSection }
                return $0.effectiveStartTime(using: SectionTimes) < $1.effectiveStartTime(using: SectionTimes)
            }
    }

    func todayCourses(now: Date = Date()) -> [CourseItem] {
        let week = computeCurrentWeek(now: now)
        let day = DateHelper.isoWeekday(for: now)
        return courses(forWeek: week, dayOfWeek: day)
    }
}

struct AppData: Codable, Equatable {
    var SchemaVersion: Int
    var Tasks: [MemoTask]
    var StickyNotes: [StickyNote]
    var Timetable: TimetableData
    var LastModifiedIso: String

    enum CodingKeys: String, CodingKey {
        case SchemaVersion
        case Tasks
        case StickyNotes
        case Notes
        case Timetable
        case LastModifiedIso
    }

    init(
        SchemaVersion: Int = 3,
        Tasks: [MemoTask] = [],
        StickyNotes: [StickyNote] = [],
        Timetable: TimetableData = TimetableData(),
        LastModifiedIso: String = DateHelper.nowIsoLocal()
    ) {
        self.SchemaVersion = SchemaVersion
        self.Tasks = Tasks
        self.StickyNotes = StickyNotes
        self.Timetable = Timetable
        self.LastModifiedIso = LastModifiedIso
    }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        self.SchemaVersion = try c.decodeIfPresent(Int.self, forKey: .SchemaVersion) ?? 3
        self.Tasks = try c.decodeIfPresent([MemoTask].self, forKey: .Tasks) ?? []
        if let sticky = try c.decodeIfPresent([StickyNote].self, forKey: .StickyNotes) {
            self.StickyNotes = sticky
        } else {
            self.StickyNotes = try c.decodeIfPresent([StickyNote].self, forKey: .Notes) ?? []
        }
        self.Timetable = try c.decodeIfPresent(TimetableData.self, forKey: .Timetable) ?? TimetableData()
        self.LastModifiedIso = try c.decodeIfPresent(String.self, forKey: .LastModifiedIso) ?? DateHelper.nowIsoLocal()
    }

    func encode(to encoder: Encoder) throws {
        var c = encoder.container(keyedBy: CodingKeys.self)
        try c.encode(SchemaVersion, forKey: .SchemaVersion)
        try c.encode(Tasks, forKey: .Tasks)
        try c.encode(StickyNotes, forKey: .StickyNotes)
        try c.encode(Timetable, forKey: .Timetable)
        try c.encode(LastModifiedIso, forKey: .LastModifiedIso)
    }
}

struct SyncTombstones: Codable, Equatable {
    var Tasks: [String: Int64]
    var StickyNotes: [String: Int64]
    var Courses: [String: Int64]

    enum CodingKeys: String, CodingKey {
        case Tasks
        case StickyNotes
        case Courses
    }

    init(
        Tasks: [String: Int64] = [:],
        StickyNotes: [String: Int64] = [:],
        Courses: [String: Int64] = [:]
    ) {
        self.Tasks = Tasks
        self.StickyNotes = StickyNotes
        self.Courses = Courses
    }

    init(from decoder: Decoder) throws {
        // Gracefully ignore non-dictionary payloads
        guard let c = try? decoder.container(keyedBy: CodingKeys.self) else {
            self.Tasks = [:]
            self.StickyNotes = [:]
            self.Courses = [:]
            return
        }
        self.Tasks = Self.decodeTimestampMap(container: c, key: .Tasks)
        self.StickyNotes = Self.decodeTimestampMap(container: c, key: .StickyNotes)
        self.Courses = Self.decodeTimestampMap(container: c, key: .Courses)
    }

    private static func decodeTimestampMap(
        container: KeyedDecodingContainer<CodingKeys>,
        key: CodingKeys
    ) -> [String: Int64] {
        if let intMap = try? container.decodeIfPresent([String: Int64].self, forKey: key) {
            return intMap
        }
        if let doubleMap = try? container.decodeIfPresent([String: Double].self, forKey: key) {
            return doubleMap.mapValues { Int64($0) }
        }
        return [:]
    }
}

struct AuthUser: Codable, Equatable {
    var id: String
    var email: String
    var nickname: String
    var isAdmin: Bool

    init(id: String = "", email: String = "", nickname: String = "", isAdmin: Bool = false) {
        self.id = id
        self.email = email
        self.nickname = nickname
        self.isAdmin = isAdmin
    }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        self.id = try c.decodeIfPresent(String.self, forKey: .id) ?? ""
        self.email = try c.decodeIfPresent(String.self, forKey: .email) ?? ""
        let rawNick = try c.decodeIfPresent(String.self, forKey: .nickname) ?? ""
        self.nickname = rawNick.isEmpty ? (self.email.split(separator: "@").first.map(String.init) ?? "用户") : rawNick
        self.isAdmin = try c.decodeIfPresent(Bool.self, forKey: .isAdmin) ?? false
    }
}

struct CloudAiConfig: Codable, Equatable {
    static let keepExistingSentinel = "__KEEP_EXISTING__"

    var AiProviderPreset: String
    var AiBaseUrl: String
    var AiApiKey: String
    var AiModel: String
    var AiCustomInstruction: String
    var ShowTodayCoursesInQuadrants: Bool
    var SortBy: String
    var PanelTheme: String

    init(
        AiProviderPreset: String = "deepseek_flash",
        AiBaseUrl: String = "https://api.deepseek.com/v1",
        AiApiKey: String = "",
        AiModel: String = "deepseek-flash",
        AiCustomInstruction: String = "",
        ShowTodayCoursesInQuadrants: Bool = true,
        SortBy: String = "ai",
        PanelTheme: String = "claude_warm"
    ) {
        self.AiProviderPreset = AiProviderPreset
        self.AiBaseUrl = AiBaseUrl
        self.AiApiKey = AiApiKey
        self.AiModel = AiModel
        self.AiCustomInstruction = AiCustomInstruction
        self.ShowTodayCoursesInQuadrants = ShowTodayCoursesInQuadrants
        self.SortBy = SortBy
        self.PanelTheme = PanelTheme
    }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        self.AiProviderPreset = try c.decodeIfPresent(String.self, forKey: .AiProviderPreset) ?? "deepseek_flash"
        self.AiBaseUrl = try c.decodeIfPresent(String.self, forKey: .AiBaseUrl) ?? "https://api.deepseek.com/v1"
        self.AiApiKey = try c.decodeIfPresent(String.self, forKey: .AiApiKey) ?? ""
        self.AiModel = try c.decodeIfPresent(String.self, forKey: .AiModel) ?? "deepseek-flash"
        self.AiCustomInstruction = try c.decodeIfPresent(String.self, forKey: .AiCustomInstruction) ?? ""
        self.ShowTodayCoursesInQuadrants = try c.decodeIfPresent(Bool.self, forKey: .ShowTodayCoursesInQuadrants) ?? true
        self.SortBy = try c.decodeIfPresent(String.self, forKey: .SortBy) ?? "ai"
        self.PanelTheme = try c.decodeIfPresent(String.self, forKey: .PanelTheme) ?? "claude_warm"
    }
}

struct ExtractedCandidate: Identifiable, Codable, Equatable {
    var id: UUID = UUID()
    var title: String
    var notes: String
    var deadlineIso: String
    var quadrant: Int
    var aiReason: String
    var selected: Bool = true

    enum CodingKeys: String, CodingKey {
        case title
        case notes
        case deadlineIso
        case quadrant
        case aiReason
    }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        self.id = UUID()
        self.title = try c.decodeIfPresent(String.self, forKey: .title) ?? ""
        self.notes = try c.decodeIfPresent(String.self, forKey: .notes) ?? ""
        self.deadlineIso = try c.decodeIfPresent(String.self, forKey: .deadlineIso) ?? ""
        self.quadrant = try c.decodeIfPresent(Int.self, forKey: .quadrant) ?? 2
        self.aiReason = try c.decodeIfPresent(String.self, forKey: .aiReason) ?? ""
        self.selected = true
    }
}

struct AiSortAssignment: Codable, Equatable {
    var id: String
    var quadrant: Int
    var aiScore: Int
    var aiReason: String

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        self.id = try c.decodeIfPresent(String.self, forKey: .id) ?? ""
        self.quadrant = try c.decodeIfPresent(Int.self, forKey: .quadrant) ?? 2
        self.aiScore = try c.decodeIfPresent(Int.self, forKey: .aiScore) ?? 60
        self.aiReason = try c.decodeIfPresent(String.self, forKey: .aiReason) ?? "AI 已分类"
    }
}
