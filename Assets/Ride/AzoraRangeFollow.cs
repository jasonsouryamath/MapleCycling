using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Keeps Azora's distant range rings centred on whichever camera is about to render (copilot,
/// 2026-09-26, A5). The rings are authored around <see cref="authoredCentre"/>; the 24 km route
/// runs out to (and past) a static ring, so from the descent the "distant" ranges stood a few
/// hundred metres away as flat grey slabs. Re-centring them per camera keeps every ring at its
/// authored radius from the viewer - a skybox-style backdrop that also stays inside the ride
/// camera's 9 km far clip. Only XZ moves; the silhouettes keep their absolute heights.
/// </summary>
[ExecuteAlways]
public sealed class AzoraRangeFollow : MonoBehaviour
{
    public Vector3 authoredCentre;

    // WP-H1: the far chain (MapleRide/HDRP/AzoraAlpineRange, _Compress = 1) packs itself just
    // inside the rendering camera's far clip, so the shader needs that value per camera.
    static readonly int RangeFarId = Shader.PropertyToID("_AzoraRangeFar");

    void OnEnable() => RenderPipelineManager.beginCameraRendering += OnBeginCamera;
    void OnDisable() => RenderPipelineManager.beginCameraRendering -= OnBeginCamera;

    void OnBeginCamera(ScriptableRenderContext ctx, Camera cam)
    {
        if (cam == null || cam.cameraType == CameraType.Preview || cam.cameraType == CameraType.Reflection) return;
        var p = cam.transform.position;
        transform.position = new Vector3(p.x - authoredCentre.x, 0f, p.z - authoredCentre.z);
        Shader.SetGlobalFloat(RangeFarId, cam.farClipPlane);
    }
}
