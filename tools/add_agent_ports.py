"""Install bounded shared execution ports without duplicating filter math."""
from pathlib import Path
ROOT = Path(__file__).resolve().parents[1]
GROUPS = {"EditDiffuse": "diffuse", "HeightFromDiffuse": "height", "NormalFromHeight": "normal", "Metallic": "metallic", "Smoothness": "smoothness", "AOFromNormal": "ao", "EdgeFromNormal": "edge"}

def main():
    for name in GROUPS:
        path = ROOT / f"Assets/Scripts/{name}Gui.cs"
        src = path.read_text(encoding="utf-8-sig")
        marker = "\t// B75: shared initialization; no Update/OnGUI required."
        if marker in src: continue
        init = f"""
{marker}
    public void InitializeForAgent(MainGui gui, GameObject preview) {{
        enabled = false;
        testObject = preview;
        thisMaterial = new Material(Shader.Find("Hidden/Blit_Shader"));
        {'MainGuiScript = gui;' if name != 'NormalFromHeight' else ''}
        Start();
    }}
"""
        pos = src.index("\n", src.index(f"public class {name}Gui : MonoBehaviour"))
        pos = src.index("{", src.index(f"public class {name}Gui : MonoBehaviour")) + 1
        src = src[:pos] + init + src[pos:]
        if name == "EditDiffuse":
            src = src.replace("\tvoid InitializeTextures()", "\tpublic void InitializeTextures()")
            src = src.replace("\tIEnumerator ProcessDiffuse(", "\tpublic IEnumerator ProcessDiffuse(")
            src = src.replace("\tIEnumerator ProcessBlur ()", "\tpublic IEnumerator ProcessBlur ()")
        # Preview swatches are 1x1; the old code writes outside their bounds.
        src = src.replace(".SetPixel (1, 1,", ".SetPixel (0, 0,")
        path.write_text(src, encoding="utf-8")

    path = ROOT / "Assets/Scripts/AlignmentGui.cs"
    src = path.read_text(encoding="utf-8-sig")
    if "public IEnumerator ExecuteForAgent" not in src:
        port = '''
    // Shared with the GUI: explicit uniforms and the original SetMaps passes.
    public IEnumerator ExecuteForAgent(MaterializeAgent.AlignmentSettings settings, GameObject preview, bool preserveAlpha) {
        enabled = false;
        testObject = preview;
        thisMaterial = new Material(Shader.Find("Hidden/Blit_Alignment"));
        Initialize();
        pointTL = settings.pointTL; pointTR = settings.pointTR;
        pointBL = settings.pointBL; pointBR = settings.pointBR;
        LensDistort = settings.LensDistort;
        PerspectiveX = settings.PerspectiveX; PerspectiveY = settings.PerspectiveY;
        blitMaterial.SetVector("_PointTL", pointTL); blitMaterial.SetVector("_PointTR", pointTR);
        blitMaterial.SetVector("_PointBL", pointBL); blitMaterial.SetVector("_PointBR", pointBR);
        blitMaterial.SetFloat("_Width", textureToAlign.width); blitMaterial.SetFloat("_Height", textureToAlign.height);
        blitMaterial.SetFloat("_Lens", LensDistort);
        blitMaterial.SetFloat("_PerspectiveX", PerspectiveX); blitMaterial.SetFloat("_PerspectiveY", PerspectiveY);
        blitMaterial.SetFloat("_PreserveAlpha", preserveAlpha ? 1 : 0);
        yield return SetMaps();
        Close();
    }
'''
        pos = src.index("{", src.index("public class AlignmentGui")) + 1
        src = src[:pos] + port + src[pos:]
        path.write_text(src, encoding="utf-8")

    path = ROOT / "Assets/Scripts/TilingTextureMakerGui.cs"
    src = path.read_text(encoding="utf-8-sig")
    if "public IEnumerator ExecuteForAgent" not in src:
        port = '''
    public IEnumerator ExecuteForAgent(MaterializeAgent.TilingSettings settings, GameObject preview, bool preserveAlpha) {
        enabled = false; testObject = preview;
        Start(); Initialize();
        NewTexSizeX = settings.width; NewTexSizeY = settings.height;
        Falloff = settings.Falloff; OverlapX = settings.OverlapX; OverlapY = settings.OverlapY;
        SplatRotation = settings.SplatRotation; SplatRotationRandom = settings.SplatRotationRandom;
        SplatScale = settings.SplatScale; SplatWobble = settings.SplatWobble; SplatRandomize = settings.SplatRandomize;
        tileTech = settings.technique == "splat" ? TileTechnique.Splat : TileTechnique.Overlap;
        blitMaterial.SetFloat("_PreserveAlpha", preserveAlpha ? 1 : 0);
        if (NewTexSizeX == NewTexSizeY) SKSquare();
        else if (NewTexSizeX > NewTexSizeY) {
            int ratio = NewTexSizeX / NewTexSizeY;
            if (ratio == 2) SKRectWide(); else if (ratio == 4) SKRectWide2(); else SKRectWide3();
        } else {
            int ratio = NewTexSizeY / NewTexSizeX;
            if (ratio == 2) SKRectTall(); else if (ratio == 4) SKRectTall2(); else SKRectTall3();
        }
        yield return TileTextures();
        yield return SetMaps();
        Close();
    }
'''
        pos = src.index("{", src.index("public class TilingTextureMakerGui")) + 1
        src = src[:pos] + port + src[pos:]
        path.write_text(src, encoding="utf-8")

    path = ROOT / "Assets/Shaders/Resources/Blit_Alignment.shader"
    src = path.read_text(encoding="utf-8-sig")
    if "_PreserveAlpha" not in src:
        src = src.replace("CGINCLUDE", "CGINCLUDE\n\tfloat _PreserveAlpha;")
        src = src.replace("return float4( c.xyz, 1 );", "return float4(c.xyz, _PreserveAlpha > 0.5 ? c.a : 1);")
        path.write_text(src, encoding="utf-8")
    path = ROOT / "Assets/Shaders/Resources/Blit_Seamless_Texture_Maker.shader"
    src = path.read_text(encoding="utf-8-sig")
    if "_PreserveAlpha" not in src:
        src = src.replace("CGINCLUDE", "CGINCLUDE\n\tfloat _PreserveAlpha;")
        # Overlap already blends RGBA; do not change its existing RGB equations.
        src = src.replace("return half4( mainTex.xyz, 1.0 );", "return half4(mainTex.xyz, _PreserveAlpha > 0.5 ? mainTex.a : 1.0);")
        path.write_text(src, encoding="utf-8")
    print("AGENT-PORTS installed")

if __name__ == "__main__": main()
