using UnityEngine;
public class KuroSpeedGauge : MonoBehaviour
{
    KuroKeyboardController controller; GUIStyle label, box;
    void OnEnable(){ controller=GetComponent<KuroKeyboardController>(); }
    void OnGUI(){ if(controller==null||RideInputGate.Locked) return; /* legacy debug gauge: the ride HUD owns speed now, and this box overlapped its telemetry pill showing 0 km/h */ if(FindAnyObjectByType<RideHud>()!=null) return; if(label==null){ label=new GUIStyle(GUI.skin.label){fontSize=24,fontStyle=FontStyle.Bold,normal={textColor=Color.white}}; box=new GUIStyle(GUI.skin.box){fontSize=18,fontStyle=FontStyle.Bold,alignment=TextAnchor.MiddleCenter}; } var r=new Rect(24,24,190,72); GUI.Box(r,"",box); GUI.Label(new Rect(40,32,165,30),$"{controller.CurrentSpeed*3.6f:0} km/h",label); GUI.Label(new Rect(40,59,165,22),"SPEED",label); }
}
