using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Mouse pan/zoom for the World Map overlay (World redraw M3): drag anywhere on the map to pan,
/// scroll to zoom about the pointer. Lives on the overlay root, so drags and scrolls that start
/// on a pin or a label card bubble up to it; a drag cancels the pin's click (Unity clears
/// eligibleForClick once a drag begins), so panning never fast-travels by accident.
/// </summary>
public sealed class WorldMapPanZoom : MonoBehaviour, IBeginDragHandler, IDragHandler, IScrollHandler
{
    public WorldMapHud map;

    [Tooltip("Zoom factor per scroll notch.")]
    public float scrollStep = 1.25f;

    private Vector2 _last;
    private bool _dragging;

    public void OnBeginDrag(PointerEventData e)
    {
        _dragging = map != null && map.ScreenToFrame(e.position, e.pressEventCamera, out _last);
    }

    public void OnDrag(PointerEventData e)
    {
        if (!_dragging || map == null) return;
        if (!map.ScreenToFrame(e.position, e.pressEventCamera, out var now)) return;
        map.PanByFrame(now - _last);
        _last = now;
    }

    public void OnScroll(PointerEventData e)
    {
        if (map == null || Mathf.Abs(e.scrollDelta.y) < 0.01f) return;
        if (!map.ScreenToFrame(e.position, e.enterEventCamera, out var frac)) frac = new Vector2(0.5f, 0.5f);
        map.ZoomAbout(frac, Mathf.Pow(scrollStep, Mathf.Sign(e.scrollDelta.y)));
    }
}
