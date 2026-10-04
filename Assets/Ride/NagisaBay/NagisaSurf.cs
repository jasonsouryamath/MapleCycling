using System;
using UnityEngine;

/// <summary>
/// NB1 - runtime mirror of the Nagisa Bay ocean (MapleRide/HDRP/NagisaOcean). Boats, surfers, swimmers and the
/// spray system ask this for the water height / break state at a point so they ride the SAME waves the shader draws.
///
/// The maths is a line-for-line port of Assets/Environment/NagisaBay/Shaders/NagisaOceanCommon.hlsl
/// (NS_Surf, NS_Trains). KEEP THEM IN SYNC. It reads the baked ground field (NagisaGround.bytes, NBG1) directly,
/// so it needs no extra asset; the editor stage "Surf" assigns <see cref="groundBytes"/> and drives shader time.
///
///   NagisaSurf.Now                        shared wave clock (== shader time while the component is running)
///   NagisaSurf.HeightAt(x, z, t)          water surface height y at world x,z (0 = sea level; swell + breakers)
///   NagisaSurf.BreakAt(x, z, t, out dir)  0..1 how hard a wave is breaking here (feathering lip / whitewater bore);
///                                         dir = unit XZ vector pointing toward the shore
///   NagisaSurf.Sample(x, z, t)            everything at once (height, breaking, whitewater, depth, shore dir, normal)
///   NagisaSurf.NextSet(t, out start, out count, out spacing)   next set of 3-5 breakers (for surfers / lifeguards)
///   NagisaSurf.DepthAt / OffshoreDistance / ShoreDir           the baked bathymetry
/// </summary>
[DisallowMultipleComponent]
public sealed class NagisaSurf : MonoBehaviour
{
    public TextAsset groundBytes;
    [Range(0.3f, 3f)] public float surfHeight = 1.3f;     // keep equal to the material's _SurfHeight
    [Range(0f, 3f)] public float chopAmp = 1f;            // keep equal to the material's _ChopAmp
    public bool driveShaderTime = true;

    public static NagisaSurf Instance { get; private set; }
    public static bool HasField => _h != null;

    /// <summary>The shared wave clock. Use this for t.</summary>
    public static float Now => Time.time;

    // ---- constants mirrored from the HLSL ---------------------------------------------------------------
    public const float SetPeriod = 72f;      // NS_C
    private const float C0 = 0.7f;           // NS_C0
    private const float EnvStart = 330f, EnvEnd = 440f;   // displacement envelope used by the mesh builder

    private static float[] _h, _sd;
    private static int _nx, _nz;
    private static float _x0, _z0, _cell;
    private static float _surfHeight = 1.3f, _chop = 1f;

    private void Awake() { Instance = this; Load(); Apply(); }
    private void OnEnable() { if (Instance == null) { Instance = this; Load(); Apply(); } }
    private void OnValidate() { Apply(); }
    private void OnDestroy() { if (Instance == this) Instance = null; Shader.SetGlobalFloat("_NagisaSurfTimeOn", 0f); }

    private void Apply() { _surfHeight = surfHeight; _chop = chopAmp; }

    private void Update()
    {
        if (!driveShaderTime) return;
        Shader.SetGlobalFloat("_NagisaSurfTimeOn", 1f);
        Shader.SetGlobalFloat("_NagisaSurfTime", Now);
    }

    private void Load()
    {
        if (_h != null || groundBytes == null) return;
        var b = groundBytes.GetData<byte>();
        if (b.Length < 24 || b[0] != 'N' || b[1] != 'B' || b[2] != 'G' || b[3] != '1')
        {
            Debug.LogError("[nagisa-surf] ground bytes are not NBG1");
            return;
        }
        var raw = b.ToArray();
        _nx = BitConverter.ToInt32(raw, 4); _nz = BitConverter.ToInt32(raw, 8);
        _x0 = BitConverter.ToSingle(raw, 12); _z0 = BitConverter.ToSingle(raw, 16); _cell = BitConverter.ToSingle(raw, 20);
        int n = _nx * _nz;
        var h = new float[n]; Buffer.BlockCopy(raw, 24, h, 0, n * 4);
        var sd = new float[n]; Buffer.BlockCopy(raw, 24 + n * 5, sd, 0, n * 4);
        _sd = sd; _h = h;
    }

    /// <summary>Editor/stage hook: load the field from raw NBG1 bytes (no scene component needed).</summary>
    public static void LoadFrom(byte[] raw)
    {
        _nx = BitConverter.ToInt32(raw, 4); _nz = BitConverter.ToInt32(raw, 8);
        _x0 = BitConverter.ToSingle(raw, 12); _z0 = BitConverter.ToSingle(raw, 16); _cell = BitConverter.ToSingle(raw, 20);
        int n = _nx * _nz;
        var h = new float[n]; Buffer.BlockCopy(raw, 24, h, 0, n * 4);
        var sd = new float[n]; Buffer.BlockCopy(raw, 24 + n * 5, sd, 0, n * 4);
        _sd = sd; _h = h;
    }

    // ---- field ----------------------------------------------------------------------------------------
    private static float Bil(float[] a, float fx, float fz)
    {
        int ix = Mathf.Clamp(Mathf.FloorToInt(fx), 0, _nx - 2), iz = Mathf.Clamp(Mathf.FloorToInt(fz), 0, _nz - 2);
        float tx = Mathf.Clamp01(fx - ix), tz = Mathf.Clamp01(fz - iz);
        float a00 = a[iz * _nx + ix], a10 = a[iz * _nx + ix + 1], a01 = a[(iz + 1) * _nx + ix], a11 = a[(iz + 1) * _nx + ix + 1];
        return Mathf.Lerp(Mathf.Lerp(a00, a10, tx), Mathf.Lerp(a01, a11, tx), tz);
    }

    /// <summary>(ground height, signed distance to the coast: + inland). Open deep sea outside the baked rectangle.</summary>
    private static void HS(float x, float z, out float h, out float sd)
    {
        if (_h == null) { h = -120f; sd = -3000f; return; }
        float fx = (x - _x0) / _cell, fz = (z - _z0) / _cell;
        if (fx < -0.5f || fx > _nx - 0.5f || fz < -0.5f || fz > _nz - 0.5f) { h = -120f; sd = -3000f; return; }
        h = Bil(_h, fx, fz); sd = Bil(_sd, fx, fz);
    }

    public static float DepthAt(float x, float z) { HS(x, z, out float h, out _); return Mathf.Max(-h, 0f); }
    /// <summary>Metres offshore (0 on land).</summary>
    public static float OffshoreDistance(float x, float z) { HS(x, z, out _, out float sd); return Mathf.Max(-sd, 0f); }

    /// <summary>Unit XZ vector pointing from the sea toward the land.</summary>
    public static Vector2 ShoreDir(float x, float z)
    {
        float c = (_h == null ? 6f : _cell) * 1.5f;
        HS(x + c, z, out _, out float a); HS(x - c, z, out _, out float b);
        HS(x, z + c, out _, out float cc); HS(x, z - c, out _, out float d);
        var g = new Vector2(a - b, cc - d);
        float l = g.magnitude;
        return l > 1e-4f ? g / l : new Vector2(0f, 1f);
    }

    // ---- hashing / noise (identical to the HLSL) ----------------------------------------------------------
    private static uint Hash(uint x)
    {
        unchecked
        {
            x ^= x >> 16; x *= 0x7feb352du; x ^= x >> 15; x *= 0x846ca68bu; x ^= x >> 16;
        }
        return x;
    }
    private static float H01(int i) => (Hash(unchecked((uint)i)) & 0xFFFFFFu) * (1f / 16777216f);

    private static float Noise(float px, float pz)
    {
        float fx0 = Mathf.Floor(px), fz0 = Mathf.Floor(pz);
        float fx = px - fx0, fz = pz - fz0;
        fx = fx * fx * (3f - 2f * fx); fz = fz * fz * (3f - 2f * fz);
        int ix = (int)fx0, iz = (int)fz0;
        float a, b, c, d;
        unchecked
        {
            a = H01(ix * 1619 + iz * 31337 + 7);
            b = H01((ix + 1) * 1619 + iz * 31337 + 7);
            c = H01(ix * 1619 + (iz + 1) * 31337 + 7);
            d = H01((ix + 1) * 1619 + (iz + 1) * 31337 + 7);
        }
        return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fz);
    }

    private static float Smooth(float a, float b, float x)
    {
        float t = Mathf.Clamp01((x - a) / (b - a));
        return t * t * (3f - 2f * t);
    }

    // ---- sets -------------------------------------------------------------------------------------------
    private static void SetParams(int k, out float start, out int n, out float spacing, out float hk)
    {
        start = k * SetPeriod + H01(unchecked(k * 3 + 1)) * 14f;
        n = 3 + (int)(H01(unchecked(k * 3 + 2)) * 3f);
        spacing = 8f + 4f * H01(unchecked(k * 3 + 3));
        hk = 0.8f + 0.4f * H01(unchecked(k * 7 + 11));
    }
    private static float WaveH(int k, int i, int n, float hk) =>
        hk * (0.75f + 0.25f * Mathf.Sin(Mathf.PI * (i + 0.5f) / n)) * (0.92f + 0.16f * H01(unchecked(k * 13 + i * 5 + 3)));

    /// <summary>The set of breakers that reaches the shoreline next after time t (start of its first wave).</summary>
    public static void NextSet(float t, out float start, out int count, out float spacing)
    {
        int k = Mathf.FloorToInt((t - 14f) / SetPeriod);
        for (int j = 0; j < 4; j++)
        {
            SetParams(k + j, out start, out count, out spacing, out _);
            if (start + (count - 1) * spacing > t) return;
        }
        SetParams(k + 4, out start, out count, out spacing, out _);
    }

    private struct Surf { public float eta, push, lip, trail, steep; }

    private static Surf SurfAt(float px, float pz, float t, float hGround, float sd)
    {
        var r = new Surf();
        float s = Mathf.Max(-sd, 0f);
        float envS = 1f - Smooth(190f, 330f, s);
        if (envS <= 0.001f || sd > 4f) return r;

        float D = Mathf.Max(-hGround, 0.05f);
        float tau = 2f * Mathf.Sqrt(s) / C0;
        float offT = (Noise(px * 0.022f, pz * 0.022f) - 0.5f) * 3f;
        float ampV = 0.80f + 0.40f * Noise(px * 0.013f + 17.3f, pz * 0.013f + 17.3f);
        float u = t + tau - offT;
        int kmin = Mathf.FloorToInt((u - 95f) / SetPeriod);
        float shoal = Mathf.Pow(Mathf.Clamp(D / 8f, 0.05f, 3f), -0.25f);

        for (int ks = 0; ks < 3; ks++)
        {
            int k = kmin + ks;
            SetParams(k, out float st, out int n, out float sp, out float hk);
            for (int i = 0; i < 5; i++)
            {
                if (i >= n) break;
                float psi = u - (st + i * sp);
                if (psi < -6f || psi > 16f) continue;

                float Hn = _surfHeight * WaveH(k, i, n, hk) * ampV * envS;
                float Hloc = Hn * shoal;
                float rb = Hloc / D;
                float sm = Mathf.Clamp01(rb / 0.78f);
                float b = Smooth(0.72f, 0.92f, rb);

                float sk = psi < 0f ? (1f + 2.4f * sm * sm) : (1f - 0.25f * sm);
                float x = psi * sk;
                float crest = Mathf.Exp(-(x * x) / 2.89f);
                float dip = 0.20f * Mathf.Exp(-(x * x) / 18f);
                float etaU = Hloc * (crest - dip);

                float Hb = Mathf.Min(Hloc, 0.85f * D);
                float front = Smooth(-0.7f, 0.2f, psi);
                float tail = Mathf.Exp(-Mathf.Max(psi, 0f) / 2.4f);
                float etaB = Hb * front * (0.30f + 0.70f * tail) * (1f - Smooth(10f, 16f, psi));

                float e = Mathf.Lerp(etaU, etaB, b);
                r.eta += e;
                r.push += Mathf.Max(e, 0f) * Mathf.Lerp(0.7f + 0.9f * sm * sm, 0.3f, b);

                float lipx = (psi + 0.15f) / 0.45f;
                float lip = Mathf.Exp(-lipx * lipx) * Smooth(0.50f, 0.80f, rb);
                float trail = b * Mathf.Clamp01((psi + 0.4f) / 0.8f) * Mathf.Exp(-Mathf.Max(psi, 0f) / 5.5f) * (1f - Smooth(12f, 16f, psi));
                r.lip = Mathf.Max(r.lip, lip);
                r.trail = Mathf.Max(r.trail, trail);
                r.steep = Mathf.Max(r.steep, sm * sm * (1f - b) * crest);
            }
        }
        return r;
    }

    // ---- trains ------------------------------------------------------------------------------------------
    private static readonly float[] TL = { 95f, 58f, 36f, 21f, 11.5f, 6.2f };
    private static readonly float[] TA = { 0.30f, 0.17f, 0.09f, 0.05f, 0.025f, 0.012f };
    private static readonly float[] TD = { 0f, 0.419f, -0.541f, 0.995f, -1.100f, 1.920f };
    private static readonly float[] TP = { 0.3f, 1.9f, 4.1f, 2.6f, 5.2f, 0.9f };

    private static void TrainDir(int i, out float dx, out float dz)
    {
        float l = Mathf.Sqrt(0.62f * 0.62f + 0.78f * 0.78f);
        float x0 = -0.62f / l, z0 = 0.78f / l;
        float c = Mathf.Cos(TD[i]), s = Mathf.Sin(TD[i]);
        dx = x0 * c - z0 * s; dz = x0 * s + z0 * c;
    }

    private static float Trains(float px, float pz, float t, float depthW, float amp, out float pushX, out float pushZ)
    {
        float h = 0f; pushX = 0f; pushZ = 0f;
        for (int i = 0; i < 6; i++)
        {
            float k = 2f * Mathf.PI / TL[i];
            float w = Mathf.Sqrt(9.81f * k);
            TrainDir(i, out float dx, out float dz);
            float ph = (px * dx + pz * dz) * k - w * t + TP[i];
            float a = TA[i] * _chop * depthW * amp;
            h += a * Mathf.Sin(ph);
            float c = 0.55f * a * Mathf.Cos(ph);
            pushX += dx * c; pushZ += dz * c;
        }
        return h;
    }

    /// <summary>Vertex-envelope used by the mesh (1 near shore, fading to 0 by 440 m offshore).</summary>
    public static float EnvelopeAt(float offshore) => 1f - Smooth(EnvStart, EnvEnd, offshore);

    // ---- public API ---------------------------------------------------------------------------------------
    public struct SurfSample
    {
        public float height;      // water surface y
        public float breaking;    // 0..1 lip / feathering at a crest about to or just breaking
        public float whitewater;  // 0..1 foamy bore behind a broken wave
        public float steepness;   // 0..1 how close a crest is to breaking
        public float depth;       // water depth under the point (m, ground-based)
        public float offshore;    // metres from the coastline
        public Vector2 shoreDir;  // unit XZ toward land
        public Vector3 normal;    // approximate surface normal (finite difference)
    }

    private static float Eval(float x, float z, float t, out Surf sf, out float depthW, out float env)
    {
        HS(x, z, out float h, out float sd);
        sf = SurfAt(x, z, t, h, sd);
        depthW = Smooth(0.2f, 6f, Mathf.Max(-h, 0f));
        env = EnvelopeAt(Mathf.Max(-sd, 0f));
        float th = Trains(x, z, t, depthW, env, out _, out _);
        return sf.eta * env + th;
    }

    /// <summary>Water surface height y at world (x, z) at time t (use <see cref="Now"/>).</summary>
    public static float HeightAt(float x, float z, float t)
    {
        // The mesh pushes crests toward the shore; find the lattice point that ends up under (x, z).
        HS(x, z, out float h, out float sd);
        Vector2 shore = ShoreDir(x, z);
        var sf = SurfAt(x, z, t, h, sd);
        float env = EnvelopeAt(Mathf.Max(-sd, 0f));
        float depthW = Smooth(0.2f, 6f, Mathf.Max(-h, 0f));
        Trains(x, z, t, depthW, env, out float tpx, out float tpz);
        float qx = x - (shore.x * sf.push * env + tpx), qz = z - (shore.y * sf.push * env + tpz);
        return Eval(qx, qz, t, out _, out _, out _);
    }

    /// <summary>0..1 breaking intensity at (x, z); dir = unit XZ toward the shore.</summary>
    public static float BreakAt(float x, float z, float t, out Vector2 dir)
    {
        HS(x, z, out float h, out float sd);
        dir = ShoreDir(x, z);
        var sf = SurfAt(x, z, t, h, sd);
        return Mathf.Clamp01(Mathf.Max(sf.lip, sf.trail * 0.7f));
    }

    public static SurfSample Sample(float x, float z, float t)
    {
        HS(x, z, out float h, out float sd);
        var sf = SurfAt(x, z, t, h, sd);
        float y = Eval(x, z, t, out _, out _, out _);
        const float d = 1.0f;
        float yx = Eval(x + d, z, t, out _, out _, out _), yz = Eval(x, z + d, t, out _, out _, out _);
        return new SurfSample
        {
            height = y,
            breaking = Mathf.Clamp01(Mathf.Max(sf.lip, sf.trail * 0.7f)),
            whitewater = sf.trail,
            steepness = sf.steep,
            depth = Mathf.Max(-h, 0f),
            offshore = Mathf.Max(-sd, 0f),
            shoreDir = ShoreDir(x, z),
            normal = new Vector3(-(yx - y) / d, 1f, -(yz - y) / d).normalized,
        };
    }
}
