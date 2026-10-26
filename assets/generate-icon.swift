import AppKit
let out = URL(fileURLWithPath: CommandLine.arguments.count > 1 ? CommandLine.arguments[1] : "assets")
let fm = FileManager.default
try fm.createDirectory(at: out.appendingPathComponent("AppIcon.iconset"), withIntermediateDirectories: true)
func draw(_ size: Int) -> Data {
    let bitmap = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: size, pixelsHigh: size, bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true, isPlanar: false, colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0)!
    NSGraphicsContext.saveGraphicsState()
    NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: bitmap)
    let context = NSGraphicsContext.current!.cgContext
    context.scaleBy(x: CGFloat(size)/1024, y: CGFloat(size)/1024)
    let shape = NSBezierPath(roundedRect: NSRect(x: 70, y: 70, width: 884, height: 884), xRadius: 195, yRadius: 195)
    NSGradient(starting: NSColor(calibratedRed: 0.06, green: 0.19, blue: 0.27, alpha: 1), ending: NSColor(calibratedRed: 0.08, green: 0.37, blue: 0.43, alpha: 1))!.draw(in: shape, angle: 70)
    let font = NSFont.systemFont(ofSize: 340, weight: .bold)
    let text = "40°" as NSString
    let attrs: [NSAttributedString.Key: Any] = [.font: font, .foregroundColor: NSColor.white]
    let bounds = text.size(withAttributes: attrs)
    text.draw(at: NSPoint(x: (1024-bounds.width)/2, y: 400), withAttributes: attrs)
    let plate = NSBezierPath()
    plate.move(to: NSPoint(x: 240, y: 315)); plate.line(to: NSPoint(x: 510, y: 390)); plate.line(to: NSPoint(x: 784, y: 315)); plate.line(to: NSPoint(x: 510, y: 235)); plate.close()
    NSColor(calibratedRed: 0.40, green: 0.93, blue: 0.85, alpha: 1).setFill(); plate.fill()
    let lower = NSBezierPath(); lower.move(to: NSPoint(x: 240, y: 260)); lower.line(to: NSPoint(x: 510, y: 180)); lower.line(to: NSPoint(x: 784, y: 260)); lower.lineWidth = 18; lower.lineJoinStyle = .round; lower.lineCapStyle = .round
    NSColor(calibratedRed: 0.40, green: 0.93, blue: 0.85, alpha: 0.65).setStroke(); lower.stroke()
    NSGraphicsContext.restoreGraphicsState()
    return bitmap.representation(using: .png, properties: [:])!
}
for size in [16,32,128,256,512] {
    try draw(size).write(to: out.appendingPathComponent("AppIcon.iconset/icon_\(size)x\(size).png"))
    try draw(size*2).write(to: out.appendingPathComponent("AppIcon.iconset/icon_\(size)x\(size)@2x.png"))
}
try draw(1024).write(to: out.appendingPathComponent("icon.png"))
// Windows Vista+ supports PNG images in ICO containers.
let sizes = [16,32,48,64,128,256]
let pngs = sizes.map { draw($0) }
var ico = Data()
func u16(_ n: Int) { ico.append(UInt8(n & 255)); ico.append(UInt8((n >> 8) & 255)) }
func u32(_ n: Int) { for i in 0..<4 { ico.append(UInt8((n >> (i*8)) & 255)) } }
u16(0); u16(1); u16(sizes.count)
var offset = 6 + 16*sizes.count
for (size, png) in zip(sizes, pngs) {
    ico.append(UInt8(size % 256)); ico.append(UInt8(size % 256)); ico.append(0); ico.append(0)
    u16(1); u16(32); u32(png.count); u32(offset); offset += png.count
}
for png in pngs { ico.append(png) }
try ico.write(to: out.appendingPathComponent("AppIcon.ico"))
