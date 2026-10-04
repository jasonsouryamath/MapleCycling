// NB1 - Nagisa Bay ocean: shared wave / surf maths (MapleRide/HDRP/NagisaOcean + NagisaOceanSwash).
//
// The surf model is a function of (world XZ, time) only, so the vertex stage, the fragment stage, the swash
// overlay and the C# mirror (Assets/Ride/NagisaBay/NagisaSurf.cs HeightAt/BreakAt) all agree. KEEP THE C# IN SYNC.
//
//   field texture (RGBAHalf, baked from NagisaGround): R = ground height (m, 0 = sea level)
//                                                      G = signed distance to the coast (m, + inland)
//   A wave "set" is a train of 3-5 breakers 8-12 s apart, a new set every NS_C seconds. Breaker n reaches the
//   shoreline at time A_n. At offshore distance s it passes at A_n - tau(s), tau(s) = 2*sqrt(s)/NS_C0
//   (constant-slope shallow-water dispersion). psi = time since the crest passed this point.
//   Wave height shoals with depth (Green's law), the wave breaks when height/depth > ~0.78 and turns into a
//   whitewater bore that dies as the water shallows, then a swash tongue runs up the sand and pulls back.
#ifndef NAGISA_OCEAN_COMMON_INCLUDED
#define NAGISA_OCEAN_COMMON_INCLUDED

#include "Assets/Environment/Shared/Shaders/HDRP/MapleRideHDRPCommon.hlsl"

TEXTURE2D(_NagisaField);  SAMPLER(sampler_NagisaField);
TEXTURE2D(_NormA);        SAMPLER(sampler_NormA);
TEXTURE2D(_NormB);        SAMPLER(sampler_NormB);
TEXTURE2D(_FoamTex);      SAMPLER(sampler_FoamTex);

// Globals driven by NagisaSurf (play mode) or the diagnostics (edit-mode captures). Off = HDRP time.
float _NagisaSurfTime;
float _NagisaSurfTimeOn;

CBUFFER_START(UnityPerMaterial)
    float4 _FieldRect;      // x0, z0, cell, -
    float4 _FieldDim;       // nx, nz, -, -
    float4 _ShallowTint;
    float4 _DeepColor;
    float4 _SandColor;
    float4 _ReefColor;
    float4 _SkyZenith;
    float4 _SkyHorizon;
    float4 _SunColor;
    float4 _FoamColor;
    float4 _SssColor;
    float4 _WetSandColor;
    float _SurfHeight;
    float _SwashHeight;
    float _ChopAmp;
    float _WhitecapAmount;
    float _AbsorbScale;
    float _AmbientWeight;
    float _SunWeight;
    float _SpecGain;
    float _SkyGain;
    float _DetailStrength;
    float _FoamGain;
    float _SwashAlpha;
CBUFFER_END

static const float NS_C = 72.0;          // seconds per wave set
static const float NS_C0 = 0.7;          // sqrt(g * 0.05): model beach slope for crest timing
static const float NS_PI = 3.14159265;

float NS_Time() { return _NagisaSurfTimeOn > 0.5 ? _NagisaSurfTime : _TimeParameters.x; }

uint NS_Hash(uint x)
{
    x ^= x >> 16; x *= 0x7feb352du; x ^= x >> 15; x *= 0x846ca68bu; x ^= x >> 16;
    return x;
}
float NS_H01(int i) { return (float)(NS_Hash((uint)i) & 0xFFFFFFu) * (1.0 / 16777216.0); }

float NS_Noise(float2 p)
{
    float2 ip = floor(p);
    float2 f = p - ip;
    f = f * f * (3.0 - 2.0 * f);
    int ix = (int)ip.x, iz = (int)ip.y;
    float a = NS_H01(ix * 1619 + iz * 31337 + 7);
    float b = NS_H01((ix + 1) * 1619 + iz * 31337 + 7);
    float c = NS_H01(ix * 1619 + (iz + 1) * 31337 + 7);
    float d = NS_H01((ix + 1) * 1619 + (iz + 1) * 31337 + 7);
    return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
}

// ---------------------------------------------------------------------------------------------- field
float2 NS_FieldUV(float2 p) { return ((p - _FieldRect.xy) / _FieldRect.z + 0.5) / _FieldDim.xy; }

// returns (ground height, signed distance to coast). Outside the baked rectangle: open deep sea.
float2 NS_HS(float2 p)
{
    float2 q = NS_FieldUV(p);
    float2 hs = SAMPLE_TEXTURE2D_LOD(_NagisaField, sampler_NagisaField, q, 0).rg;
    float inside = step(0.0, q.x) * step(q.x, 1.0) * step(0.0, q.y) * step(q.y, 1.0);
    return lerp(float2(-120.0, -3000.0), hs, inside);
}

// unit vector pointing from the sea toward the land (gradient of the signed distance)
float2 NS_ShoreDir(float2 p)
{
    float c = _FieldRect.z * 1.5;
    float gx = NS_HS(p + float2(c, 0)).y - NS_HS(p - float2(c, 0)).y;
    float gz = NS_HS(p + float2(0, c)).y - NS_HS(p - float2(0, c)).y;
    float2 g = float2(gx, gz);
    float l = length(g);
    return l > 1e-4 ? g / l : float2(0.0, 1.0);
}

// ---------------------------------------------------------------------------------------------- sets
void NS_SetParams(int k, out float start, out int n, out float spacing, out float hk)
{
    start = (float)k * NS_C + NS_H01(k * 3 + 1) * 14.0;
    n = 3 + (int)(NS_H01(k * 3 + 2) * 3.0);          // 3..5 breakers per set
    spacing = 8.0 + 4.0 * NS_H01(k * 3 + 3);         // 8..12 s apart
    hk = 0.8 + 0.4 * NS_H01(k * 7 + 11);
}
float NS_WaveH(int k, int i, int n, float hk)
{
    return hk * (0.75 + 0.25 * sin(NS_PI * ((float)i + 0.5) / (float)n)) * (0.92 + 0.16 * NS_H01(k * 13 + i * 5 + 3));
}

struct NSSurf
{
    float eta;        // surface height (m) above sea level from the breakers
    float push;       // horizontal push toward the shore (m), makes crests lean/curl
    float lip;        // 0..1 white feathering at the crest / bore front
    float trail;      // 0..1 whitewater behind the bore
    float steep;      // 0..1 how close the crest is to breaking
};

// Surf field at p. hs = (ground height, signed coast distance) at p. t = time (s).
NSSurf NS_Surf(float2 p, float t, float2 hs)
{
    NSSurf r;
    r.eta = 0; r.push = 0; r.lip = 0; r.trail = 0; r.steep = 0;
    float s = max(-hs.y, 0.0);
    float envS = 1.0 - smoothstep(190.0, 330.0, s);
    if (envS <= 0.001 || hs.y > 4.0) return r;

    float D = max(-hs.x, 0.05);
    float tau = 2.0 * sqrt(s) / NS_C0;
    float offT = (NS_Noise(p * 0.022) - 0.5) * 3.0;
    float ampV = 0.80 + 0.40 * NS_Noise(p * 0.013 + 17.3);
    float u = t + tau - offT;
    int kmin = (int)floor((u - 95.0) / NS_C);
    float shoal = pow(clamp(D / 8.0, 0.05, 3.0), -0.25);

    [loop] for (int ks = 0; ks < 3; ks++)
    {
        int k = kmin + ks;
        float st; int n; float sp; float hk;
        NS_SetParams(k, st, n, sp, hk);
        for (int i = 0; i < 5; i++)
        {
            if (i >= n) break;
            float psi = u - (st + (float)i * sp);
            if (psi < -6.0 || psi > 16.0) continue;

            float Hn = _SurfHeight * NS_WaveH(k, i, n, hk) * ampV * envS;
            float Hloc = Hn * shoal;
            float rb = Hloc / D;
            float sm = saturate(rb / 0.78);
            float b = smoothstep(0.72, 0.92, rb);

            float sk = psi < 0.0 ? (1.0 + 2.4 * sm * sm) : (1.0 - 0.25 * sm);
            float x = psi * sk;
            float crest = exp(-(x * x) / 2.89);                      // wc = 1.7 s
            float dip = 0.20 * exp(-(x * x) / 18.0);
            float etaU = Hloc * (crest - dip);

            float Hb = min(Hloc, 0.85 * D);
            float front = smoothstep(-0.7, 0.2, psi);
            float tail = exp(-max(psi, 0.0) / 2.4);
            float etaB = Hb * front * (0.30 + 0.70 * tail) * (1.0 - smoothstep(10.0, 16.0, psi));

            float e = lerp(etaU, etaB, b);
            r.eta += e;
            r.push += max(e, 0.0) * lerp(0.7 + 0.9 * sm * sm, 0.3, b);

            float lipx = (psi + 0.15) / 0.45;
            float lip = exp(-lipx * lipx) * smoothstep(0.50, 0.80, rb);
            float trail = b * saturate((psi + 0.4) / 0.8) * exp(-max(psi, 0.0) / 5.5) * (1.0 - smoothstep(12.0, 16.0, psi));
            r.lip = max(r.lip, lip);
            r.trail = max(r.trail, trail);
            r.steep = max(r.steep, sm * sm * (1.0 - b) * crest);
        }
    }
    return r;
}

// ---------------------------------------------------------------------------------------------- trains
// Six Gerstner-style trains, the first being the SE swell (travelling toward the NW). L = wavelength.
static const float NS_TL[6] = { 95.0, 58.0, 36.0, 21.0, 11.5, 6.2 };
static const float NS_TA[6] = { 0.30, 0.17, 0.09, 0.05, 0.025, 0.012 };
static const float NS_TD[6] = { 0.0, 0.419, -0.541, 0.995, -1.100, 1.920 };      // rotation from the swell direction (rad)
static const float NS_TP[6] = { 0.3, 1.9, 4.1, 2.6, 5.2, 0.9 };

float2 NS_TrainDir(int i)
{
    float2 d0 = normalize(float2(-0.62, 0.78));
    float c = cos(NS_TD[i]), s = sin(NS_TD[i]);
    return float2(d0.x * c - d0.y * s, d0.x * s + d0.y * c);
}

// height + horizontal gerstner push (xz) at p; dist fades the short waves for the fragment normal
void NS_Trains(float2 p, float t, float depthW, float amp, out float h, out float2 push)
{
    h = 0; push = float2(0, 0);
    [unroll] for (int i = 0; i < 6; i++)
    {
        float k = 2.0 * NS_PI / NS_TL[i];
        float w = sqrt(9.81 * k);
        float2 d = NS_TrainDir(i);
        float ph = dot(p, d) * k - w * t + NS_TP[i];
        float a = NS_TA[i] * _ChopAmp * depthW * amp;
        h += a * sin(ph);
        push += d * (0.55 * a * cos(ph));
    }
}

// analytic slope (d height / d xz) of the trains, distance-faded so nothing aliases at grazing angles
float2 NS_TrainGrad(float2 p, float t, float depthW, float dist)
{
    float2 g = float2(0, 0);
    [unroll] for (int i = 0; i < 6; i++)
    {
        float k = 2.0 * NS_PI / NS_TL[i];
        float w = sqrt(9.81 * k);
        float2 d = NS_TrainDir(i);
        float ph = dot(p, d) * k - w * t + NS_TP[i];
        float fade = saturate(1.0 - dist / (NS_TL[i] * 22.0));
        g += d * (NS_TA[i] * _ChopAmp * depthW * fade * k * cos(ph));
    }
    return g;
}

// ---------------------------------------------------------------------------------------------- vertex displacement
struct NSDisp { float3 d; float2 grad; };      // xz push, y height; gradient of the surf height

NSDisp NS_Displace(float2 p, float t, float env)
{
    float2 hs = NS_HS(p);
    NSSurf a = NS_Surf(p, t, hs);
    float d = 1.2;
    float2 pdx = p + float2(d, 0), pdz = p + float2(0, d);
    NSSurf bx = NS_Surf(pdx, t, NS_HS(pdx));
    NSSurf bz = NS_Surf(pdz, t, NS_HS(pdz));
    float2 shore = NS_ShoreDir(p);

    float depthW = smoothstep(0.2, 6.0, max(-hs.x, 0.0));
    float th; float2 tp;
    NS_Trains(p, t, depthW, env, th, tp);

    NSDisp o;
    o.d = float3(0, 0, 0);
    float2 push = shore * (a.push * env) + tp;
    o.d.x = push.x; o.d.z = push.y;
    o.d.y = a.eta * env + th;
    o.grad = float2(bx.eta - a.eta, bz.eta - a.eta) * (env / d);
    return o;
}

// ---------------------------------------------------------------------------------------------- shading helpers
float NS_Lace(float2 p, float t, float2 flow)
{
    float2 u1 = p * 0.31 + flow * t * 0.05;
    float2 u2 = float2(p.x * 0.093 - p.y * 0.054, p.x * 0.054 + p.y * 0.093) - flow * t * 0.03;
    float a = SAMPLE_TEXTURE2D(_FoamTex, sampler_FoamTex, u1).r;
    float b = SAMPLE_TEXTURE2D(_FoamTex, sampler_FoamTex, u2).r;
    return saturate(a * 1.5 * (0.45 + 0.9 * b));
}

#endif // NAGISA_OCEAN_COMMON_INCLUDED
