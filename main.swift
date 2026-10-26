import AppKit

let fm = FileManager.default
let keys = ["supertack_plate_temp", "supertack_plate_temp_initial_layer"]
let wanted = ["40"]
let names = ["P2S", "A1M"].flatMap { model in
    ["0.2", "0.4", "0.6", "0.8"].map { "Bambu PETG Basic @BBL \(model) \($0) nozzle.json" }
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
// Keep the earliest available original for each preset, including backups from v1.0.1.
func initialBackup(_ backups: URL) throws -> URL {
    let initial = backups.appendingPathComponent("initial")
    try fm.createDirectory(at: initial, withIntermediateDirectories: true)
    let snapshots = try fm.contentsOfDirectory(at: backups, includingPropertiesForKeys: [.creationDateKey, .isDirectoryKey])
        .filter { url in
            let parts = url.lastPathComponent.split(separator: "-", maxSplits: 1)
            return parts.count == 2 && Int(parts[0]) != nil && UUID(uuidString: String(parts[1])) != nil
        }
        .sorted {
            let left = (try? $0.resourceValues(forKeys: [.creationDateKey]).creationDate) ?? .distantPast
            let right = (try? $1.resourceValues(forKeys: [.creationDateKey]).creationDate) ?? .distantPast
            return left == right ? $0.lastPathComponent < $1.lastPathComponent : left < right
        }
    for snapshot in snapshots {
        for name in names {
            let source = snapshot.appendingPathComponent(name)
            let target = initial.appendingPathComponent(name)
            if fm.fileExists(atPath: source.path) && !fm.fileExists(atPath: target.path) {
                _ = try read(source)
                try fm.copyItem(at: source, to: target)
            }
        }
    }
    return initial
}
func apply(root: URL, backups: URL) throws -> Int {
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
        throw Failure(message: "没有找到 P2S 或 A1 mini 的官方 PETG Basic 配置。请先在 Bambu Studio 添加机器并完成配置更新。")
    }
    if edits.isEmpty { return 0 }
    let backup = backups.appendingPathComponent("\(Int(Date().timeIntervalSince1970))-\(UUID().uuidString)")
    try fm.createDirectory(at: backup, withIntermediateDirectories: true)
    for (url, _) in edits { try fm.copyItem(at: url, to: backup.appendingPathComponent(url.lastPathComponent)) }
    _ = try initialBackup(backups)
    // Persist recovery pointer before changing files, including if an individual write fails.
    try fm.createDirectory(at: backups, withIntermediateDirectories: true)
    try Data(backup.lastPathComponent.utf8).write(to: backups.appendingPathComponent("latest"), options: .atomic)
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
func restore(root: URL, backups: URL, initial: Bool = false) throws -> Int {
    let pointer = backups.appendingPathComponent("latest")
    let backup: URL
    if initial {
        backup = try initialBackup(backups)
    } else {
        guard fm.fileExists(atPath: pointer.path) else { throw Failure(message: "还没有可恢复的备份。") }
        let folder = try String(contentsOf: pointer, encoding: .utf8)
        guard !folder.contains("/"), !folder.contains(".."), !folder.isEmpty else { throw Failure(message: "备份记录无效。") }
        backup = backups.appendingPathComponent(folder)
    }
    var edits: [(URL, [String: Any], Data)] = []
    for name in names where fm.fileExists(atPath: backup.appendingPathComponent(name).path) {
        let original = try read(backup.appendingPathComponent(name))
        let url = root.appendingPathComponent(name)
        var current = try read(url)
        guard current["name"] as? String == original["name"] as? String else { throw Failure(message: "配置身份已变化，停止恢复：\(name)") }
        for key in keys {
            // Do not overwrite a later temperature edit or a vendor update.
            let now = current[key] as? [String]
            let old = original[key] as? [String]
            guard initial || now == wanted || now == old else {
                throw Failure(message: "\(name) 的温度已被其他操作更改，停止恢复以保留新设置。备份位于：\(backup.path)")
            }
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
    if !initial { try fm.removeItem(at: pointer) }
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
    let p = root.appendingPathComponent(names[0])
    let a = root.appendingPathComponent(names[4])
    try write(["name": String(names[0].dropLast(5)), keys[0]: ["60"], keys[1]: ["60"], "other": "keep"], p)
    try write(["name": String(names[4].dropLast(5)), "inherits": "base"], a)
    let changed = try apply(root: root, backups: backup)
    assert(changed == 2)
    let aAfter = try read(a)
    assert(aAfter[keys[0]] as? [String] == wanted)
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
    _ = try apply(root: root, backups: backup)
    var conflict = try read(a)
    conflict[keys[0]] = ["55"]
    try write(conflict, a)
    do { _ = try restore(root: root, backups: backup); assertionFailure("Must reject conflicting temperature") }
    catch { }
    let unchangedP = try read(p)
    assert(unchangedP[keys[0]] as? [String] == wanted)
    // Rebuild the baseline from old snapshots, then ensure later snapshots never replace it.
    try fm.removeItem(at: backup.appendingPathComponent("initial"))
    _ = try restore(root: root, backups: backup, initial: true)
    let firstP = try read(p), firstA = try read(a)
    assert(firstP[keys[0]] as? [String] == ["60"] && firstP["other"] as? String == "updated")
    assert(firstA[keys[0]] == nil)
    var updated = firstP
    updated[keys[0]] = ["65"]
    updated[keys[1]] = ["65"]
    try write(updated, p)
    _ = try apply(root: root, backups: backup)
    _ = try restore(root: root, backups: backup)
    let lastP = try read(p)
    assert(lastP[keys[0]] as? [String] == ["65"])
    _ = try restore(root: root, backups: backup, initial: true)
    _ = try restore(root: root, backups: backup, initial: true)
    let earliestP = try read(p)
    assert(earliestP[keys[0]] as? [String] == ["60"])
    print("PASS: initial backup migration, immutable baseline, latest versus initial, repeat restore; explicit/inherited values, idempotence, restore, unrelated edits, conflicting restore")
}

if CommandLine.arguments.contains("--self-test") {
    do { try selfTest() } catch { fputs("\(error)\n", stderr); exit(1) }
    exit(0)
}

let app = NSApplication.shared
app.setActivationPolicy(.regular)
app.activate(ignoringOtherApps: true)
let support = fm.homeDirectoryForCurrentUser.appendingPathComponent("Library/Application Support")
let root = support.appendingPathComponent("BambuStudio/system/BBL/filament")
let backups = support.appendingPathComponent("BambuCoolPlateHelper/Backups")
func alert(_ title: String, _ message: String, buttons: [String] = ["好"]) -> NSApplication.ModalResponse {
    let a = NSAlert()
    a.messageText = title
    a.informativeText = message
    for button in buttons { a.addButton(withTitle: button) }
    if title == "PETG 冷打板 · 40℃" {
        let links = NSStackView()
        links.orientation = .vertical
        links.alignment = .leading
        links.spacing = 8
        for (title, address) in [
            ("支持作者 · 去 MakerWorld 主页帮忙助力 ↗", "https://makerworld.com.cn/@siqijson?appSharePlatform=copy"),
            ("GitHub · 源码与新版下载 ↗", "https://github.com/taosiqi/bambu-cool-plate-helper")
        ] {
            let link = NSTextField(labelWithAttributedString: NSAttributedString(
                string: title, attributes: [.link: URL(string: address)!, .font: NSFont.systemFont(ofSize: 13)]))
            link.isSelectable = true
            link.allowsEditingTextAttributes = true
            links.addArrangedSubview(link)
        }
        links.frame = NSRect(x: 0, y: 0, width: 380, height: 52)
        a.accessoryView = links
    }
    return a.runModal()
}
let answer = alert("PETG 冷打板 · 40℃", "一键修改官方 Bambu PETG Basic 预设，所有引用它的颜色共用。\n\n机器：P2S、A1 mini\n喷嘴：本机已有的 0.2 / 0.4 / 0.6 / 0.8\n打印板：低温增粘板 SuperTack\n温度：首层和其他层均为 40℃\n\n请先保存项目并退出 Bambu Studio。修改前自动备份；官方配置更新后可再次应用。已有项目的独立覆盖需另行检查。\n\n恢复最初备份：还原各预设首次备份的温度字段（包括继承状态），会覆盖后来设置的温度，其他参数保留。", buttons: ["一键设为 40℃", "恢复上次修改", "恢复最初备份", "退出"])
if [.alertFirstButtonReturn, .alertSecondButtonReturn, .alertThirdButtonReturn].contains(answer) {
    let running = NSWorkspace.shared.runningApplications.contains {
        isStudio(bundleID: $0.bundleIdentifier, executableName: $0.executableURL?.lastPathComponent)
    }
    if running {
        _ = alert("请先退出 Bambu Studio", "请保存正在编辑的项目，再使用 ⌘Q 退出 Bambu Studio，然后重新打开本工具。工具不会强制关闭或丢弃你的项目。")
    } else {
        do {
            let applying = answer == .alertFirstButtonReturn
            let count = try applying ? apply(root: root, backups: backups) : restore(root: root, backups: backups, initial: answer == .alertThirdButtonReturn)
            _ = alert(count == 0 ? "已经是 40℃" : "\(applying ? "修改" : "恢复")完成", "\(count == 0 ? "无需重复修改。" : "已处理 \(count) 份官方耗材配置。")\n\n重新打开 Bambu Studio，检查低温增粘板的首层和其他层温度，然后重新切片。\n\n备份目录：\(backups.path)")
        } catch { _ = alert("未完成", error.localizedDescription) }
    }
}
