using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using NUnit.Framework;

[SetUpFixture]
public sealed class Setup : Rhino.Testing.Fixtures.RhinoSetupFixture { }

[TestFixture]
public class IconTests
{
    // SAM_ICON_MANIFESTS = ';'-separated paths to <repo>/design/grasshopper-icons/manifest.json
    [Test]
    public void EveryManifestObject_LoadsInGrasshopper_WithItsRedesignedIcon()
    {
        var server = Grasshopper.Instances.ComponentServer;
        Assert.That(server, Is.Not.Null);
        var failures = new List<string>();
        var report = new List<string>();
        foreach (string manifest in (Environment.GetEnvironmentVariable("SAM_ICON_MANIFESTS") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            string repo = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(manifest), "..", ".."));
            var rows = JsonDocument.Parse(File.ReadAllText(manifest)).RootElement.EnumerateArray().ToList();
            int ok = 0; int globalMax = 0; int offline = 0;
            foreach (var r in rows)
            {
                string guid = r.GetProperty("guid").GetString(), cls = r.GetProperty("class").GetString();
                string png = Path.Combine(repo, r.GetProperty("project_dir").GetString(), "Resources", "Icons", r.GetProperty("resource").GetString() + ".png");
                var obj = server.EmitObject(new Guid(guid));
                Bitmap icon;
                if (obj == null)
                {
                    // Offline fallback (e.g. Rhino.Inside.Revit plugins that plain Rhino cannot load): read the resource the
                    // Icon getter references straight from the built assembly. GUID -> resource is proven by the source re-parse.
                    string asmDir = Environment.GetEnvironmentVariable("SAM_ICON_ASSEMBLY_DIR");
                    if (string.IsNullOrEmpty(asmDir)) { failures.Add($"{Path.GetFileName(repo)} {cls} {guid}: not loaded by Grasshopper"); continue; }
                    icon = OfflineIcon(asmDir, r.GetProperty("project").GetString(), r.GetProperty("resource").GetString());
                    offline++;
                    if (icon == null) { failures.Add($"{cls}: resource {r.GetProperty("resource").GetString()} not found offline"); continue; }
                }
                else
                {
                string name = r.GetProperty("name").GetString() ?? "";
                if (!name.StartsWith("{") && obj.Name != name) failures.Add($"{cls}: name '{obj.Name}' != source '{name}'");
                string cat = r.GetProperty("category").ValueKind == JsonValueKind.String ? r.GetProperty("category").GetString() : null;
                if (cat != null && !cat.StartsWith("{") && obj.Category != cat) failures.Add($"{cls}: category '{obj.Category}' != source '{cat}'");
                icon = obj.Icon_24x24 as Bitmap;
                }
                if (icon == null) { failures.Add($"{cls}: Icon_24x24 null"); continue; }
                if (icon.Width != 24 || icon.Height != 24) { failures.Add($"{cls}: icon {icon.Width}x{icon.Height}"); continue; }
                using var expected = new Bitmap(png);
                int maxd = 0;
                for (int y = 0; y < 24; y++)
                    for (int x = 0; x < 24; x++)
                    {
                        Color a = icon.GetPixel(x, y), b = expected.GetPixel(x, y);
                        int d = Math.Abs(a.A - b.A);
                        if (a.A > 0 && b.A > 0) d = Math.Max(d, Math.Max(Math.Abs(a.R - b.R), Math.Max(Math.Abs(a.G - b.G), Math.Abs(a.B - b.B))));
                        maxd = Math.Max(maxd, d);
                    }
                string dump = Environment.GetEnvironmentVariable("SAM_ICON_DUMP");
                if (!string.IsNullOrEmpty(dump)) { Directory.CreateDirectory(dump); icon.Save(Path.Combine(dump, cls + ".png")); }
                globalMax = Math.Max(globalMax, maxd);
                if (maxd > 2) { failures.Add($"{cls}: icon differs from {Path.GetFileName(png)} (max channel diff {maxd})"); continue; }
                ok++;
            }
            report.Add($"{Path.GetFileName(repo)}: {ok}/{rows.Count} objects verified ({rows.Count - offline} loaded in Grasshopper by GUID, {offline} offline from the built assembly) with their redesigned 24x24 icon (max channel diff vs manifest PNG: {globalMax}, premultiplied-alpha rounding; tolerance 2)");
        }
        foreach (var l in report) TestContext.Progress.WriteLine(l);
        foreach (var f in failures) TestContext.Progress.WriteLine("FAIL " + f);
        File.WriteAllLines(Path.Combine(TestContext.CurrentContext.WorkDirectory, "icon_report.txt"), report.Concat(failures.Select(f => "FAIL " + f)));
        Assert.That(report, Is.Not.Empty);
        Assert.That(failures, Is.Empty);
    }

    static readonly Dictionary<string, System.Reflection.Assembly> Loaded = new();

    static Bitmap OfflineIcon(string dir, string project, string resource)
    {
        string path = Directory.GetFiles(dir, project + ".dll", SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
        if (path == null) return null;
        if (!Loaded.TryGetValue(path, out var asm)) Loaded[path] = asm = System.Reflection.Assembly.LoadFrom(path);
        foreach (string res in asm.GetManifestResourceNames().Where(n => n.EndsWith(".resources")))
        {
            var rm = new System.Resources.ResourceManager(res.Substring(0, res.Length - ".resources".Length), asm);
            object o;
            try { o = rm.GetObject(resource); } catch { continue; }
            if (o is Bitmap b) return b;
            if (o is byte[] bytes) return new Bitmap(new MemoryStream(bytes));
        }
        foreach (string res in asm.GetManifestResourceNames().Where(n => n.EndsWith("." + resource + ".png")))
            return new Bitmap(asm.GetManifestResourceStream(res));
        return null;
    }
}
