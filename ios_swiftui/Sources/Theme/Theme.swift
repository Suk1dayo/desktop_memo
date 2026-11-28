import SwiftUI
import UIKit

enum ThemePreference: String, CaseIterable, Identifiable {
    case system
    case light
    case dark

    var id: String { rawValue }

    var label: String {
        switch self {
        case .system: return "跟随系统"
        case .light: return "浅色"
        case .dark: return "深色"
        }
    }

    var colorScheme: ColorScheme? {
        switch self {
        case .system: return nil
        case .light: return .light
        case .dark: return .dark
        }
    }
}

enum Theme {
    private static func dynamic(light: UInt32, dark: UInt32) -> Color {
        Color(uiColor: UIColor { traits in
            let hex = traits.userInterfaceStyle == .dark ? dark : light
            return UIColor(
                red: CGFloat((hex >> 16) & 0xFF) / 255.0,
                green: CGFloat((hex >> 8) & 0xFF) / 255.0,
                blue: CGFloat(hex & 0xFF) / 255.0,
                alpha: 1.0
            )
        })
    }

    static let canvas = dynamic(light: 0xF6F4EF, dark: 0x171614)
    static let surface = dynamic(light: 0xFFFFFF, dark: 0x22201D)
    static let subtle = dynamic(light: 0xEDE9E0, dark: 0x2C2925)
    static let border = dynamic(light: 0xE4DFD4, dark: 0x34302B)
    static let ink = dynamic(light: 0x1F1E1C, dark: 0xF3F0E9)
    static let secondaryInk = dynamic(light: 0x6B665E, dark: 0xABA59B)
    static let accent = dynamic(light: 0xC96442, dark: 0xDF7B58)
    static let danger = dynamic(light: 0xC94736, dark: 0xE86B58)
    static let warning = dynamic(light: 0xB97E2E, dark: 0xD69B47)
    static let success = dynamic(light: 0x2F7D56, dark: 0x52A67A)

    static let coursePalette: [String] = [
        "#C96442",
        "#B97E2E",
        "#2F7D56",
        "#3B6E8C",
        "#6D5B97",
        "#B3506B",
        "#4B7F78",
        "#7A6855"
    ]

    static func quadrantColor(_ q: Int) -> Color {
        switch q {
        case 1: return dynamic(light: 0xC9503B, dark: 0xE56B55)
        case 2: return dynamic(light: 0xC96442, dark: 0xDF7B58)
        case 3: return dynamic(light: 0xB97E2E, dark: 0xD69B47)
        case 4: return dynamic(light: 0x5C7A67, dark: 0x7EA18B)
        default: return dynamic(light: 0x78716C, dark: 0xA8A29E)
        }
    }

    static func quadrantShortName(_ q: Int) -> String {
        switch q {
        case 1: return "Q1 立刻做"
        case 2: return "Q2 计划做"
        case 3: return "Q3 尽快办"
        case 4: return "Q4 稍后做"
        default: return "未分类"
        }
    }

    static func quadrantFullName(_ q: Int) -> String {
        switch q {
        case 1: return "Q1 重要且紧急"
        case 2: return "Q2 重要不紧急"
        case 3: return "Q3 紧急不重要"
        case 4: return "Q4 不重要不紧急"
        default: return "未分类（独立待办）"
        }
    }
}

extension Color {
    init(hex: String) {
        let cleaned = hex.trimmingCharacters(in: CharacterSet.alphanumerics.inverted)
        var value: UInt64 = 0xC96442
        if cleaned.count == 6, let parsed = UInt64(cleaned, radix: 16) {
            value = parsed
        }
        let r = Double((value >> 16) & 0xFF) / 255.0
        let g = Double((value >> 8) & 0xFF) / 255.0
        let b = Double(value & 0xFF) / 255.0
        self.init(.sRGB, red: r, green: g, blue: b, opacity: 1.0)
    }
}

struct WarmCardModifier: ViewModifier {
    var padding: CGFloat = 14
    var cornerRadius: CGFloat = 16

    func body(content: Content) -> some View {
        content
            .padding(padding)
            .background(Theme.surface)
            .clipShape(RoundedRectangle(cornerRadius: cornerRadius, style: .continuous))
            .overlay(
                RoundedRectangle(cornerRadius: cornerRadius, style: .continuous)
                    .stroke(Theme.border, lineWidth: 1)
            )
    }
}

extension View {
    func warmCard(padding: CGFloat = 14, cornerRadius: CGFloat = 16) -> some View {
        modifier(WarmCardModifier(padding: padding, cornerRadius: cornerRadius))
    }
}
