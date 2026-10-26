using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using System.Reflection;

[assembly: AssemblyTitle("Bambu 冷打板助手")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

static class Program
{
    const string Repo = "https://github.com/taosiqi/bambu-cool-plate-helper";
    const string Maker = "https://makerworld.com.cn/@siqijson?appSharePlatform=copy";
    static readonly string[] Keys = { "supertack_plate_temp", "supertack_plate_temp_initial_layer" };
    static string[] PresetNames(string directory)
    {
        if (!Directory.Exists(directory)) return new string[0];
        return Directory.GetFiles(directory, "*.json")
            .Select(Path.GetFileName).Where(name => name.StartsWith("Bambu PETG Basic @", StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal).ToArray();
    }
    static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024 };
    static readonly Encoding Utf8 = new UTF8Encoding(false);
    static readonly string Support = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    static readonly string Root = Path.Combine(Support, "BambuStudio", "system", "BBL", "filament");
    static readonly string Backups = Path.Combine(Support, "BambuCoolPlateHelper", "Backups");

    static object Get(Dictionary<string, object> obj, string key) { object value; return obj.TryGetValue(key, out value) ? value : null; }
    static bool Equal(object a, object b) { return Json.Serialize(a) == Json.Serialize(b); }
    static Dictionary<string, object> Read(string path)
    {
        var value = Json.DeserializeObject(File.ReadAllText(path, Utf8)) as Dictionary<string, object>;
        if (value == null) throw new Exception("配置不是有效的 JSON 对象：" + Path.GetFileName(path));
        return value;
    }
    static void Atomic(string path, byte[] data)
    {
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try {
            File.WriteAllBytes(temp, data);
            if (File.Exists(path)) File.Replace(temp, path, null);
            else File.Move(temp, path);
        } finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    static void Write(string path, Dictionary<string, object> obj) { Atomic(path, Utf8.GetBytes(Json.Serialize(obj) + "\n")); }
    static void Commit(Dictionary<string, Dictionary<string, object>> edits)
    {
        var originals = edits.Keys.ToDictionary(x => x, File.ReadAllBytes);
        try { foreach (var item in edits) Write(item.Key, item.Value); }
        catch (Exception error) {
            var failures = new List<string>();
            foreach (var item in originals) {
                try { Atomic(item.Key, item.Value); } catch { failures.Add(Path.GetFileName(item.Key)); }
            }
            throw new Exception(error.Message + "\n" + (failures.Count == 0 ? "本次写入已回滚。" : "部分回滚失败，请检查备份：" + string.Join("、", failures)));
        }
    }
    static int Apply(string root, string backups, int temperature = 40)
    {
        if (temperature < 1 || temperature > 120) throw new Exception("请输入 1–120 之间的整数温度（℃）。");
        var wanted = new[] { temperature.ToString(System.Globalization.CultureInfo.InvariantCulture) };
        var edits = new Dictionary<string, Dictionary<string, object>>();
        int found = 0;
        foreach (string name in PresetNames(root)) {
            string path = Path.Combine(root, name);
            if (!File.Exists(path)) continue;
            found++;
            var obj = Read(path);
            if (Get(obj, "name") as string != Path.GetFileNameWithoutExtension(name)) throw new Exception("配置名称不匹配：" + name);
            if (Keys.All(key => Equal(Get(obj, key), wanted))) continue;
            foreach (string key in Keys) obj[key] = wanted;
            edits.Add(path, obj);
        }
        if (found == 0) throw new Exception("没有找到官方 PETG Basic 配置。请先在 Bambu Studio 完成官方配置更新。\n" + root);
        if (edits.Count == 0) return 0;
        string stamp = ((long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds).ToString() + "-" + Guid.NewGuid();
        string backup = Path.Combine(backups, stamp);
        Directory.CreateDirectory(backup);
        foreach (string path in edits.Keys) File.Copy(path, Path.Combine(backup, Path.GetFileName(path)));
        string initial = Path.Combine(backups, "initial");
        Directory.CreateDirectory(initial);
        foreach (string path in edits.Keys) {
            string target = Path.Combine(initial, Path.GetFileName(path));
            if (!File.Exists(target)) File.Copy(path, target);
        }
        Commit(edits);
        return edits.Count;
    }
    static int Restore(string root, string backups)
    {
        string backup = Path.Combine(backups, "initial");
        var edits = new Dictionary<string, Dictionary<string, object>>();
        foreach (string name in PresetNames(backup)) {
            string source = Path.Combine(backup, name), path = Path.Combine(root, name);
            if (!File.Exists(source)) continue;
            var original = Read(source); var current = Read(path);
            if (Get(current, "name") as string != Path.GetFileNameWithoutExtension(name) || !Equal(Get(current, "name"), Get(original, "name")))
                throw new Exception("配置身份已变化，停止恢复：" + name);
            foreach (string key in Keys) {
                if (original.ContainsKey(key)) current[key] = original[key]; else current.Remove(key);
            }
            edits.Add(path, current);
        }
        if (edits.Count == 0) throw new Exception("备份中没有目标配置。");
        Commit(edits);
        return edits.Count;
    }
    static bool IsStudio(string name) { return string.Equals(name, "bambu-studio", StringComparison.OrdinalIgnoreCase) || string.Equals(name, "BambuStudio", StringComparison.OrdinalIgnoreCase); }
    static bool StudioRunning()
    {
        foreach (var process in Process.GetProcesses()) {
            using (process) { try { if (IsStudio(process.ProcessName)) return true; } catch (InvalidOperationException) { } }
        }
        return false;
    }
    static void Require(bool condition, string message) { if (!condition) throw new Exception("TEST FAILED: " + message); }
    static void SelfTest()
    {
        Require(IsStudio("bambu-studio") && IsStudio("BambuStudio") && !IsStudio("BambuCoolPlateHelper"), "process identity");
        string temp = Path.Combine(Path.GetTempPath(), "BambuHelperTest-" + Guid.NewGuid());
        string root = Path.Combine(temp, "filament"), backups = Path.Combine(temp, "Backups");
        Directory.CreateDirectory(root);
        try {
            string[] Names = { "Bambu PETG Basic @BBL P2S 0.4 nozzle.json", "Bambu PETG Basic @BBL A1M 0.4 nozzle.json" };
            string p = Path.Combine(root, Names[0]), a = Path.Combine(root, Names[1]);
            Write(p, new Dictionary<string, object> { { "name", Path.GetFileNameWithoutExtension(Names[0]) }, { Keys[0], new[] { "60" } }, { Keys[1], new[] { "60" } }, { "other", "keep" } });
            Write(a, new Dictionary<string, object> { { "name", Path.GetFileNameWithoutExtension(Names[1]) }, { "inherits", "base" } });
            string historical = Path.Combine(backups, "1000000000-00000000-0000-0000-0000-000000000001");
            Directory.CreateDirectory(historical);
            Write(Path.Combine(historical, Names[0]), new Dictionary<string, object> { { "name", Path.GetFileNameWithoutExtension(Names[0]) }, { Keys[0], new[] { "99" } } });
            bool historyRejected = false; try { Restore(root, backups); } catch { historyRejected = true; }
            Require(historyRejected, "old history must not be imported");
            Require(Apply(root, backups) == 2, "apply");
            Require(Equal(Get(Read(a), Keys[0]), new[] { "40" }), "inherited override");
            Require(Apply(root, backups) == 0, "idempotence");
            var changed = Read(p); changed["other"] = "updated"; Write(p, changed);
            Require(Restore(root, backups) == 2, "default restore");
            Require(Equal(Get(Read(p), Keys[0]), new[] { "60" }) && (string)Get(Read(p), "other") == "updated", "preserve unrelated data");
            Require(!Read(a).ContainsKey(Keys[0]), "restore inheritance");
            Apply(root, backups, 45);
            Require(Equal(Get(Read(p), Keys[0]), new[] { "45" }) && Equal(Get(Read(p), Keys[1]), new[] { "45" }), "custom temperature");
            Require(Apply(root, backups, 45) == 0, "custom temperature idempotence");
            Restore(root, backups); Restore(root, backups);
            Require(Equal(Get(Read(p), Keys[0]), new[] { "60" }) && !Read(a).ContainsKey(Keys[0]), "immutable original backup");
            bool rejected;
            foreach (int invalid in new[] { 0, -1, 121 }) {
                rejected = false; try { Apply(root, backups, invalid); } catch { rejected = true; }
                Require(rejected && Equal(Get(Read(p), Keys[0]), new[] { "60" }), "range validation before writing");
            }
            string[] extra = { "Bambu PETG Basic @BBL X1C.json", "Bambu PETG Basic @BBL H2D 0.6 nozzle.json", "Bambu PETG Basic @base.json", "Bambu PETG Basic @BBL Future 1.0 nozzle.json" };
            foreach (string name in extra) Write(Path.Combine(root, name), new Dictionary<string, object> {
                { "name", Path.GetFileNameWithoutExtension(name) }, { Keys[0], new[] { "70" } }, { Keys[1], new[] { "70" } } });
            string hf = Path.Combine(root, "Bambu PETG HF @BBL P2S.json");
            Write(hf, new Dictionary<string, object> { { "name", "Bambu PETG HF @BBL P2S" }, { Keys[0], new[] { "80" } } });
            Require(Apply(root, backups, 50) == 6, "discover all current and future model presets");
            Require(Equal(Get(Read(hf), Keys[0]), new[] { "80" }), "exclude PETG HF");
            foreach (string name in extra) Require(Equal(Get(Read(Path.Combine(root, name)), Keys[0]), new[] { "50" }), "additional model modified");
            Restore(root, backups);
            foreach (string name in extra) Require(Equal(Get(Read(Path.Combine(root, name)), Keys[0]), new[] { "70" }), "incremental original backup");
            File.WriteAllText(a, "invalid json");
            rejected = false; try { Apply(root, backups); } catch { rejected = true; }
            Require(rejected && Equal(Get(Read(p), Keys[0]), new[] { "60" }), "invalid JSON causes no partial write");
            Console.WriteLine("PASS: all models, future/base presets, exclude PETG HF, custom temperature, input range, inheritance, idempotence, default restore, ignored history, malformed JSON, process identity");
        } finally { Directory.Delete(temp, true); }
    }
    static Form CreateForm()
    {
        var form = new Form { Text = "Bambu 冷打板助手", ClientSize = new Size(620, 580), StartPosition = FormStartPosition.CenterScreen,
            Font = new Font("Microsoft YaHei UI", 10F), AutoScaleMode = AutoScaleMode.Dpi, MinimumSize = new Size(600, 600), BackColor = Color.White };
        form.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(26) };
        form.Controls.Add(flow);
        flow.Controls.Add(new PictureBox { Image = form.Icon.ToBitmap(), SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(64, 64), Margin = new Padding(0, 0, 0, 12) });
        flow.Controls.Add(new Label { Text = "PETG 冷打板助手", AutoSize = true, Font = new Font(form.Font.FontFamily, 19F, FontStyle.Bold), Margin = new Padding(0, 0, 0, 14) });
        flow.Controls.Add(new Label { Text = "全部机型 · 官方 PETG Basic\n低温增粘板 SuperTack · 首层和其他层使用相同温度\n自动扫描本机所有机型和喷嘴的官方 PETG Basic 预设。\n\n请先保存项目并退出 Bambu Studio。修改前自动备份，\n官方配置更新后可再次应用。已有项目的独立覆盖需另行检查。\n\n恢复默认会还原首次备份的温度字段（包括继承状态），\n覆盖后来设置的温度，保留其他参数。", AutoSize = true, MaximumSize = new Size(550, 0), Margin = new Padding(0, 0, 0, 18) });
        var temperature = new NumericUpDown { Minimum = 1, Maximum = 120, Value = 40, DecimalPlaces = 0, Width = 90, AccessibleName = "热床温度，摄氏度" };
        var inputRow = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 16) };
        inputRow.Controls.Add(new Label { Text = "热床温度", AutoSize = true, Margin = new Padding(0, 4, 10, 0) });
        inputRow.Controls.Add(temperature);
        inputRow.Controls.Add(new Label { Text = "℃（1–120，整数）", AutoSize = true, Margin = new Padding(8, 4, 0, 0) });
        flow.Controls.Add(inputRow);
        var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 14) };
        string[] titles = { "应用温度", "恢复默认" };
        for (int i = 0; i < titles.Length; i++) {
            int action = i;
            var button = new Button { Text = titles[i], AutoSize = true, Padding = new Padding(8, 6, 8, 6), Margin = new Padding(0, 0, 10, 0) };
            button.Click += delegate {
                try {
                    if (StudioRunning()) throw new Exception("请先保存项目并退出 Bambu Studio，然后再试。工具不会强制关闭 Studio。");
                    int count = action == 0 ? Apply(Root, Backups, (int)temperature.Value) : Restore(Root, Backups);
                    MessageBox.Show(form, (count == 0 ? "已经是指定温度，无需重复修改。" : "已处理 " + count + " 份官方耗材配置。") + "\n\n重新打开 Bambu Studio，检查温度并重新切片。\n备份：" + Backups, "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                } catch (Exception error) { MessageBox.Show(form, error.Message, "未完成", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
            };
            buttons.Controls.Add(button);
        }
        flow.Controls.Add(buttons);
        foreach (var entry in new[] { new[] { "支持作者 · MakerWorld 主页求助力 ↗", Maker }, new[] { "GitHub · 源码与新版下载 ↗", Repo } }) {
            string address = entry[1];
            var link = new LinkLabel { Text = entry[0], AutoSize = true, Margin = new Padding(0, 0, 0, 10), LinkColor = Color.Teal, Cursor = Cursors.Hand, LinkBehavior = LinkBehavior.AlwaysUnderline, ActiveLinkColor = Color.DarkCyan };
            link.LinkClicked += delegate { try { Process.Start(new ProcessStartInfo(address) { UseShellExecute = true }); } catch (Exception error) { MessageBox.Show(form, error.Message); } };
            flow.Controls.Add(link);
        }
        return form;
    }
    [STAThread]
    static int Main(string[] args)
    {
        if (args.Contains("--self-test")) { try { SelfTest(); return 0; } catch (Exception error) { Console.Error.WriteLine(error); return 1; } }
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        using (var form = CreateForm()) {
            if (args.Contains("--render-preview")) {
                form.Show(); Application.DoEvents();
                using (var image = new Bitmap(form.Width, form.Height)) {
                    form.DrawToBitmap(image, new Rectangle(0, 0, image.Width, image.Height));
                    image.Save("windows-preview.png", System.Drawing.Imaging.ImageFormat.Png);
                }
                return 0;
            }
            Application.Run(form);
        }
        return 0;
    }
}
