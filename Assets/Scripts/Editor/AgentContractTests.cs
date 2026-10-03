using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MaterializeAgent;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

public static class AgentContractTests
{
    public static void Run()
    {
        int passed = 0;
        JObject defaults = AgentSettings.Resource("gui_defaults");
        AgentSettings valid = new AgentSettings(defaults); passed++;
        foreach (string preset in new[]{"ground_tile", "wall_tile", "atlas", "foliage_atlas", "decal_atlas"}) { AgentSettings.Load(null, preset); passed++; }
        Reject(() => new AgentSettings(AgentSettings.ReadJson("{\"version\":1,\"version\":1}"))); passed++;
        Reject(() => new AgentSettings(Changed(defaults, j => j["mystery"] = 1))); passed++;
        Reject(() => new AgentSettings(Changed(defaults, j => ((JObject)j["filters"]["ao"]).Remove("Depth")))); passed++;
        Reject(() => new AgentSettings(Changed(defaults, j => j["filters"]["ao"]["Depth"] = 257))); passed++;
        Reject(() => new AgentSettings(Changed(defaults, j => j["filters"]["normal"]["SlopeBlur"] = 5.5))); passed++;
        Reject(() => new AgentSettings(Changed(defaults, j => j["alignment"]["pointTL"][0] = 1e100))); passed++;
        Reject(() => new AgentSettings(Changed(defaults, j => j["filters"]["height"]["useOriginalDiffuse"] = true))); passed++;
        Reject(() => new AgentSettings(Changed(defaults, j => j["tiling"]["enabled"] = true, j => j["tiling"]["OverlapX"] = 0))); passed++;
        Require(valid.Project.AOS.Spread == 50, "AO constructor default must beat incorrect annotation"); passed++;
        foreach (KeyValuePair<string,Type> pair in AgentSettings.FilterTypes)
        {
            string[] fields = pair.Value.GetFields().Where(f => f.FieldType != typeof(string)).Select(f => f.Name).OrderBy(s => s).ToArray();
            string[] keys = ((JObject)defaults["filters"][pair.Key]).Properties().Select(p => p.Name).OrderBy(s => s).ToArray();
            Require(fields.SequenceEqual(keys), "Missing GUI control: " + pair.Key); passed++;
        }
        AgentJob job = AgentJob.Parse(new[]{"--in", "a.png", "--preset", "ground_tile", "--maps", "orm,normal", "--out", "out"})[0];
        Require(job.Plan(valid).SequenceEqual(new[]{"diffuse", "height", "normal", "ao", "metallic", "smoothness", "orm"}), "Dependency order"); passed++;
        job.Inputs.Add("normal", "normal.png"); job.Inputs.Add("height", "height.png");
        Require(!job.Plan(valid).Contains("normal") && !job.Plan(valid).Contains("height"), "Supplied maps take precedence"); passed++;
        AgentSettings cycle = new AgentSettings(Changed(defaults, j => j["filters"]["height"]["useAdjustedDiffuse"] = false, j => j["filters"]["height"]["useNormal"] = true));
        job.Inputs.Clear(); Reject(() => job.Plan(cycle)); passed++;
        job.Inputs.Add("normal", "n.png"); Require(job.Plan(cycle).Contains("height"), "Supplied normal breaks cycle"); passed++;
        Reject(() => AgentJob.Parse(new[]{"--in", "a", "--preset", "atlas", "--maps", "bad", "--out", "o"})); passed++;
        Reject(() => AgentJob.Parse(new[]{"--in", "a", "--preset", "atlas", "--settings", "s", "--maps", "normal", "--out", "o"})); passed++;
        Reject(() => AgentJob.Parse(new[]{"--in", "protected/input.png", "--preset", "atlas", "--maps", "normal", "--out", "protected", "--overwrite"})); passed++;
        Texture2D a = Pixel(17), s = Pixel(41), m = Pixel(99);
        Texture2D packed = AgentRunner.Pack(a, s, m);
        Color32 result = packed.GetPixels32()[0];
        Require(result.r == 17 && result.g == 214 && result.b == 99 && result.a == 255, "ORM channel contract"); passed++;
        Texture2D rough = AgentRunner.Roughness(s); Require(rough.GetPixels32()[0].r == 214, "Roughness inversion"); passed++;
        foreach (Texture2D image in new[]{a,s,m,packed,rough}) UnityEngine.Object.DestroyImmediate(image);
        string root = Path.GetFullPath("build/contract-files"); Directory.CreateDirectory(root);
        string stage = Path.Combine(root, "stage"), target = Path.Combine(root, "target");
        Directory.CreateDirectory(stage); Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(stage, "new"), "complete"); File.WriteAllText(Path.Combine(target, "old"), "keep");
        Reject(() => AgentRunner.Publish(stage, target, false)); Require(File.Exists(Path.Combine(target, "old")), "Refusal preserves old outputs"); passed++;
        AgentRunner.Publish(stage,target,true); Require(File.Exists(Path.Combine(target,"new")) && !File.Exists(Path.Combine(target,"old")), "Complete directory publish"); passed++;
        Debug.Log("AGENT-CONTRACTS PASS " + passed);
    }
    static JObject Changed(JObject original, params Action<JObject>[] edits) { JObject copy = (JObject)original.DeepClone(); foreach (Action<JObject> edit in edits) edit(copy); return copy; }
    static void Require(bool ok, string message) { if (!ok) throw new Exception(message); }
    static void Reject(Action action) { try { action(); } catch (ArgumentException) { return; } catch (IOException) { return; } catch (Newtonsoft.Json.JsonException) { return; } throw new Exception("Expected refusal did not occur"); }
    static Texture2D Pixel(byte red) { Texture2D image = new Texture2D(1,1,TextureFormat.RGBA32,false,true); image.SetPixels32(new[]{new Color32(red,0,0,255)}); image.Apply(); return image; }
}
