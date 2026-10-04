using UnityEngine;

/// <summary>NB3: identifies one highway vehicle staged by NagisaBayEnvironment.Highway.cs.
/// Motion comes from the sibling W0 <see cref="AmbientPathMover"/> (1.5 km AmbientCull); this
/// component only carries metadata so QA, tests and later packages can find/count traffic.</summary>
[DisallowMultipleComponent]
public sealed class NagisaTrafficVehicle : MonoBehaviour
{
    [Tooltip("GLB stem of the vehicle model.")] public string kind;
    [Tooltip("Cruise speed km/h (PROVISIONAL tuning).")] public float kmh;
    [Tooltip("Coastal section: West / East.")] public string section;
    [Tooltip("Loop: Fast / Slow lanes.")] public string loop;
}
