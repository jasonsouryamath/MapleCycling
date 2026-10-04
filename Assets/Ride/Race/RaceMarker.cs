using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The floating race marker over one racer: a glowing diamond with the race type's glyph and a
/// label pill under it (the target art), a nameplate with the rank insignia, and sparkles.
///
/// The diamond, pill and nameplate are screen-space UI projected from the rider's head, so they
/// stay crisp and are untouched by HDRP exposure and fog; they are scaled with distance so they
/// read as an object in the world. The light beam and the ground ring ARE in the world
/// (HDRP/Unlit additive quads), because they have to sit on the road.
///
/// States (driven by <see cref="Tick"/>):
///   hidden     beyond <see cref="cullM"/>, behind the camera, or while a race is on
///   idle       gentle bob, soft glow
///   approach   inside ~14 m: pulse and scale up, brighter glow, sparkles, beam + ground ring
/// </summary>
public sealed class RaceMarker
{
    public const float ApproachM = 14f;
    public float cullM = 90f;

    private readonly RaceNpc _npc;
    private readonly RectTransform _root, _diamondRt, _glowRt, _pill, _plate;
    private readonly Image _glow, _diamond;
    private readonly Text _pillText, _plateText, _plateHint;
    private readonly Image _plateInsignia;
    private readonly RectTransform[] _sparkles = new RectTransform[5];
    private readonly GameObject _world;
    private readonly Transform _beam, _ring;
    private readonly Material _beamMat, _ringMat;
    private readonly Color _color;
    private float _approach;   // eased 0..1
    private readonly float _phase;

    public RaceMarker(RaceNpc npc, RectTransform layer)
    {
        _npc = npc;
        var def = npc.def;
        _color = RaceMath.TypeColor(def.Type);
        _phase = (def.Id.GetHashCode() & 255) / 40f;

        _root = HudKit.Rect(layer, "Marker " + def.Name);
        _root.sizeDelta = new Vector2(180f, 220f);

        _glowRt = RaceUi.Icon(_root, "Glow", RaceArt.Glow, new Vector2(240f, 240f), HudKit.WithAlpha(_color, 0.5f)).rectTransform;
        _glow = _glowRt.GetComponent<Image>();
        _glowRt.anchoredPosition = new Vector2(0f, 40f);
        for (int i = 0; i < _sparkles.Length; i++)
        {
            _sparkles[i] = RaceUi.Icon(_root, "Sparkle", RaceArt.Sparkle, new Vector2(26f, 26f), Color.white).rectTransform;
            _sparkles[i].gameObject.SetActive(false);
        }
        _diamond = RaceUi.Icon(_root, "Diamond", RaceArt.Icon(def.Type), new Vector2(150f, 150f));
        _diamondRt = _diamond.rectTransform;
        _diamondRt.anchoredPosition = new Vector2(0f, 40f);

        _pill = HudKit.SoftPanel(_root, "Pill", new Color(0.06f, 0.08f, 0.16f, 0.92f));
        _pill.anchoredPosition = new Vector2(0f, -52f);
        var edge = HudKit.SoftPanel(_pill, "Edge", HudKit.WithAlpha(_color, 0.9f), frame: true);
        RaceUi.Stretch(edge);
        _pillText = HudKit.Label(_pill, "Label", RaceMath.Label(def.Type), 24, HudKit.Chalk, TextAnchor.MiddleCenter, FontStyle.Bold);
        RaceUi.Stretch((RectTransform)_pillText.transform);
        _pill.sizeDelta = new Vector2(Mathf.Max(110f, _pillText.preferredWidth + 40f), 40f);

        _plate = HudKit.SoftPanel(_root, "Nameplate", RaceUi.CardGlassSoft);
        _plate.anchoredPosition = new Vector2(0f, -100f);
        _plateInsignia = RaceUi.Icon(_plate, "Insignia", RaceArt.Insignia(def.Level), new Vector2(34f, 34f));
        _plateInsignia.rectTransform.anchorMin = _plateInsignia.rectTransform.anchorMax = new Vector2(0f, 0.5f);
        _plateInsignia.rectTransform.anchoredPosition = new Vector2(24f, 0f);
        _plateText = HudKit.Label(_plate, "Name", $"{def.Name}  <color=#c9d3e6>Lv {def.Level}</color>", 26,
                                  HudKit.Chalk, TextAnchor.MiddleLeft, FontStyle.Bold);
        HudKit.AddShadow(_plateText);
        HudKit.Place((RectTransform)_plateText.transform, Vector2.zero, Vector2.one, new Vector2(46f, 0f), new Vector2(-10f, 0f));
        _plate.sizeDelta = new Vector2(_plateText.preferredWidth + 60f, 40f);
        _plateHint = HudKit.Label(_root, "Hint", "Stop here to race", 17, HudKit.ChalkSoft, TextAnchor.MiddleCenter);
        HudKit.AddOutline(_plateHint);
        ((RectTransform)_plateHint.transform).anchoredPosition = new Vector2(0f, -134f);
        _root.gameObject.SetActive(false);

        // world: beam + ground ring
        _world = new GameObject("~Race Marker FX " + def.Name);
        _world.transform.SetParent(npc.transform.parent, false);
        _beamMat = RaceArt.Unlit("~RaceBeam", RaceArt.Tex("race_beam"), HudKit.WithAlpha(_color, 0f), true);
        _ringMat = RaceArt.Unlit("~RaceRing", RaceArt.Tex("race_ring"), HudKit.WithAlpha(_color, 0f), true);
        _beam = RaceArt.QuadObject("Beam", _world.transform, _beamMat).transform;
        _ring = RaceArt.QuadObject("Ring", _world.transform, _ringMat).transform;
        _world.SetActive(false);
    }

    public void Destroy()
    {
        if (_root != null) Object.Destroy(_root.gameObject);
        if (_world != null) Object.Destroy(_world);
        if (_beamMat != null) Object.Destroy(_beamMat);
        if (_ringMat != null) Object.Destroy(_ringMat);
    }

    public void Hide()
    {
        if (_root.gameObject.activeSelf) _root.gameObject.SetActive(false);
        if (_world.activeSelf) _world.SetActive(false);
    }

    /// <param name="distance">Kuro to the racer, metres.</param>
    /// <param name="kuroSpeed">m/s, for the "stop here" hint.</param>
    /// <param name="canvasScale">Canvas scale factor (screen px per reference px).</param>
    public void Tick(Camera cam, float distance, float kuroSpeed, float canvasScale, float dt)
    {
        if (cam == null || distance > cullM || _npc == null)
        {
            Hide();
            return;
        }
        float t = Time.time + _phase;
        float target = distance <= ApproachM ? 1f : 0f;
        _approach = Mathf.MoveTowards(_approach, target, dt * 2.5f);
        float a = Mathf.SmoothStep(0f, 1f, _approach);

        // anchor: above the head, bobbing
        Vector3 head = _npc.HeadPosition;
        float bob = Mathf.Sin(t * 1.7f) * (0.06f + 0.03f * a);
        Vector3 world = head + Vector3.up * (0.95f + bob);
        Vector3 sp = cam.WorldToScreenPoint(world);
        if (sp.z <= 0.2f) { Hide(); return; }
        if (!_root.gameObject.activeSelf) _root.gameObject.SetActive(true);
        _root.anchorMin = _root.anchorMax = Vector2.zero;
        var pos = new Vector2(sp.x, sp.y) / Mathf.Max(0.01f, canvasScale);
        var layer = (RectTransform)_root.parent;
        float lw = layer.rect.width > 1f ? layer.rect.width : 1920f;
        float margin = 90f * Mathf.Clamp(12f / Mathf.Max(1f, sp.z), 0.4f, 1.3f);
        pos.x = Mathf.Clamp(pos.x, margin, lw - margin);   // never half off the screen edge
        _root.anchoredPosition = pos;

        // perspective scale: ~1 at 8 m, clamped so it never vanishes or swamps the screen
        float dist = Mathf.Max(1f, sp.z);
        float scale = Mathf.Clamp(12f / dist, 0.4f, 1.3f);
        float pulse = 1f + a * (0.1f + 0.07f * Mathf.Sin(t * 5.2f));
        _root.localScale = Vector3.one * scale;
        _diamondRt.localScale = Vector3.one * pulse;
        float glowA = Mathf.Lerp(0.38f + 0.06f * Mathf.Sin(t * 2f), 0.9f + 0.1f * Mathf.Sin(t * 5.2f), a);
        _glow.color = HudKit.WithAlpha(_color, glowA);
        _glowRt.localScale = Vector3.one * (0.9f + 0.35f * a) * pulse;

        // sparkles orbit the diamond while approached
        for (int i = 0; i < _sparkles.Length; i++)
        {
            var s = _sparkles[i];
            bool on = a > 0.05f;
            if (s.gameObject.activeSelf != on) s.gameObject.SetActive(on);
            if (!on) continue;
            float ph = t * 1.3f + i * (2f * Mathf.PI / _sparkles.Length);
            float twinkle = Mathf.Abs(Mathf.Sin(t * 3.1f + i * 1.7f));
            s.anchoredPosition = new Vector2(Mathf.Cos(ph) * 92f, 40f + Mathf.Sin(ph * 1.3f) * 78f);
            s.localScale = Vector3.one * (0.5f + twinkle * 0.8f) * a;
            s.GetComponent<Image>().color = new Color(1f, 1f, 1f, twinkle * a);
        }

        // nameplate + "stop" hint fade in on approach (and a little from mid range)
        float plateA = Mathf.Max(a, Mathf.InverseLerp(35f, 22f, distance) * 0.8f);
        _plate.gameObject.SetActive(plateA > 0.02f);
        if (plateA > 0.02f)
        {
            _plate.GetComponent<Image>().color = HudKit.WithAlpha(RaceUi.CardGlassSoft, RaceUi.CardGlassSoft.a * plateA);
            _plateText.color = HudKit.WithAlpha(HudKit.Chalk, plateA);
            _plateInsignia.color = new Color(1f, 1f, 1f, plateA);
        }
        bool hint = a > 0.5f && kuroSpeed > 1.5f;
        _plateHint.gameObject.SetActive(hint);
        // braking really works now: a pacing/offering racer eases to a stop beside Kuro and
        // waits with the prompt up (RaceNpc.Holding)
        _plateHint.text = _npc.AmbientRiding ? "Brake [Space] - they'll stop with you" : "Brake [Space] to stop and race";
        _plateHint.horizontalOverflow = HorizontalWrapMode.Overflow;

        // world beam + ring
        bool fx = a > 0.01f;
        if (_world.activeSelf != fx) _world.SetActive(fx);
        if (!fx) return;
        Vector3 feet = _npc.FeetPosition;
        Vector3 up = Vector3.up;
        float beamH = Mathf.Max(0.5f, world.y - feet.y);
        _beam.position = feet + up * (beamH * 0.5f);
        Vector3 toCam = cam.transform.position - _beam.position;
        toCam.y = 0f;
        if (toCam.sqrMagnitude > 1e-4f) _beam.rotation = Quaternion.LookRotation(-toCam.normalized, up);
        _beam.localScale = new Vector3(0.9f, beamH, 1f);
        _ring.position = feet + up * 0.04f;
        _ring.rotation = Quaternion.LookRotation(Vector3.down, _npc.transform.forward) *
                         Quaternion.AngleAxis(t * 25f, Vector3.forward);
        float r = 1.35f * (1f + 0.06f * Mathf.Sin(t * 5.2f));
        _ring.localScale = new Vector3(r * 2f, r * 2f, 1f);
        float hdr = 2.2f;   // additive, so a little over 1 reads as light
        _beamMat.SetColor("_UnlitColor", new Color(_color.r * hdr * 1.6f, _color.g * hdr * 1.6f, _color.b * hdr * 1.6f, 0.85f * a));
        _ringMat.SetColor("_UnlitColor", new Color(_color.r * hdr, _color.g * hdr, _color.b * hdr, 0.7f * a));
    }
}
