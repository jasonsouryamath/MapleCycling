using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Wires Taka Mountains' legendary rider - Hyoga, "The North Wall" - onto the rider that
/// <see cref="TakaNpcRoster"/> already stages.
///
/// This pass deliberately does NOT create the rider. He is roster entry 30, built by exactly the
/// same code path as the other twenty-nine, which means he inherits the whole verified rig: the
/// Coral sculpt, the 0.9-scale Colnago, the baked hand grip, the cloned kit material, the
/// greeting component and the portrait the bake pipeline writes for him. A separately-constructed
/// legendary would drift from that path the first time any rig constant changed.
///
/// What this pass adds is the LEGENDARY behaviour on top: <see cref="LegendaryRiderPresence"/>,
/// which records the first sighting in the Rider Journal and makes him lift away up the road.
///
/// SCOPE, STATED PLAINLY. The design doc's section 4 specifies a four-phase Hyoga duel. That is
/// NOT delivered here. Sakura's Hanakage duel is an eight-file stack (encounter, config, HUD,
/// camera director, audio director, roll ledger, performance sampler, playmode runner) and
/// porting it generically is its own milestone. Delivered: the rider, his spawn, his identity,
/// his escape, and his journal discovery. Deferred: the phased challenge, its HUD and its
/// rewards. <see cref="LegendaryRiderPresence.escapeSpeedMultiplier"/> documents the hook a
/// later duel port attaches to.
///
/// Idempotent: re-running converges, because it matches the rider by EXACT object name and
/// reuses any presence component already on him.
///
/// Menu: MapleRide/NPCs/Wire Taka Legendary (Hyoga)
/// </summary>
public static class TakaLegendarySetup
{
    const string ScenePath = "Assets/Scenes/SakuraPass.unity";

    /// <summary>Must match TakaNpcRoster's NamePrefix + the roster's rider-30 Name, exactly.</summary>
    const string RiderObjectName = "Taka NPC Hyoga";

    [MenuItem("MapleRide/NPCs/Wire Taka Legendary (Hyoga)", priority = 64)]
    public static void Wire()
    {
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var rider = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                          .FirstOrDefault(t => t.name == RiderObjectName);
        if (rider == null)
        {
            Debug.LogError($"[legendary] FAIL '{RiderObjectName}' is not in the scene. " +
                           "Run MapleRide/NPCs/Stage Taka Roster first - this pass decorates the " +
                           "roster rider, it does not build one.");
            return;
        }

        var cyclist = rider.GetComponent<NPCCyclist>();
        if (cyclist == null)
        {
            Debug.LogError("[legendary] FAIL the Hyoga object carries no NPCCyclist; " +
                           "presence has nothing to drive.");
            return;
        }

        var p = rider.GetComponent<LegendaryRiderPresence>();
        if (p == null) p = rider.gameObject.AddComponent<LegendaryRiderPresence>();

        // Pushed onto the SERIALIZED component, not left to the field initialisers: a value that
        // only exists as a C# default is invisible to anyone inspecting the scene, and is lost
        // the moment someone edits the prefab by hand.
        p.riderId = "hyoga";
        p.displayName = "Hyoga";
        p.title = "The North Wall";
        p.regionId = RegionCatalog.TakaMountains;
        p.sightingRadiusM = 42f;
        p.escapeSpeedMultiplier = 1.9f;
        p.escapeRampSeconds = 2.5f;
        p.escapeHoldSeconds = 14f;
        p.escapeReleaseSeconds = 6f;

        EditorUtility.SetDirty(p);
        EditorUtility.SetDirty(rider.gameObject);

        var greeting = rider.GetComponentInChildren<NpcGreeting>(true);
        Debug.Log($"[legendary] Hyoga wired at {rider.position}, seated {cyclist.progress:0} steps " +
                  $"along '{cyclist.segmentId}', speed {cyclist.speed:0.00} m/s, " +
                  $"greeting={(greeting != null ? greeting.riderName : "MISSING")}, " +
                  $"journal id 'hyoga'.");

        EditorSceneManager.MarkSceneDirty(rider.gameObject.scene);
        EditorSceneManager.SaveScene(rider.gameObject.scene);
        AssetDatabase.SaveAssets();
        Debug.Log("[legendary] scene saved.");
    }
}
