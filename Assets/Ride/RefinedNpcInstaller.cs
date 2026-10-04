using UnityEngine;

/// <summary>Updates named NPCs in existing playable scenes without re-exporting other owners' worlds.</summary>
public sealed class RefinedNpcInstaller : MonoBehaviour
{
    RefinedNpcCast cast;
    float nextScan;
    static RefinedNpcInstaller instance;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetState() { instance = null; }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (instance != null) return;
        var cast = Resources.Load<RefinedNpcCast>("RefinedNpcCast/Cast");
        if (cast == null) return;
        var go = new GameObject("Refined named NPC visuals"); DontDestroyOnLoad(go);
        instance = go.AddComponent<RefinedNpcInstaller>(); instance.cast = cast; instance.Scan();
    }
    void LateUpdate()
    {
        if (Time.unscaledTime < nextScan) return;
        nextScan = Time.unscaledTime + 1f; Scan();
    }
    void Scan()
    {
        if (cast == null) return;
        foreach (var greeting in FindObjectsByType<NpcGreeting>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var rider = cast.Find(greeting.riderName);
            if (rider != null) RefinedNpcCast.Apply(greeting.gameObject, rider);
        }
        foreach (var hanakage in FindObjectsByType<HanakageRider>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (hanakage.visualRoot != null) RefinedNpcCast.Apply(hanakage.visualRoot, cast.Find("Hanakage"));
    }
}
