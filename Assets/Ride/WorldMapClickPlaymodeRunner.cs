using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Proves the WORLDMAP-CLICK fix in REAL play mode: a real Canvas/GraphicRaycaster/EventSystem
/// stack, with a real Screen size (edit-mode batch scripts get a degenerate placeholder canvas
/// size and cannot be trusted for raycast hit-testing - see WorldMapClickProbe's own log).
///
/// Opens the World Map, raycasts at the Shiosai Coast pin's actual screen position exactly as
/// the real EventSystem would for a mouse click, executes the hit's pointerClick handler, and
/// confirms RegionDirector actually changed region. Also fast-travels back so the scene is left
/// on Sakura Pass, matching the shipped default.
///
/// Driven by WorldMapClickPlaymodeCapture (editor side), which enters play mode and spawns this.
/// </summary>
public class WorldMapClickPlaymodeRunner : MonoBehaviour
{
    public static bool Finished;
    public static bool Failed;

    private IEnumerator Start()
    {
        Finished = false;
        Failed = false;

        var boot = FindFirstObjectByType<RideBootstrap>();
        if (boot == null) { Fail("no RideBootstrap in the scene"); yield break; }
        boot.Resolve();

        if (boot.regions == null) { Fail("RideBootstrap.regions is still null after Resolve()."); yield break; }
        if (boot.hud == null) { Fail("RideBootstrap.hud is null."); yield break; }

        yield return null; // let Awake/Start of everything else run first

        var hud = boot.hud;
        if (hud.Canvas == null) hud.Build();
        var worldMap = hud.worldMap;
        worldMap.regions = boot.regions;

        var es = FindFirstObjectByType<EventSystem>();
        if (es == null) { Fail("no EventSystem in play mode - clicks are unreceivable."); yield break; }

        worldMap.selectionMode = false;
        worldMap.SetOpen(true);
        hud.Refresh();
        Canvas.ForceUpdateCanvases();
        yield return null;

        Debug.Log($"[worldmap-play] Screen={Screen.width}x{Screen.height} " +
                  $"overlayActive={worldMap.Overlay.gameObject.activeInHierarchy} isOpen={worldMap.isOpen}");

        var pinHolder = worldMap.Overlay.Find("Map/Pin " + RegionCatalog.ShiosaiCoast);
        if (pinHolder == null) { Fail("Shiosai Coast pin not found under the overlay."); yield break; }

        var dot = pinHolder.Find("Dot");
        if (dot == null) { Fail("pin has no 'Dot' child."); yield break; }
        var dotButton = dot.GetComponent<Button>();
        if (dotButton == null) { Fail("pin dot has no Button component."); yield break; }

        var dotRect = (RectTransform)dot;
        var corners = new Vector3[4];
        dotRect.GetWorldCorners(corners);
        Vector3 worldCenter = (corners[0] + corners[2]) * 0.5f;
        Camera cam = hud.Canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : hud.Canvas.worldCamera;
        Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(cam, worldCenter);
        Debug.Log($"[worldmap-play] dot world center={worldCenter} -> screen point={screenPoint}");

        var raycasters = FindObjectsByType<GraphicRaycaster>(FindObjectsSortMode.None);
        var ped = new PointerEventData(es) { position = screenPoint };
        var results = new List<RaycastResult>();
        foreach (var rc in raycasters.OrderByDescending(r => r.GetComponent<Canvas>().sortingOrder))
        {
            var sub = new List<RaycastResult>();
            rc.Raycast(ped, sub);
            results.AddRange(sub);
        }
        Debug.Log($"[worldmap-play] raycast at pin returned {results.Count} hit(s): " +
                  string.Join(", ", results.Select(r => r.gameObject.name)));

        if (results.Count == 0) { Fail("raycast at the pin hit NOTHING - pin is unreachable."); yield break; }

        var top = results[0].gameObject;
        bool topIsPin = top == dot.gameObject || top.transform.IsChildOf(pinHolder);
        if (!topIsPin) { Fail($"topmost hit '{top.name}' is not the pin - something else absorbs the click."); yield break; }

        string before = boot.regions.currentRegionId;
        ExecuteEvents.ExecuteHierarchy(top, ped, ExecuteEvents.pointerClickHandler);
        yield return null;
        string after = boot.regions.currentRegionId;
        Debug.Log($"[worldmap-play] region before='{before}' after='{after}' " +
                  $"course='{boot.session.Course?.DisplayName}' isOpen={worldMap.isOpen}");

        if (after != RegionCatalog.ShiosaiCoast)
        {
            Fail($"clicking the pin did not change region (still '{after}').");
            yield break;
        }
        if (worldMap.isOpen)
        {
            Fail("overlay is still open after a successful travel (expected auto-close).");
            yield break;
        }

        Debug.Log("[worldmap-play] PASS: pointer click on the Shiosai Coast pin travelled there " +
                  "and closed the overlay, exactly as a player's mouse click would.");

        // Leave the scene as a player would find it by default: back on Sakura Pass, map closed.
        boot.regions.FastTravel(RegionCatalog.SakuraPass);
        yield return null;
        Debug.Log($"[worldmap-play] returned to '{boot.regions.currentRegionId}' before exiting play mode.");

        Finished = true;
    }

    private void Fail(string why)
    {
        Debug.LogError("[worldmap-play] " + why);
        Failed = true;
        Finished = true;
    }
}
