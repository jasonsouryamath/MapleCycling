using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Owns the menu without instantiating a ride or a region environment.</summary>
public sealed class MapleRideBoot : MonoBehaviour
{
    public const string ScenePath = "Assets/Scenes/MapleRideBoot.unity";
    public const string SceneName = "MapleRideBoot";
    public static MapleRideBoot Active { get; private set; }
    private WorldMapHud _map;
    private Text _loading;
    private AudioListener _menuListener;
    private bool _busy;
    private RegionLoadingScreen _screen;
    public static string RegionScenePath(string id) => "Assets/Scenes/Playable/" + id + ".unity";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Initialize()
    {
        if (!MapleRideTitleScreen.PresentationEnabled) return;
        if (Active != null) return;
        // Explicit scene launches still enter through the same lightweight menu.
        var scene = SceneManager.GetActiveScene();
        // Prefer the full asset path; accept the known boot name when a player exposes no path.
        // Build index zero alone is insufficient when explicitly starting an authoring scene.
        bool isBootScene = scene.path == ScenePath ||
                           (string.IsNullOrEmpty(scene.path) && scene.name == SceneName);
        if (!isBootScene)
        {
            MapleRideFlowDirector.Ensure().RedirectStartupToBoot();
            return;
        }
        new GameObject("MapleRide Boot").AddComponent<MapleRideBoot>();
    }

    private void Awake()
    {
        if (Active != null && Active != this) { Destroy(gameObject); return; }
        Active = this;
        DontDestroyOnLoad(gameObject);
        _menuListener = gameObject.AddComponent<AudioListener>();
        RefreshAudioListener();
        SceneManager.sceneLoaded += OnSceneLoaded;
        var canvasObject = new GameObject("Menu Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasObject.transform.SetParent(transform, false);
        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1000;
        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        _map = gameObject.AddComponent<WorldMapHud>();
        _map.selectionMode = true;
        _map.destinationAvailable = IsAvailable;
        _map.destinationSelected = Choose;
        _map.Build((RectTransform)canvasObject.transform);
        _map.SetOpen(false);
        var button = canvasObject.transform.Find(WorldMapHud.ButtonName);
        if (button != null) button.gameObject.SetActive(false);
        _loading = HudKit.Label((RectTransform)canvasObject.transform, "Loading", "", 26, HudKit.Chalk,
                               TextAnchor.MiddleCenter, FontStyle.Bold);
        HudKit.Place((RectTransform)_loading.transform, new Vector2(0, 0), new Vector2(1, 0),
                     new Vector2(0, 30), new Vector2(0, 110));
        _loading.gameObject.SetActive(false);
        MapleRideFlowDirector.EnsureSingleEventSystem();
        Debug.Log("[boot] lightweight menu ready; no ride scene loaded.");
    }

    private bool IsAvailable(RegionCatalog.Region region)
    {
        var graph = RouteGraph.Load();
        return !_busy && region != null && region.Unlocked && graph != null && graph.Course(region.BuiltCourseId) != null &&
               Application.CanStreamedLevelBeLoaded(RegionScenePath(region.Id));
    }

    public void OpenMap()
    {
        MapleRideFlowDirector.EnsureSingleEventSystem();
        _map.SetOpen(true);
    }

    public static void TravelTo(RegionCatalog.Region region)
    {
        if (region == null) return;
        if (Active == null) new GameObject("MapleRide Travel Menu").AddComponent<MapleRideBoot>();
        Active.OpenMap();
        Time.timeScale = 0f;
        RideInputGate.Lock("map-loading");
        Active.Choose(region);
    }

    private void Choose(RegionCatalog.Region region)
    {
        if (!IsAvailable(region)) return;
        _busy = true;
        _map.enabled = false;
        _loading.gameObject.SetActive(true);
        _loading.transform.SetAsLastSibling();
        StartCoroutine(LoadRegion(region));
    }

    private IEnumerator LoadRegion(RegionCatalog.Region region)
    {
        _loading.text = "LOADING " + region.DisplayName.ToUpperInvariant() + "…";
        _screen = RegionLoadingScreen.Show(region);
        _screen.SetStatus(_loading.text);
        Debug.Log("[boot] selected " + region.Id + "; loading only " + RegionScenePath(region.Id));
        AsyncOperation operation = null;
        string loadFailure = null;
        try { operation = SceneManager.LoadSceneAsync(RegionScenePath(region.Id), LoadSceneMode.Single); }
        catch (System.Exception error) { loadFailure = error.Message; }
        if (loadFailure != null) { Recover("Unable to load " + region.DisplayName + ": " + loadFailure); yield break; }
        if (operation == null) { Recover("Unable to load this map. Choose again."); yield break; }
        while (!operation.isDone)
        {
            float loaded = Mathf.Clamp01(operation.progress / 0.9f);
            _loading.text = "LOADING " + region.DisplayName.ToUpperInvariant() + " · " +
                            Mathf.RoundToInt(loaded * 100) + "%";
            if (_screen != null) { _screen.SetStatus("LOADING THE MAP"); _screen.SetProgress(loaded * 0.7f); }
            yield return null;
        }
        // Allow the scene-owned HUD and RegionDirector.Start to finish before travel.
        yield return null;
        MapleRideFlowDirector.EnsureSingleEventSystem();
        var flow = MapleRideFlowDirector.Ensure();
        flow.RebindRideScene();
        var boot = FindAnyObjectByType<RideBootstrap>();
        if (boot != null) boot.Resolve();
        if (boot == null || boot.session == null || boot.regions == null || !boot.regions.CanTravelTo(region.Id))
        { Recover("This map could not start. Choose again."); yield break; }
        boot.session.EnsureCourse();
        boot.session.SelectCourse(region.BuiltCourseId);
        if (boot.session.Course == null || boot.session.courseId != region.BuiltCourseId)
        { Recover("The selected route is missing. Choose again."); yield break; }
        var streamer = FindAnyObjectByType<RegionSceneStreamer>();
        if (streamer != null)
        {
            _loading.text = "PREPARING " + region.DisplayName.ToUpperInvariant() + " · NEARBY ROADS AND SCENERY…";
            if (_screen != null) { _screen.SetStatus("PREPARING NEARBY ROADS AND SCENERY…"); _screen.SetProgress(0.85f); }
            yield return streamer.Prepare(boot.session.WorldPosition);
            if (!string.IsNullOrEmpty(streamer.Failure) || !streamer.ReadyAt(boot.session.WorldPosition))
            {
                Recover("Unable to prepare " + region.DisplayName + ": " +
                        (string.IsNullOrEmpty(streamer.Failure) ? "nearby scenery is not ready." : streamer.Failure));
                yield break;
            }
        }
        _loading.text = "STARTING " + region.DisplayName.ToUpperInvariant() + "…";
        if (_screen != null) { _screen.SetStatus("GET READY…"); _screen.SetProgress(0.95f); }
        flow.phase = MapleRideFlowDirector.Phase.MapSelection;
        if (!boot.regions.FastTravel(region.Id))
        { Recover("This route could not start. Choose again."); yield break; }
        if (flow.phase != MapleRideFlowDirector.Phase.Countdown)
        { Recover("This map has no visible environment. Choose again."); yield break; }
        if (_screen != null) { _screen.Finish(); _screen = null; }
        Destroy(gameObject);
    }

    private void Recover(string message)
    {
        Debug.LogError("[boot] " + message);
        _busy = false;
        if (_screen != null) { _screen.Dismiss(); _screen = null; }
        MapleRideFlowDirector.Ensure().phase = MapleRideFlowDirector.Phase.MapSelection;
        _map.enabled = true;
        _map.SetOpen(true);
        _loading.text = message;
        _loading.transform.SetAsLastSibling();
        Time.timeScale = 0;
        RideInputGate.Lock("map-selection");
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => RefreshAudioListener();

    private void RefreshAudioListener()
    {
        bool other = false;
        foreach (var listener in FindObjectsByType<AudioListener>(FindObjectsSortMode.None))
            if (listener != _menuListener && listener.isActiveAndEnabled) { other = true; break; }
        _menuListener.enabled = !other;
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (Active == this) Active = null;
    }
}
