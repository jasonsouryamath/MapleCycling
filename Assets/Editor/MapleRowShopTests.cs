using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

/// <summary>
/// Headless checks for the Maple Row shop (batch: -executeMethod MapleRowShopTests.Run -quit).
/// Catalogue sanity, wallet/equip rules (the real PlayerPrefs save is backed up and restored),
/// the kit shader and mask, and a render of the shop screen to
/// reference/good_graphics/maple_row/shop_ui.png.
/// </summary>
public static class MapleRowShopTests
{
    private static int _pass, _fail;
    private const BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Instance;

    private static void Check(bool ok, string what)
    {
        if (ok) _pass++; else _fail++;
        Debug.Log((ok ? "[shop-test] PASS " : "[shop-test] FAIL ") + what);
    }

    public static void Run()
    {
        _pass = _fail = 0;
        Catalogue();
        Wardrobe();
        ShopAssets();
        ProAssets();
        RenderShop();
        Debug.Log($"[shop-test] RESULT {_pass} passed, {_fail} failed");
        if (Application.isBatchMode) UnityEditor.EditorApplication.Exit(_fail == 0 ? 0 : 1);
    }

    private static void Catalogue()
    {
        var ids = new HashSet<string>();
        bool unique = true, brandsOk = true, pricesOk = true;
        foreach (var it in ShopCatalog.Items)
        {
            unique &= ids.Add(it.Id);
            brandsOk &= System.Array.IndexOf(ShopCatalog.Brands, it.Brand) >= 0;
            pricesOk &= it.Price > 0;
        }
        Check(unique, $"{ShopCatalog.Items.Count} items, unique ids");
        Check(brandsOk, "every item belongs to a listed brand");
        Check(pricesOk, "every item has a price");
        foreach (var b in ShopCatalog.Brands)
            Check(ShopCatalog.Items.Exists(i => i.Brand == b) && ShopCatalog.BrandTagline.ContainsKey(b),
                  $"brand {b} has items and a tagline");
        Check(ShopCatalog.Items.TrueForAll(i => i.Category != ShopCategory.Wheels || (i.CdaScale < 1f && i.CdaScale > 0.9f)),
              "wheels reduce drag by less than 10%");
        Check(ShopCatalog.Items.TrueForAll(i => i.Category != ShopCategory.BikeComputer || i.ComputerTier >= 1),
              "computers carry a HUD tier");
    }

    private static void Wardrobe()
    {
        const string key = "MapleRide.Wardrobe.v1";
        string backup = PlayerPrefs.HasKey(key) ? PlayerPrefs.GetString(key) : null;
        var field = typeof(PlayerWardrobe).GetField("_s", BindingFlags.NonPublic | BindingFlags.Static);
        System.Action onChange = null;
        try
        {
            PlayerPrefs.DeleteKey(key);
            field.SetValue(null, null);
            int changes = 0;
            onChange = () => changes++;
            PlayerWardrobe.Changed += onChange;

            var a = ShopCatalog.Get("ardent_jersey_classic");
            var b = ShopCatalog.Get("halcyon_jersey_tide");
            var w = ShopCatalog.Get("carbonforge_65");
            var f = ShopCatalog.Get("frame_monarch_r1");
            int start = PlayerWardrobe.Coins;
            Check(start == 5000, "a new wallet starts at 5000 MC");
            Check(PlayerWardrobe.Buy(a) && PlayerWardrobe.Owns(a.Id), "buy a jersey");
            Check(PlayerWardrobe.Coins == start - PlayerWardrobe.PriceToPay(a), "price deducted (0 while QaFreePurchases)");
            Check(!PlayerWardrobe.Buy(a), "cannot buy the same item twice");
            PlayerWardrobe.Buy(b);
            PlayerWardrobe.ToggleEquip(a);
            PlayerWardrobe.ToggleEquip(b);
            Check(PlayerWardrobe.Equipped(ShopCategory.Jersey) == b && !PlayerWardrobe.IsEquipped(a.Id),
                  "equipping a jersey replaces the other jersey");
            PlayerWardrobe.Buy(w);
            PlayerWardrobe.ToggleEquip(w);
            Check(PlayerWardrobe.Equipped(ShopCategory.Wheels) == w && PlayerWardrobe.Equipped(ShopCategory.Jersey) == b,
                  "wheels and a jersey coexist");
            PlayerWardrobe.Buy(f);
            PlayerWardrobe.ToggleEquip(f);
            Check(PlayerWardrobe.Equipped(ShopCategory.Frame) == f,
                  "a purchased frameset equips in its own slot");
            PlayerWardrobe.ToggleEquip(b);
            Check(PlayerWardrobe.Equipped(ShopCategory.Jersey) == null, "equipping an equipped item takes it off");
            PlayerWardrobe.ToggleEquip(ShopCatalog.Get("aerolite_helmet_aero"));
            Check(PlayerWardrobe.Equipped(ShopCategory.Helmet) == null, "cannot equip an item you don't own");
            int before = PlayerWardrobe.Coins;
            PlayerWardrobe.AddCoins(25);
            Check(PlayerWardrobe.Coins == before + 25, "riding adds coins");
            field.SetValue(null, null);   // reload from PlayerPrefs
            Check(PlayerWardrobe.Owns(w.Id) && PlayerWardrobe.IsEquipped(w.Id) &&
                  PlayerWardrobe.Owns(f.Id) && PlayerWardrobe.Equipped(ShopCategory.Frame)?.Id == f.Id &&
                  PlayerWardrobe.Coins == before + 25,
                  "the wardrobe and equipped frameset persist after a full reload");
            Check(changes >= 6, $"Changed fired ({changes})");

            var phys = new CyclingPhysics();
            Check(Mathf.Approximately(phys.equipmentCdaScale, 1f), "physics defaults to no equipment drag change");
        }
        finally
        {
            if (onChange != null) PlayerWardrobe.Changed -= onChange;
            if (backup != null) PlayerPrefs.SetString(key, backup); else PlayerPrefs.DeleteKey(key);
            PlayerPrefs.Save();
            field.SetValue(null, null);
        }
    }

    /// <summary>2026-09-26: framesets and photoreal kits resolve to real assets of the right shape.</summary>
    private static void ProAssets()
    {
        var frames = ShopCatalog.Items.FindAll(i => i.Category == ShopCategory.Frame);
        Check(frames.Count >= 10, $"{frames.Count} premium framesets in the catalogue");
        Check(new HashSet<string>(frames.ConvertAll(f => f.Asset)).Count == frames.Count,
              "every frameset has a unique authored prefab");
        Check(frames.TrueForAll(f => f.Price >= 8000), "every frameset is premium-priced");
        Check(MapleRowShop.StoreName.TryGetValue("CARBONFORGE", out var frameStore) && frameStore.Contains("FRAME"),
              "Maple Row visibly names CARBONFORGE as the frameset shop");
        foreach (var f in frames)
        {
            var prefab = Resources.Load<GameObject>(f.Asset);
            bool shape = prefab != null && GearVisuals.FindDeep(prefab.transform, "Frame_Main") != null &&
                         GearVisuals.FindDeep(prefab.transform, "SteerRef") != null &&
                         GearVisuals.FindDeep(prefab.transform, "Frame_Fork") != null;
            int tris = 0;
            bool hdrp = prefab != null;
            if (prefab != null)
                foreach (var r in prefab.GetComponentsInChildren<Renderer>(true))
                {
                    var mf = r.GetComponent<MeshFilter>();
                    if (mf != null && mf.sharedMesh != null) tris += mf.sharedMesh.triangles.Length / 3;
                    foreach (var m in r.sharedMaterials)
                        hdrp &= m != null && m.shader != null && m.shader.name == "HDRP/Lit" && m.GetTexture("_BaseColorMap") != null &&
                                m.GetTexture("_MaskMap") != null && m.GetFloat("_CoatMask") > 0.5f;
                }
            Check(shape && hdrp && tris > 5000 && tris < 150000 && f.CdaScale > 0.95f && f.CdaScale <= 1f,
                  $"{f.Id}: prefab {(prefab != null ? "ok" : "MISSING")}, split {(shape ? "ok" : "BAD")}, clear-coat paint {(hdrp ? "ok" : "BAD")}, {tris} tris, drag x{f.CdaScale}");
        }
        foreach (var k in ShopCatalog.Items.FindAll(i => (i.Category == ShopCategory.Jersey || i.Category == ShopCategory.Bibs) && i.Textured))
        {
            var t = Resources.Load<Texture2D>(k.Asset);
            var ti = t != null ? (UnityEditor.TextureImporter)UnityEditor.AssetImporter.GetAtPath(UnityEditor.AssetDatabase.GetAssetPath(t)) : null;
            Check(t != null && t.width == 2048 && ti != null && ti.sRGBTexture, $"{k.Id}: design atlas {(t != null ? t.width + " px" : "MISSING")}");
        }
    }

    private static void ShopAssets()
    {
        Check(Shader.Find("Hidden/MapleRide/KitComposite") != null, "KitComposite shader compiles and is findable");
        var mask = Resources.Load<Texture2D>("Shop/KuroKitMask");
        Check(mask != null, "KuroKitMask loads from Resources");
        if (mask == null) return;
        var ti = (UnityEditor.TextureImporter)UnityEditor.AssetImporter.GetAtPath(UnityEditor.AssetDatabase.GetAssetPath(mask));
        Check(ti != null && !ti.sRGBTexture && ti.textureCompression == UnityEditor.TextureImporterCompression.Uncompressed,
              "mask imported linear and uncompressed");
        if (!mask.isReadable) { Check(false, "mask readable"); return; }
        // v3: R = zone (64 jersey, 128 collar, 192 bibs), G = shading (never 0),
        //     B = trim (255 jersey band, 128 bibs gripper), A = helmet ridge
        int jerseyZone = 0, bibsZone = 0, helmetTrim = 0, jerseyTrim = 0, bibsTrim = 0, noShade = 0;
        var px = mask.GetPixels32();
        for (int i = 0; i < px.Length; i += 7)
        {
            if (px[i].r > 32 && px[i].r < 160) jerseyZone++;
            if (px[i].r >= 160) bibsZone++;
            if (px[i].a > 127) helmetTrim++;
            if (px[i].b > 192) jerseyTrim++;
            else if (px[i].b > 64) bibsTrim++;
            if (px[i].g < 32) noShade++;
        }
        Debug.Log($"[shop-test] mask v3 samples: jersey zone {jerseyZone}, bibs zone {bibsZone}, trims jersey {jerseyTrim} bibs {bibsTrim} helmet {helmetTrim}, unshaded {noShade}");
        Check(jerseyZone > 0 && bibsZone > 0 && jerseyTrim > 0 && bibsTrim > 0 && helmetTrim > 0 && noShade == 0,
              "mask v3 has jersey/bibs zones, shading and all three trims");

        // fitment: the per-triangle garment split of Kuro's body mesh
        var split = Resources.Load<Mesh>("Shop/KuroKitSplit");
        Check(split != null, "KuroKitSplit mesh loads (run KuroKitSplitBake.Bake)");
        if (split == null) return;
        int n = split.subMeshCount;
        long jer = n >= 3 ? split.GetIndexCount(n - 3) / 3 : 0, bib = n >= 3 ? split.GetIndexCount(n - 2) / 3 : 0,
             hel = n >= 3 ? split.GetIndexCount(n - 1) / 3 : 0;
        Debug.Log($"[shop-test] split mesh: {n} submeshes, jersey {jer}, bibs {bib}, helmet {hel} tris");
        Check(jer > 1000 && bib > 1000 && hel > 1000, "split mesh has jersey, bibs and helmet submeshes");
    }

    private static void RenderShop()
    {
        var host = new GameObject("~ShopTest");
        var camGo = new GameObject("~ShopTestCam");
        var rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
        try
        {
            var shop = host.AddComponent<MapleRowShop>();
            typeof(MapleRowShop).GetMethod("Build", Priv).Invoke(shop, null);
            shop.SetOpen(true);
            Check(RideInputGate.Locked, "opening the shop pauses the ride");
            shop.SetOpen(false);
            Check(!RideInputGate.Locked, "closing the shop releases the ride");
            shop.OpenStore("CARBONFORGE");
            Check(shop.CurrentBrand == "CARBONFORGE", "OpenStore opens that store");

            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.32f, 0.36f, 0.42f, 1f);
            cam.cullingMask = 1 << HudSprites.UiLayer;
            cam.targetTexture = rt;
            var canvas = host.GetComponentInChildren<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = 1f;
            Canvas.ForceUpdateCanvases();
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            string dir = Path.Combine(Application.dataPath, "../reference/good_graphics/maple_row");
            Directory.CreateDirectory(dir);
            string path = Path.GetFullPath(Path.Combine(dir, "shop_ui.png"));
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Debug.Log("[shop-test] shop screen -> " + path);
            Check(true, "shop screen rendered");
            shop.SetOpen(false);
        }
        catch (System.Exception e) { Check(false, "shop screen render: " + e); }
        finally
        {
            Object.DestroyImmediate(host);
            Object.DestroyImmediate(camGo);
            rt.Release();
            RideInputGate.Unlock();
        }
    }
}
