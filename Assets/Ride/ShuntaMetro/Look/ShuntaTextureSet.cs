using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Name -> texture table for the Shunta Metro PBR set (Assets/Textures/ShuntaMetro). Lives in
/// Resources/ShuntaMetro/ShuntaTextureSet.asset so builds include the textures; rebuilt by the editor step
/// ShuntaMetroTextures.Import. ShuntaLookKit falls back to its procedural look when an entry is missing.
/// </summary>
public sealed class ShuntaTextureSet : ScriptableObject
{
    [Serializable] public struct Entry { public string name; public Texture2D tex; }
    public List<Entry> entries = new List<Entry>();

    Dictionary<string, Texture2D> _map;

    public Texture2D Get(string n)
    {
        if (_map == null)
        {
            _map = new Dictionary<string, Texture2D>();
            foreach (var e in entries) if (e.tex != null && !string.IsNullOrEmpty(e.name)) _map[e.name] = e.tex;
        }
        return _map.TryGetValue(n, out var t) ? t : null;
    }

    void OnEnable() { _map = null; }
}
