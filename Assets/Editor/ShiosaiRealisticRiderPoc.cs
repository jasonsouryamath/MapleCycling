using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// ADDITIVE, REVERSIBLE proof-of-concept: a REALISTIC ~7.5-head adult cyclist staged on the
/// Shiosai Coast carriageway, so the realistic art direction can be judged against the target
/// reference before anyone decides whether to re-skin the game.
///
/// WHY THIS IS A SEPARATE OBJECT AND NOT A PLAYER SWAP
/// The playable scene (SakuraPass.unity) is SHARED by every region through RegionDirector, and
/// the player rider "Kuro on Sakura Pass" is the same GameObject in all of them. Re-pointing the
/// player's mesh would therefore change the rider in Sakura Pass, Azora and Maple City too -
/// the exact propagation this milestone was told not to do. The POC is instead a standalone
/// rider parented under the COAST environment root, so region visibility already scopes it to
/// Shiosai, the chibi rider everywhere else is bit-identical, and "Revert" is a delete.
///
/// MATERIALS: NOT HDRP/Lit.
/// ShiosaiShaderConversion documents that HDRP/Lit surfaces came back BLACK in this project,
/// because the scene's lights are authored at Built-in intensities (~1-3) which in HDRP's
/// physical units is darkness. The realistic look therefore has to come through
/// MapleRide/HDRP/CelLit - which is lit by the MapleRideSunBinder globals - with the CEL
/// BANDING DIALLED OUT (max ramp steps, max ramp softness, no anime rim light, no dapple).
/// That yields smooth, near-continuous PBR-ish shading rather than the flat cel/outline look.
/// KNOWN LIMITATION: CelLit has no normal-map slot, so the authored fabric normal map is not
/// used in engine; _DetailAmount stands in for it.
/// </summary>
public static class ShiosaiRealisticRiderPoc
{
    private const string RigPath = "Assets/Kuro/NPC/KuroNPC_KuroAnime_Rigged.glb";
    private const string BikePath = "Assets/Kuro/kuro_bike_colnago.glb";
    private const string MaterialDir = "Assets/Kuro/NPC/Materials";
    private const string CoastRootName = "Shiosai Coast Environment";
    private const string BikePrefix = "PocBike";

    public const string PocName = "Shiosai POC Realistic Rider";

    // ---- tunables -----------------------------------------------------------------------
    // ALL PROVISIONAL. Named so the identity call can be made by editing numbers, not geometry.

    /// <summary>The rider is authored at real scale (1.75 m tall), so the rig is NOT rescaled.</summary>
    private const float RigScale = 1.0f;

    /// <summary>
    /// MEASURED, not guessed: swept in Blender with fit_coral_to_bike.py against this rider.
    /// At 1.900 the palm-to-hood mesh gap is 7.3 mm, the wheels come out 0.665 m in diameter
    /// (correct 700c) and the leg reaches ~91% extension at the bottom of the stroke (the real
    /// saddle-height rule of thumb). The bike GLB is authored chibi-small (WHEEL_R = 0.175), so
    /// an adult needs a much larger multiplier than Kuro's 1.35 or Coral's 0.9.
    /// </summary>
    private const float BikeScale = 1.900f;

    private const float UnscaledWheelRadius = 0.175f;

    /// <summary>Chainage along the coast centreline to park the POC at. PROVISIONAL.</summary>
    private const float PocChainageM = 900f;

    /// <summary>Metres right of the centreline - riding lane, not the crown of the road.</summary>
    private const float LaneOffset = 1.5f;

    private const float GroundOffset = 0.02f;

    // Road-bike posture. Flatter than Coral's chibi 24/34 because a realistic arm chain can
    // actually reach the bar from a low position - the target reference shows a near-horizontal
    // back seen from behind. PROVISIONAL.
    private const float HipTilt = 26f;
    private const float SpineLean = 32f;
    private const float NeckLift = -12f;
    private const float HeadLift = -48f;
    private const float AnkleHeight = 0.045f;

    /// <summary>
    /// Hands on the DROPS, not the hoods. The bike only publishes Hood_L/Hood_R sockets, so the
    /// drops are expressed as an offset from the hood in the steerer's local space. Derived from
    /// build_kuro_bike.py's bar geometry (BAR_R = 0.048, hood at 50 deg, drop at ~140 deg):
    /// bar_pt(140) - bar_pt(50) = Blender (0, +0.0059, -0.0856); the hood socket carries a
    /// further +0.018 in z. Blender (bx,by,bz) lands in Unity at (-bx, bz, -by), so that delta
    /// is Unity (0, -0.0856, -0.0059). Left UNSCALED - TransformVector applies the bike scale.
    ///
    /// The extra depth beyond that geometric delta is the WRIST-VS-PALM correction the rig
    /// documents: the IK drives the WRIST onto the target, but this rider's skinned palm sits
    /// ahead of the wrist, so a purely geometric offset parked the hand ABOVE the hook with the
    /// drop empty (seen in poc_rider_grip.png). Deepened until the palm wraps the bar.
    /// </summary>
    /// ANIME KURO note: LogGrip reports a ~0.31 m "palm" gap on this mesh, but that metric
    /// samples the distal 5% of hand verts - the anime mitten's forward-pointing fingertips.
    /// The WRIST sits within 0.03 m of the hood, i.e. placement is correct; the residual is
    /// hand SHAPE (mrb2_grip_kuro.py cannot curl this mitten). Retuning this offset to the
    /// "suggested" value was tested and moved the gap 0.000 m, so the calibrated value stands.
    private static readonly Vector3 DropsOffset = new Vector3(0f, -0.144f, 0.164f);

    // ---- smooth-shading recipe for the kit ------------------------------------------------
    // Max ramp steps + max softness collapses the cel ramp into a near-continuous gradient;
    // rim light and dapple are the two most overtly "anime" terms and are turned off outright.
    private const float RampSteps = 6f;
    private const float RampSmooth = 0.35f;
    private const float RimStrength = 0f;
    private const float ShadeStrength = 0.55f;
    private static readonly Color ShadeTint = new Color(0.58f, 0.60f, 0.66f, 1f);

    [MenuItem("MapleRide/Shiosai/Stage Realistic Rider (POC)", priority = 40)]
    public static void Stage()
    {
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        OpenScene();

        var go = Build();

        // NpcGreeting drives the smile by toggling a child Renderer named exactly "SmileDecal".
        // The editor-staged POC has no NpcGreeting, so park the decal OFF (neutral, the canon
        // default expression) instead of leaving Kuro permanently smiling.
        var smile = FindSmile(go.transform);
        if (smile != null) { smile.enabled = false; Debug.Log("[poc-rider] SmileDecal found on '" + smile.name + "' -> parked disabled (neutral)"); }
        else Debug.LogWarning("[poc-rider] SmileDecal MISSING - NpcGreeting's smile hook will not fire.");

        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        Selection.activeGameObject = go;
        Debug.Log("[poc-rider] staged '" + PocName + "' at " + go.transform.position);
        if (Application.isBatchMode) EditorApplication.Exit(0);
    }

    // Exact-name lookup, matching NpcGreeting.smileRendererName.
    private static Renderer FindSmile(Transform poc)
    {
        foreach (var r in poc.GetComponentsInChildren<Renderer>(true))
            if (r.name == "SmileDecal") return r;
        return null;
    }

    [MenuItem("MapleRide/Shiosai/Revert To Chibi Rider (remove POC)", priority = 41)]
    public static void Revert()
    {
        OpenScene();
        int n = PruneAll();
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        Debug.Log("[poc-rider] reverted: removed " + n + " POC object(s). The chibi rider is untouched.");
        if (Application.isBatchMode) EditorApplication.Exit(0);
    }

    private static void OpenScene()
    {
        string path = ShiosaiCoastEnvironment.ScenePath;
        if (EditorSceneManager.GetActiveScene().path != path)
            EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
    }

    /// <summary>Idempotent: every staged copy is matched by EXACT name and destroyed first.</summary>
    private static int PruneAll()
    {
        int n = 0;
        var scene = EditorSceneManager.GetActiveScene();
        foreach (var root in scene.GetRootGameObjects())
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t == null || t.name != PocName) continue;
                Object.DestroyImmediate(t.gameObject);
                n++;
            }
        return n;
    }

    private static GameObject Build()
    {
        PruneAll();

        var rigAsset = AssetDatabase.LoadAssetAtPath<GameObject>(RigPath);
        var bikeAsset = AssetDatabase.LoadAssetAtPath<GameObject>(BikePath);
        if (rigAsset == null) throw new FileNotFoundException("POC rig not imported", RigPath);
        if (bikeAsset == null) throw new FileNotFoundException("bike not imported", BikePath);

        // Parent under the coast root so RegionDirector's existing environment visibility already
        // scopes the POC to Shiosai; fall back to a scene root only if the coast is not built.
        Transform parent = null;
        foreach (var root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
            if (root.name == CoastRootName) { parent = root.transform; break; }
        if (parent == null)
            Debug.LogWarning("[poc-rider] no '" + CoastRootName + "' root - staging at scene root, " +
                             "so the POC will NOT be region-scoped until the coast is rebuilt.");

        var route = ShiosaiCoastEnvironment.CoastRoute.Load();
        int i = Mathf.Clamp(route.IndexAt(PocChainageM), 0, route.Count - 1);
        var fwd = new Vector3(route.Tangent[i].x, 0f, route.Tangent[i].z).normalized;
        var pos = route.Position[i] + route.SideFlat(i) * LaneOffset + Vector3.up * GroundOffset;

        var poc = new GameObject(PocName);
        if (parent != null) poc.transform.SetParent(parent, false);
        poc.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(fwd, Vector3.up));
        poc.transform.localScale = Vector3.one * RigScale;

        // MUST be a child named exactly "Bike": KuroBikeRig.Setup has no global Find fallback.
        var bikeAnchor = new GameObject("Bike");
        bikeAnchor.transform.SetParent(poc.transform, false);
        bikeAnchor.transform.localScale = Vector3.one * BikeScale;
        var bikeModel = Object.Instantiate(bikeAsset, bikeAnchor.transform);
        bikeModel.name = "BikeMesh";
        Smooth(bikeModel, BikePrefix);

        var body = Object.Instantiate(rigAsset, poc.transform);
        body.name = "KuroRealArmatureAndMesh";
        Smooth(body, "PocRider");

        var rig = poc.AddComponent<KuroBikeRig>();
        rig.bikePrefab = bikeAsset;
        rig.bikeLocalOffset = Vector3.zero;
        // DriveDrivetrain divides WORLD distance by this, so BOTH scales must be folded in.
        rig.wheelRadius = UnscaledWheelRadius * BikeScale * RigScale;
        rig.gearRatio = 2.8f;
        rig.previewCadenceRpm = 55f;
        rig.hipTiltDegrees = HipTilt;
        rig.spineLeanDegrees = SpineLean;
        rig.neckLiftDegrees = NeckLift;
        rig.headLiftDegrees = HeadLift;
        rig.riderLateralOffset = 0f;
        rig.ankleHeight = AnkleHeight;
        rig.handTargetLocalOffset = DropsOffset;
        // Opt in to level pedal platforms. Without this the pedal spins with the crank arm, and
        // because the ankle IK target is pedal.up * ankleHeight the foot sinks THROUGH the pedal
        // at the top of the stroke - which is exactly what broke the pedal-stroke reads.
        rig.keepPedalsLevel = true;
        EditorUtility.SetDirty(rig);

        // NO KuroOutline. The inverted hull destroys skinned meshes (MapleRideKuroSetup:191) and
        // an outline is the single most cel-looking thing we could add to a realism POC.

        rig.SendMessage("Setup", SendMessageOptions.DontRequireReceiver);
        rig.ForceSolveOnce();
        return poc;
    }

    /// <summary>
    /// Clones every material on the instance and re-targets it at MapleRide/HDRP/CelLit with the
    /// banding dialled out. Cloning is mandatory: the player instantiates the SAME bike GLB and
    /// shares its materials BY REFERENCE, so editing in place would repaint the player's bicycle
    /// and dirty an imported asset that the next reimport silently reverts.
    /// </summary>
    private static void Smooth(GameObject root, string prefix)
    {
        var lit = Shader.Find("MapleRide/HDRP/CelLit");
        if (lit == null)
        {
            Debug.LogWarning("[poc-rider] MapleRide/HDRP/CelLit not found - materials left as imported.");
            return;
        }
        if (!AssetDatabase.IsValidFolder(MaterialDir))
            AssetDatabase.CreateFolder("Assets/Kuro/NPC", "Materials");

        var cache = new Dictionary<Material, Material>();
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
        {
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                var src = mats[i];
                if (src == null) continue;
                if (!cache.TryGetValue(src, out var clone))
                {
                    clone = Convert(src, lit, prefix);
                    if (prefix == BikePrefix) Darken(clone);
                    else Delight(clone);
                    cache[src] = clone;
                }
                mats[i] = clone;
            }
            r.sharedMaterials = mats;
            // The smile ribbon is a single-sided 3 mm-proud decal. Its winding depends on which
            // way the face was ray-cast, so a back-face cull can make it vanish entirely (it did:
            // the first capture rendered a pixel-identical neutral mouth with the decal enabled).
            // Double-siding it is winding-agnostic and costs one extra triangle pass on 40 faces.
            if (r.name == "SmileDecal")
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null || !m.HasProperty("_Cull")) continue;
                    m.SetFloat("_Cull", 0f);
                    EditorUtility.SetDirty(m);
                }
            EditorUtility.SetDirty(r);
        }
        AssetDatabase.SaveAssets();
    }

    /// POC-only dark livery. The target reference bike is near-black carbon, not the shipped red
    /// Colnago. Applied to the CLONED material only, so the player's shared bike GLB is untouched.
    /// Provisional tuning - matches Shared_Colnago_Carbon_Black_CelLit.
    private static readonly Color CarbonBlack = new Color(0.086f, 0.112f, 0.152f, 1f);

    private static void Darken(Material m)
    {
        if (!m.HasProperty("_Color")) return;
        // The sheet's KURO bike is ALL-BLACK carbon. The shipped Colnago carries a red frame
        // material AND (via other passes) a red wheel-rim accent left over from the KAGE era,
        // which is the red part the user flagged in PROBLEM_kuro_brown_helmet_red_bike.png.
        // Catch it by NAME as well as by colour: a red clone that has already been re-tinted by
        // another pass no longer trips the chromatic test, but its name still says Red.
        bool redByName = m.name.IndexOf("Red", System.StringComparison.OrdinalIgnoreCase) >= 0;
        var c = m.GetColor("_Color");
        // Only repaint the liveried frame parts; leave tyres, bar tape and metal hardware alone.
        float mx = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
        float mn = Mathf.Min(c.r, Mathf.Min(c.g, c.b));
        bool chromatic = mx > 0.18f && (mx - mn) > 0.08f;
        if (chromatic || redByName) m.SetColor("_Color", CarbonBlack);
        // Same additive-warm clamp as the character (see the BLACK-STAYS-BLACK CLAMP note): the
        // black frame tubes are near-zero albedo, so CelLit's albedo-independent spec+rim terms
        // are all that is left of them and they wash the frame warm under a low sun too.
        if (m.HasProperty("_SpecStrength")) m.SetFloat("_SpecStrength", 0.02f);
        if (m.HasProperty("_SpecTint")) m.SetColor("_SpecTint", AnimeSpecTint);
        if (m.HasProperty("_RimStrength")) m.SetFloat("_RimStrength", AnimeRimStrength);
        if (m.HasProperty("_RimColor")) m.SetColor("_RimColor", AnimeRimColor);
        EditorUtility.SetDirty(m);
    }

    /// <summary>
    /// POC-only albedo scale for the CHARACTER atlas. Meshy bakes studio lighting into its base
    /// colour, so the skin arrives at ~0.8 albedo where real linear skin is ~0.35-0.45. Against
    /// this project's non-physical light rig (ShiosaiAmbience keyIntensity ~1.16) the sunlit
    /// side of the face and arms then clips to flat white with no shading detail at all - see
    /// the pre-fix poc_rider_side.png. Scaling the albedo toward true linear values is the
    /// half of the documented light-rig fix that can be done reversibly per-material, without
    /// touching the shared light rig (which would change every region and every other NPC).
    /// PROVISIONAL - the real fix is physically-scaled lights + exposure, and until that lands
    /// this only buys headroom, it does not buy physically-correct response.
    /// </summary>
    private const float CharacterAlbedoScale = 0.72f;

    // ---- shadow-side readability floor (the crush-to-black fix) ----------------------------
    // GAMEPLAY REALITY: the real chase camera (RideCameraSetup offset (0,1.60,-4.20), FOV 52)
    // sits directly BEHIND the rider, so it only ever sees Kuro's SHADOW side. At the shader's
    // default floor (_ShadowAmbient 0.38, _AmbientStrength 1.0, and the POC's _ShadeStrength
    // 0.55) his whole camera-facing kit crushed to a near-black silhouette while the chibi
    // traffic riders beside him - brighter-albedo kits on the SAME light rig - read fine (see
    // poc_rider_gameplay_chase, the reproduction of the user's screenshot).
    //
    // The two ambient terms in CelLit are c.rgb * MR_Ambient * _AmbientStrength *
    // lerp(_ShadowAmbient, 1, shade): MULTIPLICATIVE on albedo. Lifting the shadow-kept fraction
    // therefore lifts the WHITE kit graphics (KURO wordmark, 黒 kanji, maple leaf, star socks)
    // so they read as white, while the black lycra - low albedo - stays black, which is exactly
    // the TARGET look (deep-black kit, crisp white graphics, visible form). _ShadowAmbient only
    // affects the shadow side (lerp collapses to 1 when lit), so the bright hero framings are
    // untouched. This is the per-material half of the documented light-rig fix - NONE of the
    // scene-wide RegionDirector rig (keyIntensity / Trilight / fixedExposure) is touched, so the
    // chibi riders and every other region are bit-identical.
    // SIDE-SUN BLOWOUT FIX (provisional tunables - tune against anime_kuro_side.png).
    // Symptom: under direct side sun the BLACK jersey washed to pale grey-mauve across the upper
    // back/shoulder while the chest and shorts stayed black, reading as a "shroud". It is NOT a
    // specular hotspot (_SpecStrength was already 0.03) and NOT a skinning defect (a weight
    // surgery changed the render not at all, and the mesh deforms cleanly at 70 deg arm reach and
    // an 85 deg clavicle swing in Blender). It is the READABILITY LIFT: the upper back sits in
    // the cel ramp's shadow band, where _ShadowAmbient floors it at 58% of a LAVENDER _ShadeColor
    // - i.e. pale mauve. Black has no headroom to absorb that floor; mid-tone skin does, which is
    // why only the kit blew out. Lowering the floor and neutralising the shade tint lets the kit
    // hold its tone. Kept moderate so the white maple leaf / KURO marks still read backlit.
    private const float CharacterShadowAmbient = 0.40f;   // was 0.58 (readability pass); floor that washed black to mauve
    private const float CharacterAmbientStrength = 1.12f; // unchanged - drives overall fill, not the shadow floor
    private const float CharacterShadeStrength = 0.66f;   // was Convert's 0.55 (ShadeStrength)

    private static void Delight(Material m)
    {
        if (!m.HasProperty("_Color")) return;
        var c = m.GetColor("_Color");
        m.SetColor("_Color", new Color(c.r * CharacterAlbedoScale,
                                       c.g * CharacterAlbedoScale,
                                       c.b * CharacterAlbedoScale, c.a));
        // Skin and matte lycra are not glossy; whatever roughness Meshy exported, the sunlit
        // highlight is what is reading as plastic here.
        if (m.HasProperty("_SpecStrength")) m.SetFloat("_SpecStrength", 0.04f);
        if (m.HasProperty("_Gloss")) m.SetFloat("_Gloss", 0.12f);
        // Lift the shadow-side floor so the camera-facing (backlit) kit reads in gameplay.
        if (m.HasProperty("_ShadowAmbient")) m.SetFloat("_ShadowAmbient", CharacterShadowAmbient);
        if (m.HasProperty("_AmbientStrength")) m.SetFloat("_AmbientStrength", CharacterAmbientStrength);
        if (m.HasProperty("_ShadeStrength")) m.SetFloat("_ShadeStrength", CharacterShadeStrength);
        Animefy(m);
        EditorUtility.SetDirty(m);
    }

    // ---- ANIME cel recipe (character only) -------------------------------------------------
    // DIRECTION UPDATE: the final Kuro look is anime / cel-shaded (kuro-model-sheet-01.png), not
    // photoreal. The POC already runs on MapleRide/HDRP/CelLit, but Convert() dialled the ramp to
    // a near-continuous gradient (RampSteps 6, RampSmooth 0.35) plus a photoreal roughness break
    // (_DetailAmount 0.30) to fake realism. Animefy() flips ONLY the cloned CHARACTER materials to
    // a true toon response - flat shading bands, a crisp shadow line, a controlled cool shadow
    // tint, a soft anime rim and no photoreal surface noise - while KEEPING the readability lifts
    // above (_ShadowAmbient / _AmbientStrength) so the white kit graphics still read. The shared
    // shader defaults, the bike recipe and every other NPC are untouched (POC-only, reversible).
    private const float AnimeRampSteps  = 3f;     // lit / mid / shadow flat bands (was 6, smooth)
    private const float AnimeRampSmooth = 0.055f; // crisp cel terminator (was 0.35, near-gradient)
    private static readonly Color AnimeShadeTint = new Color(0.44f, 0.44f, 0.48f, 1f); // near-neutral toon shadow (was 0.40/0.40/0.52 lavender - the mauve in the blowout)

    // ---- BLACK-STAYS-BLACK CLAMP (the "brown helmet" fix) ----------------------------------
    // Symptom (user screenshot PROBLEM_kuro_brown_helmet_red_bike.png): from the chase camera the
    // BLACK aero helmet, the hair and the kit edges render warm TAN/BROWN, while the same asset
    // renders black in the isolated studio turnarounds (bodyF_blender_*.png). So it is not the
    // texture and not the mesh - it is the in-scene lighting response.
    //
    // ROOT CAUSE, read straight off MapleRideCelLit.shader (lines 174-177):
    //     col  = c.rgb * lit * sun;                                          // albedo-scaled
    //     col += c.rgb * MR_Ambient(n) * _AmbientStrength * lerp(...);       // albedo-scaled
    //     col += tintSpec * spec * sun;                                      // ADDITIVE, NO albedo
    //     col += tintRim  * rim  * lerp(...) * sun;                          // ADDITIVE, NO albedo
    // The last two terms are NOT multiplied by the albedo, and both are multiplied by the warm
    // MR_SunColor(). On a near-black surface the two albedo-scaled terms contribute almost
    // nothing, so the ONLY thing left is warm additive spec+rim -> the helmet reads brown. The
    // mid-tone skin and the WHITE graphics are albedo-driven and are unaffected by clamping them,
    // which is exactly why this fix cannot crush the leaf / KURO / kanji: those are bright albedo.
    //
    // The ambient floor (_ShadowAmbient / _AmbientStrength) is deliberately NOT touched - it is
    // albedo-multiplicative, it is what keeps the white branding legible backlit, and it is the
    // value the user already signed off on. Only the two additive warm terms are clamped.
    // PROVISIONAL - tune against the chase render, not by feel.
    private static readonly Color AnimeRimColor  = new Color(0.50f, 0.54f, 0.68f, 1f); // COOL rim: the sun it is multiplied by is warm
    private const float AnimeRimStrength = 0.05f;  // was 0.20 -> warm edge wash on every black rib
    private static readonly Color AnimeSpecTint  = new Color(0.82f, 0.86f, 1.00f, 1f); // was warm (1,0.93,0.85)
    private const float AnimeSpecStrength = 0.0f;  // was 0.03; additive warm sheen on black = brown
    private const float AnimeGloss = 0.10f;

    private static void Animefy(Material m)
    {
        if (m.HasProperty("_RampSteps"))  m.SetFloat("_RampSteps", AnimeRampSteps);
        if (m.HasProperty("_RampSmooth")) m.SetFloat("_RampSmooth", AnimeRampSmooth);
        if (m.HasProperty("_RampOffset")) m.SetFloat("_RampOffset", 0.04f);   // pull the lit band up a touch
        if (m.HasProperty("_ShadeColor")) m.SetColor("_ShadeColor", AnimeShadeTint);
        // Flat toon surface: kill the photoreal roughness/detail break Convert() added.
        if (m.HasProperty("_DetailAmount")) m.SetFloat("_DetailAmount", 0f);
        if (m.HasProperty("_DappleStrength")) m.SetFloat("_DappleStrength", 0f);
        if (m.HasProperty("_TintVariation")) m.SetFloat("_TintVariation", 0f);
        if (m.HasProperty("_WeatherAmount")) m.SetFloat("_WeatherAmount", 0f);
        // Minimal specular (no plastic hotspot), plus a soft anime rim for silhouette pop.
        // Both are ADDITIVE and albedo-independent in CelLit, so on the black kit/helmet they are
        // the only visible term - see the BLACK-STAYS-BLACK CLAMP note above.
        if (m.HasProperty("_SpecStrength")) m.SetFloat("_SpecStrength", AnimeSpecStrength);
        if (m.HasProperty("_SpecTint")) m.SetColor("_SpecTint", AnimeSpecTint);
        if (m.HasProperty("_Gloss")) m.SetFloat("_Gloss", AnimeGloss);
        if (m.HasProperty("_RimStrength")) m.SetFloat("_RimStrength", AnimeRimStrength);
        if (m.HasProperty("_RimPower")) m.SetFloat("_RimPower", 4.5f);
        if (m.HasProperty("_RimColor")) m.SetColor("_RimColor", AnimeRimColor);
        EditorUtility.SetDirty(m);
    }

    private static Material Convert(Material src, Shader lit, string prefix)    {
        // Read BEFORE the shader swap: assigning .shader drops every property the new shader
        // does not declare, and glTF's names do not match the cel shader's.
        Color baseColor = Color.white;
        Texture baseMap = null;
        float roughness = 1f, metallic = 0f;
        foreach (var n in new[] { "baseColorFactor", "_BaseColorFactor", "_BaseColor", "_Color" })
            if (src.HasProperty(n)) { baseColor = src.GetColor(n); break; }
        foreach (var n in new[] { "baseColorTexture", "_BaseColorTexture", "_BaseColorMap", "_MainTex" })
            if (src.HasProperty(n)) { baseMap = src.GetTexture(n); break; }
        foreach (var n in new[] { "roughnessFactor", "_RoughnessFactor", "_Roughness", "_Smoothness" })
            if (src.HasProperty(n)) { roughness = src.GetFloat(n); break; }
        foreach (var n in new[] { "metallicFactor", "_MetallicFactor", "_Metallic" })
            if (src.HasProperty(n)) { metallic = src.GetFloat(n); break; }

        var m = new Material(lit) { name = prefix + "_" + src.name };
        m.SetColor("_Color", baseColor);
        if (baseMap != null) m.SetTexture("_MainTex", baseMap);
        else if (src.name.Contains("Logo"))
            Debug.LogWarning("[poc-rider] '" + src.name + "' arrived with NO base colour texture - " +
                             "the KURO logo will render as a blank patch.");
        m.SetFloat("_Gloss", Mathf.Clamp(1f - Mathf.Clamp01(roughness), 0.01f, 1f));
        m.SetFloat("_SpecStrength", Mathf.Lerp(0.08f, 0.45f, Mathf.Clamp01(metallic)));
        // The realism recipe.
        m.SetFloat("_RampSteps", RampSteps);
        m.SetFloat("_RampSmooth", RampSmooth);
        m.SetFloat("_RimStrength", RimStrength);
        m.SetFloat("_ShadeStrength", ShadeStrength);
        m.SetColor("_ShadeColor", ShadeTint);
        m.SetFloat("_DappleStrength", 0f);
        m.SetFloat("_TintVariation", 0f);
        m.SetFloat("_WeatherAmount", 0f);
        // Stands in for the fabric normal map CelLit cannot sample.
        m.SetFloat("_DetailAmount", 0.30f);
        m.SetFloat("_DetailScale", 24f);

        string path = AssetDatabase.GenerateUniqueAssetPath(MaterialDir + "/" + m.name + ".mat");
        // Reuse a stable path so re-staging does not accumulate _1, _2, ... material assets.
        string stable = MaterialDir + "/" + m.name + ".mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(stable);
        if (existing != null)
        {
            existing.shader = lit;
            existing.CopyPropertiesFromMaterial(m);
            EditorUtility.SetDirty(existing);
            return existing;
        }
        AssetDatabase.CreateAsset(m, path == stable ? path : stable);
        return m;
    }

    // ---- verification renders -------------------------------------------------------------

    [MenuItem("MapleRide/Shiosai/Capture Realistic Rider (POC)", priority = 42)]
    public static void Capture()
    {
        OpenScene();

        string dir = Path.GetFullPath(Path.Combine(Application.dataPath,
                        "../reference/good_graphics/shiosai_real_rider"));
        Directory.CreateDirectory(dir);

        var regions = Object.FindFirstObjectByType<RegionDirector>(FindObjectsInactive.Include);
        string previous = null;
        if (regions != null)
        {
            regions.Resolve();
            previous = regions.currentRegionId;
            regions.currentRegionId = RegionCatalog.ShiosaiCoast;
            regions.ApplyEnvironmentVisibility();
            regions.ApplyAmbience();
        }

        try
        {
            Transform poc = null;
            foreach (var root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    if (t.name == PocName) { poc = t; break; }
            if (poc == null) { Debug.LogError("[poc-rider] not staged - run Stage first."); return; }

            // LateUpdate is not pumped headless, so the rig would capture in its REST pose.
            var rig = poc.GetComponent<KuroBikeRig>();
            if (rig != null)
            {
                // Setup() is private; the project's staging passes reach it by SendMessage.
                rig.SendMessage("Setup", SendMessageOptions.DontRequireReceiver);
                rig.ForceSolveOnce();
            }

            // Batchmode has no player loop, so skinning is dispatched once and then never
            // refreshed - cam.Render() happily re-draws the FIRST solved pose for every
            // subsequent crank phase. That is why all four pedal-stroke frames came back with a
            // pixel-identical leg while BakeMesh (CPU, always current) correctly reported the
            // foot moving 0.28 m. Forcing the matrices per render is the fix; without it the
            // whole pedal-stroke check silently verifies nothing.
            foreach (var s in poc.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                s.forceMatrixRecalculationPerRender = true;
                s.updateWhenOffscreen = true;
            }

            var p = poc.position;
            var f = poc.forward;
            var r = poc.right;
            var up = Vector3.up;

            // Gameplay POV: three-quarter from behind, roughly where the follow camera sits.
            Shot(dir, "poc_rider_gameplay_34_behind",
                 p - f * 3.6f + r * 1.5f + up * 1.55f, p + f * 0.6f + up * 0.95f, 48f);
            // Straight behind - the exact framing of the target reference.
            Shot(dir, "poc_rider_behind",
                 p - f * 3.2f + up * 1.45f, p + f * 0.8f + up * 0.90f, 42f);
            // Head / helmet closeup, from BEHIND-3/4 - shows the helmet and hair.
            Shot(dir, "poc_rider_head",
                 p - f * 0.95f + r * 0.55f + up * 1.62f, p + f * 0.28f + up * 1.38f, 30f);
            // FACE closeup, from in FRONT - the blank-mask rejection point has to be answerable
            // with a shot that actually contains the face, not the back of the helmet.
            Shot(dir, "poc_rider_face",
                 p + f * 1.15f + r * 0.42f + up * 1.30f, p + f * 0.34f + up * 1.22f, 26f);
            // Torso / kit closeup - the KURO logo must read, and the jersey must not band.
            Shot(dir, "poc_rider_torso",
                 p - f * 1.35f + r * 0.35f + up * 1.50f, p + f * 0.05f + up * 1.10f, 34f);
            // Bike seating + grip: side-on at bar height.
            Shot(dir, "poc_rider_grip",
                 p + f * 0.55f + r * 1.25f + up * 0.98f, p + f * 0.62f + up * 0.88f, 34f);
            // Whole seated figure, side on: proves wheels on the road and feet on the pedals.
            Shot(dir, "poc_rider_side",
                 p + r * 3.4f + up * 1.05f, p + up * 0.90f, 40f);

            // Prove NpcGreeting's smile hook has something to toggle: same framing as the face
            // shot, captured with the decal forced on, then restored to its staged state.
            var smile = FindSmile(poc);
            if (smile == null) Debug.LogWarning("[poc-rider] SmileDecal MISSING - greeting smile cannot fire.");
            else
            {
                bool was = smile.enabled;
                smile.enabled = true;
                Shot(dir, "poc_rider_smile",
                     p + f * 1.15f + r * 0.42f + up * 1.30f, p + f * 0.34f + up * 1.22f, 26f);
                smile.enabled = was;
                Debug.Log("[poc-rider] SmileDecal present (staged enabled=" + was + ") - captured poc_rider_smile");
            }

            Debug.Log("[poc-rider] captured 7 views to " + dir);

            LogGrip(poc);
            CapturePedalStroke(dir, poc);
        }
        finally
        {
            if (regions != null && previous != null)
            {
                regions.currentRegionId = previous;
                regions.ApplyEnvironmentVisibility();
                regions.ApplyAmbience();
            }
            // Read-only pass: never let a dirty scene write a crash-recovery backup over the
            // real 329 MB scene.
            MapleRideSceneBootstrap.DiscardChanges();
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
    }

    /// <summary>
    /// THE PEDAL-STROKE CHECK. The user's confirmed "ideal" is explicitly that the rider is long
    /// enough that "I can actually see him pedal", so a single static frame does not verify this
    /// milestone - four phases of the same crank, from the gameplay eye, do. Also reports the
    /// leg geometry behind the claim (chain length, saddle height over the bottom bracket, and
    /// extension at the bottom of the stroke) so the renders are backed by measurement.
    /// </summary>
    private static void CapturePedalStroke(string dir, Transform poc)
    {
        var rig = poc.GetComponent<KuroBikeRig>();
        if (rig == null) { Debug.LogWarning("[poc-rider] no rig - skipping stroke capture."); return; }

        var hip = FindDescendant(poc, "LeftUpLeg");
        var knee = FindDescendant(poc, "LeftLeg");
        var ankle = FindDescendant(poc, "LeftFoot");
        var bb = FindDescendant(poc, "BB");
        var saddle = FindDescendant(poc, "SaddleTop");
        if (hip && knee && ankle)
        {
            float chain = Vector3.Distance(hip.position, knee.position)
                        + Vector3.Distance(knee.position, ankle.position);
            Debug.Log($"[poc-rider] LEG chain(hip->knee->ankle) = {chain:0.000} m (world)");
            if (bb && saddle)
            {
                float saddleOverBb = saddle.position.y - bb.position.y;
                Debug.Log($"[poc-rider] SADDLE over BB = {saddleOverBb:0.000} m; " +
                          $"extension at bottom of stroke = {(saddleOverBb + CrankLenWorld(bb, poc)) / chain:0.0%} " +
                          "(a real fit is 88-92%)");
            }
        }

        var p = poc.position; var f = poc.forward; var r = poc.right; var up = Vector3.up;

        // Quarter turns of ONE crank: 0 = start, 90 = power phase, 180 = opposite leg down,
        // 270 = recovery. If the legs read at adult length these four are visibly different.
        for (int i = 0; i < 4; i++)
        {
            if (i > 0) rig.AdvanceCrank(90f);
            string tag = (i * 90).ToString("000");
            LogPhase(poc, tag);
            // The gameplay eye - the framing the user actually judged.
            Shot(dir, "poc_rider_stroke_" + tag + "_gameplay",
                 p - f * 3.6f + r * 1.5f + up * 1.55f, p + f * 0.6f + up * 0.95f, 48f);
            // Side on, tight on the drivetrain: the unambiguous read of where the legs are.
            Shot(dir, "poc_rider_stroke_" + tag + "_side",
                 p + r * 2.9f + up * 0.95f, p + up * 0.80f, 42f);
            // TIGHT on the cranks and feet. The wide side shot at 42 deg puts a full crank
            // revolution inside ~80 px, which is small enough that "the legs are frozen" and
            // "the legs are moving" look the same - exactly the ambiguity the log is meant to
            // break, so give the eye a frame that can actually settle it.
            Shot(dir, "poc_rider_stroke_" + tag + "_drivetrain",
                 p + r * 1.30f + up * 0.55f, p + f * 0.10f + up * 0.42f, 36f);
        }

        // Leave the crank where a mid-stroke hero frame reads best.
        rig.AdvanceCrank(45f);
        Shot(dir, "poc_rider_midstroke_hero",
             p - f * 3.2f + r * 1.1f + up * 1.40f, p + f * 0.5f + up * 0.85f, 45f);
        Debug.Log("[poc-rider] captured 4 pedal-stroke phases + a mid-stroke hero frame.");
    }

    /// <summary>
    /// Crank arm length in world metres.
    ///
    /// Deliberately NOT the Pedal_L-to-BB distance: the pedal platform carries its own offset
    /// outboard of the crank arm, so that measurement over-reports the radius and in turn
    /// over-reports leg extension. The crank arm is authored at 0.070 m unscaled, so the true
    /// world radius is that times whatever scale the bike is staged at.
    /// </summary>
    private const float UnscaledCrankArmLen = 0.070f;

    private static float CrankLenWorld(Transform bb, Transform poc)
    {
        var crank = FindDescendant(poc, "Crank_L");
        float s = crank != null ? crank.lossyScale.y : (bb != null ? bb.lossyScale.y : 1f);
        return UnscaledCrankArmLen * s;
    }

    /// <summary>
    /// Dumps the drivetrain and leg state for one crank phase. Renders alone cannot distinguish
    /// "the legs are not being driven" from "the legs are driven but the motion is too small to
    /// see", and that distinction decides whether this is a rig bug or a framing problem.
    /// </summary>
    /// <summary>
    /// Measures the rendered PALM against the brake hood. KuroBikeRig drives the wrist onto the
    /// hood, but the skinned palm sits ahead of the wrist, so the fist floats. The shared rig's
    /// own note says the palm has to be sampled IN-ENGINE (Blender's glTF importer reorients
    /// bones, so bone-local offsets do not transfer) - this does exactly that and reports the
    /// residual already converted into steerer-local space, which is the space DropsOffset is in.
    /// Reported, never auto-applied: DropsOffset stays a reviewed POC-only constant.
    /// </summary>
    private static void LogGrip(Transform poc)
    {
        var smr = poc.GetComponentInChildren<SkinnedMeshRenderer>();
        var hoodR = FindDescendant(poc, "Hood_R");
        var steer = FindDescendant(poc, "SteerPivot");
        var handR = FindDescendant(poc, "RightHand");
        var foreR = FindDescendant(poc, "RightForeArm");
        if (smr == null || hoodR == null || steer == null || handR == null || foreR == null)
        {
            Debug.LogWarning("[poc-rider] GRIP: missing " +
                $"smr={smr != null} hood={hoodR != null} steer={steer != null} " +
                $"hand={handR != null} fore={foreR != null}");
            return;
        }

        var baked = new Mesh();
        smr.BakeMesh(baked, true);
        var verts = baked.vertices;
        var l2w = smr.transform.localToWorldMatrix;
        var bw = smr.sharedMesh.boneWeights;
        var bones = smr.bones;
        int hIdx = -1, fIdx = -1;
        for (int b = 0; b < bones.Length; b++)
        {
            if (bones[b] != null && bones[b].name == "RightHand") hIdx = b;
            if (bones[b] != null && bones[b].name == "RightForeArm") fIdx = b;
        }

        // The wrist bone often owns no vertices outright (it sits inside the forearm), so fall
        // back to the forearm cluster and take the tip - the verts furthest along wrist->hand.
        Vector3 axis = (handR.position - foreR.position).normalized;
        var pts = new List<Vector3>();
        int lim = Mathf.Min(bw.Length, verts.Length);
        for (int i = 0; i < lim; i++)
        {
            var w = bw[i];
            int dom = w.boneIndex0; float best = w.weight0;
            if (w.weight1 > best) { best = w.weight1; dom = w.boneIndex1; }
            if (w.weight2 > best) { best = w.weight2; dom = w.boneIndex2; }
            if (w.weight3 > best) { best = w.weight3; dom = w.boneIndex3; }
            if (dom == hIdx || dom == fIdx) pts.Add(l2w.MultiplyPoint3x4(verts[i]));
        }
        Object.DestroyImmediate(baked);
        if (pts.Count == 0) { Debug.LogWarning("[poc-rider] GRIP: no hand/forearm verts"); return; }

        pts.Sort((a, b2) => Vector3.Dot(a, axis).CompareTo(Vector3.Dot(b2, axis)));
        int tipN = Mathf.Max(1, pts.Count / 20);      // distal 5% = the fist
        Vector3 palm = Vector3.zero;
        for (int i = pts.Count - tipN; i < pts.Count; i++) palm += pts[i];
        palm /= tipN;

        Vector3 corr = hoodR.position - palm;
        Vector3 local = steer.InverseTransformVector(corr);
        Debug.Log($"[poc-rider] GRIP palm={palm.ToString("0.000")} hood={hoodR.position.ToString("0.000")} " +
                  $"wrist={handR.position.ToString("0.000")} gap={corr.magnitude:0.000} m " +
                  $"steerLocalCorrection={local.ToString("0.000")} " +
                  $"suggestedDropsOffset={(DropsOffset + local).ToString("0.000")} tipVerts={tipN}");
    }

    private static void LogPhase(Transform poc, string tag)
    {
        var crankL = FindDescendant(poc, "Crank_L");
        var crankR = FindDescendant(poc, "Crank_R");
        var pedalL = FindDescendant(poc, "Pedal_L");
        var pedalR = FindDescendant(poc, "Pedal_R");
        var ankleL = FindDescendant(poc, "LeftFoot");
        var ankleR = FindDescendant(poc, "RightFoot");
        var kneeL = FindDescendant(poc, "LeftLeg");

        Debug.Log($"[poc-rider] PHASE {tag} " +
                  $"crankL.localEuler={(crankL ? crankL.localEulerAngles.ToString("0.0") : "NULL")} " +
                  $"crankR.localEuler={(crankR ? crankR.localEulerAngles.ToString("0.0") : "NULL")}");
        Debug.Log($"[poc-rider] PHASE {tag} " +
                  $"pedalL={(pedalL ? pedalL.position.ToString("0.000") : "NULL")} " +
                  $"pedalR={(pedalR ? pedalR.position.ToString("0.000") : "NULL")}");
        Debug.Log($"[poc-rider] PHASE {tag} " +
                  $"ankleL={(ankleL ? ankleL.position.ToString("0.000") : "NULL")} " +
                  $"ankleR={(ankleR ? ankleR.position.ToString("0.000") : "NULL")} " +
                  $"kneeL={(kneeL ? kneeL.position.ToString("0.000") : "NULL")}");

        // Bone transforms moving does NOT prove the rendered mesh moves - weights could be
        // wrong. Invariant 5 of the NPC skill: measure the BAKED skinned mesh, not bounds.
        var smr = poc.GetComponentInChildren<SkinnedMeshRenderer>();
        if (smr != null)
        {
            var baked = new Mesh();
            smr.BakeMesh(baked, true);
            var verts = baked.vertices;
            var l2w = smr.transform.localToWorldMatrix;
            float minY = float.MaxValue;
            var lows = new List<float>();
            for (int i = 0; i < verts.Length; i++)
            {
                float y = l2w.MultiplyPoint3x4(verts[i]).y;
                if (y < minY) minY = y;
                lows.Add(y);
            }
            lows.Sort();
            float low200 = 0f;
            int cnt = Mathf.Min(200, lows.Count);
            for (int i = 0; i < cnt; i++) low200 += lows[i];
            Debug.Log($"[poc-rider] PHASE {tag} BAKED mesh lowestVertY={minY:0.000} " +
                      $"meanLowest200Y={(low200 / cnt):0.000} verts={verts.Length}");

            // Per-bone check. BakeMesh preserves vertex order, so sharedMesh.boneWeights
            // index-matches the baked positions. If the vertices DOMINATED by RightFoot do not
            // track the RightFoot bone, the mesh is not following the rig and every "the legs
            // move" conclusion drawn from bone transforms alone is wrong.
            var sm = smr.sharedMesh;
            var bw = sm.boneWeights;
            var bones = smr.bones;
            int rfIdx = -1, lfIdx = -1;
            for (int b = 0; b < bones.Length; b++)
            {
                if (bones[b] != null && bones[b].name == "RightFoot") rfIdx = b;
                if (bones[b] != null && bones[b].name == "LeftFoot") lfIdx = b;
            }
            ReportBoneCluster("RightFoot", rfIdx, bw, verts, l2w, tag);
            ReportBoneCluster("LeftFoot", lfIdx, bw, verts, l2w, tag);

            Object.DestroyImmediate(baked);
        }
    }

    /// <summary>
    /// Mean world position of the baked vertices dominated by one bone - the direct test of
    /// whether the skinned mesh follows that bone.
    /// </summary>
    private static void ReportBoneCluster(string boneName, int idx, BoneWeight[] bw,
                                          Vector3[] verts, Matrix4x4 l2w, string tag)
    {
        if (idx < 0) { Debug.Log($"[poc-rider] PHASE {tag} {boneName}: bone not in smr.bones"); return; }
        Vector3 sum = Vector3.zero;
        int n = 0;
        int lim = Mathf.Min(bw.Length, verts.Length);
        for (int i = 0; i < lim; i++)
        {
            var w = bw[i];
            int dom = w.boneIndex0;
            float best = w.weight0;
            if (w.weight1 > best) { best = w.weight1; dom = w.boneIndex1; }
            if (w.weight2 > best) { best = w.weight2; dom = w.boneIndex2; }
            if (w.weight3 > best) { best = w.weight3; dom = w.boneIndex3; }
            if (dom == idx) { sum += l2w.MultiplyPoint3x4(verts[i]); n++; }
        }
        Debug.Log(n == 0
            ? $"[poc-rider] PHASE {tag} {boneName}: NO dominated verts"
            : $"[poc-rider] PHASE {tag} {boneName} meshCentroid={(sum / n).ToString("0.000")} n={n}");
    }

    private static Transform FindDescendant(Transform root, string name)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t;
        return null;
    }

    private static void Shot(string dir, string name, Vector3 pos, Vector3 look, float fov)
    {
        var go = new GameObject("~PocRiderCam");
        var cam = go.AddComponent<Camera>();
        cam.transform.position = pos;
        cam.transform.LookAt(look, Vector3.up);
        cam.fieldOfView = fov;
        cam.nearClipPlane = 0.05f;
        cam.farClipPlane = 14000f;
        cam.clearFlags = CameraClearFlags.Skybox;
        // Without HDR + the region grade the frame blows out to near-white.
        cam.allowHDR = true;

        var fx = go.AddComponent<SakuraPostFX>();
        var a = RegionDirector.ShiosaiAmbience;
        fx.bloomThreshold = a.bloomThreshold; fx.bloomIntensity = a.bloomIntensity;
        fx.exposure = a.exposure; fx.saturation = a.saturation; fx.contrast = a.contrast;
        fx.lift = a.lift; fx.gain = a.gain;
        fx.vignetteStrength = a.vignette; fx.vignetteSoftness = a.vignetteSoftness;
        // No depth of field on a character check - it would hide the very defects we are hunting.
        fx.dofStrength = 0f;
        if (a.aerialRange > 0f)
        {
            fx.aerialStart = a.aerialStart; fx.aerialRange = a.aerialRange;
            fx.aerialDesaturation = a.aerialDesaturation; fx.aerialFlatten = a.aerialFlatten;
            fx.aerialTint = a.aerialTint; fx.aerialTintAmount = a.aerialTintAmount;
        }

        const int w = 1280, h = 960;
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        cam.targetTexture = rt;
        cam.Render();

        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var img = new Texture2D(w, h, TextureFormat.RGB24, false);
        img.ReadPixels(new Rect(0, 0, w, h), 0, 0);
        img.Apply();
        File.WriteAllBytes(Path.Combine(dir, name + ".png"), img.EncodeToPNG());
        RenderTexture.active = prev;

        cam.targetTexture = null;
        Object.DestroyImmediate(go);
        rt.Release();
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(img);
        Debug.Log("[poc-rider] wrote " + name + ".png");
    }
}
