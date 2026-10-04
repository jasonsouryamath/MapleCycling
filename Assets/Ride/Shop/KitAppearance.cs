using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Applies Maple Row gear to a Kuro rig (the player, or the shop's try-on mannequin):
///  * jersey / bibs / helmet: the body mesh is swapped for Resources/Shop/KuroKitSplit (the same
///    mesh split into garment submeshes per TRIANGLE by KuroKitSplitBake). Each garment slot gets
///    its own material whose texture is Kuro's atlas recoloured as that garment
///    (Hidden/MapleRide/KitComposite + Resources/Shop/KuroKitMask.png v2). The mesh decides where
///    the garment is, so kit can no longer bleed into hair, gloves or skin (the v1 texel mask did).
///    Textured garments (ShopItem.Asset, 2026-09-26) composite a full photoreal design atlas in
///    place of the two colours, still shaded by the mask's fold luminance.
///  * wheels: rim tint + CyclingPhysics.equipmentCdaScale (player only).
///  * frame: FrameVisuals swaps the stock frame + fork for the bought frameset; its CdaScale
///    multiplies the wheels' (player only).
///  * bike computer: RideHud.computerTier (player only).
/// Re-applies whenever the wardrobe changes. With nothing equipped, Kuro keeps his original look.
/// <see cref="tryOn"/> overrides the wardrobe per category (used by the shop preview).
/// </summary>
[DisallowMultipleComponent]
public sealed class KitAppearance : MonoBehaviour
{
    public RideSession session;
    public RideHud hud;
    /// <summary>When true, only <see cref="tryOn"/> is used and the wardrobe is ignored (the
    /// shop mannequin shows exactly what is on its rail, even unowned items).</summary>
    public bool previewOnly;
    public readonly Dictionary<ShopCategory, ShopItem> tryOn = new Dictionary<ShopCategory, ShopItem>();

    private static readonly ShopCategory[] Garments = { ShopCategory.Jersey, ShopCategory.Bibs, ShopCategory.Helmet };

    private Material _composite;
    private Texture _mask, _baseAtlas;
    private readonly Material[] _garmentMats = new Material[3];
    private readonly RenderTexture[] _garmentRts = new RenderTexture[3];
    private bool _split;
    private SkinnedMeshRenderer _body;
    private readonly List<Renderer> _wheelRenderers = new List<Renderer>();
    private MaterialPropertyBlock _mpb;
    private static readonly int MainTexId = Shader.PropertyToID("_MainTex");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int BaseMapId = Shader.PropertyToID("_BaseColorMap");

    public bool HasSplitMesh => _split;

    /// <summary>
    /// The split is derived from this body: same skeleton, three extra garment submeshes, and
    /// (since KuroKitSplitBake refines the player's geometry) at least as many vertices - the
    /// refined mesh keeps every original vertex first, so its first and last original vertices
    /// must sit exactly where the body's do.
    /// </summary>
    private static bool SplitMatches(Mesh split, Mesh src)
    {
        if (split.subMeshCount != src.subMeshCount + 3) return false;
        if (split.bindposes.Length != src.bindposes.Length) return false;
        int n = src.vertexCount;
        if (split.vertexCount < n || n == 0) return false;
        if (split.vertexCount == n || !split.isReadable || !src.isReadable) return true;
        var a = new List<Vector3>(split.vertexCount); split.GetVertices(a);
        var b = new List<Vector3>(n); src.GetVertices(b);
        return (a[0] - b[0]).sqrMagnitude < 1e-10f && (a[n - 1] - b[n - 1]).sqrMagnitude < 1e-10f &&
               (a[n / 2] - b[n / 2]).sqrMagnitude < 1e-10f;
    }

    private void Start()
    {
        var sh = Shader.Find("Hidden/MapleRide/KitComposite");
        if (sh != null) _composite = new Material(sh);
        _mask = Resources.Load<Texture2D>("Shop/KuroKitMask");
        _mpb = new MaterialPropertyBlock();
        FindTargets();
        PlayerWardrobe.Changed += Apply;
        Apply();
    }

    private void OnDestroy()
    {
        PlayerWardrobe.Changed -= Apply;
        foreach (var rt in _garmentRts) if (rt != null) rt.Release();
    }

    private void FindTargets()
    {
        if (Application.isPlaying) GearVisuals.FixSeatpost(transform);
        SkinnedMeshRenderer body = null;
        foreach (var smr in GetComponentsInChildren<SkinnedMeshRenderer>(true))
            if (smr.sharedMesh != null && (body == null || smr.sharedMesh.vertexCount > body.sharedMesh.vertexCount))
                body = smr;

        var split = Resources.Load<Mesh>("Shop/KuroKitSplit");
        if (body != null && split != null && body.sharedMesh == split && body.sharedMaterials.Length == split.subMeshCount)
        {
            // Already split: a clone of the player (the shop mannequin). Give it its own garment
            // material copies so recolouring it never touches the player's.
            var held = body.sharedMaterials;
            int orig = split.subMeshCount - 3;
            for (int i = 0; i < orig && _baseAtlas == null; i++)
                if (held[i] != null && held[i].HasProperty(MainTexId)) _baseAtlas = held[i].GetTexture(MainTexId);
            for (int g = 0; g < 3; g++) held[orig + g] = _garmentMats[g] = new Material(held[orig + g]) { name = "~Kit_" + Garments[g] };
            body.sharedMaterials = held;
            _body = body;
            _split = _baseAtlas != null;
        }
        else if (body != null && split != null && SplitMatches(split, body.sharedMesh))
        {
            var mats = body.materials;   // per-rig instances: never edit the shared asset
            Material bodyMat = null;
            foreach (var m in mats)
                if (m != null && m.HasProperty(MainTexId) && m.GetTexture(MainTexId) != null) { bodyMat = m; break; }
            if (bodyMat != null)
            {
                _baseAtlas = bodyMat.GetTexture(MainTexId);
                var all = new Material[mats.Length + 3];
                mats.CopyTo(all, 0);
                for (int g = 0; g < 3; g++)
                {
                    _garmentMats[g] = new Material(bodyMat) { name = "~Kit_" + Garments[g] };
                    all[mats.Length + g] = _garmentMats[g];
                }
                string sourceKey = RiderBlink.KeyFor(body);
                body.sharedMesh = split;
                RiderBlink.Alias(RiderBlink.KeyFor(body), sourceKey);   // refined split has more verts
                body.sharedMaterials = all;
                // Read back what the renderer actually holds: those are the objects to update.
                var held = body.sharedMaterials;
                for (int g = 0; g < 3; g++) _garmentMats[g] = held[mats.Length + g];
                _body = body;
                _split = true;
            }
        }

        foreach (var r in GetComponentsInChildren<Renderer>(true))
        {
            string n = r.name.ToLowerInvariant();
            bool wheel = n.Contains("rim") || n.Contains("wheel");
            foreach (var sm in r.sharedMaterials)
                if (sm != null && sm.name.ToLowerInvariant().Contains("rim")) wheel = true;
            if (wheel && r is MeshRenderer) _wheelRenderers.Add(r);
        }
        Debug.Log($"[shop] kit targets on '{name}': split mesh {(_split ? "ON" : "OFF")}, " +
                  $"atlas={(_baseAtlas != null ? _baseAtlas.name : "none")}, {_wheelRenderers.Count} wheel renderers");
        if (!_split)
            Debug.LogWarning("[shop] KuroKitSplit missing or does not match this body - apparel will not be shown. " +
                             "Run KuroKitSplitBake.Bake.");
    }

    public ShopItem ItemFor(ShopCategory c)
    {
        if (tryOn.TryGetValue(c, out var t)) return t;
        return previewOnly ? null : PlayerWardrobe.Equipped(c);
    }

    /// <summary>Renders one kit texture (mode 0 = body with jersey/bibs zones, 1 = helmet) into
    /// the RT cached at <paramref name="rtIndex"/>.</summary>
    private Texture Blit(int rtIndex, float mode, ShopItem jersey, ShopItem bibs, ShopItem helmet)
    {
        if (_composite == null || _mask == null) return _baseAtlas;
        if (_garmentRts[rtIndex] == null)
        {
            _garmentRts[rtIndex] = new RenderTexture(_baseAtlas.width, _baseAtlas.height, 0, RenderTextureFormat.ARGB32,
                                                     RenderTextureReadWrite.sRGB)
                { useMipMap = true, autoGenerateMips = true, name = rtIndex == 2 ? "~KuroKit_Helmet" : "~KuroKit_Body" };
            _garmentRts[rtIndex].Create();
        }
        _composite.SetTexture("_Mask", _mask);
        _composite.SetFloat("_Mode", mode);
        // The blit writes into an sRGB RenderTexture, so the shader takes LINEAR colours (without
        // .linear the kit was gamma-lifted twice: black bibs came out mid-grey).
        void Pair(string main, string trim, string use, ShopItem it)
        {
            _composite.SetFloat(use, it != null ? 1f : 0f);
            if (it == null) return;
            _composite.SetColor(main, it.Primary.linear);
            _composite.SetColor(trim, it.Trim.linear);
        }
        Pair("_Jersey", "_JerseyTrim", "_UseJersey", jersey);
        Pair("_Bibs", "_BibsTrim", "_UseBibs", bibs);
        Pair("_Helmet", "_HelmetTrim", "_UseHelmet", helmet);
        // photoreal garments: a full design atlas instead of the two-colour recolour
        void Design(string tex, string use, ShopItem it)
        {
            var t = DesignTexture(it);
            _composite.SetFloat(use, t != null ? 1f : 0f);
            _composite.SetTexture(tex, t != null ? t : Texture2D.blackTexture);
        }
        Design("_JerseyTex", "_UseJerseyTex", jersey);
        Design("_BibsTex", "_UseBibsTex", bibs);
        Graphics.Blit(_baseAtlas, _garmentRts[rtIndex], _composite);
        return _garmentRts[rtIndex];
    }

    private static readonly Dictionary<string, Texture2D> Designs = new Dictionary<string, Texture2D>();

    private static Texture2D DesignTexture(ShopItem it)
    {
        if (it == null || !it.Textured) return null;
        if (!Designs.TryGetValue(it.Asset, out var t))
        {
            t = Resources.Load<Texture2D>(it.Asset);
            if (t == null) Debug.LogWarning($"[shop] {it.Id}: no design atlas at Resources/{it.Asset}; falling back to the recolour");
            Designs[it.Asset] = t;
        }
        return t;
    }

    public void Apply()
    {
        if (_split)
        {
            var jersey = ItemFor(ShopCategory.Jersey);
            var bibs = ItemFor(ShopCategory.Bibs);
            var helmet = ItemFor(ShopCategory.Helmet);
            // Body: ONE texture for both the jersey and bibs slots; the mask's zones decide which
            // garment a texel is (clean 3D hems), so the two slots must share it.
            Texture body = (jersey != null || bibs != null) ? Blit(0, 0f, jersey, bibs, null) : _baseAtlas;
            Texture helm = helmet != null ? Blit(2, 1f, null, null, helmet) : _baseAtlas;
            for (int g = 0; g < 3; g++)
            {
                var item = ItemFor(Garments[g]);
                Texture tex = g == 2 ? helm : body;
                // Always write to what the renderer holds NOW: any script that reads
                // Renderer.materials (KuroOutline does) silently swaps every slot for an
                // "(Instance)" copy, which orphaned the references cached at setup.
                var held = _body.sharedMaterials;
                int slot = _body.sharedMesh.subMeshCount - 3 + g;
                var m = slot < held.Length && held[slot] != null ? held[slot] : _garmentMats[g];
                m.SetTexture(MainTexId, tex);
                if (m.HasProperty(BaseMapId)) m.SetTexture(BaseMapId, tex);
                if (item != null)
                    Debug.Log($"[shop] {name}: {Garments[g]} = {item.Id} on slot {_body.sharedMesh.subMeshCount - 3 + g} " +
                              $"(renderer holds it: {System.Array.IndexOf(_body.sharedMaterials, m) >= 0}, " +
                              $"shader {m.shader.name}, tex {tex.name}, mesh {_body.sharedMesh.name})");
            }
        }

        var wheels = ItemFor(ShopCategory.Wheels);
        var frame = ItemFor(ShopCategory.Frame);
        foreach (var r in _wheelRenderers)
        {
            r.GetPropertyBlock(_mpb);
            if (wheels != null) { _mpb.SetColor(ColorId, wheels.Primary); _mpb.SetColor(BaseColorId, wheels.Primary); }
            else _mpb.Clear();
            r.SetPropertyBlock(_mpb);
        }
        // aero gear stacks: deep wheels and an aero frame each trim the drag
        if (session != null)
            session.physics.equipmentCdaScale = (wheels != null ? wheels.CdaScale : 1f) * (frame != null ? frame.CdaScale : 1f);
        if (Application.isPlaying) GearVisuals.ApplyWheels(transform, wheels);
        if (Application.isPlaying) FrameVisuals.Apply(transform, frame);

        var computer = ItemFor(ShopCategory.BikeComputer);
        if (Application.isPlaying) GearVisuals.ApplyComputer(transform, computer);
        if (hud != null) hud.computerTier = computer != null ? computer.ComputerTier : 0;
    }
}
