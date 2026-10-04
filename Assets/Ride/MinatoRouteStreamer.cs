using System;
using UnityEngine;

/// <summary>
/// Distance-cell streamer for Minato's 19 km route. Cells are authored by the environment pass;
/// runtime work is a cheap bounds/rider distance test, and editor diagnostics keep every cell
/// visible because this component only streams in play mode.
/// </summary>
public sealed class MinatoRouteStreamer : MonoBehaviour
{
    [Serializable]
    public struct Cell
    {
        public GameObject root;
        public Bounds bounds;
        public float activeDistanceM;
    }

    public Transform rider;
    public float updateInterval = 0.25f;
    public Cell[] cells = Array.Empty<Cell>();

    private float _timer;

    private void OnEnable()
    {
        _timer = 999f;
        if (!Application.isPlaying) ShowAll();
    }

    private void Update()
    {
        if (!Application.isPlaying) return;
        _timer += Time.deltaTime;
        if (_timer < updateInterval) return;
        _timer = 0f;

        if (rider == null)
        {
            var boot = FindFirstObjectByType<RideBootstrap>(FindObjectsInactive.Include);
            if (boot != null)
            {
                boot.Resolve();
                if (boot.follower != null) rider = boot.follower.rider;
            }
        }
        if (rider == null) return;

        Vector3 p = rider.position;
        for (int i = 0; i < cells.Length; i++)
        {
            var cell = cells[i];
            if (cell.root == null) continue;
            float d = Vector3.Distance(p, cell.bounds.ClosestPoint(p));
            bool want = d <= Mathf.Max(50f, cell.activeDistanceM);
            if (cell.root.activeSelf != want) cell.root.SetActive(want);
        }
    }

    public void ShowAll()
    {
        if (cells == null) return;
        for (int i = 0; i < cells.Length; i++)
            if (cells[i].root != null) cells[i].root.SetActive(true);
    }
}
