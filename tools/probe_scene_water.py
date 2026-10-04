import re, sys, json, collections

PATH = r"C:\Users\jason\OneDrive\Desktop\MapleRide\Assets\Scenes\SakuraPass.unity"

doc_re = re.compile(r"^--- !u!(\d+) &(\d+)")

# Pass 1: collect GameObjects (id -> name, active), Transforms (goid -> (pos, parentTransformId, id)),
# MeshRenderers (goid), MeshFilters (goid -> mesh guid)
gos = {}          # goid -> [name, active]
tr_by_go = {}     # goid -> dict(pos, parent, id)
tr_id_to_go = {}  # transform id -> goid
mf_by_go = {}     # goid -> mesh ref string
mr_by_go = {}     # goid -> material ref string

cur_type = None
cur_id = None
buf = []

def flush():
    global cur_type, cur_id, buf
    if cur_type is None:
        return
    text = "\n".join(buf)
    if cur_type == 1:  # GameObject
        m = re.search(r"^  m_Name: (.*)$", text, re.M)
        a = re.search(r"^  m_IsActive: (\d)", text, re.M)
        gos[cur_id] = [m.group(1).strip() if m else "?", a.group(1) if a else "?"]
    elif cur_type == 4:  # Transform
        g = re.search(r"m_GameObject: \{fileID: (\d+)\}", text)
        p = re.search(r"m_LocalPosition: \{x: ([-\d.eE+]+), y: ([-\d.eE+]+), z: ([-\d.eE+]+)\}", text)
        s = re.search(r"m_LocalScale: \{x: ([-\d.eE+]+), y: ([-\d.eE+]+), z: ([-\d.eE+]+)\}", text)
        f = re.search(r"m_Father: \{fileID: (\d+)\}", text)
        if g:
            goid = g.group(1)
            tr_id_to_go[cur_id] = goid
            tr_by_go[goid] = {
                "pos": [float(x) for x in p.groups()] if p else None,
                "scale": [float(x) for x in s.groups()] if s else None,
                "father": f.group(1) if f else "0",
                "id": cur_id,
            }
    elif cur_type == 33:  # MeshFilter
        g = re.search(r"m_GameObject: \{fileID: (\d+)\}", text)
        me = re.search(r"m_Mesh: \{fileID: ([-\d]+), guid: ([0-9a-f]+)", text)
        if g:
            mf_by_go[g.group(1)] = me.group(2) if me else None
    elif cur_type == 23:  # MeshRenderer
        g = re.search(r"m_GameObject: \{fileID: (\d+)\}", text)
        mm = re.search(r"m_Materials:\s*\n  - \{fileID: ([-\d]+), guid: ([0-9a-f]+)", text)
        if g:
            mr_by_go[g.group(1)] = mm.group(2) if mm else None
    cur_type = None
    buf = []

with open(PATH, "r", encoding="utf-8", errors="replace") as fh:
    for line in fh:
        m = doc_re.match(line)
        if m:
            flush()
            t = int(m.group(1))
            if t in (1, 4, 23, 33):
                cur_type = t
                cur_id = m.group(2)
                buf = []
            else:
                cur_type = None
        elif cur_type is not None:
            if len(buf) < 400:
                buf.append(line.rstrip("\n"))
flush()

print("gameobjects:", len(gos), "transforms:", len(tr_by_go))

def world_pos(goid):
    """Accumulate local positions up the parent chain (ignores rotation; good enough
    because the coast/pass roots are unrotated)."""
    x = y = z = 0.0
    seen = 0
    g = goid
    while g and g in tr_by_go and seen < 64:
        t = tr_by_go[g]
        if t["pos"]:
            x += t["pos"][0]; y += t["pos"][1]; z += t["pos"][2]
        fa = t["father"]
        if fa == "0" or fa not in tr_id_to_go:
            break
        g = tr_id_to_go[fa]
        seen += 1
    return (x, y, z)

def path_of(goid):
    parts = []
    g = goid
    seen = 0
    while g and g in gos and seen < 64:
        parts.append(gos[g][0])
        t = tr_by_go.get(g)
        if not t:
            break
        fa = t["father"]
        if fa == "0" or fa not in tr_id_to_go:
            break
        g = tr_id_to_go[fa]
        seen += 1
    return "/".join(reversed(parts))

# Roots and their active flags
print("\n--- ROOTS ---")
for goid, (name, active) in gos.items():
    t = tr_by_go.get(goid)
    if t and t["father"] == "0":
        print(f"  active={active}  '{name}'  pos={t['pos']}")

KEY = re.compile(r"water|ocean|sea|shallow|lake|foreshore|beach|backdrop|start|pad|river|pond", re.I)
print("\n--- WATER/START-LIKE OBJECTS (world pos) ---")
rows = []
for goid, (name, active) in gos.items():
    if KEY.search(name):
        wp = world_pos(goid)
        rows.append((wp[1], name, active, wp, path_of(goid)))
rows.sort()
for y, name, active, wp, pth in rows[:200]:
    print(f"  y={y:10.2f}  active={active}  '{name}'  pos=({wp[0]:.1f},{wp[1]:.1f},{wp[2]:.1f})  {pth}")
print("total water/start-like:", len(rows))
