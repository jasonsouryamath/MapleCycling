using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

/// <summary>
/// Bakes a smiling front-facing HEADSHOT for every staged rider and wires it onto their
/// <see cref="NpcGreeting"/> so the greeting face card has a face to show.
///
/// WHY BAKE IN UNITY RATHER THAN IN BLENDER. Every Sakura rider is the same sculpt
/// (KuroNPC_Coral_Rigged.glb); what makes Aoi not Mika is the per-rider LIVERY - a cloned body
/// material recoloured at staging time, in the scene, by SakuraNpcRoster. A Blender render of
/// the source GLB would therefore hand back eleven identical faces in the same kit. Rendering
/// the actual staged rider is the only way each portrait shows that rider's own colours, their
/// own helmet, and the same smile decal the greeting toggles on the model.
///
/// The expression itself is NOT invented here: it is the existing decal path from
/// <c>assets/3d/kuro/smile_coral.py</c> (see the NPC skill's expressions reference). The bake
/// simply switches the rider's own "SmileDecal" renderer on for the duration of the shot, which
/// is exactly what <see cref="NpcGreeting"/> does while greeting - so the portrait is a true
/// record of what the rider looks like mid-hello - and restores its previous state afterwards.
///
/// Output: <c>Assets/Resources/NpcPortraits/&lt;Name&gt;.png</c>. Resources, deliberately:
/// <see cref="NpcGreeting.ResolveIdentity"/> can then load a portrait by rider name at runtime,
/// so riders already serialized in the scene from an earlier staging pass get a face WITHOUT a
/// re-stage. The bake also assigns the reference directly on each component and saves the
/// scene, so the wiring does not depend on the Resources fallback either.
///
/// Idempotent: writes to a fixed path per rider name, and re-running simply re-renders.
/// </summary>
public static class NpcPortraitBake
{
    private const string ScenePath = "Assets/Scenes/SakuraPass.unity";
    private const string PortraitDir = "Assets/Resources/NpcPortraits";

    // ---- provisional framing tuning ---------------------------------------------------
    // All in UNSCALED rig metres; each rider's own rig scale is folded in at bake time, because
    // the roster stages riders from 1.31 m to 1.48 m tall and a fixed metre offset would frame
    // the tallest rider's chin and the shortest rider's hairline.
    /// <summary>PROVISIONAL: camera distance in front of the face.</summary>
    private const float CamDistance = 0.62f;
    /// <summary>PROVISIONAL: half-height of the orthographic frame - i.e. how tight the crop is.
    /// The first bake at 0.135 cropped the crown off and filled the chip with jaw and jersey;
    /// this chibi's head is a much larger fraction of its body than a realistic figure.</summary>
    private const float OrthoSize = 0.17f;
    /// <summary>PROVISIONAL: lift from the Head BONE origin to the middle of the face. The bone
    /// origin sits at the jaw on this skeleton, not at the eyes.</summary>
    private const float FaceUp = 0.15f;
    /// <summary>PROVISIONAL: push forward from the Head bone origin towards the face surface.</summary>
    private const float FaceForward = 0.015f;
    /// <summary>PROVISIONAL: portrait resolution. Displayed at 128 px, baked at 2x for the card.</summary>
    private const int Size = 256;

    /// <summary>Backdrop behind the head. Matches HudKit.GlassBed so the portrait reads as part
    /// of the card rather than as a pasted-in photo.</summary>
    private static readonly Color Backdrop = new Color(0.075f, 0.106f, 0.129f, 1f);

    [MenuItem("MapleRide/NPCs/Bake Greeting Portraits", priority = 62)]
    public static void BakeAll()
    {
        // Only force-switch to SakuraPass if the CURRENTLY open scene has no riders to bake
        // from at all. This keeps every existing call site's behaviour unchanged (they either
        // already have SakuraPass open or nothing else loaded), while letting a caller that
        // deliberately opened a different scene with its own staged NpcGreeting riders (e.g.
        // Shiosai's own SC_Persistent.unity, split out of SakuraPass by ShiosaiSceneBuilder)
        // bake from that scene instead of being redirected away from it.
        bool currentSceneHasRiders = Object.FindObjectsByType<NpcGreeting>(
            FindObjectsInactive.Include, FindObjectsSortMode.None).Length > 0;
        if (!currentSceneHasRiders && EditorSceneManager.GetActiveScene().path != ScenePath && File.Exists(ScenePath))
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        Directory.CreateDirectory(PortraitDir);

        var greeters = Object.FindObjectsByType<NpcGreeting>(FindObjectsInactive.Include,
                                                             FindObjectsSortMode.None);
        if (greeters.Length == 0)
        {
            Debug.LogError("[portrait] no NpcGreeting riders in the scene - nothing to bake.");
            return;
        }

        var baked = new List<string>();
        foreach (var g in greeters)
        {
            string riderName = NameOf(g);
            string path = $"{PortraitDir}/{riderName}.png";
            if (Bake(g, path, greeters)) baked.Add(riderName);
        }

        AssetDatabase.Refresh();

        // Import settings must be applied AFTER the refresh that creates the asset, or the
        // first import wins with the project defaults (compressed + mip-mapped) and a 128 px
        // face on the card comes back soft and blocky.
        foreach (var n in baked) Configure($"{PortraitDir}/{n}.png");
        AssetDatabase.Refresh();

        // Now wire them. Second pass so every portrait exists as an asset before it is assigned.
        int wired = 0;
        foreach (var g in greeters)
        {
            string riderName = NameOf(g);
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>($"{PortraitDir}/{riderName}.png");
            g.riderName = riderName;
            g.useFaceCard = true;
            g.portrait = tex;                 // null is fine - the card falls back to silhouette
            EditorUtility.SetDirty(g);
            if (tex == null) continue;
            wired++;
            Debug.Log($"[portrait] {riderName}: baked and wired -> {PortraitDir}/{riderName}.png");
        }

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        Debug.Log($"[portrait] {baked.Count} portraits baked, {wired} riders wired, scene saved.");
    }

    /// <summary>
    /// SURGICAL single-rider re-bake. Writes exactly ONE portrait PNG (the named rider's) and
    /// touches nothing else on disk. Unlike <see cref="BakeAll"/> it never deletes the stale
    /// chip of an inactive rider, so it is safe to run in the shared SakuraPass / SC_Persistent
    /// scenes where other regions' riders are parked inactive: baking one rider must not wipe
    /// the other 110 clean portraits. Wakes only the target's own inactive ancestors for the
    /// shot, then restores their exact prior active state before saving.
    ///
    /// The caller is responsible for opening the scene that actually contains the rider
    /// (SakuraPass for the Sakura roster, SC_Persistent for the Shiosai pool).
    /// </summary>
    /// <param name="exposureEv">Portrait camera's fixed EV100 (higher = darker). Default is the
    /// value every existing region was baked with; Minato's dusk-lit pool passes a lower one.</param>
    public static bool BakeSingle(string riderName, float exposureEv = DefaultExposureEv)
    {
        var greeters = Object.FindObjectsByType<NpcGreeting>(FindObjectsInactive.Include,
                                                             FindObjectsSortMode.None);
        NpcGreeting g = null;
        foreach (var c in greeters)
            if (NameOf(c) == riderName) { g = c; break; }
        if (g == null)
        {
            Debug.LogError($"[portrait] BakeSingle: no rider '{riderName}' in the open scene " +
                           $"'{EditorSceneManager.GetActiveScene().path}'.");
            return false;
        }

        // Wake this rider's own inactive ancestors (and itself) so the tight head camera has
        // something to photograph. Record them so we can re-park to the exact prior state.
        var restore = new List<GameObject>();
        for (var t = g.transform; t != null; t = t.parent)
            if (!t.gameObject.activeSelf) { t.gameObject.SetActive(true); restore.Add(t.gameObject); }

        Directory.CreateDirectory(PortraitDir);
        string path = $"{PortraitDir}/{riderName}.png";
        bool ok = Bake(g, path, greeters, exposureEv);

        // Re-park in reverse order, matching how they were woken.
        for (int i = restore.Count - 1; i >= 0; i--)
            if (restore[i] != null) restore[i].SetActive(false);

        if (!ok)
        {
            Debug.LogError($"[portrait] BakeSingle: '{riderName}' did not bake (see warnings above).");
            return false;
        }

        AssetDatabase.Refresh();
        Configure(path);
        AssetDatabase.Refresh();

        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        g.riderName = riderName;
        g.useFaceCard = true;
        g.portrait = tex;
        EditorUtility.SetDirty(g);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        Debug.Log($"[portrait] BakeSingle: {riderName} baked and wired -> {path} (only this PNG written).");
        return true;
    }

    /// <summary>"Sakura NPC Aoi" -> "Aoi"; anything else keeps its own name.</summary>
    private static string NameOf(NpcGreeting g)
    {
        if (!string.IsNullOrEmpty(g.riderName)) return g.riderName;
        string n = g.gameObject.name;
        int cut = n.LastIndexOf(' ');
        return (cut >= 0 && cut < n.Length - 1) ? n.Substring(cut + 1) : n;
    }

    private const float DefaultExposureEv = 0.7f;

    private static bool Bake(NpcGreeting g, string path, NpcGreeting[] allGreeters,
                             float exposureEv = DefaultExposureEv)
    {
        var root = g.transform;

        // A rider who is not active in THIS scene cannot be photographed in it - the camera
        // renders an empty backdrop and the card gets a black chip that looks like a bug. The
        // riders belonging to other regions are staged inactive here, so they are skipped, any
        // blank chip from an earlier bake is removed, and they fall back to the silhouette
        // until they are baked from their own scene.
        if (!root.gameObject.activeInHierarchy)
        {
            if (File.Exists(path))
            {
                AssetDatabase.DeleteAsset(path);
                Debug.Log($"[portrait] {root.name}: inactive in this scene - blank portrait " +
                          "removed, falls back to the silhouette.");
            }
            return false;
        }

        var head = FindChild(root, "Head");
        if (head == null)
        {
            Debug.LogWarning($"[portrait] {root.name}: no 'Head' bone under the rig - skipped. " +
                             "The card will fall back to the generic silhouette.");
            return false;
        }

        // The smile is the point of the portrait: turn the rider's own decal on for the shot.
        var smile = FindRenderer(root, g.smileRendererName);
        bool prevSmile = smile != null && smile.enabled;
        if (smile != null) smile.enabled = true;
        else Debug.LogWarning($"[portrait] {root.name}: no '{g.smileRendererName}' renderer - " +
                              "the portrait will be baked with a neutral mouth.");

        // Rig scale: the roster stages each rider at their own height, so every framing offset
        // is expressed in unscaled rig metres and multiplied through here.
        // Read it off the rider's RIG ROOT, not the Head bone: Kuro-based bodies carry a 0.01
        // glTF armature unit scale, so the bone's lossyScale is 0.01 and the camera would sit
        // millimetres from the face (blank chips). For Coral-family rigs (armature at scale 1)
        // the two are identical, so their framing is unchanged.
        var rigRoot = root.GetComponentInChildren<KuroBikeRig>(true);
        float k = Mathf.Max(0.01f, rigRoot != null ? rigRoot.transform.lossyScale.y : head.lossyScale.y);

        // Facing: the rider faces the way they are riding. World up is used rather than the
        // bone's own up because bone axes on this skeleton are not a reliable convention.
        Vector3 fwd = root.forward; fwd.y = 0f;
        if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
        fwd.Normalize();

        Vector3 face = head.position + Vector3.up * (FaceUp * k) + fwd * (FaceForward * k);

        var camGo = new GameObject("~PortraitCam");
        var cam = camGo.AddComponent<Camera>();
        camGo.transform.position = face + fwd * (CamDistance * k);
        camGo.transform.rotation = Quaternion.LookRotation(-fwd, Vector3.up);
        cam.orthographic = true;
        cam.orthographicSize = OrthoSize * k;
        cam.nearClipPlane = 0.01f;
        // Far clip just past the back of the head: it is the clipping, not a mask, that keeps
        // the road, the bike, other riders and the blossom out of a headshot.
        cam.farClipPlane = (CamDistance + 0.34f) * k;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Backdrop;
        cam.cullingMask = ~(1 << HudSprites.UiLayer);
        cam.allowHDR = true;
        // The pipeline's default grade is Fixed EV100=0 with Tonemapping=None, tuned for the road
        // (see HdrpVolumeSetup). A front-on headshot lights the whole face far more evenly than the
        // grazing road sun, so at EV0 the (now peach) skin clips to flat near-white. A small LOCAL
        // fixed-exposure pull-down - no tone-map, so nothing is desaturated - lands the face back in
        // range where the peach survives. Scoped to this throwaway camera via a parented volume;
        // the shared profile and every in-world capture are untouched.
        var gradeGo = new GameObject("~PortraitExposure");
        gradeGo.transform.SetParent(camGo.transform, false);
        var gradeVol = gradeGo.AddComponent<Volume>();
        gradeVol.isGlobal = true;
        gradeVol.priority = 10000f;
        var gradeProfile = ScriptableObject.CreateInstance<VolumeProfile>();
        var expOverride = gradeProfile.Add<Exposure>(true);
        expOverride.mode.overrideState = true;
        expOverride.mode.value = ExposureMode.Fixed;
        expOverride.fixedExposure.overrideState = true;
        expOverride.fixedExposure.value = exposureEv;   // EV100; global bakes at 0. Higher = darker.
        gradeVol.sharedProfile = gradeProfile;
        // No SakuraPostFX on this camera, on purpose: the sunset grade and bloom are a property
        // of the road shot, not of a portrait chip, and they turned the first test bakes orange.

        // A key light parented to the camera so a shadowed/tunnel face still gets some light.
        var keyGo = new GameObject("~PortraitKey");
        keyGo.transform.SetParent(camGo.transform, false);
        keyGo.transform.rotation = Quaternion.LookRotation(
            Quaternion.AngleAxis(-18f, Vector3.up) * (-fwd) + Vector3.down * 0.35f);
        var key = keyGo.AddComponent<Light>();
        key.type = LightType.Directional;
        key.color = new Color(1f, 0.9f, 0.8f, 1f);
        key.intensity = 2.0f;
        keyGo.AddComponent<HDAdditionalLightData>().SetIntensity(2.0f, LightUnit.Lux);

        // ISOLATE the target rider for the shot. The Shiosai traffic pool parks all thirty of
        // its riders at the SAME local origin (ShiosaiNpcTraffic.BuildRider leaves each root at
        // localPosition zero; the runtime director places them). When the portrait rebake wakes
        // the WHOLE pool at once, every rider's head coincides, so this tight head-clip camera
        // photographs an overlapping PILE of riders instead of one face: z-fighting hair streaks,
        // blown-out overlapping geometry, and byte-identical chips for riders whose cameras line
        // up. The far clip cannot separate co-located meshes - only culling can - so hide every
        // OTHER rider's renderers while this one is photographed, then restore them. Harmless for
        // spatially-separated riders (Sakura), where the far clip already did the isolating.
        var hidden = new List<Renderer>();
        if (allGreeters != null)
        {
            foreach (var other in allGreeters)
            {
                if (other == null || other == g) continue;
                foreach (var r in other.GetComponentsInChildren<Renderer>(true))
                    if (r.enabled) { r.enabled = false; hidden.Add(r); }
            }
        }

        var tex = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32) { antiAliasing = 8 };
        cam.targetTexture = tex;
        cam.Render();

        var prevActive = RenderTexture.active;
        RenderTexture.active = tex;
        var img = new Texture2D(Size, Size, TextureFormat.RGB24, false);
        img.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
        img.Apply();
        // WARM-BALANCE THE SKIN. In editor batchmode the HDRP light loop / ambient probe do not
        // update for this manual cam.Render(), so no injected light or volume overcomes the cool
        // GradientSky ambient and the face renders neutral grey even though the albedo is peach
        // (verified exhaustively: script lights at 3-20000 lux, sun re-aim, IndirectLighting
        // controller and material weathering all left the face R==G). Rather than ship a grey HUD
        // face, warm-balance ONLY the near-neutral SKIN pixels inside the central face region to the
        // same peach the live 3D rider shows. Saturated pixels (hair, kit, iris) and the dark
        // backdrop / bright catchlights are skipped, so liveries and eyes are untouched.
        WarmBalanceFace(img);
        img.Apply();
        File.WriteAllBytes(path, img.EncodeToPNG());
        RenderTexture.active = prevActive;

        cam.targetTexture = null;
        Object.DestroyImmediate(img);
        Object.DestroyImmediate(tex);
        Object.DestroyImmediate(camGo);

        // Restore every rider we hid to photograph this one in isolation.
        foreach (var r in hidden) if (r != null) r.enabled = true;

        if (smile != null) smile.enabled = prevSmile;
        return true;
    }

    // Central face region (bottom-up normalised, matching Texture2D.GetPixels origin) and the
    // neutral-skin gate. Tuned so the ellipse covers cheeks/nose/mouth/jaw and most of the
    // forehead while the saturation gate skips coloured hair, kit and irises.
    private const float FaceCx = 0.50f, FaceCy = 0.45f, FaceRx = 0.27f, FaceRy = 0.28f;

    private static void WarmBalanceFace(Texture2D img)
    {
        int n = img.width;
        var px = img.GetPixels();
        for (int y = 0; y < n; y++)
        {
            float dy = (y / (float)n - FaceCy) / FaceRy;
            for (int x = 0; x < n; x++)
            {
                float dx = (x / (float)n - FaceCx) / FaceRx;
                if (dx * dx + dy * dy > 1f) continue;            // outside the face oval
                int i = y * n + x;
                Color c = px[i];
                float mx = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
                float mn = Mathf.Min(c.r, Mathf.Min(c.g, c.b));
                float sat = mx > 1e-4f ? (mx - mn) / mx : 0f;
                if (mx < 0.30f || mx > 0.93f) continue;          // skip backdrop and catchlights
                if (sat > 0.20f) continue;                        // skip hair / kit / iris
                float strength = Mathf.Clamp01((0.20f - sat) / 0.20f);
                var warm = new Color(Mathf.Clamp01(c.r * 1.16f), c.g, Mathf.Clamp01(c.b * 0.85f), c.a);
                px[i] = Color.Lerp(c, warm, strength);
            }
        }
        img.SetPixels(px);
    }

    private static void Configure(string path)
    {
        var imp = AssetImporter.GetAtPath(path) as TextureImporter;
        if (imp == null) return;
        imp.textureType = TextureImporterType.Default;
        imp.sRGBTexture = true;
        imp.mipmapEnabled = false;
        imp.wrapMode = TextureWrapMode.Clamp;
        imp.filterMode = FilterMode.Bilinear;
        imp.npotScale = TextureImporterNPOTScale.None;
        imp.maxTextureSize = 256;
        imp.textureCompression = TextureImporterCompression.Uncompressed;
        imp.SaveAndReimport();
    }

    private static Transform FindChild(Transform root, string name)
    {
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            var hit = FindChild(root.GetChild(i), name);
            if (hit != null) return hit;
        }
        return null;
    }

    private static Renderer FindRenderer(Transform root, string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            if (r.gameObject.name == name) return r;
        return null;
    }
}
