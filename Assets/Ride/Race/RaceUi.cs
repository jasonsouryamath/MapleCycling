using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// The race feature's canvas and its shared building blocks, in the ride HUD's smoked-glass
/// <see cref="HudKit"/> style. Layers, back to front: world markers, the E prompt, the race HUD,
/// modal screens (intro, results, Riders), toasts, then the full-screen fade.
///
/// Mouse clicks: the scene ships without an EventSystem (every earlier UI was keyboard or
/// raw-mouse driven), so one is created on demand for the clickable prompt, results and Riders
/// screen. It uses the legacy StandaloneInputModule to match the project's Input Manager setting.
/// </summary>
public sealed class RaceUi : MonoBehaviour
{
    public const string CanvasName = "MapleRide Race UI";
    public static readonly Color CardGlass = new Color(0.035f, 0.045f, 0.075f, 0.86f);
    public static readonly Color CardGlassSoft = new Color(0.035f, 0.045f, 0.075f, 0.62f);
    public static readonly Color Navy = new Color(0.07f, 0.1f, 0.2f, 0.94f);
    public static readonly Color Win = RaceMath.Hex(0x3ddc74);
    public static readonly Color Loss = RaceMath.Hex(0xff4757);
    public static readonly Color Coin = RaceMath.Hex(0xffc23d);

    public Canvas Canvas { get; private set; }
    public RectTransform Root { get; private set; }
    public RectTransform MarkerLayer { get; private set; }
    public RectTransform PromptLayer { get; private set; }
    public RectTransform HudLayer { get; private set; }
    public RectTransform ModalLayer { get; private set; }
    public RectTransform ToastLayer { get; private set; }
    private Image _fade;
    private RectTransform _toast;
    private Text _toastText;
    private float _toastUntil;

    public void Build()
    {
        if (Canvas != null) return;
        var go = new GameObject(CanvasName, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler),
                                typeof(GraphicRaycaster));
        go.transform.SetParent(transform, false);
        go.layer = HudSprites.UiLayer;
        Canvas = go.GetComponent<Canvas>();
        Canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        Canvas.sortingOrder = 130;   // above the ride HUD (100), greeting card (110) and shop (120)
        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        Root = (RectTransform)go.transform;

        MarkerLayer = Layer("Markers");
        PromptLayer = Layer("Prompt");
        HudLayer = Layer("Race HUD");
        ModalLayer = Layer("Modal");
        ToastLayer = Layer("Toasts");
        var fade = HudKit.Panel(Layer("Fade"), "Fade", new Color(0f, 0f, 0f, 0f));
        Stretch(fade);
        _fade = fade.GetComponent<Image>();
        fade.gameObject.SetActive(false);

        _toast = HudKit.SoftPanel(ToastLayer, "Toast", CardGlass);
        HudKit.Corner(_toast, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -170f), new Vector2(520f, 58f));
        _toastText = HudKit.Label(_toast, "Text", "", 26, HudKit.Chalk, TextAnchor.MiddleCenter, FontStyle.Bold);
        HudKit.AddOutline(_toastText);
        Stretch((RectTransform)_toastText.transform);
        _toast.gameObject.SetActive(false);

        EnsureEventSystem();
    }

    private RectTransform Layer(string name)
    {
        var rt = HudKit.Rect(Root, name);
        Stretch(rt);
        return rt;
    }

    public static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }

    public static void EnsureEventSystem()
    {
        if (EventSystem.current != null || FindAnyObjectByType<EventSystem>() != null) return;
        var es = new GameObject("MapleRide EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        DontDestroyOnLoad(es);
    }

    private void Update()
    {
        if (_toast != null && _toast.gameObject.activeSelf)
        {
            float left = _toastUntil - Time.unscaledTime;
            if (left <= 0f) _toast.gameObject.SetActive(false);
            else
            {
                float a = Mathf.Clamp01(left / 0.3f);
                _toast.GetComponent<Image>().color = HudKit.WithAlpha(CardGlass, CardGlass.a * a);
                _toastText.color = HudKit.WithAlpha(_toastText.color, a);
            }
        }
    }

    public void Toast(string text, Color color, float seconds = 2.2f)
    {
        if (_toast == null) return;
        _toastText.text = text;
        _toastText.color = color;
        _toastUntil = Time.unscaledTime + seconds;
        _toast.gameObject.SetActive(true);
        _toast.GetComponent<Image>().color = CardGlass;
        float w = Mathf.Clamp(_toastText.preferredWidth + 60f, 280f, 1100f);
        _toast.sizeDelta = new Vector2(w, 58f);
    }

    public bool FadeActive => _fade != null && _fade.gameObject.activeSelf;

    public IEnumerator Fade(float to, float seconds)
    {
        if (_fade == null) yield break;
        _fade.gameObject.SetActive(true);
        float from = _fade.color.a, t = 0f;
        while (t < seconds)
        {
            t += Time.unscaledDeltaTime;
            _fade.color = new Color(0f, 0f, 0f, Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t / seconds)));
            yield return null;
        }
        _fade.color = new Color(0f, 0f, 0f, to);
        _fade.gameObject.SetActive(to > 0.001f);
    }

    public void SetFade(float a)
    {
        if (_fade == null) return;
        _fade.color = new Color(0f, 0f, 0f, a);
        _fade.gameObject.SetActive(a > 0.001f);
    }

    // ------------------------------------------------------------------ building blocks

    /// <summary>A clickable glass button with a key-cap hint ("[E] Race"), like the target art.</summary>
    public static Button KeyButton(Transform parent, string name, string key, string label, Color accent,
                                   Action onClick, int fontSize = 24)
    {
        var rt = HudKit.SoftPanel(parent, name, new Color(0.05f, 0.07f, 0.12f, 0.92f));
        var img = rt.GetComponent<Image>();
        img.raycastTarget = true;
        var edge = HudKit.SoftPanel(rt, "Edge", HudKit.WithAlpha(accent, 0.85f), frame: true);
        Stretch(edge);
        var btn = rt.gameObject.AddComponent<Button>();
        var colors = btn.colors;
        colors.highlightedColor = new Color(1.25f, 1.25f, 1.35f, 1f);
        colors.pressedColor = new Color(0.8f, 0.8f, 0.85f, 1f);
        btn.colors = colors;
        btn.targetGraphic = img;
        if (onClick != null) btn.onClick.AddListener(() => onClick());

        float x = 12f;
        if (!string.IsNullOrEmpty(key))
        {
            var cap = HudKit.SoftPanel(rt, "Key", HudKit.Chalk);
            HudKit.Corner(cap, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(12f, 0f),
                          new Vector2(Mathf.Max(34f, 16f + key.Length * 13f), 34f));
            var k = HudKit.Label(cap, "Text", key, 20, HudKit.Ink, TextAnchor.MiddleCenter, FontStyle.Bold);
            Stretch((RectTransform)k.transform);
            x = 12f + cap.sizeDelta.x + 12f;
        }
        var t = HudKit.Label(rt, "Label", label, fontSize, HudKit.Chalk, TextAnchor.MiddleLeft, FontStyle.Bold);
        HudKit.AddShadow(t);
        HudKit.Place((RectTransform)t.transform, Vector2.zero, Vector2.one, new Vector2(x, 0f), new Vector2(-10f, 0f));
        return btn;
    }

    public static Image Icon(Transform parent, string name, Sprite sprite, Vector2 size, Color? tint = null)
    {
        var rt = HudKit.Panel(parent, name, tint ?? Color.white);
        var img = rt.GetComponent<Image>();
        img.sprite = sprite;
        img.preserveAspect = true;
        rt.sizeDelta = size;
        return img;
    }

    public static RawImage Portrait(Transform parent, string name, Texture tex, float size)
    {
        var frame = HudKit.SoftPanel(parent, name, new Color(0.02f, 0.03f, 0.05f, 0.9f));
        frame.sizeDelta = new Vector2(size, size);
        var go = new GameObject("Image", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
        go.transform.SetParent(frame, false);
        var ri = go.GetComponent<RawImage>();
        ri.texture = tex;
        ri.raycastTarget = false;
        HudKit.Place((RectTransform)go.transform, Vector2.zero, Vector2.one, new Vector2(3f, 3f), new Vector2(-3f, -3f));
        return ri;
    }

    /// <summary>A horizontal bar: returns the fill image (use fillAmount or width).</summary>
    public static Image Bar(Transform parent, string name, Vector2 size, Color bed, Color fill)
    {
        var rt = HudKit.SoftPanel(parent, name, bed);
        rt.sizeDelta = size;
        var f = HudKit.SoftPanel(rt, "Fill", fill);
        f.anchorMin = new Vector2(0f, 0f);
        f.anchorMax = new Vector2(0f, 1f);
        f.pivot = new Vector2(0f, 0.5f);
        f.offsetMin = new Vector2(2f, 2f);
        f.offsetMax = new Vector2(2f, -2f);
        return f.GetComponent<Image>();
    }

    public static void SetBar(Image fill, float fraction)
    {
        if (fill == null) return;
        var bed = (RectTransform)fill.transform.parent;
        var rt = (RectTransform)fill.transform;
        float w = Mathf.Max(0f, (bed.rect.width > 1f ? bed.rect.width : bed.sizeDelta.x) - 4f);
        rt.sizeDelta = new Vector2(w * Mathf.Clamp01(fraction), rt.sizeDelta.y);
        fill.enabled = fraction > 0.002f;
    }

    public static string Coins(int n) => n.ToString("N0") + " MC";
}
