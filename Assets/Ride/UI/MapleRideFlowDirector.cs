using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Owns MapleRide's opening flow: TITLE -> WORLD MAP SELECTION -> 3 . 2 . 1 . GO -> RIDING.
///
/// WHY THIS EXISTS
/// The title screen used to hand the player straight to whatever scene happened to be loaded:
/// pressing ENTER only set <c>Time.timeScale = 1</c> and destroyed itself, so the player was
/// dropped into Sakura Pass with no map choice and no start line. This director is the missing
/// middle: it keeps the world frozen until a map is actually chosen, drives the selected map
/// through <see cref="RegionDirector.FastTravel"/>, PROVES the chosen region's environment is
/// really on screen, and only then counts the rider in.
///
/// It reuses the shipped <see cref="WorldMapHud"/> overlay for the selection step rather than
/// growing a second world map, so the pins, the art, the lock states and the fast-travel path
/// are all the ones the rest of the game already uses.
///
/// Lives on a DontDestroyOnLoad root created on demand, exactly like the title screen, so the
/// flow is reliable no matter which scene Play starts from. It NEVER creates itself: batchmode
/// QA captures and self-tests are only ever gated if something explicitly asks for the flow.
/// </summary>
[DefaultExecutionOrder(-400)]
public sealed class MapleRideFlowDirector : MonoBehaviour
{
    public const string RootName = "MapleRide Flow";
    public const string CountdownCanvasName = "MapleRide Countdown";

    public enum Phase { Idle, Title, MapSelection, Countdown, Riding }

    // ---------------------------------------------------------------- tuning (all provisional)

    [Header("Countdown (PROVISIONAL tuning)")]
    [Tooltip("PROVISIONAL: numbers counted down before GO. 3 gives the design's '3 2 1 GO!'.")]
    [Min(1)] public int countFrom = 3;
    [Tooltip("PROVISIONAL: seconds each number is held.")]
    public float beatSeconds = 1.0f;
    [Tooltip("PROVISIONAL: seconds 'GO!' stays up after input is released.")]
    public float goHoldSeconds = 0.85f;
    [Tooltip("PROVISIONAL: font size of the big centred numeral at the 1920x1080 reference.")]
    public int numeralFontSize = 380;
    [Tooltip("PROVISIONAL: font size of the GO! word.")]
    public int goFontSize = 260;

    [Header("Selection music (PROVISIONAL tuning)")]
    [Tooltip("PROVISIONAL: tempo of the game-selection cue, so it reads as a different track " +
             "from the title theme without shipping a second audio file.")]
    public float selectionBpm = 138f;
    [Tooltip("PROVISIONAL: volume of the game-selection cue.")]
    [Range(0f, 0.5f)] public float selectionVolume = 0.26f;
    [Tooltip("PROVISIONAL: semitone transpose that separates the selection cue from the title.")]
    public int selectionTranspose = 5;

    [Header("Behaviour")]
    [Tooltip("Also count the rider in when a map is chosen from the in-ride World Map (TAB). " +
             "PROVISIONAL: arriving on a new map is the same moment either way.")]
    public bool countdownOnFastTravel = true;

    [Header("State (read only)")]
    public Phase phase = Phase.Idle;

    // ---------------------------------------------------------------- live wiring

    private RideBootstrap _boot;
    private RegionDirector _regions;
    private RideHud _hud;
    private WorldMapHud _worldMap;
    private MapleRideTitleBgm _bgm;

    private Canvas _countdownCanvas;
    private CanvasGroup _countdownGroup;
    private Text _numeral;
    private Text _caption;
    private Text _lockNotice;
    private RectTransform _band;
    private Coroutine _countdown;
    private bool _subscribed;

    public static MapleRideFlowDirector Instance { get; private set; }

    /// <summary>Creates (or returns) the flow root. Never called by batchmode capture paths.</summary>
    public static MapleRideFlowDirector Ensure()
    {
        if (Instance != null) return Instance;
        var existing = FindAnyObjectByType<MapleRideFlowDirector>();
        if (existing != null) { Instance = existing; return existing; }

        var root = new GameObject(RootName);
        DontDestroyOnLoad(root);
        return root.AddComponent<MapleRideFlowDirector>();
    }

    private void Awake()
    {
        // Converge, never accumulate: a second director would mean two countdowns and two gates.
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        if (phase == Phase.Idle) phase = Phase.Title;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (_regions != null && _subscribed) _regions.RegionChanged -= OnRegionChanged;
        // A director torn down mid-countdown must never leave the bike locked.
        RideInputGate.Unlock();
    }

    // ================================================================= step 1: map selection

    /// <summary>
    /// ENTER on the title screen lands here. The world stays frozen and the ride HUD stays out
    /// of the way; only the world map is on screen, with the game-selection cue playing.
    /// </summary>
    public bool OpenMapSelection()
    {
        phase = Phase.MapSelection;
        Time.timeScale = 0f;
        RideInputGate.Lock("map-selection");
        StartSelectionBgm();
        if (MapleRideBoot.Active != null)
        {
            MapleRideBoot.Active.OpenMap();
            return true;
        }
        if (_loadingBootScene) return true;
        if (!Application.CanStreamedLevelBeLoaded(MapleRideBoot.ScenePath))
        {
            Debug.LogError("[flow] MapleRideBoot is missing from build settings; cannot open map selection.");
            phase = Phase.Title;
            RideInputGate.Unlock();
            StopBgm();
            return false;
        }
        _loadingBootScene = true;
        StartCoroutine(LoadBootMenu());
        return true;
    }

    private bool _loadingBootScene;

    /// <summary>Unload an explicitly launched ride scene while keeping the title in front.</summary>
    public void RedirectStartupToBoot()
    {
        if (_loadingBootScene || MapleRideBoot.Active != null) return;
        if (!Application.CanStreamedLevelBeLoaded(MapleRideBoot.ScenePath))
        {
            Debug.LogError("[flow] MapleRideBoot is missing from build settings; startup redirect failed.");
            return;
        }
        _loadingBootScene = true;
        StartCoroutine(LoadBootMenu());
    }

    private IEnumerator LoadBootMenu()
    {
        AsyncOperation operation = null;
        string failure = null;
        try
        {
            operation = UnityEngine.SceneManagement.SceneManager.LoadSceneAsync(
                MapleRideBoot.ScenePath, UnityEngine.SceneManagement.LoadSceneMode.Single);
        }
        catch (System.Exception error) { failure = error.Message; }
        if (operation == null)
        {
            _loadingBootScene = false;
            phase = Phase.Title;
            RideInputGate.Unlock();
            StopBgm();
            Debug.LogError("[flow] Unable to load the boot menu: " + failure);
            yield break;
        }
        while (!operation.isDone) yield return null;
        yield return null;
        _loadingBootScene = false;
        RebindRideScene();
        // Runtime initialization only runs on the first scene, so create the menu on redirects.
        if (MapleRideBoot.Active == null)
            new GameObject("MapleRide Boot").AddComponent<MapleRideBoot>();
        EnsureSingleEventSystem();
        if (FindAnyObjectByType<MapleRideTitleScreen>() == null)
            OpenMapSelection();
    }

    /// <summary>All title/start entry points require an explicit map choice.</summary>
    public bool StartCurrentRide() => OpenMapSelection();

    public static void EnsureSingleEventSystem()
    {
        var systems = FindObjectsByType<EventSystem>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (systems.Length == 0)
        {
            var go = new GameObject("MapleRide EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            EventSystem.current = go.GetComponent<EventSystem>();
            return;
        }

        EventSystem keep = null;
        string activeScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        foreach (var system in systems)
        {
            if (system.isActiveAndEnabled && system.gameObject.scene.IsValid() && system.gameObject.scene.name == activeScene)
            {
                keep = system;
                break;
            }
        }
        if (keep == null)
            foreach (var system in systems)
                if (system.isActiveAndEnabled) { keep = system; break; }
        if (keep == null) keep = systems[0];
        keep.gameObject.SetActive(true);
        keep.enabled = true;
        foreach (var system in systems)
            if (system != keep)
            {
                system.enabled = false;
                Destroy(system.gameObject);
            }
        EventSystem.current = keep;
    }

    private void OnRegionChanged(string regionId)
    {
        if (phase == Phase.MapSelection ||
            (countdownOnFastTravel && phase == Phase.Riding))
            BeginCountdown();
    }

    // ================================================================= step 2: countdown

    /// <summary>
    /// The chosen map is already loaded by <see cref="RegionDirector.FastTravel"/> at this point.
    /// Put the world back on screen, prove it is actually there, then count the rider in.
    /// </summary>
    public void BeginCountdown()
    {
        Resolve();
        phase = Phase.Countdown;

        if (_worldMap != null)
        {
            _worldMap.selectionMode = false;
            _worldMap.SetOpen(false);
        }
        if (_hud != null) _hud.SetRideWidgetsVisible(true);
        StopBgm();

        // The world runs during the countdown - petals fall, the sun moves, the camera lives -
        // but the RIDER cannot act. Freezing timeScale instead would give a dead photograph.
        Time.timeScale = 1f;
        RideInputGate.Lock("countdown");

        if (!EnsureEnvironmentVisible())
        {
            ReturnToMapSelection("The selected region has no visible environment yet.");
            return;
        }

        BuildCountdownUi();
        if (_countdown != null) StopCoroutine(_countdown);
        _countdown = StartCoroutine(CountdownRoutine());
    }

    /// <summary>
    /// The "empty map" guard.
    ///
    /// The scene stores each region's environment root as an ACTIVE-STATE, which means a play
    /// session or a batchmode pass that ended in another region can leave every Sakura root
    /// switched off in the saved scene - and the player is then dropped into a blue void with
    /// only the drifting leaves of a region that is not even being ridden. Re-asserting the
    /// visibility from the session's own course here makes the ride's start line the single
    /// place that decides what the world looks like, and the renderer census makes a failure
    /// LOUD instead of a silent photograph of open water.
    /// </summary>
    public bool EnsureEnvironmentVisible()
    {
        if (_regions == null) { Debug.LogWarning("[flow] no RegionDirector to verify."); return false; }

        _regions.Resolve();
        _regions.SyncFromSession();            // region follows the course that is actually loaded
        _regions.ApplyEnvironmentVisibility();
        _regions.ApplyAmbience();

        if (_boot != null)
        {
            if (_boot.follower != null) _boot.follower.Apply();
            var streamer = _boot.GetComponent<RouteDressingStreamer>();
            if (streamer != null && _boot.rider != null) streamer.Apply(_boot.rider.position);
        }

        var region = RegionCatalog.Find(_regions.currentRegionId);
        int roots = 0, active = 0;
        if (region != null && !string.IsNullOrEmpty(region.EnvironmentRoot))
        {
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            {
                var scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                if (!scene.IsValid() || !scene.isLoaded) continue;
                foreach (var go in scene.GetRootGameObjects())
                {
                    if (go.name != region.EnvironmentRoot) continue;
                    roots++;
                    if (!go.activeInHierarchy) continue;
                    foreach (var r in go.GetComponentsInChildren<Renderer>(false))
                        if (r.enabled) active++;
                }
            }
        }

        string label = region != null ? region.DisplayName : _regions.currentRegionId;
        if (active <= 0)
        {
            Debug.LogError($"[flow] {label}: the map is EMPTY - {roots} environment root(s), " +
                           "0 active renderers. The rider would start in a void.");
            return false;
        }
        else
            Debug.Log($"[flow] {label}: environment present - {roots} root(s), " +
                      $"{active} active renderers.");
        return true;
    }

    private void ReturnToMapSelection(string reason)
    {
        Debug.LogError("[flow] " + reason + " Returning to world-map selection.");
        phase = Phase.MapSelection;
        Time.timeScale = 0f;
        RideInputGate.Lock("map-selection");
        if (_hud != null) _hud.SetRideWidgetsVisible(false);
        StartSelectionBgm();
        if (_worldMap != null)
        {
            _worldMap.selectionMode = true;
            _worldMap.SetOpen(true);
        }
    }

    private IEnumerator CountdownRoutine()
    {
        var session = _boot != null ? _boot.session : null;
        if (session != null) session.ResetRide();       // every countdown starts a fresh run

        for (int n = Mathf.Max(1, countFrom); n >= 1; n--)
        {
            SetBeat(n.ToString(), "GET READY", HudKit.Chalk);
            float t = 0f;
            while (t < beatSeconds)
            {
                t += Time.unscaledDeltaTime;           // the count is real seconds, always
                Punch(t / Mathf.Max(0.01f, beatSeconds));
                yield return null;
            }
        }

        SetBeat("GO!", "", HudKit.Ember);
        RideInputGate.Unlock();                        // the ride is the player's from here
        phase = Phase.Riding;
        Debug.Log("[flow] GO - ride input released.");

        float g = 0f;
        while (g < goHoldSeconds)
        {
            g += Time.unscaledDeltaTime;
            Punch(g / Mathf.Max(0.01f, goHoldSeconds));
            if (_countdownGroup != null)
                _countdownGroup.alpha = Mathf.Clamp01(1f - (g / Mathf.Max(0.01f, goHoldSeconds)));
            yield return null;
        }

        if (_countdownCanvas != null) _countdownCanvas.gameObject.SetActive(false);
        _countdown = null;
    }

    private void SetBeat(string value, string caption, Color color)
    {
        if (_countdownCanvas != null) _countdownCanvas.gameObject.SetActive(true);
        if (_countdownGroup != null) _countdownGroup.alpha = 1f;
        bool isGo = value == "GO!";
        if (_numeral != null)
        {
            _numeral.text = value;
            _numeral.color = color;
            _numeral.fontSize = isGo ? goFontSize : numeralFontSize;
        }
        if (_caption != null) _caption.text = caption;
        // The lock notice must go the instant the lock does, or GO reads as "still frozen".
        if (_lockNotice != null) _lockNotice.gameObject.SetActive(!isGo);
    }

    /// <summary>Each beat lands hard and settles: a scale punch on the numeral, 0..1 through the beat.</summary>
    private void Punch(float t01)
    {
        if (_numeral == null) return;
        float k = 1.22f - 0.22f * Mathf.Clamp01(t01 * 3.2f);
        _numeral.transform.localScale = new Vector3(k, k, 1f);
        if (_band != null)
        {
            var c = _band.GetComponent<Image>().color;
            c.a = Mathf.Lerp(0.55f, 0.32f, Mathf.Clamp01(t01 * 2f));
            _band.GetComponent<Image>().color = c;
        }
    }

    // ================================================================= countdown UI

    /// <summary>
    /// Built in code into its own overlay canvas named <see cref="CountdownCanvasName"/>, in the
    /// house style: converge by exact name, never accumulate.
    /// </summary>
    private void BuildCountdownUi()
    {
        var old = transform.Find(CountdownCanvasName);
        if (old != null) DestroyImmediate(old.gameObject);

        var go = new GameObject(CountdownCanvasName, typeof(RectTransform), typeof(Canvas),
                                typeof(CanvasScaler), typeof(CanvasGroup));
        go.transform.SetParent(transform, false);

        _countdownCanvas = go.GetComponent<Canvas>();
        _countdownCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // Above the ride HUD (100) and the world map, below the title screen (2000).
        _countdownCanvas.sortingOrder = 1500;

        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        _countdownGroup = go.GetComponent<CanvasGroup>();
        _countdownGroup.blocksRaycasts = false;      // the count is presentation, not a menu
        _countdownGroup.interactable = false;

        var root = (RectTransform)go.transform;

        // A WIDE band across the middle of the screen, so the count reads at a glance from a
        // trainer two metres away rather than as a small HUD digit.
        _band = HudKit.Panel(root, "Band", new Color(0.016f, 0.024f, 0.035f, 0.42f));
        HudKit.Place(_band, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f),
                     new Vector2(0f, -190f), new Vector2(0f, 190f));
        _band.GetComponent<Image>().raycastTarget = false;

        _numeral = HudKit.Label(root, "Numeral", "3", numeralFontSize, HudKit.Chalk,
                                TextAnchor.MiddleCenter, FontStyle.Bold);
        HudKit.AddOutline(_numeral, 0.95f, 5f);
        HudKit.Place((RectTransform)_numeral.transform, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f),
                     new Vector2(0f, -200f), new Vector2(0f, 200f));

        _caption = HudKit.Label(root, "Caption", "GET READY", 34, HudKit.Gold,
                                TextAnchor.MiddleCenter, FontStyle.Bold);
        HudKit.AddOutline(_caption, 0.9f, 1.6f);
        HudKit.Place((RectTransform)_caption.transform, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f),
                     new Vector2(0f, -258f), new Vector2(0f, -200f));

        var locked = HudKit.Label(root, "Locked", "HOLD - PEDAL INPUT LOCKED", 22,
                                  new Color(0.90f, 0.89f, 0.87f, 0.92f),
                                  TextAnchor.MiddleCenter, FontStyle.Bold);
        HudKit.AddShadow(locked, 0.9f, 1.4f);
        HudKit.Place((RectTransform)locked.transform, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f),
                     new Vector2(0f, 200f), new Vector2(0f, 252f));
        _lockNotice = locked;

        HudSprites.SetLayerRecursively(go, HudSprites.UiLayer);
    }

    /// <summary>The numeral currently on screen ("3", "2", "1", "GO!"), for the capture harness.</summary>
    public string CurrentBeat => _numeral != null ? _numeral.text : "";

    /// <summary>The countdown canvas, so a capture harness can route it like the other HUDs.</summary>
    public Canvas CountdownCanvas => _countdownCanvas;

    // ================================================================= plumbing

    public void Resolve()
    {
        if (_boot == null) _boot = FindAnyObjectByType<RideBootstrap>();
        if (_boot != null)
        {
            _boot.Resolve();
            if (_regions == null) _regions = _boot.regions;
            if (_hud == null) _hud = _boot.hud;
        }
        if (_regions == null) _regions = FindAnyObjectByType<RegionDirector>();
        if (_hud == null) _hud = FindAnyObjectByType<RideHud>();
        if (_worldMap == null && _hud != null) _worldMap = _hud.worldMap;
        if (_worldMap == null) _worldMap = FindAnyObjectByType<WorldMapHud>();
        if (_worldMap != null && _worldMap.regions == null) _worldMap.regions = _regions;

        if (_regions != null && !_subscribed)
        {
            _regions.RegionChanged += OnRegionChanged;
            _subscribed = true;
        }
    }

    public void RebindRideScene()
    {
        if (_regions != null && _subscribed) _regions.RegionChanged -= OnRegionChanged;
        _boot = null;
        _regions = null;
        _hud = null;
        _worldMap = null;
        _subscribed = false;
        Resolve();
    }

    private void StartSelectionBgm()
    {
        if (_bgm != null) return;
        _bgm = gameObject.AddComponent<MapleRideTitleBgm>();
        _bgm.bpm = selectionBpm;
        _bgm.volume = selectionVolume;
        _bgm.transposeSemitones = selectionTranspose;
    }

    private void StopBgm()
    {
        if (_bgm == null) return;
        Destroy(_bgm);
        _bgm = null;
    }

    /// <summary>True while the selection cue is audible - used by the capture self-test.</summary>
    public bool SelectionBgmPlaying => _bgm != null;
}
