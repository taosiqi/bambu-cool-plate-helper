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
    static string MaterialName(string filename)
    {
        int separator = filename.IndexOf(" @", StringComparison.Ordinal);
        if (!filename.EndsWith(".json", StringComparison.Ordinal) || separator < 0) return null;
        string material = filename.Substring(0, separator);
        return material == "Bambu PETG" || material.StartsWith("Bambu PETG ", StringComparison.Ordinal) || material.StartsWith("Bambu PETG-", StringComparison.Ordinal) ? material : null;
    }
    static string[] Selection(string[] materials)
    {
        if (materials == null) return new[] { "Bambu PETG Basic" };
        if (materials.Length == 0) throw new Exception("请至少勾选一种耗材。");
        return materials;
    }
    static string[] PresetNames(string directory)
    {
        if (!Directory.Exists(directory)) return new string[0];
        return Directory.GetFiles(directory, "*.json")
            .Select(Path.GetFileName).Where(name => MaterialName(name) != null)
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
    static int Apply(string root, string backups, int temperature = 40, string[] materials = null)
    {
        materials = Selection(materials);
        if (temperature < 1 || temperature > 120) throw new Exception("请输入 1–120 之间的整数温度（℃）。");
        var wanted = new[] { temperature.ToString(System.Globalization.CultureInfo.InvariantCulture) };
        var edits = new Dictionary<string, Dictionary<string, object>>();
        int found = 0;
        foreach (string name in PresetNames(root).Where(name => materials.Contains(MaterialName(name)))) {
            string path = Path.Combine(root, name);
            if (!File.Exists(path)) continue;
            found++;
            var obj = Read(path);
            if (Get(obj, "name") as string != Path.GetFileNameWithoutExtension(name)) throw new Exception("配置名称不匹配：" + name);
            if (Keys.All(key => Equal(Get(obj, key), wanted))) continue;
            foreach (string key in Keys) obj[key] = wanted;
            edits.Add(path, obj);
        }
        if (found == 0) throw new Exception("没有找到所选耗材的官方配置。请先在 Bambu Studio 完成官方配置更新。\n" + root);
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
    static int Restore(string root, string backups, string[] materials = null)
    {
        materials = Selection(materials);
        string backup = Path.Combine(backups, "initial");
        var edits = new Dictionary<string, Dictionary<string, object>>();
        foreach (string name in PresetNames(backup).Where(name => materials.Contains(MaterialName(name)))) {
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
        if (edits.Count == 0) throw new Exception("所选耗材还没有默认备份。");
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
            Require(PresetNames(root).Select(MaterialName).Distinct().Count() == 2, "discover Basic and HF");
            Require(MaterialName("Bambu PETG-CF @base.json") == "Bambu PETG-CF" && MaterialName("Bambu PLA Basic @base.json") == null, "material family boundary");
            Require(Apply(root, backups, 42, new[] { "Bambu PETG HF" }) == 1, "HF only");
            Require(Equal(Get(Read(p), Keys[0]), new[] { "60" }) && Equal(Get(Read(hf), Keys[0]), new[] { "42" }), "HF selection isolates Basic");
            Require(Apply(root, backups, 45, new[] { "Bambu PETG Basic", "Bambu PETG HF" }) == 7, "multi selection");
            Restore(root, backups, new[] { "Bambu PETG HF" });
            Require(Equal(Get(Read(p), Keys[0]), new[] { "45" }) && Equal(Get(Read(hf), Keys[0]), new[] { "80" }), "restore only selected material");
            Restore(root, backups);
            rejected = false; try { Apply(root, backups, 40, new string[0]); } catch { rejected = true; }
            Require(rejected, "empty apply selection");
            rejected = false; try { Restore(root, backups, new string[0]); } catch { rejected = true; }
            Require(rejected, "empty restore selection");
            File.WriteAllText(a, "invalid json");
            rejected = false; try { Apply(root, backups); } catch { rejected = true; }
            Require(rejected && Equal(Get(Read(p), Keys[0]), new[] { "60" }), "invalid JSON causes no partial write");
            Console.WriteLine("PASS: all models, future/base presets, discovery, Basic/HF multi-selection, isolated restore, empty selection, custom temperature, input range, inheritance, idempotence, default restore, ignored history, malformed JSON, process identity");
        } finally { Directory.Delete(temp, true); }
    }
    static Form CreateForm(string[] previewMaterials = null)
    {
        var form = new Form { Text = "Bambu 冷打板助手", ClientSize = new Size(620, 650), StartPosition = FormStartPosition.CenterScreen,
            Font = new Font("Microsoft YaHei UI", 10F), AutoScaleMode = AutoScaleMode.Dpi, MinimumSize = new Size(600, 670), BackColor = Color.White };
        form.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(26) };
        form.Controls.Add(flow);
        flow.Controls.Add(new PictureBox { Image = form.Icon.ToBitmap(), SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(64, 64), Margin = new Padding(0, 0, 0, 12) });
        flow.Controls.Add(new Label { Text = "PETG 冷打板助手", AutoSize = true, Font = new Font(form.Font.FontFamily, 19F, FontStyle.Bold), Margin = new Padding(0, 0, 0, 14) });
        flow.Controls.Add(new Label { Text = "所选耗材的全部机型、喷嘴和颜色共用温度。\n低温增粘板 SuperTack · 首层和其他层同步修改。\n\n请先保存项目并退出 Bambu Studio，只勾选打印板支持的耗材。\n修改前自动备份，修改后重新切片。\n恢复默认只还原勾选耗材首次备份的温度设置。", AutoSize = true, MaximumSize = new Size(550, 0), Margin = new Padding(0, 0, 0, 18) });
        flow.Controls.Add(new Label { Text = "勾选要修改或恢复的耗材", AutoSize = true, Margin = new Padding(0, 0, 0, 6) });
        var choices = new CheckedListBox { CheckOnClick = true, Width = 530, Height = 130, BorderStyle = BorderStyle.FixedSingle, IntegralHeight = false, AccessibleName = "耗材选择" };
        try {
            var materials = previewMaterials ?? PresetNames(Root).Select(MaterialName).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToArray();
            foreach (string material in materials) choices.Items.Add(material, material == "Bambu PETG Basic");
            if (materials.Length == 0) flow.Controls.Add(new Label { Text = "未发现官方 PETG 预设，请先在 Studio 更新配置。", AutoSize = true });
        } catch (Exception error) {
            flow.Controls.Add(new Label { Text = "无法读取耗材列表：" + error.Message, AutoSize = true, MaximumSize = new Size(550, 0) });
        }
        flow.Controls.Add(choices);
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
                    var selected = choices.CheckedItems.Cast<string>().ToArray();
                    int count = action == 0 ? Apply(Root, Backups, (int)temperature.Value, selected) : Restore(Root, Backups, selected);
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
        using (var form = CreateForm(args.Contains("--render-preview") ? new[] { "Bambu PETG Basic", "Bambu PETG HF", "Bambu PETG Matte", "Bambu PETG Translucent", "Bambu PETG-CF" } : null)) {
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
