using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace MaterializeAgent
{
    public sealed class AgentJob
    {
        public string Input, Output, Preset, SettingsFile, Alpha;
        public bool Overwrite, Reference;
        public double Timeout = 300;
        public string[] Maps;
        public readonly Dictionary<string, string> Inputs = new Dictionary<string, string>();
        public static readonly string[] OutputMaps = {"albedo", "diffuse", "height", "normal", "metallic", "smoothness", "roughness", "edge", "ao", "orm"};
        public static readonly string[] InputMaps = {"diffuse", "height", "normal", "metallic", "smoothness", "edge", "ao"};
        public static List<AgentJob> Parse(string[] args)
        {
            AgentJob job = new AgentJob();
            string batch = null;
            HashSet<string> seen = new HashSet<string>();
            for (int i = 0; i < args.Length; i++)
            {
                string key = args[i];
                if (key == "--") continue;
                if (!seen.Add(key) && key != "--map-in") throw new ArgumentException("Duplicate argument: " + key);
                if (key == "--overwrite") { job.Overwrite = true; continue; }
                if (key == "--reference") { job.Reference = true; continue; }
                if (i + 1 >= args.Length) throw new ArgumentException("Missing argument value: " + key);
                string value = args[++i];
                switch (key)
                {
                    case "--in": job.Input = value; break;
                    case "--out": job.Output = value; break;
                    case "--preset": job.Preset = value; break;
                    case "--settings": job.SettingsFile = value; break;
                    case "--alpha": job.Alpha = value; break;
                    case "--maps": job.Maps = value.Split(','); break;
                    case "--batch": batch = value; break;
                    case "--timeout": job.Timeout = double.Parse(value, System.Globalization.CultureInfo.InvariantCulture); break;
                    case "--map-in":
                        int equals = value.IndexOf('=');
                        if (equals < 1) throw new ArgumentException("Expected --map-in TYPE=PATH");
                        string name = value.Substring(0, equals);
                        if (!InputMaps.Contains(name) || job.Inputs.ContainsKey(name)) throw new ArgumentException("Invalid/duplicate supplied map: " + name);
                        job.Inputs.Add(name, value.Substring(equals + 1)); break;
                    default: throw new ArgumentException("Unknown argument: " + key);
                }
            }
            if (batch == null) { job.Check(); return new List<AgentJob> {job}; }
            if (seen.Any(k => k != "--batch" && k != "--overwrite" && k != "--reference")) throw new ArgumentException("Batch cannot be combined with single-job arguments");
            JObject root = AgentSettings.ReadJson(File.ReadAllText(batch));
            if (root.Properties().Any(p => p.Name != "version" && p.Name != "jobs") || (int?)root["version"] != 1 || !(root["jobs"] is JArray)) throw new ArgumentException("Expected batch {version:1,jobs:[...]}");
            string dir = Path.GetDirectoryName(Path.GetFullPath(batch));
            List<AgentJob> result = new List<AgentJob>();
            foreach (JObject row in (JArray)root["jobs"])
            {
                if (row.Properties().Any(p => !new[]{"in", "out", "preset", "settings", "maps", "map_inputs", "alpha", "timeout"}.Contains(p.Name))) throw new ArgumentException("Unknown batch job key");
                AgentJob entry = new AgentJob { Input = Resolve(dir, (string)row["in"]), Output = Resolve(dir, (string)row["out"]), Preset = (string)row["preset"], SettingsFile = Resolve(dir, (string)row["settings"]), Alpha = (string)row["alpha"], Maps = row["maps"]?.ToObject<string[]>(), Overwrite = job.Overwrite, Reference = job.Reference, Timeout = (double?)row["timeout"] ?? 300 };
                if (row["map_inputs"] is JObject inputs) foreach (JProperty p in inputs.Properties()) entry.Inputs.Add(p.Name, Resolve(dir, (string)p.Value));
                entry.Check(); result.Add(entry);
            }
            if (result.Count == 0 || result.Select(j => Path.GetFullPath(j.Output)).Distinct(StringComparer.OrdinalIgnoreCase).Count() != result.Count) throw new ArgumentException("Batch needs jobs with distinct output directories");
            foreach (AgentJob owner in result)
                foreach (AgentJob other in result)
                {
                    owner.ProtectInputs(other);
                    if (owner != other && ContainsPath(owner.Output, other.Output)) throw new ArgumentException("Batch output directories must not nest");
                }
            return result;
        }
        static string Resolve(string dir, string path) => path == null ? null : Path.GetFullPath(Path.Combine(dir, path));
        public void Check()
        {
            if (Input == null || Output == null || Maps == null || Maps.Length == 0 || Maps.Distinct().Count() != Maps.Length || Maps.Any(m => !OutputMaps.Contains(m))) throw new ArgumentException("Require --in, --out and valid unique --maps");
            if ((Preset == null) == (SettingsFile == null)) throw new ArgumentException("Require exactly one preset or settings file");
            if (double.IsNaN(Timeout) || double.IsInfinity(Timeout) || Timeout <= 0 || Timeout > 3600) throw new ArgumentException("Timeout must be positive and at most 3600 seconds");
            foreach (KeyValuePair<string,string> input in Inputs) if (!InputMaps.Contains(input.Key) || string.IsNullOrEmpty(input.Value)) throw new ArgumentException("Invalid supplied map: " + input.Key);
            ProtectInputs(this);
        }
        static bool ContainsPath(string directory, string path)
        {
            string root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string file = Path.GetFullPath(path);
            return file.Equals(root, StringComparison.OrdinalIgnoreCase) || file.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
        void ProtectInputs(AgentJob other)
        {
            foreach (string input in other.Inputs.Values.Concat(new[]{other.Input, other.SettingsFile}).Where(p => p != null))
                if (ContainsPath(Output, input)) throw new ArgumentException("Output directory contains a job input: " + input);
        }
        public List<string> Plan(AgentSettings settings)
        {
            HashSet<string> ready = new HashSet<string>(Inputs.Keys) {"albedo"};
            HashSet<string> visiting = new HashSet<string>();
            List<string> order = new List<string>();
            foreach (string map in Maps) Visit(map, settings.Project, ready, visiting, order);
            if ((bool)settings.Json["tiling"]["enabled"]) Visit("height", settings.Project, ready, visiting, order);
            return order;
        }
        static void Visit(string map, ProjectObject p, HashSet<string> ready, HashSet<string> visiting, List<string> order)
        {
            if (ready.Contains(map)) return;
            if (!visiting.Add(map)) throw new ArgumentException("Unresolved dependency cycle at " + map + "; supply a dependency map");
            string[] deps;
            switch (map)
            {
                case "diffuse": deps = new[]{"albedo"}; break;
                case "height": deps = new[]{p.HFDS.useNormal ? "normal" : p.HFDS.useAdjustedDiffuse ? "diffuse" : "albedo"}; break;
                case "normal": deps = new[]{"height"}; break;
                case "metallic": deps = new[]{p.MS.useAdjustedDiffuse ? "diffuse" : "albedo"}; break;
                case "smoothness": deps = new[]{p.SS.useAdjustedDiffuse ? "diffuse" : "albedo", "metallic"}; break;
                case "roughness": deps = new[]{"smoothness"}; break;
                case "edge": deps = new[]{"normal"}; break;
                case "ao": deps = new[]{"normal", "height"}; break;
                case "orm": deps = new[]{"ao", "smoothness", "metallic"}; break;
                default: throw new ArgumentException("Unknown map: " + map);
            }
            foreach (string dep in deps) Visit(dep, p, ready, visiting, order);
            visiting.Remove(map); ready.Add(map); order.Add(map);
        }
    }
}
