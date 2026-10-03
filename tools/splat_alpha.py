"""Add independent alpha accumulation alongside unchanged splat RGB/height."""
from pathlib import Path
ROOT = Path(__file__).resolve().parents[1]

def main():
    shader = ROOT / "Assets/Shaders/Resources/Blit_Seamless_Texture_Maker.shader"
    src = shader.read_text(encoding="utf-8")
    if "frag_splat_alpha" not in src:
        begin = src.index("\tfloat4 frag_splat (")
        end = src.index("\tfloat4 frag_clear", begin)
        alpha = src[begin:end].replace("frag_splat (", "frag_splat_alpha (")
        alpha = alpha.replace("float4 tempTex = float4(0,0,0,0);", "float alpha = tex2Dlod(_TargetAlphaTex, float4(IN.uv,0,0)).r;")
        alpha = alpha.replace("targetTex.xyz = lerp( thisTex.xyz, targetTex.xyz, TexBlend );", "alpha = lerp(thisTex.a, alpha, TexBlend);")
        alpha = alpha.replace("return targetTex;", "return float4(alpha,0,0,1);")
        src = src.replace("\tfloat4 frag_clear", alpha + "\tfloat4 frag_clear", 1)
        src = src.replace("float _PreserveAlpha;", "float _PreserveAlpha;\n\tsampler2D _TargetAlphaTex;\n\tsampler2D _FinalAlphaTex;")
        src = src.replace("return float4(mainTex.xyz,1);", "return float4(mainTex.xyz, _PreserveAlpha > 0.5 ? tex2Dlod(_FinalAlphaTex,float4(IN.uv,0,0)).r : 1);")
        last = src.rfind("}")
        subend = src.rfind("}", 0, last)
        src = src[:subend] + '''
        Pass {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag_splat_alpha
            #pragma target 3.0
            ENDCG
        }
''' + src[subend:]
        shader.write_text(src, encoding="utf-8")
    path = ROOT / "Assets/Scripts/TilingTextureMakerGui.cs"
    src = path.read_text(encoding="utf-8")
    if "_AlphaTemp" not in src:
        src = src.replace("\tRenderTexture _SplatTemp;", "\tRenderTexture _AlphaTemp;\n\tRenderTexture _AlphaTempAlt;\n\tRenderTexture _SplatTemp;")
        src = src.replace('// Clear the ping pong buffers', '''// Independent coverage buffers: RGB alpha is reserved for the height guide.
        bool carryAlpha = blitMaterial.GetFloat("_PreserveAlpha") > 0.5f && (TexName == "_DiffuseMap" || TexName == "_DiffuseMapOriginal");
        _AlphaTemp = RenderTexture.GetTemporary(NewTexSizeX, NewTexSizeY, 0, RenderTextureFormat.RHalf, RenderTextureReadWrite.Linear);
        _AlphaTempAlt = RenderTexture.GetTemporary(NewTexSizeX, NewTexSizeY, 0, RenderTextureFormat.RHalf, RenderTextureReadWrite.Linear);
        Graphics.Blit(Texture2D.blackTexture, _AlphaTemp);
        Graphics.Blit(Texture2D.blackTexture, _AlphaTempAlt);
        // Clear the ping pong buffers''')
        src = src.replace("Graphics.Blit (textureToTile, _SplatTemp, blitMaterial, 1);", '''Graphics.Blit (textureToTile, _SplatTemp, blitMaterial, 1);
                if (carryAlpha) { blitMaterial.SetTexture("_TargetAlphaTex", _AlphaTempAlt); Graphics.Blit(textureToTile, _AlphaTemp, blitMaterial, 4); }''')
        src = src.replace("Graphics.Blit (textureToTile, _SplatTempAlt, blitMaterial, 1);", '''Graphics.Blit (textureToTile, _SplatTempAlt, blitMaterial, 1);
                if (carryAlpha) { blitMaterial.SetTexture("_TargetAlphaTex", _AlphaTemp); Graphics.Blit(textureToTile, _AlphaTempAlt, blitMaterial, 4); }''')
        src = src.replace("if (isEven) {\n\t\t\tGraphics.Blit (_SplatTempAlt", '''blitMaterial.SetFloat("_PreserveAlpha", carryAlpha ? 1 : 0);
        blitMaterial.SetTexture("_FinalAlphaTex", isEven ? _AlphaTempAlt : _AlphaTemp);
        if (isEven) {
            Graphics.Blit (_SplatTempAlt''')
        src = src.replace("CleanupTexture( _SplatTempAlt );", '''CleanupTexture( _SplatTempAlt );
        RenderTexture.ReleaseTemporary(_AlphaTemp); RenderTexture.ReleaseTemporary(_AlphaTempAlt);
        _AlphaTemp = null; _AlphaTempAlt = null;''')
        # Restore the job's policy before each map: numeric maps never consume coverage.
        src = src.replace("bool doStuff = false;", "bool doStuff = false;\n    bool agentPreserveAlpha = false;")
        src = src.replace("Start(); Initialize();", "Start(); Initialize(); agentPreserveAlpha = preserveAlpha;")
        src = src.replace('RenderTexture TileTexture ( Texture2D textureToTile, RenderTexture textureTarget, string TexName ) {', '''RenderTexture TileTexture ( Texture2D textureToTile, RenderTexture textureTarget, string TexName ) {
        blitMaterial.SetFloat("_PreserveAlpha", agentPreserveAlpha && (TexName == "_DiffuseMap" || TexName == "_DiffuseMapOriginal") ? 1 : 0);''')
        path.write_text(src, encoding="utf-8")
    print("SPLAT-ALPHA installed")

if __name__ == "__main__": main()
