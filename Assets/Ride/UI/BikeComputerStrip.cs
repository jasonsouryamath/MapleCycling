using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Extra readouts unlocked by the APEX bike computers from Maple Row (RideHud.computerTier):
///   tier 1 (APEX Pro)   W/kg + a live power-zone bar (Z1-Z6 against FTP)
///   tier 2 (APEX Elite) the above + average power + a climb readout (grade, ascent)
/// A smoked-glass pill directly under the compact telemetry pill. Hidden at tier 0 and
/// whenever the ride widgets are hidden (the pill it sits next to is inactive).
/// </summary>
[DisallowMultipleComponent]
public sealed class BikeComputerStrip : MonoBehaviour
{
    public RideHud hud;

    // Coggan-style zone ceilings as a fraction of FTP, and their colours.
    private static readonly float[] ZoneTop = { 0.55f, 0.75f, 0.90f, 1.05f, 1.20f, 9f };
    private static readonly Color[] ZoneCol =
    {
        new Color(0.62f, 0.66f, 0.70f), new Color(0.26f, 0.56f, 0.96f), new Color(0.25f, 0.78f, 0.45f),
        new Color(0.98f, 0.80f, 0.26f), new Color(0.98f, 0.52f, 0.22f), new Color(0.93f, 0.26f, 0.30f),
    };

    private RectTransform _pill;
    private Canvas _builtFor;
    private int _builtTier = -1;
    private Text _wkg, _zone, _avg, _climb, _brand;
    private readonly Image[] _segs = new Image[6];
    private double _wattSeconds;
    private float _seconds;
    private int _runSerial = -1;

    private void LateUpdate()
    {
        if (hud == null || hud.session == null || hud.devices == null) return;
        var s = hud.session;
        var tm = hud.devices.Telemetry;

        if (s.RunSerial != _runSerial) { _runSerial = s.RunSerial; _wattSeconds = 0; _seconds = 0; }
        if (!RideInputGate.Locked && !s.Finished && s.SpeedMps > 0.3f)
        {
            _wattSeconds += tm.Watts * Time.deltaTime;
            _seconds += Time.deltaTime;
        }

        int tier = hud.computerTier;
        if (hud.Canvas != _builtFor || tier != _builtTier) Build(tier);
        if (_pill == null) return;
        var telemetry = hud.Canvas.transform.Find("Telemetry Card");
        _pill.gameObject.SetActive(telemetry == null || telemetry.gameObject.activeInHierarchy);
        if (!_pill.gameObject.activeSelf) return;

        float ftp = Mathf.Max(1f, hud.devices.ftpWatts);
        float frac = tm.Watts / ftp;
        int z = 0;
        while (z < 5 && frac > ZoneTop[z]) z++;
        _wkg.text = $"{tm.Watts / Mathf.Max(1f, hud.devices.riderMassKg):0.0} W/kg";
        _zone.text = $"Z{z + 1}";
        _zone.color = ZoneCol[z];
        for (int i = 0; i < 6; i++)
            _segs[i].color = HudKit.WithAlpha(ZoneCol[i], i == z ? 1f : 0.22f);
        if (_avg != null) _avg.text = $"AVG {(_seconds > 1f ? _wattSeconds / _seconds : 0):0} W";
        if (_climb != null)
        {
            float g = s.DisplayGradePct;
            _climb.text = $"{(g > 0.05f ? "+" : "")}{g:0.0}%  .  +{s.AscentM:0} m";
        }
    }

    private void Build(int tier)
    {
        if (_pill != null) Destroy(_pill.gameObject);
        _pill = null;
        _wkg = _zone = _avg = _climb = null;
        if (hud.Canvas != _builtFor) _draftShift = 0f;   // a rebuilt HUD has a fresh draft card
        _builtFor = hud.Canvas;
        _builtTier = tier;
        ShiftDraft(tier > 0 ? -60f : 0f);
        if (tier <= 0 || hud.Canvas == null) return;

        float w = tier >= 2 ? 560f : 300f;
        _pill = HudKit.SoftPanel(hud.Canvas.transform, "Bike Computer", new Color(0.035f, 0.045f, 0.065f, 0.70f));
        // Telemetry pill: 26 px margin, 340 x 52. Sit 8 px under it (beside it collided with the
        // top-centre route title); the draft card moves down to make room (ShiftDraft).
        HudKit.Corner(_pill, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(26f, -86f), new Vector2(w, 52f));
        var edge = HudKit.SoftPanel(_pill, "Edge", HudKit.WithAlpha(HudKit.GlassEdge, 0.22f), frame: true);
        HudKit.Place(edge, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        _brand = Lbl("Brand", tier >= 2 ? "APEX ELITE" : "APEX PRO", 10, 14f, 100f, new Color(0.886f, 0.745f, 0.431f));
        _brand.fontSize = 11;
        // Both labels used 30 px boxes only 21 px apart, so "APEX ELITE" ran into the W/kg
        // readout. Thin, non-overlapping boxes: brand +9..+23, W/kg -19..+5.
        var brandRt = (RectTransform)_brand.transform;
        brandRt.anchoredPosition = new Vector2(14f, 16f);
        brandRt.sizeDelta = new Vector2(brandRt.sizeDelta.x, 14f);
        _wkg = Lbl("WKg", "0.0 W/kg", 20, 14f, 120f, HudKit.Chalk);
        var wkgRt = (RectTransform)_wkg.transform;
        wkgRt.anchoredPosition = new Vector2(14f, -7f);
        wkgRt.sizeDelta = new Vector2(wkgRt.sizeDelta.x, 24f);
        _zone = Lbl("Zone", "Z1", 20, 134f, 40f, HudKit.Chalk);
        for (int i = 0; i < 6; i++)
        {
            var seg = HudKit.Panel(_pill, "Z" + (i + 1), ZoneCol[i]);
            HudKit.Corner(seg, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(178f + i * 18f, 0f), new Vector2(15f, 14f));
            _segs[i] = seg.GetComponent<Image>();
        }
        if (tier >= 2)
        {
            _avg = Lbl("Avg", "AVG 0 W", 18, 306f, 110f, HudKit.ChalkSoft);
            _climb = Lbl("Climb", "0.0%", 18, 420f, 130f, HudKit.ChalkSoft);
        }
        HudSprites.SetLayerRecursively(_pill.gameObject, HudSprites.UiLayer);
    }

    private float _draftShift;

    /// <summary>The draft card sits right under the telemetry pill; slide it below the strip.</summary>
    private void ShiftDraft(float shift)
    {
        var draft = hud.Canvas != null ? hud.Canvas.transform.Find("Draft Card") as RectTransform : null;
        if (draft == null) return;
        draft.anchoredPosition += new Vector2(0f, shift - _draftShift);
        _draftShift = shift;
    }

    private Text Lbl(string name, string text, int size, float x, float width, Color col)
    {
        var t = HudKit.Label(_pill, name, text, size, col, TextAnchor.MiddleLeft, FontStyle.Bold);
        HudKit.AddShadow(t, 0.7f, 1.2f);
        HudKit.Corner((RectTransform)t.transform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(x, 0f), new Vector2(width, 30f));
        return t;
    }
}
