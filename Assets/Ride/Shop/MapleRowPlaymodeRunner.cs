using System.Collections;
using System.IO;
using UnityEngine;

/// <summary>
/// Play-mode proof for Maple Row (Maple City only): the approach alert, the street itself
/// (chase frames plus HUD-free side views of both frontages), the shop opened on the street, and
/// a full purchase (jersey, bibs, helmet, CF65 wheels, APEX Elite) checked on the real rider,
/// physics and HUD. Then everything comes off again and the originals must come back.
/// Frames go to reference/good_graphics/maple_row/. Driven by MapleRowPlaymodeCapture (editor).
/// The player's real wardrobe save is backed up by the editor side and restored afterwards.
/// </summary>
public class MapleRowPlaymodeRunner : MonoBehaviour
{
    public string outDir;
    public float[] chaseStations = { 300f, 450f, 575f, 690f };
    public float[] sideStations = { 500f, 640f };

    public static bool Finished, Failed;
    private static int _pass, _fail;

    private RideBootstrap _boot;
    private Camera _cam, _uiCam;
    private bool _hideHud;
    private float _lastUiDelta;

    private void Check(bool ok, string what)
    {
        if (ok) _pass++; else _fail++;
        Debug.Log((ok ? "[maplerow-play] PASS " : "[maplerow-play] FAIL ") + what);
    }

    private IEnumerator Start()
    {
        Finished = Failed = false;
        _pass = _fail = 0;
        _boot = FindFirstObjectByType<RideBootstrap>();
        if (_boot == null) { Fail("no RideBootstrap"); yield break; }
        _boot.Resolve();
        _cam = _boot.rideCamera != null ? _boot.rideCamera : Camera.main;
        yield return null;

        if (_boot.regions == null || !_boot.regions.FastTravel(RegionCatalog.MapleCity))
            Debug.LogWarning("[maplerow-play] could not fast travel to Maple City.");
        _boot.session.autoLapsFromTarget = false;
        _boot.session.plannedLaps = 1;
        _boot.session.SelectCourse(RegionCatalog.Find(RegionCatalog.MapleCity).BuiltCourseId);
        _boot.devices.acceptKeyboardEffort = false;
        yield return null;
        yield return null;

        var shop = _boot.GetComponent<MapleRowShop>();
        var kit = FindFirstObjectByType<KitAppearance>();
        Check(shop != null, "MapleRowShop is on the ride root");
        Check(kit != null, "KitAppearance is on the rider");
        Check(kit != null && kit.HasSplitMesh, "rider body uses the per-triangle garment split (fitment)");
        Check(shop != null && shop.InMapleCity, $"course '{_boot.session.courseId}' counts as Maple City");
        if (shop == null) { Fail("no shop"); yield break; }
        RouteCanvasesToCamera(shop);

        var street = GameObject.Find("Maple Row Boutiques");
        Debug.Log("[maplerow-play] storefront root 'Maple Row' " + (street != null ? "FOUND" : "not found (C6 not in scene yet)"));

        // --- approach alert + street chase frames
        foreach (float d in chaseStations)
        {
            yield return Seek(d, 0.6f, 24);
            yield return Capture($"maplerow_chase_{d:000}m");
            var toast = shop.transform.Find("Maple Row Shop UI/Maple Row Alert");
            bool shown = toast != null && toast.gameObject.activeSelf;
            bool expect = d >= shop.alertFromM && d <= shop.streetEndM;
            Check(shown == expect && (!shown || RenderedByUiCam(toast)),
                  $"alert at {d:0} m is {(shown ? "shown" : "hidden")}{(shown ? (RenderedByUiCam(toast) ? " and drawn by the UI camera" : " but NOT drawn by the UI camera") : "")}");
        }

        // --- HUD-free side views of both frontages
        _hideHud = true;
        var follow = _cam.GetComponent<KuroFollowCamera>();
        foreach (float d in sideStations)
        {
            yield return Seek(d, 0f, 20);
            var t = follow != null ? follow.target : null;
            if (t == null) continue;
            if (follow != null) follow.enabled = false;
            foreach (int side in new[] { 1, -1 })
            {
                _cam.transform.position = t.position - t.forward * 14f + t.right * side * 1.5f + Vector3.up * 1.7f;
                _cam.transform.LookAt(t.position + t.forward * 20f + t.right * side * 14f + Vector3.up * 3.5f);
                yield return Capture($"maplerow_side_{d:000}m_{(side > 0 ? "right" : "left")}");
            }
            // elevated look down the street
            _cam.transform.position = t.position - t.forward * 30f + Vector3.up * 14f;
            _cam.transform.LookAt(t.position + t.forward * 60f);
            yield return Capture($"maplerow_street_{d:000}m");
            if (follow != null) follow.enabled = true;
        }
        _hideHud = false;

        // --- a store door: prompt, dismount, the store screen
        Check(shop.Stores.Count == 6, "six store doors on the street: " +
              string.Join(", ", shop.Stores.ConvertAll(s => $"{s.Brand} {s.Station:0} m {(s.Side > 0 ? "R" : "L")}")));
        var first = shop.Stores.Count > 0 ? shop.Stores[0] : null;
        yield return Seek(first != null ? first.Station : 575f, 0f, 24);
        var door = shop.DoorAt(_boot.session.DistanceM, out var across);
        var title = shop.transform.Find("Maple Row Shop UI/Maple Row Alert/Title")?.GetComponent<UnityEngine.UI.Text>();
        Check(door != null && title != null && title.text.Contains(MapleRowShop.StoreName[door.Brand]),
              $"at {_boot.session.DistanceM:0} m (rider side {shop.RiderSide()}) the prompt names the {door?.Brand} door: " +
              $"'{title?.text}'{(across != null ? $", {across.Brand} across the road" : "")}");
        yield return Capture("maplerow_door_prompt");

        StartCoroutine(shop.EnterStore(door != null ? door.Brand : "HALCYON"));
        float t0 = Time.realtimeSinceStartup;
        int shot = 0;
        foreach (float at in new[] { 0.45f, 1.1f, 1.7f, 2.3f, 2.8f })
        {
            while (Time.realtimeSinceStartup - t0 < at) yield return null;
            _hideHud = true;
            yield return Capture($"maplerow_dismount_{shot++}");
            _hideHud = false;
        }
        float tw = Time.realtimeSinceStartup;
        while (shop.Busy && Time.realtimeSinceStartup - tw < 10f) yield return null;
        Check(shop.IsOpen && RideInputGate.Locked, "[B] at the door: dismount, then the store opens with the ride paused");
        var dismount = FindFirstObjectByType<KuroDismount>();
        Check(dismount != null && dismount.T > 0.99f, $"Kuro is off the bike (dismount T = {(dismount != null ? dismount.T : -1f):0.00})");
        yield return new WaitForSecondsRealtime(1.2f);
        yield return Capture("maplerow_shop_open");
        var panel = shop.transform.Find("Maple Row Shop UI/Maple Row Shop");
        Check(RenderedByUiCam(panel) && _lastUiDelta > 0.15f,
              $"store screen is active, on the UI camera and drawn into the frame (centre delta {_lastUiDelta:0.000})");
        Check(shop.Studio != null && shop.Studio.Kit != null && shop.Studio.Kit.HasSplitMesh,
              "fitting room mannequin is the player's own rider with the garment split");

        // try-on views in three stores: outfit, wheels, head unit
        foreach (var brand in ShopCatalog.Brands)
        {
            shop.OpenStore(brand);
            for (int i = 0; i < shop.ItemCount; i++)
            {
                shop.Select(i);
                yield return new WaitForSecondsRealtime(1.4f);
                yield return Capture($"maplerow_tryon_{brand.ToLowerInvariant()}_{i}");
            }
        }

        // The actual user path: CARBONFORGE opens on frames (not wheels), then the real BUY /
        // EQUIP button must transform the current bike synchronously through Wardrobe.Changed.
        shop.OpenStore("CARBONFORGE");
        var carbonItems = ShopCatalog.Items.FindAll(i => i.Brand == "CARBONFORGE");
        carbonItems.Sort((a, b) => (a.Category == ShopCategory.Frame ? 0 : 1)
            .CompareTo(b.Category == ShopCategory.Frame ? 0 : 1));
        var buyFrame = ShopCatalog.Get("frame_monarch_r1");
        int buyIndex = carbonItems.IndexOf(buyFrame);
        Check(buyIndex >= 0 && carbonItems[0].Category == ShopCategory.Frame && carbonItems.FindAll(i => i.Category == ShopCategory.Frame).Count >= 10,
              $"CARBONFORGE opens as a frameset shop ({carbonItems.FindAll(i => i.Category == ShopCategory.Frame).Count} frames, frame first)");
        if (PlayerWardrobe.IsEquipped(buyFrame.Id)) PlayerWardrobe.ToggleEquip(buyFrame); // deterministic reruns
        bool wasOwned = PlayerWardrobe.Owns(buyFrame.Id);
        shop.Select(buyIndex);
        var action = shop.transform.Find("Maple Row Shop UI/Maple Row Shop/Counter/Action")?.GetComponent<UnityEngine.UI.Button>();
        action?.onClick.Invoke();
        yield return null;
        var liveSteer = GearVisuals.FindDeep(kit.transform, "SteerPivot");
        Check(action != null && PlayerWardrobe.Owns(buyFrame.Id) && PlayerWardrobe.IsEquipped(buyFrame.Id) &&
              FrameVisuals.HasFrame(kit.transform) && liveSteer != null && liveSteer.Find(FrameVisuals.ForkName) != null &&
              FrameVisuals.VisibleStockParts(kit.transform) == 0,
              $"real {(wasOwned ? "EQUIP" : "BUY AND WEAR")} button immediately transforms the current bike to {buyFrame.Name}");
        yield return Capture("maplerow_frameshop_buy_transformed");

        // --- buy and wear a full kit
        var cdaBefore = _boot.session.physics.equipmentCdaScale;
        foreach (var id in new[] { "halcyon_jersey_tide", "ardent_bibs_classic", "aerolite_helmet_climb", "carbonforge_65", "apex_elite" })
        {
            var it = ShopCatalog.Get(id);
            if (!PlayerWardrobe.Owns(id)) PlayerWardrobe.Buy(it);
            if (!PlayerWardrobe.IsEquipped(id)) PlayerWardrobe.ToggleEquip(it);
        }
        yield return null;
        yield return null;
        {
            SkinnedMeshRenderer body = null;
            foreach (var smr in kit.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (smr.sharedMesh != null && (body == null || smr.sharedMesh.vertexCount > body.sharedMesh.vertexCount)) body = smr;
            var mats = body != null ? body.sharedMaterials : new Material[0];
            string texNames = string.Join(", ", System.Array.ConvertAll(mats, m => m == null ? "null" :
                m.name + ":" + (m.HasProperty("_MainTex") && m.GetTexture("_MainTex") != null ? m.GetTexture("_MainTex").name : "-")));
            Debug.Log($"[maplerow-play] body renderer '{(body != null ? body.name : "none")}' mesh '{(body != null ? body.sharedMesh.name : "")}' " +
                      $"enabled {(body != null && body.enabled && body.gameObject.activeInHierarchy)}; materials {texNames}");
            Check(body != null && body.sharedMesh.name == "KuroKitSplit" && texNames.Contains("~KuroKit_Body"),
                  "after purchase the visible body renders the split mesh with the recoloured jersey");
        }
        Check(Mathf.Approximately(_boot.session.physics.equipmentCdaScale, 0.97f), $"CF65 wheels set cdA scale {cdaBefore:0.000} -> {_boot.session.physics.equipmentCdaScale:0.000}");
        Check(_boot.hud != null && _boot.hud.computerTier == 2, "APEX Elite sets HUD tier 2");
        yield return new WaitForSecondsRealtime(1.0f);
        yield return Capture("maplerow_shop_bought");

        // --- 2026-09-26: framesets + photoreal kits. Every frame must replace the stock frame and
        // fork on the real rider (frame under the bike root, fork under SteerPivot), stack its drag
        // with the wheels, and the pro kits must composite their design atlas.
        var riderRoot = kit.transform;
        int stockBaseline = FrameVisuals.VisibleStockParts(riderRoot);
        foreach (var id in new[] { "halcyon_jersey_contour", "alpentek_bibs_rsr" })
        {
            var it = ShopCatalog.Get(id);
            if (!PlayerWardrobe.Owns(id)) PlayerWardrobe.Buy(it);
            if (!PlayerWardrobe.IsEquipped(id)) PlayerWardrobe.ToggleEquip(it);
        }
        yield return null;
        Check(Resources.Load<Texture2D>(ShopCatalog.Get("halcyon_jersey_contour").Asset) != null &&
              Resources.Load<Texture2D>(ShopCatalog.Get("alpentek_bibs_rsr").Asset) != null,
              "pro kit design atlases load from Resources/Shop/Kits");
        Check(stockBaseline >= 10, $"stock bike exposes its frame parts ({stockBaseline} stock tube renderers)");

        StartCoroutine(shop.LeaveStore());
        yield return new WaitForSecondsRealtime(1.3f);
        _hideHud = true;
        yield return Capture("maplerow_remount");
        _hideHud = false;
        tw = Time.realtimeSinceStartup;
        while (shop.Busy && Time.realtimeSinceStartup - tw < 10f) yield return null;
        Check(!shop.IsOpen && !RideInputGate.Locked && (dismount == null || dismount.T <= 0.001f),
              "leaving: back on the bike and the ride is released");

        yield return Seek(620f, 0.6f, 30);
        Check(_boot.hud.Canvas.transform.Find("Bike Computer") != null, "bike computer strip is on the HUD");
        yield return Capture("maplerow_kit_chase");
        yield return RiderCloseUps("maplerow_kit");

        // --- every frameset on the real bike, riding: frame + fork swapped, drag stacked
        foreach (var it in ShopCatalog.Items.FindAll(i => i.Category == ShopCategory.Frame))
        {
            if (!PlayerWardrobe.Owns(it.Id)) PlayerWardrobe.Buy(it);
            if (!PlayerWardrobe.IsEquipped(it.Id)) PlayerWardrobe.ToggleEquip(it);
            yield return null;
            yield return null;
            var steer = GearVisuals.FindDeep(riderRoot, "SteerPivot");
            bool frameOn = FrameVisuals.HasFrame(riderRoot);
            bool forkOn = steer != null && steer.Find(FrameVisuals.ForkName) != null;
            int stock = FrameVisuals.VisibleStockParts(riderRoot);
            float want = 0.97f * it.CdaScale;
            Check(frameOn && forkOn && stock == 0 && Mathf.Abs(_boot.session.physics.equipmentCdaScale - want) < 1e-4f,
                  $"{it.Id}: frame {(frameOn ? "on" : "MISSING")}, fork {(forkOn ? "on SteerPivot" : "MISSING")}, " +
                  $"{stock} stock tubes still drawn, cdA x{_boot.session.physics.equipmentCdaScale:0.0000} (want {want:0.0000})");
            yield return RiderCloseUps("maplerow_frame_" + it.Id.Substring("frame_".Length));
        }

        // --- take it all off: Kuro's own look comes back
        foreach (var it in ShopCatalog.Items)
            if (PlayerWardrobe.IsEquipped(it.Id)) PlayerWardrobe.ToggleEquip(it);
        yield return null;
        Check(Mathf.Approximately(_boot.session.physics.equipmentCdaScale, 1f) && _boot.hud.computerTier == 0,
              "unequipping restores physics and HUD");
        Check(!FrameVisuals.HasFrame(kit.transform) && FrameVisuals.VisibleStockParts(kit.transform) == stockBaseline,
              $"unequipping the frame restores the stock frame ({FrameVisuals.VisibleStockParts(kit.transform)}/{stockBaseline} stock tubes drawn)");
        yield return RiderCloseUps("maplerow_default");

        Debug.Log($"[maplerow-play] RESULT {_pass} passed, {_fail} failed; frames in {outDir}");
        Failed = _fail > 0;
        Finished = true;
    }

    private IEnumerator RiderCloseUps(string prefix)
    {
        var follow = _cam.GetComponent<KuroFollowCamera>();
        var t = follow != null ? follow.target : null;
        if (t == null) yield break;
        _hideHud = true;
        follow.enabled = false;
        _boot.devices.HoldZeroPower = true;
        for (int f = 0; f < 10; f++) { _boot.devices.EffortInput = 0f; yield return null; }
        _cam.transform.position = t.position + t.right * 2.6f + t.forward * 1.2f + Vector3.up * 1.4f;
        _cam.transform.LookAt(t.position + Vector3.up * 1.0f);
        yield return Capture(prefix + "_side");
        _cam.transform.position = t.position - t.forward * 3.2f + t.right * 0.8f + Vector3.up * 1.8f;
        _cam.transform.LookAt(t.position + Vector3.up * 1.1f);
        yield return Capture(prefix + "_rear");
        _cam.transform.position = t.position + t.forward * 2.4f + t.right * 1.1f + Vector3.up * 1.3f;
        _cam.transform.LookAt(t.position + Vector3.up * 0.95f);
        yield return Capture(prefix + "_front");
        _boot.devices.HoldZeroPower = false;
        follow.enabled = true;
        _hideHud = false;
    }

    private IEnumerator Seek(float d, float effort, int frames)
    {
        _boot.session.SeekTo(d);
        if (_boot.follower != null) _boot.follower.Apply();
        SnapCamera();
        for (int f = 0; f < frames; f++) { _boot.devices.EffortInput = effort; yield return null; }
        SnapCamera();
    }

    private void Fail(string why)
    {
        Debug.LogError("[maplerow-play] " + why);
        Failed = true;
        Finished = true;
    }

    private void SnapCamera()
    {
        var follow = _cam.GetComponent<KuroFollowCamera>();
        if (follow == null || follow.target == null || !follow.enabled) return;
        var t = follow.target;
        _cam.transform.position = t.position + t.TransformDirection(follow.offset);
        _cam.transform.LookAt(t.position + Vector3.up * 1.1f);
    }

    private void RouteCanvasesToCamera(MapleRowShop shop)
    {
        _cam.cullingMask &= ~(1 << HudSprites.UiLayer);
        var uiGo = new GameObject("~MapleRowHudCam");
        uiGo.transform.SetParent(_cam.transform, false);
        _uiCam = uiGo.AddComponent<Camera>();
        _uiCam.clearFlags = CameraClearFlags.Depth;
        _uiCam.cullingMask = 1 << HudSprites.UiLayer;
        _uiCam.nearClipPlane = 0.05f;
        _uiCam.farClipPlane = 100f;
        _uiCam.depth = _cam.depth + 10f;
        _uiCam.allowHDR = false;
        var hd = uiGo.AddComponent<UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData>();
        hd.clearColorMode = UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData.ClearColorMode.None;
        hd.volumeLayerMask = 0;
        hd.customRenderingSettings = true;
        foreach (var f in new[] { UnityEngine.Rendering.HighDefinition.FrameSettingsField.AtmosphericScattering,
                                  UnityEngine.Rendering.HighDefinition.FrameSettingsField.Volumetrics,
                                  UnityEngine.Rendering.HighDefinition.FrameSettingsField.Postprocess })
        {
            hd.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)f] = true;
            hd.renderingPathCustomFrameSettings.SetEnabled(f, false);
        }
        RouteOverlayCanvases();
    }

    // Overlay canvases never reach a RenderTexture capture. Re-run before every UI render so
    // canvases built or rebuilt later (the shop's own canvas, a HUD rebuild on a tier change)
    // are drawn too. The HUD and the shop both live on the ride root, so a
    // GetComponentInChildren<Canvas> lookup only ever found the HUD.
    private void RouteOverlayCanvases()
    {
        if (_uiCam == null) return;
        foreach (var c in FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (c == null || c.renderMode != RenderMode.ScreenSpaceOverlay) continue;
            var parent = c.transform.parent;
            if (parent != null && parent.GetComponentInParent<Canvas>(true) != null) continue;
            c.renderMode = RenderMode.ScreenSpaceCamera;
            c.worldCamera = _uiCam;
            c.planeDistance = 1f;
            HudSprites.SetLayerRecursively(c.gameObject, HudSprites.UiLayer);
        }
    }

    private bool RenderedByUiCam(Transform t)
    {
        if (t == null || _uiCam == null || !t.gameObject.activeInHierarchy) return false;
        var c = t.GetComponentInParent<Canvas>();
        if (c == null) return false;
        var root = c.rootCanvas;
        return root.isActiveAndEnabled && root.renderMode == RenderMode.ScreenSpaceCamera &&
               root.worldCamera == _uiCam && (_uiCam.cullingMask & (1 << t.gameObject.layer)) != 0;
    }

    private IEnumerator Capture(string name)
    {
        yield return null;
        const int W = 1920, H = 1080;
        var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
        var prev = _cam.targetTexture;
        _cam.targetTexture = rt;
        _cam.Render();
        _cam.targetTexture = prev;
        _lastUiDelta = 0f;
        var prevActive = RenderTexture.active;
        if (_uiCam != null && !_hideHud)
        {
            // centre half of the frame, before and after the UI pass: how much the UI changed it
            var probe = new Rect(W / 4, H / 4, W / 2, H / 2);
            RenderTexture.active = rt;
            var before = new Texture2D(W / 2, H / 2, TextureFormat.RGB24, false);
            before.ReadPixels(probe, 0, 0);
            RenderTexture.active = prevActive;

            RouteOverlayCanvases();
            _uiCam.fieldOfView = _cam.fieldOfView;
            _uiCam.targetTexture = rt;
            Canvas.ForceUpdateCanvases();
            _uiCam.Render();
            _uiCam.targetTexture = null;

            RenderTexture.active = rt;
            var after = new Texture2D(W / 2, H / 2, TextureFormat.RGB24, false);
            after.ReadPixels(probe, 0, 0);
            RenderTexture.active = prevActive;
            var a = before.GetPixels32();
            var b = after.GetPixels32();
            long sum = 0;
            for (int i = 0; i < a.Length; i++)
                sum += Mathf.Abs(a[i].r - b[i].r) + Mathf.Abs(a[i].g - b[i].g) + Mathf.Abs(a[i].b - b[i].b);
            _lastUiDelta = sum / (a.Length * 3f * 255f);
            Destroy(before);
            Destroy(after);
        }
        RenderTexture.active = rt;
        var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
        tex.Apply();
        RenderTexture.active = prevActive;
        Directory.CreateDirectory(outDir);
        File.WriteAllBytes(Path.Combine(outDir, name + ".png"), tex.EncodeToPNG());
        Destroy(tex);
        rt.Release();
        Destroy(rt);
        Debug.Log($"[maplerow-play] {name}: d {_boot.session.DistanceM:0} m, ui centre delta {_lastUiDelta:0.000}");
    }
}
