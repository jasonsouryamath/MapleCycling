using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Maple Row framesets on Kuro's bike (2026-09-26). A bought frame REPLACES the stock frame and
/// fork of the player's bike (and of the shop mannequin, which is a clone of the player):
///  * the stock tube renderers of kuro_bike_mapleride_aero.glb are hidden with
///    <see cref="Renderer.forceRenderingOff"/> (so scripts that toggle <c>enabled</c> are never
///    fought) and restored when the frame comes off;
///  * the frameset prefab (Resources/Shop/Frames/FrameKit_*, built by ShopFrameKitBuilder from
///    design_assets/3d/kuro/build_shop_frames.py) is split: <c>Frame_Main</c> goes under the bike's
///    GLB root (the parent of the BB socket) and <c>Frame_Fork</c> under <c>SteerPivot</c>, so the
///    fork steers with the bars. Every frame is authored on the stock bike's hard points, so the
///    stock wheels, drivetrain, cockpit, saddle and every KuroBikeRig socket stay exactly where
///    they were.
/// Idempotent: re-applying swaps the frame; null restores the stock bike.
/// </summary>
public static class FrameVisuals
{
    public const string FrameName = "~GearFrame", ForkName = "~GearFork";

    /// <summary>The stock frame and fork parts (object names from build_kuro_mapleride_aero_bike.py).</summary>
    public static readonly HashSet<string> StockFrameParts = new HashSet<string>
    {
        "AeroDownTube", "AeroSeatTube", "SlopedAeroTopTube", "TaperedHeadTube",
        "AeroChainstay_L", "AeroChainstay_R", "DroppedSeatstay_L", "DroppedSeatstay_R",
        "MapleAccent_DownTube", "AeroHighlight_TopTube", "AeroForkBlade_L", "AeroForkBlade_R",
        "BottomBracketShell",
    };

    public static void Apply(Transform root, ShopItem frame)
    {
        if (root == null) return;
        var bb = GearVisuals.FindDeep(root, "BB");
        var steer = GearVisuals.FindDeep(root, "SteerPivot");
        if (bb == null || steer == null || bb.parent == null) return;
        var bike = bb.parent;
        Remove(bike.Find(FrameName));
        Remove(steer.Find(ForkName));

        GameObject prefab = null;
        if (frame != null && frame.Textured)
        {
            prefab = Resources.Load<GameObject>(frame.Asset);
            if (prefab == null) Debug.LogWarning($"[frame] {frame.Id}: no prefab at Resources/{frame.Asset} (run ShopFrameKitBuilder.Build)");
        }
        SetStockVisible(root, prefab == null);
        if (prefab == null) return;

        var inst = Object.Instantiate(prefab);
        var main = GearVisuals.FindDeep(inst.transform, "Frame_Main");
        var steerRef = GearVisuals.FindDeep(inst.transform, "SteerRef");
        var fork = GearVisuals.FindDeep(inst.transform, "Frame_Fork");
        if (main == null || steerRef == null || fork == null)
        {
            Debug.LogWarning($"[frame] {frame.Id}: prefab lacks Frame_Main / SteerRef / Frame_Fork");
            Kill(inst);
            SetStockVisible(root, true);
            return;
        }
        // Pose relative to the kit root / SteerRef BEFORE re-parenting: those frames correspond
        // exactly to the stock bike's GLB root and SteerPivot at rest.
        var mainRel = main.parent.worldToLocalMatrix * main.localToWorldMatrix;
        var forkRel = steerRef.worldToLocalMatrix * fork.localToWorldMatrix;
        Attach(main, bike, mainRel, FrameName, bb.gameObject.layer);
        Attach(fork, steer, forkRel, ForkName, bb.gameObject.layer);
        Kill(inst);
        Debug.Log($"[frame] '{root.name}': {frame.Id} fitted (frame under '{bike.name}', fork under '{steer.name}')");
    }

    /// <summary>True when a bought frame is currently fitted under this rig.</summary>
    public static bool HasFrame(Transform root)
    {
        var bb = GearVisuals.FindDeep(root, "BB");
        return bb != null && bb.parent != null && bb.parent.Find(FrameName) != null;
    }

    /// <summary>Number of stock frame renderers still drawn (0 when a bought frame is fitted).</summary>
    public static int VisibleStockParts(Transform root)
    {
        int n = 0;
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            if (StockFrameParts.Contains(r.name) && !r.forceRenderingOff) n++;
        return n;
    }

    private static void SetStockVisible(Transform root, bool visible)
    {
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            if (StockFrameParts.Contains(r.name)) r.forceRenderingOff = !visible;
    }

    private static void Attach(Transform part, Transform parent, Matrix4x4 rel, string name, int layer)
    {
        part.SetParent(parent, false);
        part.localPosition = rel.GetColumn(3);
        part.localRotation = rel.rotation;
        part.localScale = rel.lossyScale;
        part.name = name;
        foreach (var t in part.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = layer;
    }

    private static void Kill(GameObject g)
    {
        if (Application.isPlaying) Object.Destroy(g); else Object.DestroyImmediate(g);
    }

    private static void Remove(Transform t)
    {
        if (t == null) return;
        t.SetParent(null, false);           // so a same-frame re-apply never finds the old one
        if (Application.isPlaying) Object.Destroy(t.gameObject); else Object.DestroyImmediate(t.gameObject);
    }
}
