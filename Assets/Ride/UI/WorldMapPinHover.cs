using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Hover feedback for a World Map pin: while the pointer is over the pin the point swells and
/// its gold ring lights up as a glow, then it settles back when the pointer leaves. Purely
/// presentational - actually choosing a region is still the pin's <see cref="Button"/>.
///
/// Lives on the pin HOLDER (the parent of the dot and the name card), so Unity's pointer
/// enter/exit propagation treats the dot and its label card as one region: moving the pointer
/// from the point onto its card keeps the same pin lit instead of flickering.
///
/// Animates on <see cref="Time.unscaledDeltaTime"/> because the map-selection screen runs with
/// <c>Time.timeScale == 0</c> (the world is frozen until a map is chosen), where scaled delta
/// time is zero and a normal tween would never move.
/// </summary>
public sealed class WorldMapPinHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [Tooltip("The pin holder that swells on hover (usually this object's own RectTransform).")]
    public RectTransform target;

    [Tooltip("The ring that lights up as the hover glow.")]
    public Image glow;

    [Tooltip("How much the point grows while hovered.")]
    public float hoverScale = 1.35f;

    [Tooltip("Higher settles the swell faster.")]
    public float responsiveness = 16f;

    private bool _hovered;
    private bool _glowWasOn;

    /// <summary>Keyboard focus (WorldMapHud arrow-key selection): swells and glows like a hover.</summary>
    public bool focused;

    public void OnPointerEnter(PointerEventData eventData)
    {
        _hovered = true;
        if (glow != null)
        {
            // Remember whether the ring was already lit (this is the current region) so leaving
            // the pin restores that state instead of always switching the glow off.
            _glowWasOn = glow.gameObject.activeSelf;
            glow.gameObject.SetActive(true);
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        _hovered = false;
        if (glow != null && !_glowWasOn) glow.gameObject.SetActive(false);
    }

    private void OnDisable()
    {
        _hovered = false;
        if (target != null) target.localScale = Vector3.one;
    }

    private void Update()
    {
        if (target == null) return;
        float goal = _hovered || focused ? hoverScale : 1f;
        if (focused && glow != null && !glow.gameObject.activeSelf) glow.gameObject.SetActive(true);
        float k = 1f - Mathf.Exp(-responsiveness * Time.unscaledDeltaTime);
        float s = Mathf.Lerp(target.localScale.x, goal, k);
        if (Mathf.Abs(s - goal) < 0.001f) s = goal;
        target.localScale = new Vector3(s, s, 1f);
    }
}
