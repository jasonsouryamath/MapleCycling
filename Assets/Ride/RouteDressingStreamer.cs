using System;
using UnityEngine;

/// <summary>
/// Route-arc dressing streamer.
///
/// The scatter pass groups every prop into a <c>Route Dressing/Chunk_####</c> bucket, one per
/// 100 m of centreline, and records each bucket's centre and radius here. At run time only the
/// buckets within <see cref="activeRadiusM"/> of the rider are enabled, and only those within
/// <see cref="shadowRadiusM"/> cast shadows.
///
/// This is a RANGE test, not a frustum guess: the rider's arc position on the course is known
/// exactly, so a chunk can be switched off with certainty rather than hoped about. Chunk centres
/// are baked by the editor pass so nothing has to walk thousands of renderers at load.
/// </summary>
[DefaultExecutionOrder(-10)]
public class RouteDressingStreamer : MonoBehaviour
{
    [Serializable]
    public struct Chunk
    {
        public Transform root;
        public Vector3 center;
        public float radius;
    }

    public Transform rider;

    [Tooltip("PROVISIONAL: dressing inside this radius of the rider is enabled.")]
    public float activeRadiusM = 420f;
    [Tooltip("PROVISIONAL: dressing inside this radius also casts shadows.")]
    public float shadowRadiusM = 190f;
    [Tooltip("Seconds between visibility passes. The rider covers ~12 m at 45 km/h in 1 s.")]
    public float updateInterval = 0.25f;
    [Tooltip("Turn off to see the whole world - useful when taking wide diagnostic renders.")]
    public bool streamingEnabled = true;

    public Chunk[] chunks = Array.Empty<Chunk>();

    private float _timer;
    private bool[] _state;
    private bool[] _shadow;

    private void OnEnable()
    {
        _state = new bool[chunks.Length];
        _shadow = new bool[chunks.Length];
        for (int i = 0; i < chunks.Length; i++)
        {
            _state[i] = chunks[i].root != null && chunks[i].root.gameObject.activeSelf;
            _shadow[i] = true;
        }
        _timer = 999f;
    }

    private void Update()
    {
        _timer += Time.deltaTime;
        if (_timer < updateInterval) return;
        _timer = 0f;
        Apply(rider != null ? rider.position : Vector3.zero);
    }

    /// <summary>Deterministic pass, callable from an editor harness.</summary>
    public void Apply(Vector3 riderPos)
    {
        if (chunks == null || chunks.Length == 0) return;
        if (_state == null || _state.Length != chunks.Length) OnEnable();

        for (int i = 0; i < chunks.Length; i++)
        {
            var c = chunks[i];
            if (c.root == null) continue;

            bool want = true;
            bool shadow = true;
            if (streamingEnabled)
            {
                float d = Vector3.Distance(riderPos, c.center) - c.radius;
                want = d <= activeRadiusM;
                shadow = d <= shadowRadiusM;
            }

            if (want != _state[i])
            {
                c.root.gameObject.SetActive(want);
                _state[i] = want;
            }
            if (want && shadow != _shadow[i])
            {
                var mode = shadow ? UnityEngine.Rendering.ShadowCastingMode.On
                                  : UnityEngine.Rendering.ShadowCastingMode.Off;
                foreach (var r in c.root.GetComponentsInChildren<Renderer>(true))
                {
                    // A prop the pass deliberately turned off (far-band blossom, petal drifts)
                    // must stay off, so this only ever *removes* shadow casting.
                    if (!shadow) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    else if (r.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.Off)
                        r.shadowCastingMode = mode;
                }
                _shadow[i] = shadow;
            }
        }
    }

    /// <summary>Re-enables every chunk. Used before a wide diagnostic capture.</summary>
    public void ShowAll()
    {
        for (int i = 0; i < chunks.Length; i++)
            if (chunks[i].root != null) chunks[i].root.gameObject.SetActive(true);
        _state = null;
    }
}
