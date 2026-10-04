using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Brings the Nagisa Bay Summit Cafe to life (2026-10-02, user: "filled with cyclists grabbing shaved ice,
/// waiting in line, a bunch of tables for people to eat their shaved ice").
///
/// The kiosk is baked into the streamed cells at route 8118 m, its serving window facing a paved terrace
/// about 11 m wide. Everything here is spawned at run time when the rider is near, so no region re-export:
///   * a queue at the window: parked cyclists straddling their bikes (clones of the Kuro riders, stopped)
///     plus a line of walkers, a few holding shaved-ice cups;
///   * a seating area: round tables with chairs and parasols on the terrace, people seated eating shaved ice,
///     cups on the tables, a few more bikes leaned beside tables.
/// People are clones of already-dressed Nagisa pedestrians (so they keep the Kuro-kit look, hair, no helmets);
/// the seated ones clone existing seated figures. Props are plain primitives tinted from a cloned CelLit
/// material. Spawned once; lives under the Nagisa root so region gating hides it elsewhere.
/// Disable with env MR_NAGISA_CAFE=0.
/// </summary>
public sealed class NagisaSummitCafe : MonoBehaviour
{
    public const string GroupName = "Summit Cafe Life";
    public const string RiderGroupName = "Summit Cafe Riders";
    const float CafeM = 8118f;
    const float WindowLat = 15.0f;          // serving-window plane, metres bay-ward of the centreline
    const float SpawnRadiusM = 420f;
    const float SeatH = 0.45f, TableH = 0.76f;

    static NagisaSummitCafe _instance;
    bool _spawning, _done;
    float _next;
    RegionDirector _regions;
    Material _celSource;
    readonly Dictionary<Color, Material> _mats = new Dictionary<Color, Material>();
    System.Random _rng;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (_instance != null) return;
        if (System.Environment.GetEnvironmentVariable("MR_NAGISA_CAFE") == "0") return;
        var go = new GameObject("~Nagisa Summit Cafe");
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<NagisaSummitCafe>();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Reset() { _instance = null; }

    void Update()
    {
        if (_spawning || Time.unscaledTime < _next) return;
        _next = Time.unscaledTime + 1.5f;
        if (_regions == null) _regions = FindFirstObjectByType<RegionDirector>();
        if (_regions == null || _regions.currentRegionId != NagisaBayLook.RegionId) { _done = false; return; }
        if (_done || !NagisaRoadIndex.Ready) return;
        var cam = Camera.main;
        if (cam == null) return;
        Vector3 cafe = NagisaRoadIndex.PositionAt(CafeM);
        if ((cam.transform.position - cafe).magnitude > SpawnRadiusM) return;
        var root = GameObject.Find("Nagisa Bay Environment");
        if (root == null) return;
        if (root.transform.Find(GroupName) != null) { _done = true; return; }
        StartCoroutine(Build(root.transform));
    }

    // ------------------------------------------------------------------ frame helpers

    /// <summary>World point at <paramref name="along"/> metres along the road from the cafe and <paramref name="lat"/> bay-ward.</summary>
    Vector3 W(float along, float lat, float y = 0f)
    {
        float d = CafeM + along;
        var p = NagisaRoadIndex.PositionAt(d);
        var p0 = p + BayDir(d) * lat;
        return new Vector3(p0.x, GroundY(p0, p.y) + y, p0.z);
    }

    /// <summary>World point in cafe coordinates (metres along the road from the kiosk, bay-ward, height). Used by QA cameras.</summary>
    public static Vector3 Point(float along, float lat, float y = 0f)
    {
        float d = CafeM + along;
        var p = NagisaRoadIndex.PositionAt(d);
        var p0 = p + BayDir(d) * lat;
        return new Vector3(p0.x, GroundY(p0, p.y) + y, p0.z);
    }

    static Vector3 BayDir(float d)
    {
        // The overlook opens to the bay on the side whose horizontal side vector points to -z (see Overlook.cs OvSign).
        var s = NagisaRoadIndex.SideAt(CafeM + 0f);
        float sg = s.z > 0f ? -1f : 1f;
        var here = NagisaRoadIndex.SideAt(d);
        return here * sg;
    }

    static float GroundY(Vector3 p, float roadY)
    {
        if (Physics.Raycast(new Vector3(p.x, roadY + 6f, p.z), Vector3.down, out var hit, 14f, ~0, QueryTriggerInteraction.Ignore))
            return hit.point.y;
        return roadY - 0.15f;   // terrace sits slightly below the road surface
    }

    Material Cel(Color c)
    {
        if (_celSource == null)
        {
            foreach (var r in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                var m = r.sharedMaterial;
                if (m != null && m.shader != null && m.shader.name == "MapleRide/HDRP/CelLit") { _celSource = m; break; }
            }
        }
        if (_celSource == null) return null;
        if (_mats.TryGetValue(c, out var made)) return made;
        made = new Material(_celSource) { name = "SummitCafe_" + ColorUtility.ToHtmlStringRGB(c), enableInstancing = true };
        if (made.HasProperty("_MainTex")) made.SetTexture("_MainTex", null);
        if (made.HasProperty("_NormalStrength")) made.SetFloat("_NormalStrength", 0f);
        if (made.HasProperty("_Color")) made.SetColor("_Color", c);
        _mats[c] = made;
        return made;
    }

    GameObject Prim(Transform parent, PrimitiveType type, Vector3 pos, Vector3 scale, Color c, string name, Quaternion? rot = null)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        var col = go.GetComponent<Collider>(); if (col != null) Destroy(col);
        go.transform.SetParent(parent, false);
        go.transform.position = pos;
        go.transform.rotation = rot ?? Quaternion.identity;
        go.transform.localScale = scale;
        var m = Cel(c);
        if (m != null) go.GetComponent<MeshRenderer>().sharedMaterial = m;
        return go;
    }

    // ------------------------------------------------------------------ props

    static readonly Color Teak = new Color(0.58f, 0.40f, 0.24f);
    static readonly Color White = new Color(0.94f, 0.94f, 0.92f);
    static readonly Color[] Ice =
    {
        new Color(1.00f, 0.42f, 0.58f), new Color(0.35f, 0.72f, 1.00f), new Color(1.00f, 0.86f, 0.30f),
        new Color(0.45f, 0.92f, 0.55f), new Color(0.72f, 0.50f, 1.00f), new Color(1.00f, 0.60f, 0.25f),
    };
    static readonly Color[] Umbrella = { new Color(0.95f, 0.45f, 0.38f), new Color(0.20f, 0.72f, 0.74f), new Color(0.98f, 0.82f, 0.35f) };

    void Table(Transform parent, Vector3 c, int chairs, float yaw, int umbrella)
    {
        Prim(parent, PrimitiveType.Cylinder, c + Vector3.up * (TableH - 0.02f), new Vector3(1.0f, 0.02f, 1.0f), White, "Cafe Table Top");
        Prim(parent, PrimitiveType.Cylinder, c + Vector3.up * (TableH * 0.5f), new Vector3(0.09f, TableH * 0.5f, 0.09f), Teak, "Cafe Table Leg");
        Prim(parent, PrimitiveType.Cylinder, c + Vector3.up * 0.02f, new Vector3(0.5f, 0.02f, 0.5f), Teak, "Cafe Table Foot");
        if (umbrella >= 0)
        {
            Prim(parent, PrimitiveType.Cylinder, c + Vector3.up * 1.2f, new Vector3(0.04f, 1.2f, 0.04f), White, "Parasol Pole");
            Prim(parent, PrimitiveType.Sphere, c + Vector3.up * 2.28f, new Vector3(2.5f, 0.34f, 2.5f), Umbrella[umbrella % Umbrella.Length], "Parasol");
        }
    }

    void Chair(Transform parent, Vector3 pos, Vector3 facing)
    {
        var rot = Quaternion.LookRotation(facing, Vector3.up);
        Prim(parent, PrimitiveType.Cube, pos + Vector3.up * (SeatH - 0.02f), new Vector3(0.42f, 0.04f, 0.42f), Teak, "Chair Seat", rot);
        Prim(parent, PrimitiveType.Cube, pos - facing * 0.19f + Vector3.up * (SeatH + 0.24f), new Vector3(0.42f, 0.42f, 0.03f), Teak, "Chair Back", rot);
        foreach (var o in new[] { new Vector2(-0.17f, -0.17f), new Vector2(0.17f, -0.17f), new Vector2(-0.17f, 0.17f), new Vector2(0.17f, 0.17f) })
            Prim(parent, PrimitiveType.Cylinder, pos + rot * new Vector3(o.x, SeatH * 0.5f, o.y), new Vector3(0.035f, SeatH * 0.5f, 0.035f), White, "Chair Leg");
    }

    GameObject IceCup(Transform parent, Vector3 pos, int flavour)
    {
        var holder = new GameObject("Shaved Ice");
        holder.transform.SetParent(parent, false);
        holder.transform.position = pos;
        Prim(holder.transform, PrimitiveType.Cylinder, pos + Vector3.up * 0.05f, new Vector3(0.085f, 0.05f, 0.085f), White, "Cup");
        Prim(holder.transform, PrimitiveType.Sphere, pos + Vector3.up * 0.13f, new Vector3(0.14f, 0.11f, 0.14f), Ice[flavour % Ice.Length], "Ice");
        return holder;
    }

    // ------------------------------------------------------------------ people

    sealed class Sources
    {
        public readonly List<MinatoCrowdActor> sit = new List<MinatoCrowdActor>();
        public readonly List<MinatoCrowdActor> stand = new List<MinatoCrowdActor>();
        public readonly List<NPCCyclist> riders = new List<NPCCyclist>();
    }

    Sources Gather(Vector3 near)
    {
        var s = new Sources();
        var sitL = new List<(float, MinatoCrowdActor)>(); var standL = new List<(float, MinatoCrowdActor)>();
        foreach (var a in FindObjectsByType<MinatoCrowdActor>(FindObjectsSortMode.None))
        {
            if (a == null || !a.isActiveAndEnabled || !a.IsReady) continue;
            var look = a.GetComponent<PedestrianAppearance>();
            if (look == null || !look.applied) continue;                 // only already-dressed figures
            if (a.GetComponent<MapleCityRunner>() != null || a.GetComponent<NagisaSunbather>() != null) continue;
            if (a.transform.IsChildOf(transform)) continue;
            float d = (a.transform.position - near).sqrMagnitude;
            if (a.motion == MinatoCrowdActor.MotionKind.Sit) sitL.Add((d, a));
            else if (a.motion == MinatoCrowdActor.MotionKind.Idle || a.motion == MinatoCrowdActor.MotionKind.Wave) standL.Add((d, a));
        }
        sitL.Sort((x, y) => x.Item1.CompareTo(y.Item1)); standL.Sort((x, y) => x.Item1.CompareTo(y.Item1));
        for (int i = 0; i < Mathf.Min(40, sitL.Count); i++) s.sit.Add(sitL[i].Item2);
        for (int i = 0; i < Mathf.Min(60, standL.Count); i++) s.stand.Add(standL[i].Item2);
        var root = GameObject.Find("Nagisa NPCs");
        if (root != null)
            foreach (var c in root.GetComponentsInChildren<NPCCyclist>(false))
                if (c != null && c.isActiveAndEnabled && !c.name.EndsWith("Kaimana")) s.riders.Add(c);
        return s;
    }

    MinatoCrowdActor Pick(List<MinatoCrowdActor> list, int k) => list.Count == 0 ? null : list[(k * 5 + 1) % list.Count];

    GameObject Person(MinatoCrowdActor src, Transform parent, Vector3 pos, Vector3 facing, string name)
    {
        if (src == null) return null;
        var go = Instantiate(src.gameObject, parent);
        go.name = name;
        go.transform.position = pos;
        facing.y = 0f;
        if (facing.sqrMagnitude > 1e-4f) go.transform.rotation = Quaternion.LookRotation(facing.normalized, Vector3.up);
        return go;
    }

    void HoldCup(GameObject person, int flavour)
    {
        var actor = person != null ? person.GetComponent<MinatoCrowdActor>() : null;
        var hand = actor != null ? actor.Bone("RightHand") : null;
        if (hand == null) return;
        var cup = IceCup(hand, hand.position + person.transform.forward * 0.05f, flavour);
        cup.transform.SetParent(hand, true);
    }

    GameObject Rider(NPCCyclist src, Transform parent, Vector3 pos, Vector3 heading, string name)
    {
        if (src == null) return null;
        var go = Instantiate(src.gameObject, parent);
        go.name = name;
        var c = go.GetComponent<NPCCyclist>();
        if (c != null) c.enabled = false;                       // stopped: stays exactly where we put it
        var greet = go.GetComponent<NpcGreeting>(); if (greet != null) greet.enabled = false;
        go.transform.position = pos;
        heading.y = 0f;
        go.transform.rotation = Quaternion.LookRotation(heading.normalized, Vector3.up);
        return go;
    }

    // ------------------------------------------------------------------ build

    IEnumerator Build(Transform root)
    {
        _spawning = true;
        _rng = new System.Random(8118);
        var group = new GameObject(GroupName).transform; group.SetParent(root, false);
        yield return null;
        var src = Gather(W(0f, WindowLat));
        Vector3 bay = BayDir(CafeM), toWindow = -bay;
        int people = 0, riders = 0, tables = 0;

        // ---- seating: two rows of tables on the paved terrace, both sides of the kiosk
        float[] alongs = { -27f, -20f, -13f, 12f, 19f, 26f };
        float[] lats = { 20.5f, 24.0f };
        int n = 0;
        foreach (float along in alongs)
            foreach (float lat in lats)
            {
                var c = W(along, lat);
                float yaw = (float)_rng.NextDouble() * 360f;
                Table(group, c, 3, yaw, (n % 3 == 0) ? n / 3 : -1);
                int seats = 2 + (n % 2);
                for (int k = 0; k < seats; k++)
                {
                    float a = (k / (float)seats) * Mathf.PI * 2f + yaw * Mathf.Deg2Rad;
                    var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    var seat = c + dir * 0.78f;
                    Chair(group, seat, -dir);
                    bool occupied = k < seats - ((n % 4 == 3) ? 1 : 0);
                    if (occupied && src.sit.Count > 0)
                    {
                        var p = Person(Pick(src.sit, n * 7 + k), group, seat, -dir, $"Cafe Diner {n:D2}-{k}");
                        if (p != null) { people++; IceCup(group, c + dir * 0.18f + Vector3.up * TableH, n + k); }
                    }
                    else if (!occupied) IceCup(group, c + dir * 0.18f + Vector3.up * TableH, n + k + 2);
                }
                tables++; n++;
                if (n % 3 == 0) yield return null;
            }

        // ---- queue at the window: walkers in a line leading away from the right edge of the counter
        if (src.stand.Count > 0)
            for (int i = 0; i < 6; i++)
            {
                // Open side of the window: an existing terrace bench occupies the +along side.
                var pos = W(-2.7f - (i % 2) * 0.25f, WindowLat + 1.1f + i * 0.95f);
                var head = i == 0 ? W(-1.2f, WindowLat + 0.6f) : W(-2.7f - ((i - 1) % 2) * 0.25f, WindowLat + 1.1f + (i - 1) * 0.95f);
                var p = Person(Pick(src.stand, i + 3), group, pos, head - pos, $"Cafe Queue {i}");
                if (p != null) { people++; if (i % 2 == 0) HoldCup(p, i); }
            }

        // ---- cyclists: three bikes straddled at the counter, three more leaned by the tables
        var riderGroup = new GameObject(RiderGroupName).transform;
        var nr = GameObject.Find("Nagisa NPCs");
        riderGroup.SetParent(nr != null ? nr.transform : root, false);
        if (src.riders.Count > 0)
        {
            float[] ra = { -1.5f, 0.1f, 1.7f };
            for (int i = 0; i < ra.Length; i++)
            {
                var r = Rider(src.riders[(i * 11 + 2) % src.riders.Count], riderGroup, W(ra[i], WindowLat + 1.55f), toWindow, $"Cafe Cyclist {i + 1}");
                if (r != null) riders++;
            }
            float[,] park = { { -23.5f, 17.6f }, { 15.5f, 17.6f }, { 29.5f, 21.5f } };
            for (int i = 0; i < 3; i++)
            {
                var r = Rider(src.riders[(i * 13 + 5) % src.riders.Count], riderGroup, W(park[i, 0], park[i, 1]), bay + new Vector3(0.3f * (i - 1), 0f, 0f), $"Cafe Parked Cyclist {i + 1}");
                if (r != null) riders++;
            }
        }
        Debug.Log($"[nagisa-cafe] summit cafe populated: {tables} tables, {people} people ({src.sit.Count} seated / {src.stand.Count} standing sources), {riders} cyclists.");
        _spawning = false; _done = true;
    }
}
