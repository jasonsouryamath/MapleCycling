using UnityEditor;
using UnityEngine;

public static class MetroProbe
{
    public static void Run()
    {
        string[] paths =
        {
            "Assets/Environment/NagisaBay/Models/Nagisa_B_Konbini.glb",
            "Assets/Environment/NagisaBay/Models/Nagisa_B_ShopA_4.glb",
            "Assets/Environment/NagisaBay/Models/Nagisa_B_ShopA_7.glb",
            "Assets/Environment/NagisaBay/Models/Nagisa_B_ShopA_15.glb",
            "Assets/Environment/NagisaBay/Models/Nagisa_B_ShopB_1.glb",
            "Assets/Environment/NagisaBay/Models/Nagisa_B_ShopB_5.glb",
            "Assets/Environment/NagisaBay/Models/Nagisa_B_ShopC_2.glb",
            "Assets/Environment/NagisaBay/Models/Nagisa_B_ShopC_10.glb",
            "Assets/Environment/NagisaBay/Models/Nagisa_B_Condo1.glb",
            "Assets/Environment/NagisaBay/Models/Nagisa_B_HeroTower.glb",
            "Assets/Environment/NagisaBay/Models/Nagisa_HotelTower.glb",
            "Assets/Environment/NagisaBay/Models/Nagisa_MidriseA.glb",
            "Assets/Environment/NagisaBay/Models/Nagisa_MidriseB.glb",
            "Assets/Environment/NagisaBay/Models/Nagisa_MidriseC.glb",
            "Assets/Environment/NagisaBay/Models/Nagisa_S_CarSedan.glb",
            "Assets/Environment/NagisaBay/Models/Nagisa_S_CarTaxi.glb",
            "Assets/Environment/NagisaBay/Models/Nagisa_S_CarKei.glb",
            "Assets/Environment/NagisaBay/Models/Nagisa_S_CarVan.glb",
            "Assets/Environment/NagisaBay/Models/Nagisa_S_Scooter.glb",
            "Assets/Environment/NagisaBay/Models/Nagisa_S_StreetLight.glb",
            "Assets/Environment/NagisaBay/Models/Nagisa_S_FoodTruck.glb",
            "Assets/Environment/NagisaBay/Models/Nagisa_S_BusStop.glb",
            "Assets/Environment/MinatoCoast/Models/Minato_City_ShopHouse.fbx",
            "Assets/Environment/MinatoCoast/Models/Minato_City_MarketStall.fbx",
            "Assets/Environment/MinatoCoast/Models/Minato_City_MarketStallB.fbx",
            "Assets/Environment/MinatoCoast/Models/Minato_City_MarketTent.fbx",
            "Assets/Environment/MinatoCoast/Models/Minato_City_SkyTowerA.fbx",
            "Assets/Environment/MinatoCoast/Models/Minato_City_SkyTowerC.fbx",
            "Assets/Environment/MinatoCoast/Models/Minato_City_BlockA.fbx",
            "Assets/Environment/MinatoCoast/Models/Minato_City_BannerFlag.fbx",
            "Assets/Environment/MinatoCoast/Models/Minato_Port_StreetLamp.fbx",
        };
        foreach (var p in paths)
        {
            var g = AssetDatabase.LoadAssetAtPath<GameObject>(p);
            if (g == null) { Debug.Log($"[metroprobe] MISSING {p}"); continue; }
            var go = (GameObject)Object.Instantiate(g);
            var b = new Bounds(); bool first = true;
            var slots = new System.Collections.Generic.HashSet<string>();
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (r.name.Contains("_LOD1") || r.name.Contains("_LOD2") || r.name.Contains("_LOD3")) continue;
                if (first) { b = r.bounds; first = false; } else b.Encapsulate(r.bounds);
                foreach (var m in r.sharedMaterials) if (m != null) slots.Add(m.name);
            }
            Debug.Log($"[metroprobe] {System.IO.Path.GetFileNameWithoutExtension(p)} min={b.min:F2} max={b.max:F2} slots={string.Join("|", slots)}");
            Object.DestroyImmediate(go);
        }
    }
}
