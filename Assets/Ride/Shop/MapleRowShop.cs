using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Maple Row: the boutique street on the Maple City course (route metres 450-700).
///
/// UX (user brief 2026-09-25):
///  * Riding past a storefront shows "ARDENT CYCLE CLUB . on your right . Press [B] to enter".
///    Stores are found in the scene ("&lt;BRAND&gt; Flagship" under "Maple Row Boutiques", built by
///    MapleRowBoutiques.cs) and projected onto the course, so the prompt always matches the
///    street that is actually built.
///  * [B] stops the ride, the camera swings round, and Kuro unclips, puts a foot down and steps off
///    the bike (<see cref="KuroDismount"/>). Then the store screen takes over.
///  * The store is full screen: a live 3D fitting room (<see cref="ShopStudio"/>: a clone of
///    YOUR rider and bike, turning on a plinth) wearing the item you are looking at over what you
///    already own, with the camera framing the outfit, helmet, wheel or head unit. The item list,
///    price and BUY / EQUIP are on the right.
///  * [B] or [Esc] leaves: fade back to the street, Kuro gets back on, and the ride resumes.
///  * Only on Maple City courses. Riding earns <see cref="coinsPerKm"/> MapleCoins everywhere.
/// </summary>
[DisallowMultipleComponent]
public sealed class MapleRowShop : MonoBehaviour
{
    public RideSession session;

    [Header("Maple Row (Maple City route metres)")]
    public float streetStartM = 450f;
    public float streetEndM = 700f;
    public float alertFromM = 250f;
    [Tooltip("How close (route metres) to a store's door the [B] prompt appears.")]
    public float doorRangeM = 18f;
    public KeyCode shopKey = KeyCode.B;

    [Header("Economy")]
    public int coinsPerKm = 25;
    [Tooltip("PROVISIONAL: MapleCoins for each checkpoint reached, once per checkpoint per lap.")]
    public int checkpointBonus = 15;

    private const string GateReason = "maple-row-shop";
    private static readonly Color Gold = new Color(0.886f, 0.745f, 0.431f, 1f);
    private static readonly Color Velvet = new Color(0.024f, 0.027f, 0.035f, 0.92f);
    private static readonly Color Row = new Color(1f, 1f, 1f, 0.06f);
    private static readonly Color RowOn = new Color(0.886f, 0.745f, 0.431f, 0.20f);

    public static readonly Dictionary<string, string> StoreName = new Dictionary<string, string>
    {
        { "ARDENT", "ARDENT CYCLE CLUB" }, { "HALCYON", "HALCYON" }, { "ALPENTEK", "ALPENTEK" },
        { "CARBONFORGE", "CARBONFORGE SUPERBIKE FRAMES" }, { "APEX", "APEX INSTRUMENTS" }, { "AEROLITE", "AEROLITE HELMETS" },
    };

    public sealed class Store { public string Brand; public float Station; public int Side; }   // Side +1 = rider's right
    public readonly List<Store> Stores = new List<Store>();

    private Canvas _canvas;
    private RectTransform _toast, _shop, _itemsRoot;
    private Text _toastTitle, _toastBody, _coins, _brandTitle, _brandTag, _status, _detailName, _detailBlurb, _detailPrice, _actionText;
    private Image _fade;
    private Image _counterBg, _counterEdge, _brandBar, _actionBg;   // store-themed (BrandAccent)
    private RawImage _view;
    private readonly List<(ShopItem item, Image bg, Text state)> _cards = new List<(ShopItem, Image, Text)>();
    private int _brand, _item;
    private bool _open, _busy;
    private float _earnedM, _lastTotalM = -1f, _statusUntil;
    private string _storesForCourse;
    private ShopStudio _studio;
    private KuroDismount _dismount;
    private KuroFollowCamera _follow;

    // Earn feedback (2026-09-25 refinement). Coins used to arrive silently, so nothing told the
    // rider that riding pays for the shop. A small "+25 MC" pop now shows on ANY course.
    private Text _coinPop;
    private float _coinPopAt = -99f;
    private int _coinPopSum;
    private RouteDirector _director;
    private float _directorSeekAt;
    private readonly HashSet<string> _bonusPaid = new HashSet<string>();
    private const float CoinPopSeconds = 2.4f;

    public bool IsOpen => _open;
    public bool Busy => _busy;
    public string CurrentBrand => ShopCatalog.Brands[_brand];

    /// <summary>True on a Maple City course (the only region with the street).</summary>
    public bool InMapleCity =>
        session != null && session.Graph != null &&
        session.Graph.RegionOfCourse(session.courseId) == RegionCatalog.MapleCity;

    public bool OnStreet => InMapleCity && session.DistanceM >= streetStartM && session.DistanceM <= streetEndM;

    private void Start()
    {
        if (session == null) session = GetComponent<RideSession>();
        Build();
        PlayerWardrobe.Changed += RefreshShop;
        PlayerWardrobe.CoinsEarned += OnCoinsEarned;
        SetOpen(false);
    }

    private void OnDestroy()
    {
        PlayerWardrobe.Changed -= RefreshShop;
        PlayerWardrobe.CoinsEarned -= OnCoinsEarned;
        if (_director != null) _director.CheckpointReached -= OnCheckpoint;
        if (_open && RideInputGate.Reason == GateReason) RideInputGate.Unlock();
        if (_studio != null) Destroy(_studio.gameObject);
    }

    private void Update()
    {
        if (session == null) return;
        EarnCoins();
        TickCoinPop();
        if (_director == null && Time.unscaledTime > _directorSeekAt)
        {
            _directorSeekAt = Time.unscaledTime + 1f;
            _director = FindFirstObjectByType<RouteDirector>();
            if (_director != null) _director.CheckpointReached += OnCheckpoint;
        }
        if (_busy) return;
        if (RaceDirector.Busy) { if (_toast != null && _toast.gameObject.activeSelf) _toast.gameObject.SetActive(false); return; }

        if (_open)
        {
            HandleShopKeys();
            if (_status != null && _statusUntil > 0f && Time.unscaledTime > _statusUntil)
            { _status.text = Hint; _status.color = HudKit.Chalk; _statusUntil = 0f; }
            return;
        }

        bool maple = InMapleCity;
        if (maple) EnsureStores();
        float d = session.DistanceM;
        bool approaching = maple && d >= alertFromM && d < streetStartM;
        bool onStreet = maple && d >= streetStartM && d <= streetEndM;
        _toast.gameObject.SetActive((approaching || onStreet) && !session.Finished);
        Store optA = null, optB = null;
        if (onStreet && !session.Finished)
        {
            var d0 = DoorAt(d, out var ac0);
            optA = d0; optB = ac0;
        }
        SetOptions(optA, optB);
        if (approaching)
        {
            _toastTitle.text = $"MAPLE ROW  .  {streetStartM - d:0} m";
            _toastBody.text = "Six cycling boutiques on both sides of the road. When you reach a door, click a boutique.";
        }
        else if (onStreet)
        {
            var door = DoorAt(d, out var across);
            if (door != null)
            {
                _toastTitle.text = "MAPLE ROW";
                _toastBody.text = $"Click a boutique to go in.   <color=#E2BE6E>{PlayerWardrobe.Coins:N0} MC</color>" +
                                  (ShopCatalog.BrandTagline.TryGetValue(door.Brand, out var tag) && across == null ? $"\n<size=17><i>{tag}</i></size>" : "");
                if (Input.GetKeyDown(shopKey) && !RideInputGate.Locked) StartCoroutine(EnterStore(door.Brand));
            }
            else
            {
                var next = NextStoreAhead(d);
                _toastTitle.text = "MAPLE ROW";
                _toastBody.text = next != null
                    ? $"Next: <b>{StoreName[next.Brand]}</b> in {next.Station - d:0} m on your {(next.Side > 0 ? "right" : "left")}"
                    : "End of the street. See you next lap.";
            }
        }
    }

    private readonly RectTransform[] _optBtn = new RectTransform[2];
    private readonly Text[] _optText = new Text[2];
    private readonly string[] _optBrand = new string[2];

    private void ClickOption(int i)
    {
        if (_busy || _open || string.IsNullOrEmpty(_optBrand[i]) || RideInputGate.Locked) return;
        StartCoroutine(EnterStore(_optBrand[i]));
    }

    private void SetOptions(Store a, Store b)
    {
        var stores = new[] { a, b };
        for (int i = 0; i < 2; i++)
        {
            bool on = stores[i] != null;
            _optBrand[i] = on ? stores[i].Brand : null;
            if (_optBtn[i] == null) continue;
            _optBtn[i].gameObject.SetActive(on);
            if (on) _optText[i].text = $"{i + 1}  {Title(stores[i].Brand)}  ({(stores[i].Side > 0 ? "right" : "left")})";
        }
    }

    private static string Title(string brand) => StoreName.TryGetValue(brand, out var n)
        ? System.Globalization.CultureInfo.InvariantCulture.TextInfo.ToTitleCase(n.ToLowerInvariant()) : brand;

    // ------------------------------------------------------------------ stores on the street

    private void EnsureStores()
    {
        string key = session.courseId + "|" + (session.Course != null ? session.Course.Position.Length : 0);
        if (_storesForCourse == key && Stores.Count > 0) return;
        _storesForCourse = key;
        Stores.Clear();
        var course = session.Course;
        var root = GameObject.Find("Maple Row Boutiques");
        foreach (var brand in ShopCatalog.Brands)
        {
            var flag = root != null ? GearVisuals.FindDeep(root.transform, brand + " Flagship") : null;
            if (flag != null && course != null && course.Position.Length > 1)
            {
                int best = -1; float bestD = float.MaxValue;
                for (int i = 0; i < course.Position.Length; i++)
                {
                    if (course.Distance[i] < streetStartM - 60f || course.Distance[i] > streetEndM + 60f) continue;
                    float dd = (course.Position[i] - flag.position).sqrMagnitude;
                    if (dd < bestD) { bestD = dd; best = i; }
                }
                if (best >= 0)
                {
                    var right = Vector3.Cross(Vector3.up, course.Tangent[best]).normalized;
                    Stores.Add(new Store { Brand = brand, Station = course.Distance[best],
                                           Side = Vector3.Dot(flag.position - course.Position[best], right) >= 0f ? 1 : -1 });
                    continue;
                }
            }
            // fallback: MapleRowBoutiques' layout (right: ARDENT, CARBONFORGE, APEX; left: HALCYON, ALPENTEK, AEROLITE)
            int side = brand == "ARDENT" || brand == "CARBONFORGE" || brand == "APEX" ? 1 : -1;
            int slot = System.Array.IndexOf(side > 0 ? new[] { "ARDENT", "CARBONFORGE", "APEX" }
                                                     : new[] { "HALCYON", "ALPENTEK", "AEROLITE" }, brand);
            Stores.Add(new Store { Brand = brand, Station = Mathf.Lerp(streetStartM + 40f, streetEndM - 40f, (slot + 0.5f) / 3f) + (side > 0 ? 0f : 10f), Side = side });
        }
        Stores.Sort((a, b) => a.Station.CompareTo(b.Station));
        Debug.Log("[shop] Maple Row doors: " + string.Join(", ", Stores.ConvertAll(s => $"{s.Brand} {s.Station:0} m {(s.Side > 0 ? "R" : "L")}")));
    }

    /// <summary>The door [B] would enter at route metre <paramref name="d"/>: of the stores within
    /// <see cref="doorRangeM"/>, the one on the side of the road the rider is riding on (the
    /// flagships face each other in pairs). <paramref name="across"/> is the other one, if any.</summary>
    public Store DoorAt(float d, out Store across)
    {
        across = null;
        Store right = null, left = null;
        foreach (var s in Stores)
        {
            if (Mathf.Abs(s.Station - d) > doorRangeM) continue;
            if (s.Side > 0) { if (right == null || Mathf.Abs(s.Station - d) < Mathf.Abs(right.Station - d)) right = s; }
            else if (left == null || Mathf.Abs(s.Station - d) < Mathf.Abs(left.Station - d)) left = s;
        }
        if (right == null || left == null) return right ?? left;
        bool onLeft = RiderSide() < 0;
        across = onLeft ? right : left;
        return onLeft ? left : right;
    }

    /// <summary>+1 if the rider is right of the course centreline, -1 if left.</summary>
    public int RiderSide()
    {
        var rider = Rider();
        var course = session != null ? session.Course : null;
        if (rider == null || course == null || course.Position.Length < 2) return 1;
        float d = session.DistanceM;
        int i = 0;
        while (i < course.Distance.Length - 1 && course.Distance[i + 1] < d) i++;
        var right = Vector3.Cross(Vector3.up, course.Tangent[i]).normalized;
        return Vector3.Dot(rider.position - course.Position[i], right) >= -0.05f ? 1 : -1;
    }

    public Store NearestStore(float d, out float gap)
    {
        Store best = null; gap = float.MaxValue;
        foreach (var s in Stores)
            if (Mathf.Abs(s.Station - d) < Mathf.Abs(gap)) { gap = s.Station - d; best = s; }
        return best;
    }

    private Store NextStoreAhead(float d)
    {
        foreach (var s in Stores) if (s.Station > d + doorRangeM) return s;
        return null;
    }

    private void EarnCoins()
    {
        float t = session.TotalDistanceM;
        if (_lastTotalM < 0f || t < _lastTotalM) { _lastTotalM = t; return; }   // reset / seek back
        float step = t - _lastTotalM;
        _lastTotalM = t;
        if (step > 200f) return;                     // a jump (keys 1-5), not riding
        _earnedM += step;
        if (_earnedM >= 1000f && coinsPerKm > 0)
        {
            int km = Mathf.FloorToInt(_earnedM / 1000f);
            _earnedM -= km * 1000f;
            PlayerWardrobe.AddCoins(km * coinsPerKm);
        }
    }

    /// <summary>Checkpoint bonus, paid once per checkpoint per lap per course (so hovering
    /// back and forth over a pin cannot farm it).</summary>
    private void OnCheckpoint(CourseCheckpoint cp)
    {
        if (checkpointBonus <= 0 || session == null) return;
        string key = $"{session.courseId}|{session.LapIndex}|{cp.Name}|{cp.Distance:0}";
        if (_bonusPaid.Add(key)) PlayerWardrobe.AddCoins(checkpointBonus);
    }

    private void OnCoinsEarned(int amount)
    {
        if (_coinPop == null) return;
        // Pops that land close together add up instead of flickering.
        _coinPopSum = Time.unscaledTime - _coinPopAt < CoinPopSeconds ? _coinPopSum + amount : amount;
        _coinPopAt = Time.unscaledTime;
        _coinPop.text = $"+{_coinPopSum:N0} MC";
    }

    private void TickCoinPop()
    {
        if (_coinPop == null) return;
        float age = Time.unscaledTime - _coinPopAt;
        bool show = age < CoinPopSeconds && !_open;
        if (_coinPop.gameObject.activeSelf != show) _coinPop.gameObject.SetActive(show);
        if (!show) return;
        float a = age < 0.2f ? age / 0.2f : 1f - Mathf.Clamp01((age - CoinPopSeconds + 0.6f) / 0.6f);
        var c = _coinPop.color; c.a = a; _coinPop.color = c;
        var rt = (RectTransform)_coinPop.transform;
        rt.anchoredPosition = new Vector2(0f, -250f + 14f * Mathf.Clamp01(age / CoinPopSeconds));
    }

    // ------------------------------------------------------------------ enter / leave

    private Transform Rider()
    {
        var boot = GetComponent<RideBootstrap>();
        if (boot != null && boot.rider != null) return boot.rider;
        var kit = FindFirstObjectByType<KitAppearance>();
        return kit != null ? kit.transform : null;
    }

    /// <summary>Stop, dismount, store screen. Player path ([B] at a door).</summary>
    public IEnumerator EnterStore(string brand)
    {
        if (_busy || _open) yield break;
        _busy = true;
        if (!RideInputGate.Locked) RideInputGate.Lock(GateReason);
        _toast.gameObject.SetActive(false);
        _brand = Mathf.Max(0, System.Array.IndexOf(ShopCatalog.Brands, brand));

        var rider = Rider();
        if (rider != null)
        {
            _dismount = rider.GetComponent<KuroDismount>() ?? rider.gameObject.AddComponent<KuroDismount>();
            var cam = GetComponent<RideBootstrap>() != null ? GetComponent<RideBootstrap>().rideCamera : Camera.main;
            _follow = cam != null ? cam.GetComponent<KuroFollowCamera>() : null;
            var store = Stores.Find(s => s.Brand == brand);
            int side = store != null ? store.Side : 1;
            if (_follow != null) _follow.enabled = false;
            _dismount.side = side;                  // step off toward the store
            StartCoroutine(CinematicCamera(cam, rider, side, _dismount.dismountSeconds));
            yield return _dismount.Dismount();
        }
        yield return Fade(1f, 0.35f);
        SetOpen(true);
        yield return Fade(0f, 0.35f);
        _busy = false;
    }

    /// <summary>Close the store, back to the street, get on and ride.</summary>
    public IEnumerator LeaveStore()
    {
        if (_busy || !_open) yield break;
        _busy = true;
        yield return Fade(1f, 0.3f);
        SetOpen(false, keepGate: true);
        yield return Fade(0f, 0.3f);
        if (_dismount != null && _dismount.T > 0f) yield return _dismount.Remount();
        if (_follow != null) _follow.enabled = true;
        if (RideInputGate.Locked && RideInputGate.Reason == GateReason) RideInputGate.Unlock();
        _busy = false;
    }

    private IEnumerator CinematicCamera(Camera cam, Transform rider, int side, float seconds)
    {
        if (cam == null) yield break;
        Vector3 p0 = cam.transform.position; Quaternion r0 = cam.transform.rotation;
        float t = 0f;
        while (t < seconds + 0.4f && (_busy || _open))
        {
            t += Time.unscaledDeltaTime;
            // three-quarter front view from the kerb side the rider steps down to
            Vector3 goal = rider.position + rider.forward * 2.6f + rider.right * side * 1.9f + Vector3.up * 1.25f;
            Quaternion look = Quaternion.LookRotation(rider.position + Vector3.up * 0.75f - goal, Vector3.up);
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 1.1f));
            cam.transform.position = Vector3.Lerp(p0, goal, k);
            cam.transform.rotation = Quaternion.Slerp(r0, look, k);
            yield return null;
        }
    }

    private IEnumerator Fade(float to, float seconds)
    {
        if (_fade == null) yield break;
        _fade.gameObject.SetActive(true);
        float from = _fade.color.a, t = 0f;
        while (t < seconds)
        {
            t += Time.unscaledDeltaTime;
            var c = _fade.color; c.a = Mathf.Lerp(from, to, t / seconds); _fade.color = c;
            yield return null;
        }
        var e = _fade.color; e.a = to; _fade.color = e;
        _fade.gameObject.SetActive(to > 0.001f);
    }

    /// <summary>Opens/closes the store screen immediately (tests and the enter/leave sequences).</summary>
    public void SetOpen(bool open) => SetOpen(open, keepGate: false);

    private void SetOpen(bool open, bool keepGate)
    {
        if (open == _open && _shop != null && _shop.gameObject.activeSelf == open) return;
        _open = open;
        _shop.gameObject.SetActive(open);
        if (open)
        {
            _toast.gameObject.SetActive(false);
            if (!RideInputGate.Locked) RideInputGate.Lock(GateReason);
            EnsureEventSystem();
            var rider = Rider();
            if (_studio == null && rider != null && Application.isPlaying)
            {
                _studio = ShopStudio.Create(rider);
                _view.texture = _studio.Texture;
            }
            _item = 0;
            BuildItems();
            RefreshShop();
            Debug.Log($"[shop] {StoreName[CurrentBrand]} opened at {session?.DistanceM:0} m, {PlayerWardrobe.Coins} MC");
        }
        else
        {
            if (_studio != null) { Destroy(_studio.gameObject); _studio = null; }
            if (_view != null) _view.texture = null;
            if (!keepGate && RideInputGate.Locked && RideInputGate.Reason == GateReason)
                RideInputGate.Unlock();
        }
    }

    /// <summary>For tests: open a given store directly.</summary>
    public void OpenStore(string brand)
    {
        _brand = Mathf.Max(0, System.Array.IndexOf(ShopCatalog.Brands, brand));
        if (_open) { _item = 0; BuildItems(); RefreshShop(); }
        else SetOpen(true);
    }

    public int ItemCount => _cards.Count;

    public ShopStudio Studio => _studio;

    private void HandleShopKeys()
    {
        if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(shopKey)) { StartCoroutine(LeaveStore()); return; }
        if (_cards.Count > 0)
        {
            if (Input.GetKeyDown(KeyCode.UpArrow)) Select((_item + _cards.Count - 1) % _cards.Count);
            if (Input.GetKeyDown(KeyCode.DownArrow)) Select((_item + 1) % _cards.Count);
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) Act(_cards[_item].item);
        }
    }

    public void Select(int i)
    {
        _item = Mathf.Clamp(i, 0, Mathf.Max(0, _cards.Count - 1));
        RefreshShop();
    }

    private void Act(ShopItem it)
    {
        if (it == null) return;
        if (!PlayerWardrobe.Owns(it.Id))
        {
            if (PlayerWardrobe.Buy(it))
            {
                PlayerWardrobe.ToggleEquip(it);       // bought = worn straight away
                Flash($"Bought and equipped {it.Brand} {it.Name}.", true);
            }
            else
            {
                int shortBy = PlayerWardrobe.PriceToPay(it) - PlayerWardrobe.Coins;
                Flash($"Not enough MapleCoins: {it.Name} needs {shortBy:N0} MC more" +
                      (coinsPerKm > 0 ? $" (about {Mathf.CeilToInt(shortBy / (float)coinsPerKm)} km of riding at {coinsPerKm} MC/km)." : "."), false);
            }
        }
        else
        {
            bool was = PlayerWardrobe.IsEquipped(it.Id);
            PlayerWardrobe.ToggleEquip(it);
            Flash(was ? $"Took off {it.Name}. Back to the stock kit for that slot." : $"Equipped {it.Name}.", true);
        }
    }

    /// <summary>Status line over the fitting room: store accent for good news, ember for a refusal.</summary>
    private void Flash(string msg, bool ok)
    {
        if (_status == null) return;
        _status.text = msg;
        _status.color = ok ? Accent() : HudKit.Ember;
        _statusUntil = Time.unscaledTime + 3f;
    }

    private const string Hint = "[Up/Down] choose   [Enter] buy / equip   [Q/E] turn   [B] or [Esc] back to the bike";

    // ------------------------------------------------------------------ UI

    private void Build()
    {
        var go = new GameObject("Maple Row Shop UI", typeof(RectTransform), typeof(Canvas),
                                typeof(CanvasScaler), typeof(GraphicRaycaster));
        go.transform.SetParent(transform, false);
        _canvas = go.GetComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 120;               // above the ride HUD (100)
        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        var root = (RectTransform)go.transform;

        // --- street prompt (upper centre, under the route bar)
        _toast = HudKit.SoftPanel(root, "Maple Row Alert", Velvet);
        HudKit.Corner(_toast, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -132f), new Vector2(720f, 156f));
        var tEdge = HudKit.SoftPanel(_toast, "Edge", HudKit.WithAlpha(Gold, 0.55f), frame: true);
        HudKit.Place(tEdge, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        _toastTitle = HudKit.Label(_toast, "Title", "", 19, Gold, TextAnchor.UpperCenter, FontStyle.Bold);
        HudKit.Place((RectTransform)_toastTitle.transform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -40f), new Vector2(0f, -12f));
        _toastBody = HudKit.Label(_toast, "Body", "", 20, HudKit.Chalk, TextAnchor.UpperCenter);
        _toastBody.supportRichText = true;
        HudKit.Place((RectTransform)_toastBody.transform, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(16f, 58f), new Vector2(-16f, -44f));
        HudKit.AddShadow(_toastBody);
        EnsureEventSystem();
        for (int k = 0; k < 2; k++)
        {
            int idx = k;
            var b = Button(_toast, "Option " + (k + 1), "", () => ClickOption(idx));
            HudKit.Place(b, new Vector2(k == 0 ? 0.03f : 0.52f, 0f), new Vector2(k == 0 ? 0.48f : 0.97f, 0f), new Vector2(0f, 10f), new Vector2(0f, 50f));
            _optBtn[k] = b;
            _optText[k] = b.GetComponentInChildren<Text>();
            b.gameObject.SetActive(false);
        }

        // --- "+25 MC" earn pop, under the street prompt's slot
        _coinPop = HudKit.Label(root, "Coin Pop", "", 26, Gold, TextAnchor.MiddleCenter, FontStyle.Bold);
        HudKit.AddOutline(_coinPop);
        HudKit.Corner((RectTransform)_coinPop.transform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                      new Vector2(0f, -250f), new Vector2(320f, 40f));
        _coinPop.gameObject.SetActive(false);

        // --- the store: full screen
        _shop = HudKit.Panel(root, "Maple Row Shop", new Color(0.015f, 0.016f, 0.02f, 1f));
        HudKit.Place(_shop, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        _shop.GetComponent<Image>().raycastTarget = true;

        // 3D fitting room, left 64 %
        var viewGo = new GameObject("Fitting Room", typeof(RectTransform), typeof(RawImage));
        viewGo.transform.SetParent(_shop, false);
        _view = viewGo.GetComponent<RawImage>();
        _view.color = Color.white;
        HudKit.Place((RectTransform)viewGo.transform, new Vector2(0f, 0f), new Vector2(0.64f, 1f), Vector2.zero, Vector2.zero);
        var vign = HudKit.Panel((RectTransform)viewGo.transform, "Floor Shade", new Color(0f, 0f, 0f, 0.35f));
        HudKit.Place(vign, new Vector2(0f, 0f), new Vector2(1f, 0.12f), Vector2.zero, Vector2.zero);
        // top shade: the store title sat straight on bright sky / city (APEX close-up) and lost contrast
        var topShade = HudKit.Panel((RectTransform)viewGo.transform, "Top Shade", new Color(0f, 0f, 0f, 0.32f));
        HudKit.Place(topShade, new Vector2(0f, 0.84f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);

        _brandTitle = HudKit.Label(_shop, "Store", "", 44, Gold, TextAnchor.UpperLeft, FontStyle.Bold);
        HudKit.Place((RectTransform)_brandTitle.transform, new Vector2(0f, 1f), new Vector2(0.64f, 1f), new Vector2(48f, -104f), new Vector2(0f, -36f));
        HudKit.AddOutline(_brandTitle);
        _brandBar = HudKit.Panel(_shop, "Brand Bar", Gold).GetComponent<Image>();
        HudKit.Place((RectTransform)_brandBar.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(50f, -152f), new Vector2(210f, -147f));
        _brandTag = HudKit.Label(_shop, "Tagline", "", 20, HudKit.Chalk, TextAnchor.UpperLeft, FontStyle.Italic);
        HudKit.Place((RectTransform)_brandTag.transform, new Vector2(0f, 1f), new Vector2(0.64f, 1f), new Vector2(50f, -140f), new Vector2(0f, -104f));
        HudKit.AddOutline(_brandTag, 0.7f, 1.1f);

        // right column
        var col = HudKit.SoftPanel(_shop, "Counter", Velvet);
        HudKit.Place(col, new Vector2(0.64f, 0f), new Vector2(1f, 1f), Vector2.zero, Vector2.zero);
        _counterBg = col.GetComponent<Image>();
        var cEdge = HudKit.Panel(col, "Edge", HudKit.WithAlpha(Gold, 0.5f));
        _counterEdge = cEdge.GetComponent<Image>();
        HudKit.Place(cEdge, new Vector2(0f, 0f), new Vector2(0f, 1f), Vector2.zero, new Vector2(2f, 0f));

        var cap = HudKit.Label(col, "Cap", "M A P L E   R O W", 16, HudKit.ChalkSoft, TextAnchor.UpperLeft, FontStyle.Bold);
        HudKit.Place((RectTransform)cap.transform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(40f, -62f), new Vector2(-40f, -36f));
        _coins = HudKit.Label(col, "Coins", "", 28, HudKit.Chalk, TextAnchor.UpperRight, FontStyle.Bold);
        HudKit.Place((RectTransform)_coins.transform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(40f, -70f), new Vector2(-40f, -30f));

        _itemsRoot = HudKit.Rect(col, "Items");
        HudKit.Place(_itemsRoot, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(32f, -440f), new Vector2(-32f, -96f));

        _detailName = HudKit.Label(col, "Detail Name", "", 30, HudKit.Chalk, TextAnchor.UpperLeft, FontStyle.Bold);
        _detailName.horizontalOverflow = HorizontalWrapMode.Wrap;
        HudKit.Place((RectTransform)_detailName.transform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(40f, 470f), new Vector2(-40f, 560f));
        _detailBlurb = HudKit.Label(col, "Detail Blurb", "", 19, HudKit.ChalkSoft, TextAnchor.UpperLeft);
        _detailBlurb.horizontalOverflow = HorizontalWrapMode.Wrap;
        HudKit.Place((RectTransform)_detailBlurb.transform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(40f, 300f), new Vector2(-40f, 466f));
        _detailPrice = HudKit.Label(col, "Detail Price", "", 34, Gold, TextAnchor.UpperLeft, FontStyle.Bold);
        _detailPrice.supportRichText = true;
        HudKit.Place((RectTransform)_detailPrice.transform, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(40f, 236f), new Vector2(-40f, 292f));

        var act = Button(col, "Action", "", () => { if (_item < _cards.Count) Act(_cards[_item].item); });
        HudKit.Place(act, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(40f, 140f), new Vector2(-40f, 214f));
        _actionBg = act.GetComponent<Image>();
        _actionBg.color = HudKit.WithAlpha(Gold, 0.30f);
        _actionText = act.GetComponentInChildren<Text>();
        _actionText.fontSize = 24;

        var leave = Button(col, "Leave", "BACK TO THE BIKE  [B]", () => StartCoroutine(LeaveStore()));
        HudKit.Place(leave, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(40f, 70f), new Vector2(-40f, 124f));

        _status = HudKit.Label(_shop, "Status", Hint, 18, HudKit.Chalk, TextAnchor.LowerLeft);
        _status.horizontalOverflow = HorizontalWrapMode.Wrap;   // long "not enough MC" messages wrap instead of running under the counter
        HudKit.Place((RectTransform)_status.transform, new Vector2(0f, 0f), new Vector2(0.64f, 0f), new Vector2(48f, 24f), new Vector2(-20f, 76f));
        HudKit.AddOutline(_status, 0.8f, 1.2f);   // it sits over the rider's shoes/bike in the fitting room

        // fade (above everything)
        var fade = HudKit.Panel(root, "Fade", new Color(0f, 0f, 0f, 0f));
        HudKit.Place(fade, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        _fade = fade.GetComponent<Image>();
        _fade.raycastTarget = false;
        fade.gameObject.SetActive(false);

        HudSprites.SetLayerRecursively(go, HudSprites.UiLayer);
        BuildItems();
    }

    private void BuildItems()
    {
        if (_itemsRoot == null) return;
        for (int i = _itemsRoot.childCount - 1; i >= 0; i--)
        {
            var c = _itemsRoot.GetChild(i).gameObject;
            c.transform.SetParent(null, false);
            if (Application.isPlaying) Destroy(c); else DestroyImmediate(c);
        }
        _cards.Clear();
        string brand = ShopCatalog.Brands[_brand];
        var items = ShopCatalog.Items.FindAll(i => i.Brand == brand);
        // CARBONFORGE is first and foremost the frameset destination. Frames used to sit after
        // three wheelsets, so a rider opening the store saw only wheels and reasonably concluded
        // that Maple City had no frame shop. Keep every frame ahead of accessories.
        if (brand == "CARBONFORGE")
            items.Sort((a, b) => (a.Category == ShopCategory.Frame ? 0 : 1)
                .CompareTo(b.Category == ShopCategory.Frame ? 0 : 1));
        const float H = 96f, Gap = 12f;
        for (int i = 0; i < items.Count; i++)
        {
            var it = items[i];
            int idx = i;
            var card = Button(_itemsRoot, "Item " + it.Id, null, () => Select(idx));
            // colour chip (apparel/wheels) or tier chip (computer)
            // computers: Pro and Elite used to share one blank mint chip; now the tier is written on it
            var chip = HudKit.SoftPanel(card, "Chip", it.Category == ShopCategory.BikeComputer
                ? (it.ComputerTier >= 2 ? new Color(0.36f, 0.95f, 0.78f) : new Color(0.72f, 0.80f, 0.74f)) : it.Primary);
            HudKit.Place(chip, new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(14f, 14f), new Vector2(82f, -14f));
            if (it.Category == ShopCategory.BikeComputer)
            {
                var tier = HudKit.Label(chip, "Tier", it.ComputerTier >= 2 ? "ELITE" : "PRO", 14,
                                        new Color(0.05f, 0.07f, 0.08f, 1f), TextAnchor.MiddleCenter, FontStyle.Bold);
                HudKit.Place((RectTransform)tier.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            }
            else
            {
                var band = HudKit.Panel(chip, "Trim", it.Trim);
                HudKit.Place(band, new Vector2(0f, 0.40f), new Vector2(1f, 0.58f), Vector2.zero, Vector2.zero);
            }
            var nm = HudKit.Label(card, "Name", it.Name, 22, HudKit.Chalk, TextAnchor.UpperLeft, FontStyle.Bold);
            HudKit.Place((RectTransform)nm.transform, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(100f, 40f), new Vector2(-12f, -14f));
            var st = HudKit.Label(card, "State", "", 17, HudKit.ChalkSoft, TextAnchor.LowerLeft);
            st.supportRichText = true;
            HudKit.Place((RectTransform)st.transform, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(100f, 14f), new Vector2(-12f, -54f));
            _cards.Add((it, card.GetComponent<Image>(), st));
        }
        // "more" hints above / below the visible page (stores with more than MaxItemsPerBrand items)
        _moreUp = HudKit.Label(_itemsRoot, "More Up", "", 15, HudKit.ChalkSoft, TextAnchor.LowerRight, FontStyle.Bold);
        HudKit.Place((RectTransform)_moreUp.transform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, 2f), new Vector2(-6f, 22f));
        _moreDown = HudKit.Label(_itemsRoot, "More Down", "", 15, HudKit.ChalkSoft, TextAnchor.UpperRight, FontStyle.Bold);
        HudKit.Place((RectTransform)_moreDown.transform, new Vector2(0f, 1f), new Vector2(1f, 1f),
                     new Vector2(0f, -ShopCatalog.MaxItemsPerBrand * (H + Gap) - 20f), new Vector2(-6f, -ShopCatalog.MaxItemsPerBrand * (H + Gap) + 2f));
        LayoutCards();
        HudSprites.SetLayerRecursively(_itemsRoot.gameObject, HudSprites.UiLayer);
    }

    private Text _moreUp, _moreDown;

    /// <summary>Shows a page of <see cref="ShopCatalog.MaxItemsPerBrand"/> cards that keeps the
    /// selected one in view (the frameset atelier carries more than a dozen items).</summary>
    private void LayoutCards()
    {
        const float H = 96f, Gap = 12f;
        int page = ShopCatalog.MaxItemsPerBrand;
        int first = Mathf.Clamp(_item - page / 2, 0, Mathf.Max(0, _cards.Count - page));
        for (int i = 0; i < _cards.Count; i++)
        {
            var rt = (RectTransform)_cards[i].bg.transform;
            int row = i - first;
            bool show = row >= 0 && row < page;
            rt.gameObject.SetActive(show);
            if (show)
                HudKit.Place(rt, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -(row + 1) * H - row * Gap), new Vector2(0f, -row * (H + Gap)));
        }
        if (_moreUp != null) _moreUp.text = first > 0 ? $"{first} more  ^" : "";
        int below = _cards.Count - first - page;
        if (_moreDown != null) _moreDown.text = below > 0 ? $"{below} more  v   [Up/Down]" : "";
    }

    private static string CategoryLabel(ShopItem it) =>
        it.Category == ShopCategory.Wheels ? $"WHEELSET  .  {(1f - it.CdaScale) * 100f:0.#}% less drag"
      : it.Category == ShopCategory.BikeComputer ? (it.ComputerTier >= 2 ? "BIKE COMPUTER  .  Elite HUD" : "BIKE COMPUTER  .  Pro HUD")
      : it.Category == ShopCategory.Frame ? (it.CdaScale < 0.9995f ? $"FRAMESET  .  {(1f - it.CdaScale) * 100f:0.#}% less drag" : "FRAMESET  .  climbing")
      : it.Textured ? it.Category.ToString().ToUpperInvariant() + "  .  PRO"
      : it.Category.ToString().ToUpperInvariant();

    private Color Accent() =>
        ShopCatalog.BrandAccent.TryGetValue(ShopCatalog.Brands[_brand], out var c) ? c : Gold;

    /// <summary>What the item DOES in the game, in plain words. Wheels: the aero power saved at
    /// 35 km/h from CyclingPhysics' own cdA and air density (P = 0.5 rho CdA v^3), so the number
    /// matches equipmentCdaScale exactly. Everything else is honest about being cosmetic / HUD.</summary>
    private string EffectLine(ShopItem it)
    {
        string hex = ColorUtility.ToHtmlStringRGB(Accent());
        switch (it.Category)
        {
            case ShopCategory.Frame when it.CdaScale < 0.9995f:
            case ShopCategory.Wheels:
            {
                var ph = session != null ? session.physics : null;
                float cda = ph != null ? ph.cdA : 0.32f, rho = ph != null ? ph.airDensityKgM3 : 1.225f;
                float v = 35f / 3.6f;
                float watts = 0.5f * rho * cda * v * v * v * (1f - it.CdaScale);
                return $"<color=#{hex}>Ride effect: saves about {watts:0.#} W at 35 km/h (drag x{it.CdaScale:0.###}).</color>" +
                       (it.Category == ShopCategory.Frame ? $"\n<color=#{hex}>Replaces the frame and fork on your bike.</color>" : "");
            }
            case ShopCategory.Frame:
                return $"<color=#{hex}>Look only: replaces the frame and fork on your bike. No speed change.</color>";
            case ShopCategory.BikeComputer:
                return $"<color=#{hex}>Ride effect: adds a strip under your power readout. No speed change.</color>";
            default:
                return it.Textured ? $"<color=#{hex}>Look only: Kuro wears it on the road. No speed change.</color>"
                                   : $"<color=#{hex}>Look only: recolours your kit on the road. No speed change.</color>";
        }
    }

    private static string ReplacesLine(ShopItem it)
    {
        var worn = PlayerWardrobe.Equipped(it.Category);
        return worn == null || worn == it ? "" : $"\nReplaces your {worn.Name}.";
    }

    private void RefreshShop()
    {
        if (_shop == null) return;
        _coins.text = $"{PlayerWardrobe.Coins:N0} MC";
        string brand = ShopCatalog.Brands[_brand];
        _brandTitle.text = StoreName.TryGetValue(brand, out var sn) ? sn : brand;
        ShopCatalog.BrandTagline.TryGetValue(brand, out var tag);
        _brandTag.text = tag ?? "";
        // store identity: every store used the same gold; now each wears its brand accent
        var accent = Accent();
        _brandTitle.color = accent;
        if (_brandBar != null) _brandBar.color = ShopCatalog.BrandSecond.TryGetValue(brand, out var second) ? second : Gold;
        if (_counterEdge != null) _counterEdge.color = HudKit.WithAlpha(accent, 0.55f);
        if (_counterBg != null) _counterBg.color = new Color(Mathf.Lerp(Velvet.r, accent.r, 0.05f), Mathf.Lerp(Velvet.g, accent.g, 0.05f),
                                                             Mathf.Lerp(Velvet.b, accent.b, 0.05f), Velvet.a);
        if (_studio != null) _studio.SetAccent(accent);
        LayoutCards();
        for (int i = 0; i < _cards.Count; i++)
        {
            var (it, bg, state) = _cards[i];
            bg.color = i == _item ? HudKit.WithAlpha(accent, RowOn.a + 0.04f) : Row;
            state.text = CategoryLabel(it) + "   " + (!PlayerWardrobe.Owns(it.Id) ? $"<color=#E2BE6E>{it.Price:N0} MC</color>"
                        : PlayerWardrobe.IsEquipped(it.Id) ? "<color=#E2BE6E>WEARING</color>" : "OWNED");
        }
        if (_item < _cards.Count)
        {
            var it = _cards[_item].item;
            _detailName.text = it.Name;
            _detailBlurb.text = CategoryLabel(it) + "\n" + it.Blurb + "\n\n" + EffectLine(it) + ReplacesLine(it);
            int pay = PlayerWardrobe.PriceToPay(it);
            bool owned = PlayerWardrobe.Owns(it.Id);
            bool affordable = PlayerWardrobe.Coins >= pay;
            string accentHex = ColorUtility.ToHtmlStringRGB(accent);
            _detailPrice.color = owned ? accent : Gold;
            // QaFreePurchases: the old "1,600 MC  FREE (QA)" read like a sale; say plainly it is a test build
            _detailPrice.text = owned ? (PlayerWardrobe.IsEquipped(it.Id) ? "Wearing it now" : "In your wardrobe")
                              : pay == it.Price ? (affordable ? $"{it.Price:N0} MC"
                                                              : $"{it.Price:N0} MC  <size=20><color=#F96A51>need {pay - PlayerWardrobe.Coins:N0} more</color></size>")
                              : $"<color=#E5E4DF>{it.Price:N0} MC</color>  <size=20><color=#{accentHex}>free in this QA build</color></size>";
            _actionText.text = !owned ? (affordable ? "BUY AND WEAR  [Enter]" : "NOT ENOUGH MAPLECOINS")
                             : PlayerWardrobe.IsEquipped(it.Id) ? "TAKE OFF  [Enter]" : "WEAR  [Enter]";
            if (_actionBg != null)
                _actionBg.color = !owned && !affordable ? new Color(0.35f, 0.35f, 0.37f, 0.35f) : HudKit.WithAlpha(accent, 0.34f);
            if (_studio != null) _studio.TryOn(it);
        }
    }

    private static RectTransform Button(Transform parent, string name, string label, UnityEngine.Events.UnityAction onClick)
    {
        var rt = HudKit.SoftPanel(parent, name, Row);
        var img = rt.GetComponent<Image>();
        img.raycastTarget = true;
        var b = rt.gameObject.AddComponent<Button>();
        b.targetGraphic = img;
        var cb = b.colors;
        cb.highlightedColor = new Color(1.25f, 1.2f, 1.1f, 1f);
        cb.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
        b.colors = cb;
        b.onClick.AddListener(onClick);
        if (label != null || name == "Action")
        {
            var t = HudKit.Label(rt, "Label", label ?? "", 19, HudKit.Chalk, TextAnchor.MiddleCenter, FontStyle.Bold);
            t.supportRichText = true;
            HudKit.Place((RectTransform)t.transform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        }
        return rt;
    }

    private static void EnsureEventSystem()
    {
        if (EventSystem.current != null || !Application.isPlaying) return;
        var es = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        DontDestroyOnLoad(es);
    }
}
