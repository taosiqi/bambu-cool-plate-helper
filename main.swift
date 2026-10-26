import AppKit

let fm = FileManager.default
let keys = ["supertack_plate_temp", "supertack_plate_temp_initial_layer"]
func presetNames(in directory: URL) throws -> [String] {
    guard fm.fileExists(atPath: directory.path) else { return [] }
    return try fm.contentsOfDirectory(at: directory, includingPropertiesForKeys: [.isRegularFileKey])
        .filter { $0.lastPathComponent.hasPrefix("Bambu PETG Basic @") && $0.pathExtension == "json"
            && (try? $0.resourceValues(forKeys: [.isRegularFileKey]).isRegularFile) == true }
        .map { $0.lastPathComponent }.sorted()
}
struct Failure: LocalizedError {
    let message: String
    var errorDescription: String? { message }
}
func read(_ url: URL) throws -> [String: Any] {
    guard let obj = try JSONSerialization.jsonObject(with: Data(contentsOf: url)) as? [String: Any] else {
        throw Failure(message: "配置不是有效的 JSON 对象：\(url.lastPathComponent)")
    }
    return obj
}
func write(_ obj: [String: Any], _ url: URL) throws {
    let data = try JSONSerialization.data(withJSONObject: obj, options: [.prettyPrinted, .sortedKeys, .withoutEscapingSlashes])
    try (data + Data([10])).write(to: url, options: .atomic)
}
func temperatureValue(_ text: String) throws -> Int {
    guard let value = Int(text.trimmingCharacters(in: .whitespacesAndNewlines)), (1...120).contains(value) else {
        throw Failure(message: "请输入 1–120 之间的整数温度（℃）。")
    }
    return value
}
func apply(root: URL, backups: URL, temperature: Int = 40) throws -> Int {
    let wanted = [String(try temperatureValue(String(temperature)))]
    let names = try presetNames(in: root)
    // Validate every target before writing any file.
    var edits: [(URL, [String: Any])] = []
    for name in names {
        let url = root.appendingPathComponent(name)
        guard fm.fileExists(atPath: url.path) else { continue }
        var obj = try read(url)
        guard obj["name"] as? String == String(name.dropLast(5)) else {
            throw Failure(message: "配置名称不匹配：\(name)")
        }
        if keys.allSatisfy({ obj[$0] as? [String] == wanted }) { continue }
        for key in keys { obj[key] = wanted }
        edits.append((url, obj))
    }
    guard names.contains(where: { fm.fileExists(atPath: root.appendingPathComponent($0).path) }) else {
        throw Failure(message: "没有找到官方 PETG Basic 配置。请先在 Bambu Studio 完成官方配置更新。")
    }
    if edits.isEmpty { return 0 }
    let backup = backups.appendingPathComponent("\(Int(Date().timeIntervalSince1970))-\(UUID().uuidString)")
    try fm.createDirectory(at: backup, withIntermediateDirectories: true)
    for (url, _) in edits { try fm.copyItem(at: url, to: backup.appendingPathComponent(url.lastPathComponent)) }
    let initial = backups.appendingPathComponent("initial")
    try fm.createDirectory(at: initial, withIntermediateDirectories: true)
    for (url, _) in edits {
        let target = initial.appendingPathComponent(url.lastPathComponent)
        if !fm.fileExists(atPath: target.path) { try fm.copyItem(at: url, to: target) }
    }
    do {
        for (url, obj) in edits { try write(obj, url) }
    } catch {
        var failures: [String] = []
        for (url, _) in edits {
            do { try Data(contentsOf: backup.appendingPathComponent(url.lastPathComponent)).write(to: url, options: .atomic) }
            catch { failures.append(url.lastPathComponent) }
        }
        throw Failure(message: "修改失败：\(error.localizedDescription)\n" + (failures.isEmpty ? "本次修改已回滚。" : "部分回滚失败，请从备份恢复：\(backup.path)"))
    }
    return edits.count
}
func restore(root: URL, backups: URL) throws -> Int {
    let backup = backups.appendingPathComponent("initial")
    var edits: [(URL, [String: Any], Data)] = []
    for name in try presetNames(in: backup) {
        let original = try read(backup.appendingPathComponent(name))
        let url = root.appendingPathComponent(name)
        var current = try read(url)
        guard current["name"] as? String == String(name.dropLast(5)), current["name"] as? String == original["name"] as? String else { throw Failure(message: "配置身份已变化，停止恢复：\(name)") }
        for key in keys {
            current[key] = original[key]
        }
        edits.append((url, current, try Data(contentsOf: url)))
    }
    guard !edits.isEmpty else { throw Failure(message: "备份中没有目标配置。") }
    do {
        for (url, obj, _) in edits { try write(obj, url) }
    } catch {
        var failed = false
        for (url, _, data) in edits {
            do { try data.write(to: url, options: .atomic) } catch { failed = true }
        }
        throw Failure(message: "恢复失败：\(error.localizedDescription)\n\(failed ? "部分回滚失败，请检查备份。" : "本次恢复已回滚。")")
    }
    return edits.count
}

func isStudio(bundleID: String?, executableName: String?) -> Bool {
    return bundleID == "com.bambulab.bambu-studio" || executableName == "BambuStudio"
}
func selfTest() throws {
    assert(isStudio(bundleID: "com.bambulab.bambu-studio", executableName: nil))
    assert(isStudio(bundleID: nil, executableName: "BambuStudio"))
    assert(!isStudio(bundleID: "local.taosiqi.BambuCoolPlateHelper", executableName: "BambuCoolPlateHelper"))
    assert(!isStudio(bundleID: nil, executableName: nil))
    let temp = fm.temporaryDirectory.appendingPathComponent(UUID().uuidString)
    defer { try? fm.removeItem(at: temp) }
    let root = temp.appendingPathComponent("filament")
    let backup = temp.appendingPathComponent("backup")
    try fm.createDirectory(at: root, withIntermediateDirectories: true)
    let names = ["Bambu PETG Basic @BBL P2S 0.4 nozzle.json", "Bambu PETG Basic @BBL A1M 0.4 nozzle.json"]
    let p = root.appendingPathComponent(names[0])
    let a = root.appendingPathComponent(names[1])
    try write(["name": String(names[0].dropLast(5)), keys[0]: ["60"], keys[1]: ["60"], "other": "keep"], p)
    try write(["name": String(names[1].dropLast(5)), "inherits": "base"], a)
    let historical = backup.appendingPathComponent("1000000000-00000000-0000-0000-0000-000000000001")
    try fm.createDirectory(at: historical, withIntermediateDirectories: true)
    try write(["name": String(names[0].dropLast(5)), keys[0]: ["99"]], historical.appendingPathComponent(names[0]))
    do { _ = try restore(root: root, backups: backup); assertionFailure("History must not be imported") } catch { }
    let changed = try apply(root: root, backups: backup)
    assert(changed == 2)
    let aAfter = try read(a)
    assert(aAfter[keys[0]] as? [String] == ["40"])
    let again = try apply(root: root, backups: backup)
    assert(again == 0)
    var pAfter = try read(p)
    pAfter["other"] = "updated"
    try write(pAfter, p)
    let restored = try restore(root: root, backups: backup)
    assert(restored == 2)
    let oldP = try read(p), oldA = try read(a)
    assert(oldP[keys[0]] as? [String] == ["60"] && oldP["other"] as? String == "updated")
    assert(oldA[keys[0]] == nil && oldA[keys[1]] == nil)
    _ = try apply(root: root, backups: backup, temperature: 45)
    let custom = try read(p)
    assert(custom[keys[0]] as? [String] == ["45"] && custom[keys[1]] as? [String] == ["45"])
    let repeatCustom = try apply(root: root, backups: backup, temperature: 45)
    assert(repeatCustom == 0)
    // Repeated edits preserve the first baseline, without scanning historical snapshots.
    _ = try restore(root: root, backups: backup)
    _ = try restore(root: root, backups: backup)
    let earliestP = try read(p), earliestA = try read(a)
    assert(earliestP[keys[0]] as? [String] == ["60"] && earliestP["other"] as? String == "updated")
    assert(earliestA[keys[0]] == nil)
    for invalid in ["", "abc", "40.5", "0", "121"] {
        do { _ = try temperatureValue(invalid); assertionFailure("Invalid temperature accepted") } catch { }
    }
    let valid = try temperatureValue(" 45 ")
    assert(valid == 45)
    do { _ = try apply(root: root, backups: backup, temperature: 121); assertionFailure("Range check missing") } catch { }
    let untouched = try read(p)
    assert(untouched[keys[0]] as? [String] == ["60"])
    let extraNames = ["Bambu PETG Basic @BBL X1C.json", "Bambu PETG Basic @BBL H2D 0.6 nozzle.json", "Bambu PETG Basic @base.json", "Bambu PETG Basic @BBL Future 1.0 nozzle.json"]
    for name in extraNames {
        try write(["name": String(name.dropLast(5)), keys[0]: ["70"], keys[1]: ["70"]], root.appendingPathComponent(name))
    }
    let unrelated = root.appendingPathComponent("Bambu PETG HF @BBL P2S.json")
    try write(["name": "Bambu PETG HF @BBL P2S", keys[0]: ["80"]], unrelated)
    let allModels = try apply(root: root, backups: backup, temperature: 50)
    assert(allModels == 6)
    let untouchedHF = try read(unrelated)
    assert(untouchedHF[keys[0]] as? [String] == ["80"])
    for name in extraNames {
        let obj = try read(root.appendingPathComponent(name))
        assert(obj[keys[0]] as? [String] == ["50"])
    }
    _ = try restore(root: root, backups: backup)
    for name in extraNames {
        let obj = try read(root.appendingPathComponent(name))
        assert(obj[keys[0]] as? [String] == ["70"])
    }
    try Data("invalid JSON".utf8).write(to: a)
    do { _ = try apply(root: root, backups: backup); assertionFailure("Malformed JSON accepted") } catch { }
    let afterInvalidJSON = try read(p)
    assert(afterInvalidJSON[keys[0]] as? [String] == ["60"])
    print("PASS: all models, new/future presets, base preset, exclude PETG HF, custom temperature, range/input validation, idempotence, immutable original backup, default restore, inheritance, unrelated edits, malformed JSON, process identity")
}

if CommandLine.arguments.contains("--self-test") {
    do { try selfTest() } catch { fputs("\(error)\n", stderr); exit(1) }
    exit(0)
}

final class LinkButton: NSButton {
    let destination: URL
    init(title: String, url: String) {
        destination = URL(string: url)!
        super.init(frame: .zero)
        isBordered = false
        alignment = .left
        attributedTitle = NSAttributedString(string: title, attributes: [
            .foregroundColor: NSColor.linkColor, .font: NSFont.systemFont(ofSize: 13),
            .underlineStyle: NSUnderlineStyle.single.rawValue])
        target = self
        action = #selector(openDestination)
        toolTip = destination.absoluteString
        setAccessibilityLabel(title)
    }
    required init?(coder: NSCoder) { fatalError("Not used") }
    override func resetCursorRects() {
        super.resetCursorRects()
        addCursorRect(bounds, cursor: .pointingHand)
    }
    @objc private func openDestination() { NSWorkspace.shared.open(destination) }
}

let app = NSApplication.shared
app.setActivationPolicy(.regular)
app.activate(ignoringOtherApps: true)
let support = fm.homeDirectoryForCurrentUser.appendingPathComponent("Library/Application Support")
let root = support.appendingPathComponent("BambuStudio/system/BBL/filament")
let backups = support.appendingPathComponent("BambuCoolPlateHelper/Backups")
let temperatureInput = NSTextField(string: "40")
func alert(_ title: String, _ message: String, buttons: [String] = ["好"]) -> NSApplication.ModalResponse {
    let a = NSAlert()
    a.messageText = title
    a.informativeText = message
    for button in buttons { a.addButton(withTitle: button) }
    if title == "PETG 冷打板助手" {
        let links = NSStackView()
        links.orientation = .vertical
        links.alignment = .leading
        links.spacing = 12
        temperatureInput.widthAnchor.constraint(equalToConstant: 90).isActive = true
        temperatureInput.setAccessibilityLabel("热床温度，摄氏度")
        let row = NSStackView(views: [NSTextField(labelWithString: "热床温度"), temperatureInput, NSTextField(labelWithString: "℃（1–120，整数）")])
        row.orientation = .horizontal
        row.spacing = 8
        links.addArrangedSubview(row)
        for (title, address) in [
            ("支持作者 · 去 MakerWorld 主页帮忙助力 ↗", "https://makerworld.com.cn/@siqijson?appSharePlatform=copy"),
            ("GitHub · 源码与新版下载 ↗", "https://github.com/taosiqi/bambu-cool-plate-helper")
        ] {
            let link = LinkButton(title: title, url: address)
            links.addArrangedSubview(link)
        }
        links.frame = NSRect(x: 0, y: 0, width: 400, height: 98)
        a.accessoryView = links
    }
    return a.runModal()
}
while true {
    let answer = alert("PETG 冷打板助手", "一键修改官方 Bambu PETG Basic 预设，所有引用它的颜色共用。\n\n机器与喷嘴：本机官方 PETG Basic 的全部预设\n打印板：低温增粘板 SuperTack\n首层和其他层使用相同温度，默认填入 40℃。\n\n请先保存项目并退出 Bambu Studio。修改前自动备份；官方配置更新后可再次应用。已有项目的独立覆盖需另行检查。\n\n恢复默认：还原各预设首次备份的温度字段（包括继承状态），其他参数保留。", buttons: ["应用温度", "恢复默认", "退出"])
    guard answer == .alertFirstButtonReturn || answer == .alertSecondButtonReturn else { break }
    let running = NSWorkspace.shared.runningApplications.contains {
        isStudio(bundleID: $0.bundleIdentifier, executableName: $0.executableURL?.lastPathComponent)
    }
    if running {
        _ = alert("请先退出 Bambu Studio", "请保存正在编辑的项目，再使用 ⌘Q 退出 Bambu Studio，然后重试。工具不会强制关闭或丢弃你的项目。")
        continue
    }
    do {
        let applying = answer == .alertFirstButtonReturn
        let count = try applying ? apply(root: root, backups: backups, temperature: temperatureValue(temperatureInput.stringValue)) : restore(root: root, backups: backups)
        _ = alert(count == 0 ? "已经是指定温度" : "\(applying ? "修改" : "恢复")完成", "\(count == 0 ? "无需重复修改。" : "已处理 \(count) 份官方耗材配置。")\n\n重新打开 Bambu Studio，检查低温增粘板的首层和其他层温度，然后重新切片。\n\n备份目录：\(backups.path)")
        break
    } catch { _ = alert("未完成", error.localizedDescription) }
}
