import re, shutil, os, sys
P = "ProjectSettings/ProjectSettings.asset"
ICON, BG, FG = "e9555789ce854c188b10e324a65b9e92", "a1672f4cafd845309d5799380a45bbe6", "14c5e04b36d94aabb114c9d9faba35a5"
ref = lambda g: "{fileID: 2800000, guid: %s, type: 3}" % g
s = open(P, encoding="utf-8").read()
if ICON in s:
    print("already applied"); sys.exit()
os.makedirs("_backup_before_icon", exist_ok=True)
shutil.copy2(P, "_backup_before_icon/ProjectSettings.asset")
# default icon (used for every size Unity isn't given explicitly)
s = s.replace("  m_BuildTargetIcons: []\n",
    "  m_BuildTargetIcons:\n  - m_BuildTarget: \n    m_Icons:\n    - serializedVersion: 2\n"
    "      m_Icon: " + ref(ICON) + "\n      m_Width: 128\n      m_Height: 128\n      m_Kind: 0\n", 1)
lines = s.split("\n"); out = []; target = None
for i, ln in enumerate(lines):
    m = re.match(r"  - m_BuildTarget: (\S*)$", ln)
    if m: target = m.group(1)
    if ln.startswith("  m_BuildTargetBatching") or ln.startswith("  m_BuildTargetShaderSettings"): target = None
    if ln == "    - m_Textures: []" and target in ("iPhone", "Android"):
        # look ahead for m_Kind
        kind = next(l for l in lines[i+1:i+5] if "m_Kind:" in l).split(":")[1].strip()
        if target == "Android" and kind == "2":
            out += ["    - m_Textures:", "      - " + ref(BG), "      - " + ref(FG)]; continue
        if target == "iPhone":
            out += ["    - m_Textures:", "      - " + ref(ICON)]; continue
    out.append(ln)
open(P, "w", encoding="utf-8", newline="\n").write("\n".join(out))
print("applied")
