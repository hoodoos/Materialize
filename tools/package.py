"""Package the verified Windows player and identify every shipped file."""
import hashlib
import json
import shutil
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]

def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

if __name__ == "__main__":
    player = ROOT / "build/windows"
    shutil.copyfile(ROOT/"README.md",player/"README.md")
    shutil.copytree(ROOT/"docs",player/"docs",dirs_exist_ok=True)
    shutil.copytree(ROOT/"Assets/Resources/Agent",player/"Assets/Resources/Agent",dirs_exist_ok=True,ignore=shutil.ignore_patterns("*.meta","Reference"))
    commit = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip()
    manifest = {"source_commit":commit,"source_url":f"https://github.com/hoodoos/Materialize/tree/{commit}","unity":"6000.6.3f1","files_sha256":{p.relative_to(player).as_posix():sha(p) for p in sorted(player.rglob("*")) if p.is_file() and p.name!="build.manifest.json"}}
    (player/"build.manifest.json").write_text(json.dumps(manifest,indent=2)+"\n",encoding="utf-8")
    archive=Path(shutil.make_archive(str(ROOT/"build/materialize-agent-windows-x64"),"zip",ROOT/"build","windows"))
    print(json.dumps({"archive":str(archive),"bytes":archive.stat().st_size,"sha256":sha(archive),"source_commit":commit}))
