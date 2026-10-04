/// <summary>World footprint of a baked minimap image (see Assets/Editor/MinimapBake.cs).</summary>
[System.Serializable]
public struct MinimapBounds
{
    public float cx, cz, size;   // world XZ centre and edge length (metres) of the square image
}
