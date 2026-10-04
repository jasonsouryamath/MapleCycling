using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Read-only inventory of the player's body renderer (mesh, submeshes, tangents, materials) and
/// of the textures embedded in Kuro's GLB. Never saves the scene.
///   run_steps.ps1 "KuroDetailProbe.Run|copilot_kuro_probe.log|1"
/// </summary>
public static class KuroDetailProbe
{
    const string ScenePath = "Assets/Scenes/SakuraPass.unity";
    const string GlbPath = "Assets/Kuro/NPC/KuroNPC_KuroAnime_Rigged.glb";

    public static void Run()
    {
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var sb = new StringBuilder("[kuro-probe]\n");
        var player = GameObject.Find("Kuro on Sakura Pass");
        sb.AppendLine("player: " + (player != null ? player.name : "NOT FOUND"));
        if (player != null)
            foreach (var smr in player.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var m = smr.sharedMesh;
                sb.AppendLine($"  SMR '{smr.name}' enabled={smr.enabled} mesh='{(m ? m.name : "null")}' " +
                              $"path={(m ? AssetDatabase.GetAssetPath(m) : "")} verts={(m ? m.vertexCount : 0)} " +
                              $"subs={(m ? m.subMeshCount : 0)} tangents={(m ? m.tangents.Length : 0)} " +
                              $"blend={(m ? m.blendShapeCount : 0)} bones={smr.bones.Length} " +
                              $"bounds={(m ? m.bounds.size.ToString("F3") : "")} quality={smr.quality}");
                foreach (var mat in smr.sharedMaterials)
                {
                    if (mat == null) { sb.AppendLine("    mat null"); continue; }
                    var tex = mat.HasProperty("_MainTex") ? mat.GetTexture("_MainTex") : null;
                    sb.AppendLine($"    mat '{mat.name}' shader={mat.shader.name} path={AssetDatabase.GetAssetPath(mat)} " +
                                  $"main={(tex ? tex.name + " " + AssetDatabase.GetAssetPath(tex) : "none")}");
                }
            }
        foreach (var o in AssetDatabase.LoadAllAssetsAtPath(GlbPath))
            if (o is Texture2D t) sb.AppendLine($"  glb tex '{t.name}' {t.width}x{t.height} fmt={t.format} linear={!t.isDataSRGB}");
            else if (o is Mesh gm) sb.AppendLine($"  glb mesh '{gm.name}' verts={gm.vertexCount} subs={gm.subMeshCount} tangents={gm.tangents.Length}");
        var split = Resources.Load<Mesh>("Shop/KuroKitSplit");
        if (split) sb.AppendLine($"  KuroKitSplit verts={split.vertexCount} subs={split.subMeshCount} tris={split.triangles.Length / 3}");
        Debug.Log(sb.ToString());
    }

    /// <summary>
    /// Before/after close-ups of the posed player: BEFORE = the scene's GLB body with the normal
    /// map forced off; AFTER = the refined KuroKitSplit swapped in exactly the way KitAppearance
    /// does at Start (body material on every slot) with the material as authored. Never saves.
    ///   run_steps.ps1 "KuroDetailProbe.Capture|copilot_kuro_cap.log|1"
    /// </summary>
    public static void Capture()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var player = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "Kuro on Sakura Pass");
        var rig = player != null ? player.GetComponent<KuroBikeRig>() : null;
        if (rig == null) { Debug.LogError("[kuro-detail] player/rig not found"); return; }
        var pose = player.GetComponent<KuroRidePose>();
        if (pose != null)
        {
            pose.SelectPosture(KuroRidePose.CyclingPosture.RoadRacerAggressive, false);
            pose.enableClimbOverlay = false; pose.enableSprintOverlay = false; pose.SprintOverride = 0f;
            pose.Tick(1f);
        }
        rig.ForceSolveOnce();

        var body = player.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                         .OrderByDescending(s => s.sharedMesh ? s.sharedMesh.vertexCount : 0).First();
        var origMesh = body.sharedMesh;
        var origMats = body.sharedMaterials;
        var split = Resources.Load<Mesh>("Shop/KuroKitSplit");
        Debug.Log($"[kuro-detail] body '{body.name}' {origMesh.vertexCount} verts {Tris(origMesh)} tris; " +
                  $"KuroKitSplit {(split ? split.vertexCount + " verts " + Tris(split) + " tris" : "MISSING")}");

        var dir = System.IO.Path.Combine(MapleRidePaths.Renders, "kuro_detail");
        System.IO.Directory.CreateDirectory(dir);
        var camObj = new GameObject("KuroDetailCam");
        var cam = camObj.AddComponent<Camera>();
        cam.fieldOfView = 30f; cam.nearClipPlane = 0.02f; cam.clearFlags = CameraClearFlags.Skybox;

        var shots = new (string name, float az, float dist, float up, float lookUp)[]
        {
            ("front34", 40f, 1.9f, 0.25f, 0.62f), ("side", 90f, 1.9f, 0.2f, 0.6f),
            ("rear34", 140f, 1.9f, 0.3f, 0.62f), ("chase", 180f, 2.6f, 0.55f, 0.7f),
            ("torso_close", 60f, 0.95f, 0.15f, 0.78f), ("head_close", 30f, 0.8f, 0.1f, 1.02f),
        };

        var before = origMats.Select(m => { var c = new Material(m); if (c.HasProperty("_NormalStrength")) c.SetFloat("_NormalStrength", 0f); return c; }).ToArray();
        body.sharedMaterials = before;
        Shoot(cam, player, shots, dir, "before_");

        if (split != null)
        {
            // A FRESH renderer: re-assigning sharedMesh on an SMR that was already CPU-skinned in this
            // editor frame draws nothing (2026-09-26) - an editor-capture artefact, not a game bug.
            var bodyMat = origMats[0];
            var all = new Material[split.subMeshCount];
            for (int i = 0; i < all.Length; i++) all[i] = bodyMat;
            var afterGo = Object.Instantiate(body.gameObject, body.transform.parent);
            afterGo.transform.SetLocalPositionAndRotation(body.transform.localPosition, body.transform.localRotation);
            afterGo.transform.localScale = body.transform.localScale;
            var after = afterGo.GetComponent<SkinnedMeshRenderer>();
            after.bones = body.bones; after.rootBone = body.rootBone;
            after.sharedMesh = split;
            after.sharedMaterials = all;
            body.enabled = false;
            Shoot(cam, player, shots, dir, "after_");
            Object.DestroyImmediate(afterGo);
            body.enabled = true;
        }
        body.sharedMesh = origMesh;
        body.sharedMaterials = origMats;
        foreach (var m in before) Object.DestroyImmediate(m);
        Object.DestroyImmediate(camObj);
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);   // drop every temp change
        Debug.Log("[kuro-detail] wrote renders to " + System.IO.Path.GetFullPath(dir));
    }

    static int Tris(Mesh m) { int t = 0; for (int s = 0; s < m.subMeshCount; s++) t += (int)m.GetIndexCount(s) / 3; return t; }

    static void Shoot(Camera cam, GameObject player, (string name, float az, float dist, float up, float lookUp)[] shots,
                      string dir, string prefix)
    {
        const int W = 1400, H = 1050;
        foreach (var s in shots)
        {
            var pivot = player.transform.position + Vector3.up * s.lookUp;
            var off = Quaternion.AngleAxis(s.az, Vector3.up) * player.transform.forward;
            cam.transform.position = pivot + off * s.dist + Vector3.up * s.up;
            cam.transform.LookAt(pivot);
            var rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32) { antiAliasing = 8 };
            cam.targetTexture = rt;
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var img = new Texture2D(W, H, TextureFormat.RGB24, false);
            img.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            img.Apply();
            System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, prefix + s.name + ".png"), img.EncodeToPNG());
            Object.DestroyImmediate(img);
            RenderTexture.active = prev;
            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
        }
    }

    /// <summary>Bake chain: refined kit split + player normal-map recipe. Then run Capture.</summary>
    public static void BakeAll()
    {
        KuroKitSplitBake.Bake();
        KuroPlayerBodyCelLit.ApplyDetailToPlayerMaterial();
    }
}
