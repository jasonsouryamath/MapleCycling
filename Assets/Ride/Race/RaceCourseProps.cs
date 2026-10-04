using UnityEngine;

/// <summary>
/// The temporary race furniture on the real road: a START arch, a FINISH arch, and 200 m /
/// 100 m-to-go boards at the kerb. Built when a race is set up and destroyed when it is torn
/// down, so nothing is added to the saved scene. Lit with the project's CelLit shader so the
/// arches sit in each region's light like the rest of the street; no colliders (the ride is on
/// rails, nothing raycasts against them).
/// </summary>
public static class RaceCourseProps
{
    public static GameObject Build(RouteCourse course, float startM, float finishM, float roadHalfWidth,
                                   Color accent, Transform parent)
    {
        var root = new GameObject("~Race Course Props");
        if (parent != null) root.transform.SetParent(parent, false);
        var post = RaceArt.CelLit("~RacePost", null, new Color(0.13f, 0.16f, 0.24f));
        var postAccent = RaceArt.CelLit("~RacePostAccent", null, accent);
        var startMat = BannerMat("~RaceBannerStart", "race_banner_start");
        var finishMat = BannerMat("~RaceBannerFinish", "race_banner_finish");

        Arch(root.transform, course, startM, roadHalfWidth, post, postAccent, startMat, "START Arch");
        Arch(root.transform, course, finishM, roadHalfWidth, post, postAccent, finishMat, "FINISH Arch");
        foreach (int toGo in new[] { 200, 100 })
        {
            float m = finishM - toGo;
            if (m <= startM + 15f) continue;   // the 200 m Sprint starts at its own 200 board
            Board(root.transform, course, m, roadHalfWidth, post, BannerMat("~RaceBoard" + toGo, "race_board_" + toGo),
                  toGo + " m Board");
        }
        return root;
    }

    private static Material BannerMat(string name, string tex)
    {
        var m = RaceArt.CelLit(name, RaceArt.Tex(tex), Color.white);
        if (m.HasProperty("_RimStrength")) m.SetFloat("_RimStrength", 0.15f);
        if (m.HasProperty("_DappleStrength")) m.SetFloat("_DappleStrength", 0f);
        if (m.HasProperty("_ShadeStrength")) m.SetFloat("_ShadeStrength", 0.35f);
        return m;
    }

    private static void Frame(RouteCourse course, float m, out Vector3 p, out Vector3 side, out Vector3 up,
                              out Quaternion rot)
    {
        p = course.PositionAt(m);
        side = course.SideAt(m);
        up = Vector3.up;
        Vector3 fwd = course.TangentAt(m);
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
        // the banner faces riders coming TOWARD it
        rot = Quaternion.LookRotation(-fwd.normalized, up);
    }

    private static void Arch(Transform parent, RouteCourse course, float m, float halfWidth,
                             Material post, Material accent, Material banner, string name)
    {
        Frame(course, m, out var p, out var side, out var up, out var rot);
        var arch = new GameObject(name).transform;
        arch.SetParent(parent, false);
        arch.SetPositionAndRotation(p, rot);
        float span = halfWidth + 0.55f;
        const float H = 4.4f, bannerH = 0.95f;
        foreach (float s in new[] { -1f, 1f })
        {
            var pole = Box(arch, "Post", post, new Vector3(0.28f, H, 0.28f));
            pole.localPosition = new Vector3(s * span, H * 0.5f - 0.3f, 0f);   // sunk into the verge
            var band = Box(arch, "Post Band", accent, new Vector3(0.32f, 0.35f, 0.32f));
            band.localPosition = new Vector3(s * span, 1.4f, 0f);
            var foot = Box(arch, "Foot", post, new Vector3(0.6f, 0.25f, 0.6f));
            foot.localPosition = new Vector3(s * span, 0.02f, 0f);
        }
        var b = Box(arch, "Banner", banner, new Vector3(span * 2f + 0.28f, bannerH, 0.1f));
        b.localPosition = new Vector3(0f, H - 0.3f - bannerH * 0.5f, 0f);
        var top = Box(arch, "Top Rail", accent, new Vector3(span * 2f + 0.5f, 0.12f, 0.16f));
        top.localPosition = new Vector3(0f, H - 0.3f + 0.04f, 0f);
    }

    private static void Board(Transform parent, RouteCourse course, float m, float halfWidth,
                              Material post, Material face, string name)
    {
        Frame(course, m, out var p, out var side, out var up, out var rot);
        var root = new GameObject(name).transform;
        root.SetParent(parent, false);
        // on the LEFT verge (Japan keeps left, and so do the riders), angled a touch toward them
        root.SetPositionAndRotation(p - side * (halfWidth + 0.5f), rot * Quaternion.Euler(0f, -18f, 0f));
        var pole = Box(root, "Pole", post, new Vector3(0.1f, 1.9f, 0.1f));
        pole.localPosition = new Vector3(0f, 0.75f, 0.03f);
        var sign = Box(root, "Face", face, new Vector3(0.95f, 0.95f, 0.05f));
        sign.localPosition = new Vector3(0f, 1.75f, 0f);
    }

    private static Transform Box(Transform parent, string name, Material mat, Vector3 size)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        var col = go.GetComponent<Collider>();
        if (col != null) Object.Destroy(col);
        go.transform.SetParent(parent, false);
        go.transform.localScale = size;
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        return go.transform;
    }
}
