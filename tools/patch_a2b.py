import io

p = r"C:\Users\jason\OneDrive\Desktop\MapleRide\Assets\Editor\HanakageNpcSetup.cs"
s = io.open(p, encoding="utf-8").read()

for tag in ("hanakage_pose_neutral.png", "hanakage_lookback.png", "hanakage_standing.png"):
    old = 'RenderShot(camPos, aim, 34f, Path.Combine(dir, "%s"), 1100, 850);' % tag
    new = 'RenderPosedShot(npc, camPos, aim, Path.Combine(dir, "%s"));' % tag
    assert old in s, tag
    s = s.replace(old, new)

io.open(p, "w", encoding="utf-8").write(s)
print("ok")
