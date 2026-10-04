using UnityEngine;

/// Keeps Kuro's feet on the nearest road/ground surface while climbing the pass.
public class KuroRoadGrounding : MonoBehaviour
{
    public float rayHeight = 8f;
    public float rayDistance = 30f;
    public float footOffset = 0.06f;

    void LateUpdate()
    {
        var origin = transform.position + Vector3.up * rayHeight;
        if (Physics.Raycast(origin, Vector3.down, out var hit, rayDistance, ~0, QueryTriggerInteraction.Ignore))
        {
            var p = transform.position;
            p.y = hit.point.y + footOffset;
            transform.position = p;
        }
    }
}
