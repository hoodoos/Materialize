using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Xml.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace MaterializeAgent
{
    public sealed class AgentRunner : MonoBehaviour
    {
        List<AgentJob> jobs;
        string failure, staging;
        float deadline;
        GameObject context;
        MainGui gui;
        readonly Dictionary<string, MonoBehaviour> filters = new Dictionary<string, MonoBehaviour>();
        readonly Dictionary<string, string> provenance = new Dictionary<string, string>();
        static readonly Dictionary<string, Type> GuiTypes = new Dictionary<string, Type>
        { {"diffuse", typeof(EditDiffuseGui)}, {"height", typeof(HeightFromDiffuseGui)}, {"normal", typeof(NormalFromHeightGui)}, {"metallic", typeof(MetallicGui)}, {"smoothness", typeof(SmoothnessGui)}, {"ao", typeof(AOFromNormalGui)}, {"edge", typeof(EdgeFromNormalGui)} };
        static readonly Dictionary<string,string> MapFields = new Dictionary<string,string>
        { {"albedo", "_DiffuseMapOriginal"}, {"diffuse", "_DiffuseMap"}, {"height", "_HeightMap"}, {"normal", "_NormalMap"}, {"metallic", "_MetallicMap"}, {"smoothness", "_SmoothnessMap"}, {"edge", "_EdgeMap"}, {"ao", "_AOMap"} };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot()
        {
            string[] args = Environment.GetCommandLineArgs();
            int separator = Array.IndexOf(args, "--");
            string[] cli = separator < 0 ? args.Skip(1).Where(a => a.StartsWith("--")).ToArray() : args.Skip(separator + 1).ToArray();
            if (cli.Length == 0 || cli.Contains("--help"))
            {
                Console.WriteLine("materialize --in PNG (--preset NAME | --settings JSON) --maps normal,orm --out DIR [--map-in TYPE=PNG] [--alpha preserve|opaque] [--overwrite] [--timeout SECONDS]\nmaterialize --batch jobs.json [--overwrite]\nPresets: ground_tile wall_tile atlas foliage_atlas decal_atlas");
                Application.Quit(cli.Length == 0 ? 2 : 0); return;
            }
            GameObject root = new GameObject("Materialize Agent");
            Object.DontDestroyOnLoad(root);
            AgentRunner runner = root.AddComponent<AgentRunner>();
            try { runner.jobs = AgentJob.Parse(cli); }
            catch (Exception error) { Console.Error.WriteLine(error.Message); Application.Quit(2); return; }
            runner.StartCoroutine(runner.Guard(runner.Run()));
        }
        void OnEnable() { Application.logMessageReceived += Log; }
        void OnDisable() { Application.logMessageReceived -= Log; }
        void Log(string message, string trace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) failure = message;
        }
        void Update()
        {
            if (deadline > 0 && Time.realtimeSinceStartup > deadline) { failure = "Job timed out"; Fail(); }
        }
        IEnumerator Guard(IEnumerator operation)
        {
            Stack<IEnumerator> stack = new Stack<IEnumerator>(); stack.Push(operation);
            while (stack.Count != 0)
            {
                object yielded = null;
                bool moved = false;
                try { moved = stack.Peek().MoveNext(); if (moved) yielded = stack.Peek().Current; }
                catch (Exception error) { failure = error.ToString(); }
                if (failure != null) { Fail(); yield break; }
                if (!moved) { (stack.Pop() as IDisposable)?.Dispose(); continue; }
                if (yielded is IEnumerator nested) stack.Push(nested);
                else yield return yielded;
            }
        }
        void Fail()
        {
            Console.Error.WriteLine("MATERIALIZE FAILED: " + failure);
            if (staging != null && Directory.Exists(staging)) Directory.Delete(staging, true);
            Application.Quit(1); StopAllCoroutines();
        }
        IEnumerator Run()
        {
            Stopwatch total = Stopwatch.StartNew();
            foreach (AgentJob job in jobs)
            {
                Stopwatch timer = Stopwatch.StartNew();
                deadline = Time.realtimeSinceStartup + (float)job.Timeout;
                AgentSettings settings = AgentSettings.Load(job.SettingsFile, job.Preset, job.Alpha);
                List<string> plan = job.Plan(settings);
                bool align = (bool)settings.Json["alignment"]["enabled"], tile = (bool)settings.Json["tiling"]["enabled"];
                bool preserve = (string)settings.Json["alpha"] == "preserve";
                List<string> outputs = job.Maps.ToList();
                if ((align || tile) && !outputs.Contains("albedo")) outputs.Add("albedo");
                string target = Path.GetFullPath(job.Output);
                if (Directory.Exists(target) && !job.Overwrite && Directory.EnumerateFileSystemEntries(target).Any()) throw new IOException("Output directory is not empty; use --overwrite: " + target);
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                staging = target + ".staging-" + Guid.NewGuid().ToString("N"); Directory.CreateDirectory(staging);
                context = new GameObject("Job Context"); context.transform.SetParent(transform);
                gui = context.AddComponent<MainGui>(); gui.enabled = false; MainGui.instance = gui;
                gui._DiffuseMapOriginal = LoadPng(job.Input);
                provenance.Clear(); provenance.Add("albedo", "input:" + Path.GetFullPath(job.Input));
                foreach (KeyValuePair<string,string> input in job.Inputs)
                {
                    Texture2D map = LoadPng(input.Value);
                    if (map.width != gui._DiffuseMapOriginal.width || map.height != gui._DiffuseMapOriginal.height) { Object.Destroy(map); throw new ArgumentException("Supplied map dimensions differ: " + input.Key); }
                    SetMap(input.Key, map); provenance[input.Key] = "input:" + Path.GetFullPath(input.Value);
                }
                GameObject preview = new GameObject("Shared Preview"); preview.transform.SetParent(context.transform); preview.AddComponent<MeshRenderer>();
                gui.testObject = preview;
                gui.FullMaterial = new Material(Shader.Find("Hidden/Blit_Shader"));
                SettingsGui preferences = Child<SettingsGui>("Fixed Normal Convention");
                preferences.settings = new Settings { normalMapMayaStyle = true, normalMapMaxStyle = false };
                SettingsGui.instance = preferences;
                Shader.SetGlobalInt("_FlipNormalY", 1); // Maya = OpenGL; keep consumers on the same convention.
                Shader.SetGlobalFloat("_GamaCorrection", 2.2f);
                if (align)
                {
                    AlignmentGui tool = Child<AlignmentGui>("Alignment");
                    yield return tool.ExecuteForAgent(new AlignmentSettings(settings.Json["alignment"]), preview, preserve && !job.Reference, job.Reference);
                }
                filters.Clear();
                foreach (KeyValuePair<string, Type> entry in GuiTypes)
                {
                    GameObject obj = new GameObject(entry.Key); obj.transform.SetParent(context.transform);
                    MonoBehaviour component = (MonoBehaviour)obj.AddComponent(entry.Value); component.enabled = false;
                    entry.Value.GetMethod("InitializeForAgent").Invoke(component, new object[]{gui, preview});
                    // Production assigns typed settings directly. Reference takes GUI SetValues via XML.
                    if (!job.Reference)
                        entry.Value.GetField(AgentSettings.ProjectFields[entry.Key], BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(component, typeof(ProjectObject).GetField(AgentSettings.ProjectFields[entry.Key]).GetValue(settings.Project));
                    filters.Add(entry.Key, component);
                }
                if (job.Reference)
                {
                    XmlSerializer serializer = new XmlSerializer(typeof(ProjectObject));
                    ProjectObject reference;
                    using (StringWriter writer = new StringWriter()) { serializer.Serialize(writer, settings.Project); using (StringReader reader = new StringReader(writer.ToString())) reference = (ProjectObject)serializer.Deserialize(reader); }
                    foreach (KeyValuePair<string, MonoBehaviour> entry in filters) entry.Value.GetType().GetMethod("SetValues").Invoke(entry.Value, new object[]{reference});
                }
                foreach (string map in plan)
                {
                    if (map == "orm" || map == "roughness") continue;
                    yield return Generate(map);
                    if (Map(map) == null) throw new InvalidOperationException("Filter did not produce " + map);
                    provenance[map] = "generated";
                    if (map == "diffuse" && preserve) CopyAlpha(gui._DiffuseMapOriginal, gui._DiffuseMap);
                }
                if (tile)
                {
                    TilingTextureMakerGui tool = Child<TilingTextureMakerGui>("Tiling");
                    yield return tool.ExecuteForAgent(new TilingSettings(settings.Json["tiling"]), preview, preserve && !job.Reference, job.Reference);
                }
                foreach (string output in outputs)
                {
                    Texture2D image = output == "orm" ? Pack(gui._AOMap, gui._SmoothnessMap, gui._MetallicMap) : output == "roughness" ? Roughness(gui._SmoothnessMap) : Map(output);
                    if (image == null) throw new InvalidOperationException("Missing requested output " + output);
                    if (output == "albedo" && !preserve) SetOpaque(image);
                    File.WriteAllBytes(Path.Combine(staging, output + ".png"), image.EncodeToPNG());
                    if (output == "orm" || output == "roughness") Object.Destroy(image);
                }
                JObject inputHashes = new JObject { ["albedo"] = Hash(job.Input) };
                foreach (KeyValuePair<string,string> input in job.Inputs) inputHashes[input.Key] = Hash(input.Value);
                JObject used = new JObject { ["settings"] = settings.Json, ["provenance"] = JObject.FromObject(provenance), ["outputs"] = new JArray(outputs), ["input_sha256"] = inputHashes, ["environment"] = new JObject { ["unity"] = Application.unityVersion, ["gpu"] = SystemInfo.graphicsDeviceName, ["backend"] = SystemInfo.graphicsDeviceType.ToString(), ["driver"] = SystemInfo.graphicsDeviceVersion }, ["normal_convention"] = "OpenGL", ["orm_channels"] = "AO,1-smoothness,metallic" };
                File.WriteAllText(Path.Combine(staging, "settings.used.json"), used.ToString(Formatting.Indented) + "\n");
                if (failure != null) throw new InvalidOperationException(failure);
                Publish(staging, target, job.Overwrite); staging = null;
                Console.WriteLine("MATERIALIZE JOB PASS " + target + " " + timer.Elapsed.TotalSeconds.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) + "s");
                Cleanup(); deadline = 0;
                yield return null;
            }
            Console.WriteLine("MATERIALIZE BATCH PASS " + jobs.Count + " jobs " + total.Elapsed.TotalSeconds.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) + "s");
            Application.Quit(0);
        }
        T Child<T>(string name) where T : MonoBehaviour
        {
            GameObject child = new GameObject(name); child.transform.SetParent(context.transform);
            T component = child.AddComponent<T>(); component.enabled = false; return component;
        }
        IEnumerator Generate(string map)
        {
            MonoBehaviour component = filters[map];
            component.GetType().GetMethod("InitializeTextures").Invoke(component, null);
            switch (map)
            {
                case "diffuse":
                    EditDiffuseGui d = (EditDiffuseGui)component; yield return d.ProcessBlur(); yield return d.ProcessDiffuse(MapType.diffuse); break;
                case "height":
                    HeightFromDiffuseGui h = (HeightFromDiffuseGui)component;
                    if (((HeightFromDiffuseSettings)typeof(HeightFromDiffuseGui).GetField("HFDS", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(h)).useNormal) yield return h.ProcessNormal(); else yield return h.ProcessDiffuse();
                    yield return h.ProcessHeight(); break;
                case "normal": NormalFromHeightGui n = (NormalFromHeightGui)component; yield return n.ProcessHeight(); yield return n.ProcessNormal(); break;
                case "metallic": MetallicGui m = (MetallicGui)component; yield return m.ProcessBlur(); yield return m.ProcessMetallic(); break;
                case "smoothness": SmoothnessGui s = (SmoothnessGui)component; yield return s.ProcessBlur(); yield return s.ProcessSmoothness(); break;
                case "edge": EdgeFromNormalGui e = (EdgeFromNormalGui)component; yield return e.ProcessNormal(); yield return e.ProcessEdge(); break;
                case "ao": AOFromNormalGui a = (AOFromNormalGui)component; yield return a.ProcessNormalDepth(); yield return a.ProcessAO(); break;
            }
            component.GetType().GetMethod("Close").Invoke(component, null);
        }
        Texture2D Map(string name) => (Texture2D)typeof(MainGui).GetField(MapFields[name]).GetValue(gui);
        void SetMap(string name, Texture2D texture) => typeof(MainGui).GetField(MapFields[name]).SetValue(gui, texture);
        public static Texture2D LoadPng(string path)
        {
            byte[] data = File.ReadAllBytes(path);
            if (data.Length < 8 || data[0] != 137 || data[1] != 80 || data[2] != 78 || data[3] != 71 || data[4] != 13 || data[5] != 10 || data[6] != 26 || data[7] != 10) throw new ArgumentException("Expected a PNG: " + path);
            Texture2D image = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
            if (!image.LoadImage(data)) { Object.Destroy(image); throw new ArgumentException("Invalid PNG: " + path); }
            image.wrapMode = TextureWrapMode.Repeat; image.filterMode = FilterMode.Bilinear;
            return image;
        }
        public static Texture2D Pack(Texture2D ao, Texture2D smoothness, Texture2D metallic)
        {
            Color32[] a = ao.GetPixels32(), s = smoothness.GetPixels32(), m = metallic.GetPixels32();
            if (ao.width != smoothness.width || ao.height != smoothness.height || ao.width != metallic.width || ao.height != metallic.height) throw new ArgumentException("ORM dimensions differ");
            for (int i = 0; i < a.Length; i++) a[i] = new Color32(a[i].r, (byte)(255-s[i].r), m[i].r, 255);
            return Image(ao.width, ao.height, a);
        }
        public static Texture2D Roughness(Texture2D smoothness)
        {
            Color32[] pixels = smoothness.GetPixels32();
            for (int i = 0; i < pixels.Length; i++) { byte v = (byte)(255-pixels[i].r); pixels[i] = new Color32(v,v,v,255); }
            return Image(smoothness.width, smoothness.height, pixels);
        }
        static Texture2D Image(int width, int height, Color32[] pixels) { Texture2D image = new Texture2D(width, height, TextureFormat.RGBA32, false, true); image.SetPixels32(pixels); image.Apply(); return image; }
        static void CopyAlpha(Texture2D source, Texture2D target)
        {
            Color32[] s = source.GetPixels32(), t = target.GetPixels32();
            for (int i = 0; i < t.Length; i++) t[i].a = s[i].a;
            target.SetPixels32(t); target.Apply();
        }
        static void SetOpaque(Texture2D image) { Color32[] pixels = image.GetPixels32(); for (int i = 0; i < pixels.Length; i++) pixels[i].a = 255; image.SetPixels32(pixels); image.Apply(); }
        public static string Hash(string path) { using (SHA256 sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant(); }
        public static void Publish(string stage, string target, bool overwrite)
        {
            string backup = null;
            if (Directory.Exists(target))
            {
                if (!overwrite && Directory.EnumerateFileSystemEntries(target).Any()) throw new IOException("Output exists: " + target);
                backup = target + ".previous-" + Guid.NewGuid().ToString("N"); Directory.Move(target, backup);
            }
            try { Directory.Move(stage, target); }
            catch { if (backup != null) Directory.Move(backup, target); throw; }
            if (backup != null) Directory.Delete(backup, true);
        }
        void Cleanup()
        {
            foreach (MonoBehaviour filter in filters.Values)
            {
                filter.GetType().GetMethod("Close").Invoke(filter, null);
                foreach (FieldInfo field in filter.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                    if (field.GetValue(filter) is Material material) Object.Destroy(material);
            }
            foreach (string map in MapFields.Keys) if (Map(map) != null) Object.Destroy(Map(map));
            if (gui._HDHeightMap != null) { gui._HDHeightMap.Release(); Object.Destroy(gui._HDHeightMap); }
            Object.Destroy(gui.FullMaterial); Object.Destroy(context); MainGui.instance = null; SettingsGui.instance = null; filters.Clear();
        }
    }
}
