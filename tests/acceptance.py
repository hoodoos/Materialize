"""Real GPU acceptance, CLI/GUI reference parity and cold/batch measurements."""
import argparse
import hashlib
import json
import subprocess
import time
from pathlib import Path
import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
EXE = ROOT / "build/windows/materialize.exe"
MAPS = "albedo,diffuse,height,normal,metallic,smoothness,roughness,edge,ao,orm"

def fixtures():
    target = ROOT / "tests/fixtures"
    target.mkdir(parents=True, exist_ok=True)
    y, x = np.mgrid[:1024, :1024]
    rng = np.random.default_rng(75)
    noise = rng.integers(0, 25, (1024, 1024))
    textures = {
        "ground": np.stack([85 + noise + (x // 64 % 2)*25, 90 + noise, 70 + noise, np.full_like(x,255)], -1),
        "wall": np.stack([80+(x//128%2)*55+(y//64%2)*15, 60+noise, 45+noise, np.full_like(x,255)], -1),
        "atlas": np.stack([x%256, y%256, (x+y)%256, np.where((x%256-128)**2+(y%256-128)**2<90**2,255,0)], -1)
    }
    for name, pixels in textures.items():
        path = target / (name + ".png")
        if not path.exists(): Image.fromarray(pixels.astype(np.uint8)).save(path)
    return target

def run(name, args, expected=0, timeout=360):
    log = ROOT / "build/evidence" / (name + ".log")
    log.parent.mkdir(parents=True, exist_ok=True)
    begin = time.perf_counter()
    result = subprocess.run([str(EXE), "-batchmode", "-force-d3d11", "-logFile", str(log), "--", *map(str,args)], cwd=ROOT, timeout=timeout, creationflags=subprocess.CREATE_NO_WINDOW)
    seconds = time.perf_counter()-begin
    text = log.read_text(encoding="utf-8", errors="replace")
    if result.returncode != expected or (expected == 0 and "MATERIALIZE BATCH PASS" not in text):
        raise AssertionError(f"{name}: exit {result.returncode}, expected {expected}\n" + text[-4500:])
    print(f"{name}: PASS {seconds:.3f}s", flush=True)
    return seconds

def sha(path): return hashlib.sha256(path.read_bytes()).hexdigest()

def diff(left, right, rgb_only=False):
    a, b = np.array(Image.open(left).convert("RGBA")), np.array(Image.open(right).convert("RGBA"))
    if a.shape != b.shape: raise AssertionError("Shape mismatch")
    if rgb_only: a,b = a[:,:,:3],b[:,:,:3]
    return int(np.max(np.abs(a.astype(np.int16)-b.astype(np.int16))))

def verify_presets(textures, evidence):
    names=["ground_tile","wall_tile","atlas","foliage_atlas","decal_atlas"]
    jobs={"version":1,"jobs":[{"in":str(textures/("atlas.png" if "atlas" in name else "ground.png")),"preset":name,"maps":["albedo","metallic"],"out":str(evidence/"presets"/name)} for name in names]}
    manifest=evidence/"presets.json"; manifest.write_text(json.dumps(jobs))
    seconds=run("five-presets",["--batch",manifest,"--overwrite"])
    for name in names:
        if name!="atlas":
            assert np.all(np.array(Image.open(evidence/"presets"/name/"metallic.png"))[:,:,:3]==0), "Nonmetal preset: "+name
        if "atlas" in name:
            assert diff(evidence/"presets"/name/"albedo.png",textures/"atlas.png")==0, "Preset lost transparency: "+name
    return {"seconds":seconds,"nonmetal_presets":"ground_tile,wall_tile,foliage_atlas,decal_atlas","transparent_atlases":"atlas,foliage_atlas,decal_atlas"}

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--smoke", action="store_true")
    parser.add_argument("--benchmark", action="store_true")
    args = parser.parse_args()
    textures = fixtures()
    evidence = ROOT / "build/evidence"
    evidence.mkdir(parents=True, exist_ok=True)
    defaults = json.loads((ROOT/"Assets/Resources/Agent/gui_defaults.json").read_text())
    # Nondefault values ensure parity exercises settings application.
    defaults["filters"]["height"]["FinalContrast"] = 2.1
    defaults["filters"]["normal"]["FinalContrast"] = 3.4
    defaults["filters"]["smoothness"]["BaseSmoothness"] = .27
    defaults["filters"]["ao"]["Depth"] = 73
    settings = evidence/"settings.json"
    settings.write_text(json.dumps(defaults,indent=2)+"\n")
    reports = {}
    names = ["ground"] if args.smoke else ["ground","wall","atlas"]
    for name in names:
        output = evidence/name
        common = ["--in",textures/(name+".png"),"--settings",settings,"--maps",MAPS,"--overwrite"]
        cold = run(name, [*common,"--out",output])
        if args.smoke: return
        repeat = evidence/(name+"-repeat")
        reference = evidence/(name+"-gui")
        run(name+"-repeat",[*common,"--out",repeat])
        run(name+"-gui",[*common,"--out",reference,"--reference"])
        deltas = {}
        for path in output.glob("*.png"):
            assert sha(path)==sha(repeat/path.name), "Nondeterministic: "+path.name
            delta = diff(path,reference/path.name)
            assert delta<=1, f"GUI parity: {name}/{path.name}: {delta}"
            deltas[path.name]=delta
        assert sha(output/"settings.used.json")==sha(repeat/"settings.used.json"), "Resolved metadata is nondeterministic"
        orm = np.array(Image.open(output/"orm.png"))
        for index,mapname,invert in [(0,"ao",False),(1,"smoothness",True),(2,"metallic",False)]:
            value = np.array(Image.open(output/(mapname+".png")))[:,:,0]
            assert np.array_equal(orm[:,:,index],255-value if invert else value), "ORM packing"
        reports[name]={"cold_seconds":cold,"parity_max_channel_delta":deltas,"png_sha256":{p.name:sha(p) for p in output.glob("*.png")}}
    # Registration, preserve/opaque alpha and both tiling methods.
    for mode in ("alignment","overlap","splat"):
        config = json.loads(json.dumps(defaults))
        if mode == "alignment":
            config["alignment"].update(enabled=True,LensDistort=.15,PerspectiveX=.25)
        else: config["tiling"].update(enabled=True,technique=mode,width=512,height=1024)
        file = evidence/(mode+".json"); file.write_text(json.dumps(config))
        base=["--in",textures/"atlas.png","--settings",file,"--maps",MAPS,"--overwrite"]
        for policy in ("preserve","opaque"):
            output=evidence/(mode+"-"+policy)
            run(mode+"-"+policy,[*base,"--alpha",policy,"--out",output])
            image=np.array(Image.open(output/"albedo.png").convert("RGBA"))
            assert np.any(image[:,:,3]<255) if policy=="preserve" else np.all(image[:,:,3]==255), "Alpha policy"
        assert diff(evidence/(mode+"-preserve/albedo.png"),evidence/(mode+"-opaque/albedo.png"),True)==0, "Alpha changed RGB"
        reference=evidence/(mode+"-gui")
        run(mode+"-gui",[*base,"--alpha","opaque","--out",reference,"--reference"])
        deltas={p.name:diff(p,reference/p.name,True) for p in (evidence/(mode+"-preserve")).glob("*.png")}
        assert max(deltas.values())<=1, f"Frozen upstream transform RGB parity: {mode}: {deltas}"
        reports[mode]={"upstream_rgb_max_channel_delta":deltas}
        for policy in ("preserve","opaque"):
            output=evidence/(mode+"-"+policy)
            repeat=evidence/(mode+"-"+policy+"-repeat")
            run(mode+"-"+policy+"-repeat",[*base,"--alpha",policy,"--out",repeat])
            for path in output.glob("*.png"): assert sha(path)==sha(repeat/path.name), "Transform nondeterminism"
    # Caller-provided maps are authoritative, no regeneration.
    run("supplied",["--in",textures/"ground.png","--preset","ground_tile","--maps","normal,orm","--map-in",f"normal={evidence/'ground/normal.png'}","--map-in",f"height={evidence/'ground/height.png'}","--out",evidence/"supplied","--overwrite"])
    assert sha(evidence/"ground/normal.png")==sha(evidence/"supplied/normal.png")
    # Sign calibration: image-down height gradient tilts the OpenGL normal +Y.
    yy,xx=np.mgrid[:512,:1024]
    height=np.clip((xx/1023+yy/511)*127.5,0,255).astype(np.uint8)
    ramp=evidence/"height-ramp.png"; Image.fromarray(height).save(ramp)
    rectangle=evidence/"rectangle.png"; Image.open(textures/"ground.png").resize((1024,512)).save(rectangle)
    config=json.loads(json.dumps(defaults)); config["filters"]["normal"].update(UseDiffuse=False,ShapeRecognition=0)
    ramp_settings=evidence/"ramp.json"; ramp_settings.write_text(json.dumps(config))
    run("normal-convention",["--in",rectangle,"--settings",ramp_settings,"--map-in",f"height={ramp}","--maps","normal","--out",evidence/"ramp","--overwrite"])
    normal=np.array(Image.open(evidence/"ramp/normal.png"))
    assert normal.shape[:2]==(512,1024), "Rectangular output dimensions"
    center=normal[256,512,:3]
    assert center[0]<128 and center[1]>128 and center[2]>128, f"OpenGL normal signs: {center}"
    run("mismatched-input",["--in",textures/"ground.png","--preset","atlas","--map-in",f"height={ramp}","--maps","normal","--out",evidence/"mismatch","--overwrite"],expected=1)
    assert not (evidence/"mismatch").exists()
    before=sha(evidence/"ground/normal.png")
    run("timeout",["--in",textures/"ground.png","--preset","atlas","--maps","normal","--out",evidence/"ground","--overwrite","--timeout","0.001"],expected=1)
    assert sha(evidence/"ground/normal.png")==before, "Timeout damaged existing outputs"
    assert not list(evidence.glob("*.staging-*")), "Failed jobs left staging outputs"
    run("unknown-flag",["--bogus","x"],expected=2)
    run("protected-output",["--in",textures/"ground.png","--preset","atlas","--maps","normal","--out",textures,"--overwrite"],expected=2)
    assert (textures/"ground.png").exists(), "Overwrite deleted an input"
    run("existing-output",["--in",textures/"ground.png","--preset","atlas","--maps","normal","--out",evidence/"ground"],expected=1)
    if args.benchmark:
        batch={"version":1,"jobs":[{"in":str(textures/(names[i%3]+".png")),"preset":"ground_tile","maps":["normal","orm"],"out":str(evidence/"batch"/str(i))} for i in range(100)]}
        manifest=evidence/"batch.json"; manifest.write_text(json.dumps(batch))
        seconds=run("batch-100",["--batch",manifest,"--overwrite"],timeout=3600)
        for i in range(3):
            # Adjacent jobs must not inherit earlier texture/setting state.
            assert (evidence/"batch"/str(i)/"normal.png").exists()
            assert sha(evidence/"batch"/str(i)/"normal.png")==sha(evidence/"batch"/str(i+3)/"normal.png")
        reports["batch100"]={"seconds":seconds,"seconds_per_texture":seconds/100,"maps":["normal","orm"]}
    reports["presets"]=verify_presets(textures,evidence)
    (evidence/"report.json").write_text(json.dumps(reports,indent=2)+"\n")
    print("GPU-ACCEPTANCE PASS",flush=True)

if __name__=="__main__":main()
