"""Build the JSON catalog from upstream constructor defaults and GUI ranges.

Generated files are checked in, and --check detects drift. No DefaultValue
annotations are used: some disagree with the actual constructor.
"""
import argparse
import copy
import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
FILTERS = {"diffuse": ("EditDiffuse", "EditDiffuseSettings"), "height": ("HeightFromDiffuse", "HeightFromDiffuseSettings"), "normal": ("NormalFromHeight", "NormalFromHeightSettings"), "metallic": ("Metallic", "MetallicSettings"), "smoothness": ("Smoothness", "SmoothnessSettings"), "ao": ("AOFromNormal", "AOSettings"), "edge": ("EdgeFromNormal", "EdgeSettings")}

def scalar(value):
    value = value.strip().rstrip("f")
    if value in ("true", "false"):
        return value == "true"
    if value == "Color.black":
        return [0, 0, 0]
    if value == "Vector2.zero":
        return [0, 0]
    return float(value) if "." in value else int(value)

def bounds(group, field):
    if "Weight" in field or field.startswith(("Mask", "Sample")) or field in ("BaseSmoothness", "MetalSmoothness", "Blend", "BlendAmount", "Angularity", "AngularIntensity", "ShapeRecognition", "ShapeBias", "SampleBlend", "EdgeAmount", "CreviceAmount"):
        return (0, 1)
    if field == "LightRotation": return (-3.14, 3.14)
    if field == "Depth": return (0, 256)
    if field == "SpreadBoost": return (1, 5)
    if field == "Spread": return (10, 200 if group == "height" else 100)
    if field == "Pinch": return (.1, 10)
    if field == "Pillow": return (.1, 5)
    if field == "BlurOverlay": return (-10, 10)
    if field in ("BlurSize", "AvgColorBlurSize", "OverlayBlurSize", "SlopeBlur"):
        return (0 if group in ("metallic", "smoothness") and field == "BlurSize" else 10 if field == "OverlayBlurSize" else 5, 100)
    if field == "BlurContrast": return (-1, 1)
    if field.startswith("Blur") and field.endswith("Contrast"):
        return {"height": (-5, 5), "normal": (0, 50), "edge": (0, 5)}[group]
    if field == "FinalContrast": return {"diffuse": (-2, 2), "height": (-10, 10), "normal": (0, 10), "metallic": (-2, 2), "smoothness": (-2, 2), "ao": (.1, 10), "edge": (.1, 30)}[group]
    if field == "FinalBias": return (-1, 1) if group in ("height", "ao", "edge") else (-.5, .5)
    if field == "FinalGain": return (-.5, .5)
    if group == "diffuse": return (0, 1)
    raise ValueError(f"Unclassified range: {group}.{field}")

def obj(properties):
    return {"type": "object", "additionalProperties": False, "required": list(properties), "properties": properties}

def array_schema(default):
    return {"type": "array", "minItems": len(default), "maxItems": len(default), "items": {"type": "number", "minimum": 0, "maximum": 1}}

def catalog():
    defaults, filters = {}, {}
    for group, (file, name) in FILTERS.items():
        src = (ROOT / f"Assets/Scripts/{file}Gui.cs").read_text(encoding="utf-8-sig")
        definition = src.split("public class ")[1].split(f"public {name}(")[0]
        fields = re.findall(r"public (float|int|bool|Color|Vector2) (\w+)\s*;", definition)
        ctor = src.split(f"public {name}(", 1)[1].split("public class", 1)[0]
        values = dict(re.findall(r"this\.(\w+)\s*=\s*([^;]+);", ctor))
        props, default = {}, {}
        for kind, field in fields:
            value = scalar(values[field])
            default[field] = value
            spec = {"type": "boolean"} if kind == "bool" else array_schema(value) if kind in ("Color", "Vector2") else {"type": "integer" if kind == "int" else "number", "minimum": bounds(group, field)[0], "maximum": bounds(group, field)[1]}
            spec["default"] = value
            if field.startswith("SampleUV") or field == "BlendAmount" or (group == "smoothness" and (field.endswith("3") or field.startswith("Isolate"))):
                spec["description"] = "Serialized upstream control; inactive in exported pixels. Preserved for GUI parity."
            props[field] = spec
        defaults[group] = default
        filters[group] = obj(props)
    align = {"enabled": False, "pointTL": [0, 1], "pointTR": [1, 1], "pointBL": [0, 0], "pointBR": [1, 0], "LensDistort": 0, "PerspectiveX": 0, "PerspectiveY": 0}
    tiling = {"enabled": False, "technique": "overlap", "width": 2048, "height": 2048, "Falloff": .1, "OverlapX": .2, "OverlapY": .2, "SplatRotation": 0, "SplatRotationRandom": .25, "SplatScale": 1, "SplatWobble": .2, "SplatRandomize": 0}
    ap = {"enabled": {"type": "boolean", "default": False}}
    for key, val in align.items():
        if key == "enabled": continue
        ap[key] = array_schema(val) if isinstance(val, list) else {"type": "number", "minimum": -1 if key == "LensDistort" else -5, "maximum": 1 if key == "LensDistort" else 5}
        if isinstance(val, list):
            ap[key]["items"] = {"type": "number"} # Dragged GUI UV corners are not clamped.
        ap[key]["default"] = val
    ap["PerspectiveY"]["description"] = "Upstream shader no-op; preserved for parity."
    tp = {"enabled": {"type": "boolean"}, "technique": {"type": "string", "enum": ["overlap", "splat"]}}
    for key, val in tiling.items():
        if key in tp: continue
        tp[key] = {"type": "integer", "enum": [512, 1024, 2048, 4096]} if key in ("width", "height") else {"type": "number", "minimum": .01 if key == "Falloff" else .5 if key == "SplatScale" else 0, "maximum": 2 if key == "SplatScale" else 1}
        tp[key]["default"] = val
    defaults = {"version": 1, "alpha": "preserve", "filters": defaults, "alignment": align, "tiling": tiling}
    schema = obj({"version": {"type": "integer", "const": 1}, "alpha": {"type": "string", "enum": ["preserve", "opaque"]}, "filters": obj(filters), "alignment": obj(ap), "tiling": obj(tp)})
    schema["$schema"] = "https://json-schema.org/draft/2020-12/schema"
    return defaults, schema

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    defaults, schema = catalog()
    files = {"Assets/Resources/Agent/settings.schema.json": schema, "Assets/Resources/Agent/gui_defaults.json": defaults}
    for name in ("ground_tile", "wall_tile", "atlas", "foliage_atlas", "decal_atlas"):
        preset = copy.deepcopy(defaults)
        if name != "atlas":
            preset["filters"]["metallic"].update(FinalContrast=0, FinalBias=-.5)
        # Modest normal contrast; presets are conservative starting points.
        preset["filters"]["normal"]["FinalContrast"] = 2
        files[f"Assets/Resources/Agent/presets/{name}.json"] = preset
    for name, content in files.items():
        path = ROOT / name
        data = json.dumps(content, indent=2, ensure_ascii=False) + "\n"
        if args.check:
            if not path.exists() or path.read_text(encoding="utf-8") != data:
                raise SystemExit(f"Catalog drift: {name}")
        else:
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(data, encoding="utf-8")
    print(f"SETTINGS-CATALOG PASS ({len(files)} files)")

if __name__ == "__main__": main()
