"""Mechanical compile compatibility fixes; no image-filter changes."""
from pathlib import Path
import json

ROOT = Path(__file__).resolve().parents[1]

def main():
    # This upstream bundle omits OBJData/OBJLoader; keep its sources recoverable.
    for path in (ROOT / "Assets/OBJ-IO").rglob("*.cs"):
        text = path.read_text(encoding="utf-8-sig")
        if not text.startswith("#if MATERIALIZE_LEGACY_OBJ"):
            path.write_text("#if MATERIALIZE_LEGACY_OBJ\n" + text + "\n#endif\n", encoding="utf-8")
    path = ROOT / "Assets/Scripts/SuggestionGui.cs"
    text = path.read_text(encoding="utf-8-sig")
    text = text.replace("\tAuthenticateGui AuthenticateScript;", "\t// Upstream authentication source is absent from the GPL release.")
    text = text.replace("\t\tAuthenticateScript = AuthenticateObject.GetComponent<AuthenticateGui> ();", "")
    text = text.replace("\t\tstringEmail = AuthenticateScript.stringEmail;", "\t\tstringEmail = \"\";")
    path.write_text(text, encoding="utf-8")
    for path in (ROOT / "Assets/Plugins").glob("*.dll.meta"):
        text = path.read_text(encoding="utf-8")
        guid = next(line.split(": ")[1] for line in text.splitlines() if line.startswith("guid:"))
        native = path.name == "FreeImage.dll.meta"
        # Pin importer platform data; v1 PluginImporter cannot be read by Unity 6.
        path.write_text(f"""fileFormatVersion: 2
guid: {guid}
PluginImporter:
  serializedVersion: 2
  iconMap: {{}}
  executionOrder: {{}}
  isPreloaded: 0
  isOverridable: 0
  isExplicitlyReferenced: 0
  validateReferences: 1
  platformData:
  - first:
      '': Any
    second:
      enabled: {0 if native else 1}
      settings: {{}}
  - first:
      Any:
    second:
      enabled: {0 if native else 1}
      settings: {{}}
  - first:
      Editor: Editor
    second:
      enabled: 1
      settings:
        CPU: {'x86_64' if native else 'AnyCPU'}
        OS: {'Windows' if native else 'AnyOS'}
  - first:
      Standalone: Win64
    second:
      enabled: 1
      settings:
        CPU: {'x86_64' if native else 'AnyCPU'}
  userData:
  assetBundleName:
  assetBundleVariant:
""", encoding="utf-8")
    deps = {"com.unity.nuget.newtonsoft-json": "3.2.2", "com.unity.test-framework": "1.8.0"}
    for module in ["imageconversion", "imgui", "jsonserialize", "physics", "audio", "animation", "assetbundle", "unitywebrequest", "unitywebrequestwww", "ui"]:
        deps["com.unity.modules." + module] = "1.0.0"
    (ROOT / "Packages").mkdir(exist_ok=True)
    (ROOT / "Packages/manifest.json").write_text(json.dumps({"dependencies": deps}, indent=2) + "\n", encoding="utf-8")

if __name__ == "__main__":
    main()
