import io, sys

p = r"C:\Users\jason\OneDrive\Desktop\MapleRide\Assets\Editor\HanakageEncounterSetup.cs"
s = io.open(p, encoding="utf-8").read()
old = "            enc.Tick(Dt);\n"
new = "            enc.Tick(Dt);\n            TickPerformance(boot, Dt);\n"
if "TickPerformance(boot, Dt);" in s:
    print("already patched")
else:
    n = s.count(old)
    s = s.replace(old, new)
    io.open(p, "w", encoding="utf-8").write(s)
    print("patched", n)
