using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// The opening presentation layer for MapleRide.
///
/// It is intentionally created before the first scene rather than stored in a particular
/// scene. That makes the opening screen reliable when Play starts from Sakura Pass, another
/// region, or a future boot scene. The approved sea-bridge artwork stays under
/// Assets/Environment/TitleScreen/Resources so it is included in a player build.
/// </summary>
public sealed class MapleRideTitleScreen : MonoBehaviour
{
    public const string RootName = "MapleRide Title Screen";
    private const string BackgroundResource = "MapleRideTitleSeaBridge";

    private CanvasGroup _group;
    private bool _starting;
    private EventSystem _ownEventSystem;
    private GameObject _startButton;
    private static MapleRideTitleScreen _instance;

    /// <summary>
    /// Opt-in for the play-mode capture harness ONLY. Batchmode is normally exempt from the
    /// opening flow (see below); a harness that is specifically photographing the flow has to be
    /// able to ask for it. Set from an editor [InitializeOnLoad] hook, which runs before this.
    /// </summary>
    public static bool forceInBatchMode;

    // The process environment survives domain reloads and editor static constructor ordering.
    public static bool PresentationEnabled => !Application.isBatchMode || forceInBatchMode ||
        System.Environment.GetEnvironmentVariable("MR_BOOT_VALIDATION") == "1";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void CreateBeforeFirstScene()
    {
        // Batch-mode passes validate and capture the ride scene; they must not be paused behind
        // a presentation screen. Players always get the menu.
        if (!PresentationEnabled) return;
        if (FindAnyObjectByType<MapleRideTitleScreen>() != null) return;

        var root = new GameObject(RootName);
        DontDestroyOnLoad(root);
        root.AddComponent<MapleRideTitleScreen>();
    }

    private void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;
        DontDestroyOnLoad(gameObject);
        Time.timeScale = 0f;
        if (GetComponent<MapleRideTitleBgm>() == null)
            gameObject.AddComponent<MapleRideTitleBgm>();
        Build();
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDestroy()
    {
        if (_instance == this) _instance = null;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    /// <summary>
    /// The title is built BEFORE the first scene loads, when no EventSystem exists yet, so it makes
    /// its own. The ride scene ships "MapleRide EventSystem", which left TWO live EventSystems for
    /// the whole session (probe: claude_flow_cap2.log). Once the scene's arrives, retire ours and
    /// hand the START selection over, so exactly one module owns input.
    /// </summary>
    private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        MapleRideFlowDirector.EnsureSingleEventSystem();
        _ownEventSystem = null;
        if (_startButton != null && EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(_startButton);
    }

    private void Update()
    {
        if (_starting) return;
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) ||
            Input.GetKeyDown(KeyCode.Space))
            StartRide();
    }

    private void Build()
    {
        var canvasObject = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler),
                                          typeof(GraphicRaycaster), typeof(CanvasGroup));
        canvasObject.transform.SetParent(transform, false);

        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 2000;

        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        _group = canvasObject.GetComponent<CanvasGroup>();

        var eventSystem = EnsureEventSystem();

        var root = (RectTransform)canvasObject.transform;
        var texture = Resources.Load<Texture2D>(BackgroundResource);
        if (texture == null)
        {
            Debug.LogError("[mapleride] Title-screen artwork is missing from Resources: " +
                           BackgroundResource);
        }
        else
        {
            var background = CreateRaw(root, "Sea Bridge Background", texture, Color.white);
            Stretch((RectTransform)background.transform);
            var fitter = background.gameObject.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fitter.aspectRatio = (float)texture.width / texture.height;
        }

        // A restrained bottom scrim preserves the clean art while guaranteeing contrast for the
        // one interactive element, even on a bright ocean display.
        var bottomShade = CreateImage(root, "Start Area Shade", new Color(0.012f, 0.024f, 0.045f, 0.42f));
        StretchHorizontal((RectTransform)bottomShade.transform, 0f, 0f, 250f);

        var button = CreateImage(root, "Start Button", new Color(0.93f, 0.98f, 1f, 0.96f));
        Anchor((RectTransform)button.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 82f),
               new Vector2(410f, 86f));
        var startButton = button.gameObject.AddComponent<Button>();
        startButton.targetGraphic = button;
        startButton.transition = Selectable.Transition.ColorTint;
        var colors = startButton.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1f, 0.84f, 0.52f, 1f);
        colors.pressedColor = new Color(0.88f, 0.37f, 0.25f, 1f);
        colors.selectedColor = colors.highlightedColor;
        colors.fadeDuration = 0.08f;
        startButton.colors = colors;
        startButton.onClick.AddListener(StartRide);

        var label = CreateText(button.transform, "Label", "START RIDE", 30, new Color(0.06f, 0.10f, 0.15f, 1f),
                               FontStyle.Bold);
        Stretch((RectTransform)label.transform);
        label.alignment = TextAnchor.MiddleCenter;

        var hint = CreateText(root, "Keyboard Hint", "PRESS ENTER", 14,
                              new Color(1f, 1f, 1f, 0.86f), FontStyle.Bold);
        Anchor((RectTransform)hint.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
               new Vector2(0f, 38f), new Vector2(410f, 24f));
        hint.alignment = TextAnchor.MiddleCenter;
        AddShadow(hint);

        // Select immediately so controller/keyboard submit works without a preliminary click.
        _startButton = startButton.gameObject;
        eventSystem.SetSelectedGameObject(startButton.gameObject);
    }

    /// <summary>
    /// ENTER / SPACE / START RIDE.
    ///
    /// This used to simply unpause and delete itself, which meant the player was handed whatever
    /// scene happened to be loaded - always Sakura Pass - with no map choice and no start line.
    /// It now hands off to <see cref="MapleRideFlowDirector"/>, which keeps the world frozen and
    /// opens the WORLD MAP so a map is genuinely chosen before any riding happens.
    ///
    /// Note that timeScale is deliberately NOT restored here: the flow director owns it until
    /// the countdown starts.
    /// </summary>
    public void StartRide()
    {
        if (_starting) return;
        _starting = true;
        // Keep the title alive if the flow cannot reach a usable ride scene/map.  The
        // previous unconditional destroy left the player with no retry path when the
        // scene was unavailable or the HUD wiring was incomplete.
        if (MapleRideFlowDirector.Ensure().OpenMapSelection())
            Destroy(gameObject);
        else
            _starting = false;
    }

    private EventSystem EnsureEventSystem()
    {
        if (EventSystem.current != null) return EventSystem.current;
        var eventSystem = new GameObject("EventSystem", typeof(EventSystem),
                                         typeof(StandaloneInputModule));
        DontDestroyOnLoad(eventSystem);
        _ownEventSystem = eventSystem.GetComponent<EventSystem>();
        return _ownEventSystem;
    }

    private static RawImage CreateRaw(Transform parent, string name, Texture texture, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
        go.transform.SetParent(parent, false);
        var image = go.GetComponent<RawImage>();
        image.texture = texture;
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static Image CreateImage(Transform parent, string name, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        var image = go.GetComponent<Image>();
        image.color = color;
        return image;
    }

    private static Text CreateText(Transform parent, string name, string value, int size, Color color,
                                   FontStyle style)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        go.transform.SetParent(parent, false);
        var text = go.GetComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (text.font == null) text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        text.text = value;
        text.fontSize = size;
        text.fontStyle = style;
        text.color = color;
        text.raycastTarget = false;
        return text;
    }

    private static void AddShadow(Graphic graphic)
    {
        var shadow = graphic.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0f, 0f, 0.86f);
        shadow.effectDistance = new Vector2(1.5f, -1.5f);
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static void StretchHorizontal(RectTransform rect, float left, float bottom, float height)
    {
        rect.anchorMin = new Vector2(0f, 0f);
        rect.anchorMax = new Vector2(1f, 0f);
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(-left, bottom + height);
    }

    private static void Anchor(RectTransform rect, Vector2 anchor, Vector2 pivot, Vector2 position,
                               Vector2 size)
    {
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = pivot;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }
}
