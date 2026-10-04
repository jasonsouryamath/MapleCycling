using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>Bakes private mesh/material resources and six animated NPC prefabs, without restaging worlds.</summary>
public static class RefinedNpcBuild
{
    const string Folder = "Assets/Resources/RefinedNpcCast";
    static readonly string[] Names = { "Akihiro", "Akane", "Shiori", "Shinobu", "Coral", "Hanakage" };
    static void Require(bool ok, string message)
    { if (!ok) throw new InvalidOperationException("[refined-npcs] " + message); Debug.Log("[refined-npcs] PASS " + message); }

    [MenuItem("MapleRide/NPCs/Build Refined ImageGen Cast")]
    public static void Build()
    {
        Directory.CreateDirectory(Folder + "/Materials"); Directory.CreateDirectory(Folder + "/Meshes");
        Directory.CreateDirectory(Folder + "/Textures"); Directory.CreateDirectory(Folder + "/Riders"); AssetDatabase.Refresh();
        var rows = new List<RefinedNpcCast.Rider>(); var prefabs = new List<GameObject>();
        var bikeAsset = AssetDatabase.LoadAssetAtPath<GameObject>(NpcCanonicalConformance.BikeAssetPath);
        Require(bikeAsset != null, "canonical cycling bicycle available");
        foreach (string name in Names)
        {
            string path = "Assets/Kuro/NPC/ImageGenRefined/" + name + "_Refined.glb";
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Require(model != null, "imported refined " + name);
            var parking = new GameObject("~Refined NPC bake"); parking.SetActive(false);
            try
            {
                var rider = new GameObject("Refined_" + name); rider.transform.SetParent(parking.transform, false);
                var body = UnityEngine.Object.Instantiate(model, rider.transform); body.name = name + "ArmatureAndMesh";
                if (name == "Hanakage")
                    foreach (var renderer in body.GetComponentsInChildren<Renderer>(true))
                        if (renderer.name.StartsWith("HanakageHelm", StringComparison.Ordinal)) UnityEngine.Object.DestroyImmediate(renderer);
                string conversionDir = Folder + "/Materials/" + name + "_Import";
                Directory.CreateDirectory(conversionDir); AssetDatabase.Refresh();
                NpcCelLitConversion.ConvertRiderBody(body, conversionDir);
                PersistMaterials(body, name);
                var skins = body.GetComponentsInChildren<SkinnedMeshRenderer>(true).OrderByDescending(s => s.sharedMesh.vertexCount).ToArray();
                Require(skins.Length >= 2, name + " body and head-skinned helmet");
                var primary = skins[0];
                var row = new RefinedNpcCast.Rider { name = name, body = Part(primary, name + "_Body"),
                    extras = skins.Skip(1).Where(s => s.name.Contains("CleanVentedHelmet")).Select((s, i) => Part(s, name + "_Helmet" + i)).ToArray() };
                Require(row.extras.Length == 1, name + " exactly one vented helmet");
                rows.Add(row);
                // The same persistent private data feeds both scene replacements and traffic prefabs.
                primary.sharedMesh = row.body.mesh; primary.sharedMaterials = row.body.materials;
                foreach (var skin in skins.Skip(1).Where(s => s.name.Contains("CleanVentedHelmet")))
                { skin.sharedMesh = row.extras[0].mesh; skin.sharedMaterials = row.extras[0].materials; }
                var bike = new GameObject("Bike"); bike.transform.SetParent(rider.transform, false);
                var bikeMesh = UnityEngine.Object.Instantiate(bikeAsset, bike.transform); bikeMesh.name = "BikeMesh";
                string bikeDir = Folder + "/Materials/" + name + "_BikeImport";
                Directory.CreateDirectory(bikeDir); AssetDatabase.Refresh();
                NpcCelLitConversion.ConvertRiderBody(bikeMesh, bikeDir);
                PersistMaterials(bikeMesh, name + "_Bike");
                var rig = rider.AddComponent<CoralBikeRig>(); rig.bikePrefab = bikeAsset;
                rig.useRideCadenceForCrank = false; rig.previewCadenceRpm = 65;
                NpcCanonicalConformance.Configure(rig);
                NpcCanonicalConformance.FinalizeStagedPose(rig);
                // Configure makes reach sleeves after material conversion. Its name-based
                // bike material lookup cannot identify renamed private copies, so explicitly
                // reuse the persistent dark helmet-liner material rather than the Built-in fallback.
                foreach (var renderer in rider.GetComponentsInChildren<Renderer>(true))
                    if (renderer.name == "ForwardDrop_L" || renderer.name == "ForwardDrop_R")
                        renderer.sharedMaterial = row.extras[0].materials.Last();
                foreach (var collider in rider.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
                rider.AddComponent<RefinedNpcApplied>().identity = name;
                rider.SetActive(true);
                var prefab = PrefabUtility.SaveAsPrefabAsset(rider, Folder + "/Riders/" + name + ".prefab");
                Require(prefab != null, "saved animated " + name); prefabs.Add(prefab);
            }
            finally { UnityEngine.Object.DestroyImmediate(parking); }
        }
        string castPath = Folder + "/Cast.asset";
        var cast = AssetDatabase.LoadAssetAtPath<RefinedNpcCast>(castPath);
        if (cast == null) { cast = ScriptableObject.CreateInstance<RefinedNpcCast>(); AssetDatabase.CreateAsset(cast, castPath); }
        cast.riders = rows.ToArray(); EditorUtility.SetDirty(cast);
        var library = AssetDatabase.LoadAssetAtPath<ShuntaNpcLibrary>("Assets/Resources/ShuntaMetro/Actors/NpcLibrary.asset");
        Require(library != null, "existing Shunta authored cast available");
        var other = library.riders.Where(p => p != null && p.GetComponent<RefinedNpcApplied>() == null).ToArray();
        var oldOrigins = library.origins ?? Array.Empty<string>();
        var origins = new List<string>(Names.Select(n => "Refined: " + n));
        foreach (var p in other)
        {
            int ix = Array.IndexOf(library.riders, p); origins.Add(ix < oldOrigins.Length ? oldOrigins[ix] : p.name);
        }
        library.riders = prefabs.Concat(other).ToArray(); library.origins = origins.ToArray(); EditorUtility.SetDirty(library);
        AssetDatabase.SaveAssets(); Validate();
    }
    static RefinedNpcCast.Part Part(SkinnedMeshRenderer skin, string name)
    {
        var copy = UnityEngine.Object.Instantiate(skin.sharedMesh); copy.name = name;
        string path = Folder + "/Meshes/" + name + ".asset";
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (mesh == null) { mesh = copy; AssetDatabase.CreateAsset(mesh, path); }
        else { EditorUtility.CopySerialized(copy, mesh); UnityEngine.Object.DestroyImmediate(copy); }
        return new RefinedNpcCast.Part { mesh = mesh, materials = skin.sharedMaterials,
            bones = skin.bones.Select(b => b.name).ToArray(), rootBone = skin.rootBone != null ? skin.rootBone.name : null };
    }
    static void PersistMaterials(GameObject root, string name)
    {
        int ri = 0;
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            var slots = renderer.sharedMaterials;
            for (int i=0; i<slots.Length; i++)
            {
                if (slots[i] == null) continue;
                var copy = new Material(slots[i]) { name = name + "_" + ri + "_" + i };
                foreach (string prop in copy.GetTexturePropertyNames())
                {
                    var texture = copy.GetTexture(prop); if (texture == null || AssetDatabase.Contains(texture)) continue;
                    string tp = Folder + "/Textures/" + name + "_" + ri + "_" + i + "_" + prop + ".asset";
                    var saved = AssetDatabase.LoadAssetAtPath<Texture>(tp);
                    if (saved == null) { saved = UnityEngine.Object.Instantiate(texture); saved.hideFlags = HideFlags.None; AssetDatabase.CreateAsset(saved, tp); }
                    else EditorUtility.CopySerialized(texture, saved);
                    copy.SetTexture(prop, saved);
                }
                foreach (string prop in new[] { "_DetailAmount", "_TintVariation", "_WearAmount", "_MossAmount", "_GrimeAmount", "_DappleStrength" })
                    if (copy.HasProperty(prop)) copy.SetFloat(prop, 0);
                if (copy.HasProperty("_SpecStrength")) copy.SetFloat("_SpecStrength", .08f);
                if (copy.HasProperty("_CharacterLight")) copy.SetFloat("_CharacterLight", 1);
                if (copy.HasProperty("_CharKeyIntensity")) copy.SetFloat("_CharKeyIntensity", 1);
                if (copy.HasProperty("_CharFillIntensity")) copy.SetFloat("_CharFillIntensity", .5f);
                if (copy.HasProperty("_CharAmbient")) copy.SetFloat("_CharAmbient", .35f);
                string mp = Folder + "/Materials/" + copy.name + ".mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(mp);
                if (material == null) { material = copy; AssetDatabase.CreateAsset(material, mp); }
                else { EditorUtility.CopySerialized(copy, material); UnityEngine.Object.DestroyImmediate(copy); }
                slots[i] = material;
            }
            renderer.sharedMaterials = slots; ri++;
        }
    }
    public static void Validate()
    {
        var cast = AssetDatabase.LoadAssetAtPath<RefinedNpcCast>(Folder + "/Cast.asset");
        Require(cast != null && cast.riders.Length == 6, "six refined identities in runtime resource");
        foreach (var row in cast.riders)
        {
            Require(row.body.bones.Length == 24 && row.body.mesh.bindposes.Length == 24, row.name + " original 24-joint rig");
            foreach (var part in new[] { row.body }.Concat(row.extras))
            {
                Require(part.mesh.vertexCount > 0 && part.mesh.bounds.size.sqrMagnitude > .001f, row.name + " nonempty mesh/bounds");
                Require(part.materials.All(m => m != null && m.shader != null && !ShaderUtil.ShaderHasError(m.shader)), row.name + " valid materials");
            }
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/Riders/" + row.name + ".prefab");
            Require(prefab != null && prefab.GetComponent<CoralBikeRig>() != null && prefab.transform.Find("Bike") != null, row.name + " cycling prefab");
            Require(prefab.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials)
                .All(m => m != null && m.shader != null && m.shader.name != "Standard" && !ShaderUtil.ShaderHasError(m.shader)), row.name + " no Built-in fallback materials including grips");
            var posed = UnityEngine.Object.Instantiate(prefab);
            try
            {
                var rig = posed.GetComponent<CoralBikeRig>(); rig.ForceSolveOnce();
                foreach (string socket in new[] { "BB", "Axle_F", "Axle_R", "Pedal_L", "Pedal_R", "Hood_L", "Hood_R", "SaddleTop" })
                    Require(posed.GetComponentsInChildren<Transform>(true).Any(t => t.name == socket), row.name + " socket " + socket);
                var skin = posed.GetComponentsInChildren<SkinnedMeshRenderer>(true).OrderByDescending(s => s.sharedMesh.vertexCount).First();
                using (var a = new MeshScope()) using (var b = new MeshScope())
                {
                    skin.BakeMesh(a.mesh); rig.AdvanceCrank(90); skin.BakeMesh(b.mesh);
                    Require(a.mesh.vertices.Zip(b.mesh.vertices, (x,y) => (x-y).sqrMagnitude).Any(d => d > .00001f), row.name + " pedal phase deforms skin");
                    Require(b.mesh.vertices.All(v => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z)), row.name + " finite pedalling geometry");
                    Require(b.mesh.bounds.size.magnitude < 4 && b.mesh.bounds.size.magnitude > .3f, row.name + " bounded pedalling pose");
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(posed); }
            // Exercise actual bone remapping onto an original donor, plus duplicate installation guard.
            var original = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Kuro/NPC/KuroNPC_" + row.name + "_Rigged.glb");
            var instance = UnityEngine.Object.Instantiate(original); instance.SetActive(false);
            try
            {
                Require(RefinedNpcCast.Apply(instance, row), row.name + " all refined parts remap onto donor skeleton");
                Require(!RefinedNpcCast.Apply(instance, row), row.name + " repeated scan creates no duplicate helmet");
                var marker = instance.GetComponent<RefinedNpcApplied>();
                marker.primary.enabled = false; marker.SyncVisibility();
                Require(marker.extras.All(s => !s.enabled), row.name + " helmet follows body LOD culling");
                marker.primary.enabled = true; marker.SyncVisibility();
                foreach (var skin in instance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    using (var baked = new MeshScope()) { skin.BakeMesh(baked.mesh); Require(baked.mesh.vertexCount > 0, row.name + " skin bake"); }
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(instance); }
        }
        var library = AssetDatabase.LoadAssetAtPath<ShuntaNpcLibrary>("Assets/Resources/ShuntaMetro/Actors/NpcLibrary.asset");
        Require(library != null && library.riders.Take(6).Select(p => p.GetComponent<RefinedNpcApplied>()?.identity).SequenceEqual(Names), "all six in visible Shunta traffic indexes");
        Debug.Log("[refined-npcs] ALL PASS");
    }
    sealed class MeshScope : IDisposable
    { public readonly Mesh mesh = new Mesh(); public void Dispose() { UnityEngine.Object.DestroyImmediate(mesh); } }
}
