using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Streams exported static geometry without changing the base scene or its lighting.
/// Terrain, horizon geometry and reference-sensitive objects belong in the base scene.
/// Bounds must cover the complete geometry in each cell, including its vertical extent.
/// </summary>
[DefaultExecutionOrder(-200)]
[DisallowMultipleComponent]
public sealed class RegionSceneStreamer : MonoBehaviour
{
    [Serializable]
    public sealed class Cell
    {
        public string scenePath;
        public Bounds bounds;

        public Cell() { }
        public Cell(string scenePath, Bounds bounds)
        {
            this.scenePath = scenePath;
            this.bounds = bounds;
        }
    }

    public string regionId;
    public int exportVersion;
    public Cell[] cells = Array.Empty<Cell>();
    public float loadDistanceM = 500f;
    public float unloadDistanceM = 800f;
    public float criticalDistanceM = 180f;
    public float updateIntervalSeconds = 0.25f;

    private sealed class State
    {
        public Cell cell;
        public int attempts;
        public float retryAt;
        public string error;
        public Scene scene;
    }

    private const int MaxAttempts = 3;
    private readonly List<State> _states = new List<State>();
    private readonly Dictionary<int, Vector3> _preparations = new Dictionary<int, Vector3>();
    private RideBootstrap _boot;
    private Scene _baseScene;
    private bool _initialized;
    private bool _pumping;
    private bool _destroyed;
    private bool _ownsGate;
    private bool _ownsTimeScale;
    private float _previousTimeScale;
    private int _requestSerial;
    private float _nextUpdate;
    private string _gateReason;
    private State _failedPreparation;
    private GameObject _loadingOverlay;
    private Text _loadingText;

    public bool IsBusy => _pumping || _preparations.Count != 0;

    /// <summary>Terminal failures in the currently requested load set; retries use unscaled time.</summary>
    public string Failure
    {
        get
        {
            Initialize();
            Vector3 position = Position();
            foreach (var state in _states)
                if (state.attempts >= MaxAttempts && !Loaded(state) && Wanted(state, position))
                    return state.error;
            if (_failedPreparation != null && !Loaded(_failedPreparation))
                return _failedPreparation.error;
            return null;
        }
    }

    private void Awake() => Initialize();

    private void Initialize()
    {
        if (_initialized) return;
        _initialized = true;
        _boot = GetComponent<RideBootstrap>();
        _baseScene = gameObject.scene;
        _gateReason = "region-streaming-" + GetInstanceID();
        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var cell in cells ?? Array.Empty<Cell>())
        {
            var state = new State { cell = cell };
            if (cell == null || string.IsNullOrWhiteSpace(cell.scenePath) ||
                cell.scenePath == _baseScene.path || !paths.Add(cell.scenePath))
            {
                state.attempts = MaxAttempts;
                state.error = "Invalid or duplicate streaming cell in " + regionId;
            }
            else state.scene = SceneManager.GetSceneByPath(cell.scenePath);
            _states.Add(state);
        }
    }

    /// <summary>
    /// Registers a preload request with the same pump used by Update. Concurrent callers
    /// retain their own requested positions; no competing scene operations are started.
    /// Returns only when the full nearby set is loaded, or a terminal failure is exposed.
    /// </summary>
    public IEnumerator Prepare(Vector3 position)
    {
        Initialize();
        int request = ++_requestSerial;
        _failedPreparation = null;
        _preparations.Add(request, position);
        try
        {
            while (!_destroyed && isActiveAndEnabled && !ReadyAt(position))
            {
                if (FailedAt(position)) yield break;
                EnsurePump();
                yield return null;
            }
        }
        finally { _preparations.Remove(request); }
    }

    public bool ReadyAt(Vector3 position)
    {
        Initialize();
        foreach (var state in _states)
            if (Near(state, position, LoadRadius) && !Loaded(state)) return false;
        return true;
    }

    private float LoadRadius => Mathf.Max(0f, loadDistanceM, criticalDistanceM);
    private float UnloadRadius => Mathf.Max(LoadRadius, unloadDistanceM);

    private Vector3 Position()
    {
        if (_boot != null)
        {
            if (_boot.routeFollowing && _boot.session != null && _boot.session.Course != null)
                return _boot.session.WorldPosition;
            if (_boot.rider != null) return _boot.rider.position;
        }
        return transform.position;
    }

    private static bool Near(State state, Vector3 position, float radius) =>
        state.cell == null || state.cell.bounds.SqrDistance(position) <= radius * radius;

    private static bool Loaded(State state)
    {
        return state.scene.IsValid() && state.scene.isLoaded;
    }

    private bool Wanted(State state, Vector3 position)
    {
        if (Near(state, position, LoadRadius)) return true;
        foreach (var requested in _preparations.Values)
            if (Near(state, requested, LoadRadius)) return true;
        return false;
    }

    private bool FailedAt(Vector3 position)
    {
        foreach (var state in _states)
            if (Near(state, position, LoadRadius) && !Loaded(state) && state.attempts >= MaxAttempts)
            {
                _failedPreparation = state;
                return true;
            }
        return false;
    }

    private void Update()
    {
        // RideBootstrap's number-key seeks run at -300; RideSession ticks at -100.
        // Check every frame so a teleport cannot advance simulation over absent geometry.
        UpdateGate();
        if (Time.unscaledTime < _nextUpdate) return;
        _nextUpdate = Time.unscaledTime + Mathf.Max(0.05f, updateIntervalSeconds);
        EnsurePump();
    }

    private void UpdateGate()
    {
        bool missing = false;
        Vector3 position = Position();
        foreach (var state in _states)
            if (Near(state, position, Mathf.Max(0f, criticalDistanceM)) && !Loaded(state))
            { missing = true; break; }
        if (!missing) { ReleaseGate(); return; }
        // This project's gate has one owner, not reference-counted locks. Never replace
        // selection/countdown/shop/race ownership and never restore someone else's state.
        if (!RideInputGate.Locked)
        {
            RideInputGate.Lock(_gateReason);
            _ownsGate = true;
        }
        else if (RideInputGate.Reason != _gateReason)
        {
            _ownsGate = false;
            // A menu/race/pause owner took over. It now owns the clock as well;
            // restoring our saved scale would unpause its screen.
            _ownsTimeScale = false;
        }
        if (_ownsGate && _boot != null && !_boot.routeFollowing && !_ownsTimeScale)
        {
            // Free-roam controllers do not all consult RideInputGate. Freeze their
            // scaled movement too, while async loading and the status UI stay unscaled.
            _previousTimeScale = Time.timeScale;
            _ownsTimeScale = true;
            Time.timeScale = 0f;
        }
        ShowLoading(_ownsGate);
    }

    private void ReleaseGate()
    {
        if (_ownsGate && RideInputGate.Locked && RideInputGate.Reason == _gateReason)
        {
            // Restore only our own zero; leave an explicitly changed clock alone.
            if (_ownsTimeScale && Time.timeScale == 0f) Time.timeScale = _previousTimeScale;
            RideInputGate.Unlock();
        }
        _ownsTimeScale = false;
        _ownsGate = false;
        ShowLoading(false);
    }

    private void ShowLoading(bool show)
    {
        if (!show)
        {
            if (_loadingOverlay != null && _loadingOverlay.activeSelf) _loadingOverlay.SetActive(false);
            return;
        }
        if (_loadingOverlay == null)
        {
            _loadingOverlay = new GameObject("Region Streaming Status", typeof(Canvas), typeof(CanvasScaler));
            _loadingOverlay.transform.SetParent(transform, false);
            var canvas = _loadingOverlay.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 900;
            var scaler = _loadingOverlay.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            var panel = new GameObject("Loading Card", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(_loadingOverlay.transform, false);
            var rect = (RectTransform)panel.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(720, 112);
            panel.GetComponent<Image>().color = HudKit.Shade;
            panel.GetComponent<Image>().raycastTarget = false;
            _loadingText = HudKit.Label(rect, "Status", "", 25, HudKit.Chalk, TextAnchor.MiddleCenter);
            var textRect = (RectTransform)_loadingText.transform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(24, 12);
            textRect.offsetMax = new Vector2(-24, -12);
            _loadingText.raycastTarget = false;
        }
        if (!_loadingOverlay.activeSelf) _loadingOverlay.SetActive(true);
        _loadingText.text = string.IsNullOrEmpty(Failure)
            ? "LOADING NEARBY ROADS AND SCENERY…"
            : "SCENERY COULD NOT LOAD\nThe ride is paused.";
    }

    private void EnsurePump()
    {
        if (_pumping || _destroyed || !isActiveAndEnabled) return;
        // Set before StartCoroutine, since a pump with no work can complete synchronously.
        _pumping = true;
        StartCoroutine(Pump());
    }

    private IEnumerator Pump()
    {
        try
        {
            while (!_destroyed && isActiveAndEnabled)
            {
                Vector3 position = Position();
                State next = null;
                float nearest = float.PositiveInfinity;
                bool waitingForLoad = false;
                foreach (var state in _states)
                {
                    if (!Wanted(state, position) || Loaded(state)) continue;
                    // Retain existing cells during retries/failure so a partial load does
                    // not also erase the last usable surroundings.
                    waitingForLoad = true;
                    if (state.attempts >= MaxAttempts || Time.unscaledTime < state.retryAt) continue;
                    float distance = state.cell.bounds.SqrDistance(position);
                    foreach (var requested in _preparations.Values)
                        distance = Mathf.Min(distance, state.cell.bounds.SqrDistance(requested));
                    if (distance < nearest) { nearest = distance; next = state; }
                }
                if (next != null)
                {
                    yield return Load(next);
                    continue; // Recompute position after every operation, including teleports.
                }
                if (waitingForLoad) yield break;

                State far = null;
                foreach (var state in _states)
                {
                    if (!Loaded(state) || Wanted(state, position) || Near(state, position, UnloadRadius)) continue;
                    bool retain = false;
                    foreach (var requested in _preparations.Values)
                        if (Near(state, requested, UnloadRadius)) { retain = true; break; }
                    if (!retain) { far = state; break; }
                }
                if (far == null) yield break;
                AsyncOperation operation = null;
                try { operation = SceneManager.UnloadSceneAsync(far.scene); }
                catch (Exception error) { Debug.LogWarning("[streaming] unload: " + error.Message, this); }
                if (operation == null) yield break;
                while (!operation.isDone) yield return null;
            }
        }
        finally { _pumping = false; }
    }

    private IEnumerator Load(State state)
    {
        state.attempts++;
        AsyncOperation operation = null;
        try
        {
            if (!Application.CanStreamedLevelBeLoaded(state.cell.scenePath))
                state.attempts = MaxAttempts;
            else operation = SceneManager.LoadSceneAsync(state.cell.scenePath, LoadSceneMode.Additive);
        }
        catch (Exception error) { state.error = error.Message; }
        if (operation != null)
        {
            // Unity scene operations cannot be cancelled. A Single transition may destroy
            // this host before an additive operation completes; remove its orphan if needed.
            string path = state.cell.scenePath;
            operation.completed += _ =>
            {
                // Host lifetime is authoritative: a native Scene handle may be reused
                // by a later Single load, but that cannot revive this scene-owned host.
                if (this != null && !_destroyed) return;
                var orphan = SceneManager.GetSceneByPath(path);
                if (orphan.IsValid() && orphan.isLoaded) SceneManager.UnloadSceneAsync(orphan);
            };
            while (!operation.isDone) yield return null;
        }
        if (_destroyed) yield break;
        if (operation != null) state.scene = SceneManager.GetSceneByPath(state.cell.scenePath);
        if (Loaded(state))
        {
            state.attempts = 0;
            state.error = null;
        }
        else
        {
            state.error = "Unable to load " + state.cell.scenePath +
                          (string.IsNullOrEmpty(state.error) ? "" : ": " + state.error);
            state.retryAt = Time.unscaledTime + Mathf.Min(8f, Mathf.Pow(2f, state.attempts - 1));
            Debug.LogError("[streaming] " + state.error + " (attempt " + state.attempts + ")", this);
        }
    }

    private void OnDisable() => ReleaseGate();

    private void OnDestroy()
    {
        _destroyed = true;
        ReleaseGate();
        // Single scene loads unload all additive cells themselves. Do not enqueue extra
        // unloads here: that would race the incoming region's initial preparation.
    }
}
