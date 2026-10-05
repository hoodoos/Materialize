"""Freeze upstream transform shaders for independent RGB regression evidence."""
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
REVISION = "366579f"

if __name__ == "__main__":
    target = ROOT / "Assets/Resources/Agent/Reference"
    target.mkdir(parents=True, exist_ok=True)
    for name in ("Blit_Alignment", "Blit_Seamless_Texture_Maker"):
        source = subprocess.check_output(["git", "show", f"{REVISION}:Assets/Shaders/Resources/{name}.shader"], cwd=ROOT).decode("utf-8-sig")
        source = source.replace(f'Shader "Hidden/{name}"', f'Shader "Hidden/AgentReference/{name}"', 1)
        source = "\n".join(line.rstrip() for line in source.splitlines()) + "\n"
        (target / (name + ".shader")).write_text(source, encoding="utf-8")
