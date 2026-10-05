using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace MaterializeAgent
{
    public sealed class AgentSettings
    {
        public readonly JObject Json;
        public readonly ProjectObject Project;
        public static readonly Dictionary<string, Type> FilterTypes = new Dictionary<string, Type>
        {
            {"diffuse", typeof(EditDiffuseSettings)}, {"height", typeof(HeightFromDiffuseSettings)},
            {"normal", typeof(NormalFromHeightSettings)}, {"metallic", typeof(MetallicSettings)},
            {"smoothness", typeof(SmoothnessSettings)}, {"ao", typeof(AOSettings)}, {"edge", typeof(EdgeSettings)}
        };
        public static readonly Dictionary<string, string> ProjectFields = new Dictionary<string, string>
        { {"diffuse", "EDS"}, {"height", "HFDS"}, {"normal", "NFHS"}, {"metallic", "MS"}, {"smoothness", "SS"}, {"ao", "AOS"}, {"edge", "ES"} };

        public static JObject ReadJson(string text)
        {
            using (JsonTextReader reader = new JsonTextReader(new StringReader(text)))
            {
                reader.DateParseHandling = DateParseHandling.None;
                JObject value = JObject.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
                if (reader.Read()) throw new ArgumentException("Trailing JSON content");
                return value;
            }
        }
        public static JObject Resource(string name)
        {
            TextAsset asset = Resources.Load<TextAsset>("Agent/" + name);
            if (asset == null) throw new FileNotFoundException("Missing bundled settings: " + name);
            return ReadJson(asset.text);
        }
        public static AgentSettings Load(string file, string preset, string alpha = null)
        {
            if ((file == null) == (preset == null)) throw new ArgumentException("Choose exactly one --settings or --preset");
            if (preset != null && !new[]{"ground_tile", "wall_tile", "atlas", "foliage_atlas", "decal_atlas"}.Contains(preset)) throw new ArgumentException("Unknown preset: " + preset);
            return new AgentSettings(file == null ? Resource("presets/" + preset) : ReadJson(File.ReadAllText(file)), alpha);
        }
        public AgentSettings(JObject json, string alpha = null)
        {
            Json = (JObject)json.DeepClone();
            if (alpha != null) Json["alpha"] = alpha;
            Validate(Json, Resource("settings.schema"), "settings");
            JObject filters = (JObject)Json["filters"];
            foreach (string group in new[]{"height", "metallic", "smoothness"})
            {
                JObject filter = (JObject)filters[group];
                int selected = filter.Properties().Count(p => p.Name.StartsWith("use", StringComparison.Ordinal) && p.Value.Type == JTokenType.Boolean && (bool)p.Value);
                if (selected != 1) throw new ArgumentException(group + " must select exactly one source");
            }
            foreach (string group in new[]{"height", "metallic", "smoothness"})
                foreach (JProperty prop in ((JObject)filters[group]).Properties().Where(p => p.Name.StartsWith("MaskLow")))
                {
                    string high = prop.Name.Replace("Low", "High");
                    if ((double)prop.Value >= (double)filters[group][high]) throw new ArgumentException(group + " mask low must be below high");
                }
            if ((bool)Json["tiling"]["enabled"] && ((double)Json["tiling"]["OverlapX"] <= 0 || (double)Json["tiling"]["OverlapY"] <= 0)) throw new ArgumentException("Tiling overlap must be positive (zero is a singular upstream boundary)");
            Project = new ProjectObject();
            foreach (KeyValuePair<string, Type> entry in FilterTypes)
                typeof(ProjectObject).GetField(ProjectFields[entry.Key]).SetValue(Project, ConvertFilter((JObject)filters[entry.Key], entry.Value));
        }
        public static void Validate(JToken value, JObject schema, string path)
        {
            string kind = (string)schema["type"];
            if (kind == "object")
            {
                JObject obj = value as JObject;
                if (obj == null) throw new ArgumentException(path + " must be an object");
                JObject properties = (JObject)schema["properties"];
                foreach (JProperty prop in obj.Properties())
                {
                    if (properties[prop.Name] == null) throw new ArgumentException("Unknown key: " + path + "." + prop.Name);
                    Validate(prop.Value, (JObject)properties[prop.Name], path + "." + prop.Name);
                }
                foreach (JToken key in (JArray)schema["required"])
                    if (obj[(string)key] == null) throw new ArgumentException("Missing key: " + path + "." + key);
            }
            else if (kind == "array")
            {
                JArray array = value as JArray;
                if (array == null || array.Count != (int)schema["minItems"]) throw new ArgumentException(path + " has an invalid array length");
                for (int i = 0; i < array.Count; i++) Validate(array[i], (JObject)schema["items"], path + "[" + i + "]");
            }
            else if (kind == "number" || kind == "integer")
            {
                if (value.Type != JTokenType.Integer && value.Type != JTokenType.Float) throw new ArgumentException(path + " must be numeric");
                double number = (double)value;
                if (double.IsNaN(number) || double.IsInfinity(number) || Math.Abs(number) > float.MaxValue || (kind == "integer" && number != Math.Truncate(number))) throw new ArgumentException(path + " must be finite in the filter's numeric representation" + (kind == "integer" ? " and integral" : ""));
                if (schema["minimum"] != null && number < (double)schema["minimum"] || schema["maximum"] != null && number > (double)schema["maximum"]) throw new ArgumentException(path + " is outside the GUI range");
            }
            else if (kind == "boolean" && value.Type != JTokenType.Boolean || kind == "string" && value.Type != JTokenType.String) throw new ArgumentException(path + " has the wrong type");
            if (schema["enum"] != null && !schema["enum"].Any(v => JToken.DeepEquals(v, value))) throw new ArgumentException(path + " has an unsupported value");
            if (schema["const"] != null && !JToken.DeepEquals(schema["const"], value)) throw new ArgumentException(path + " has an unsupported version");
        }
        static object ConvertFilter(JObject values, Type type)
        {
            object settings = Activator.CreateInstance(type);
            foreach (JProperty prop in values.Properties())
            {
                FieldInfo field = type.GetField(prop.Name);
                JToken value = prop.Value;
                object converted = field.FieldType == typeof(Color) ? (object)new Color((float)value[0], (float)value[1], (float)value[2], 1) :
                    field.FieldType == typeof(Vector2) ? new Vector2((float)value[0], (float)value[1]) : value.ToObject(field.FieldType);
                field.SetValue(settings, converted);
                FieldInfo display = type.GetField(prop.Name + "Text");
                if (display != null) display.SetValue(settings, Convert.ToString(converted, CultureInfo.InvariantCulture));
            }
            return settings;
        }
    }
}
