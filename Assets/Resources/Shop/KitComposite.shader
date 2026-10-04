// Recolours Kuro's character atlas for shop apparel (see KitAppearance.cs, KuroKitMask.png v3,
// KuroKitSplitBake.cs). Blit-only: base atlas + mask -> RenderTexture.
//  _Mode 0 = BODY: one texture for the jersey AND bibs submeshes. The garment per texel comes from
//            the mask's ZONE (clean 3D cuts: sleeve hem, waist, short hem), not from triangle
//            edges or the painted edges of Kuro's own kit, so hems are straight.
//  _Mode 1 = HELMET: the whole helmet slot in the helmet colour, crown-ridge trim.
//  Textured garments (2026-09-26): _UseJerseyTex / _UseBibsTex sample a full design atlas
//  (Resources/Shop/Kits, painted in body space by make_shop_kits.py, same UVs as the base atlas)
//  instead of the two colours; the mask's fold shading (rel) still multiplies it.
Shader "Hidden/MapleRide/KitComposite"
{
    Properties
    {
        _MainTex ("Base atlas", 2D) = "white" {}
        _Mask ("Kit mask v3", 2D) = "black" {}
        _JerseyTex ("Jersey design", 2D) = "black" {}
        _BibsTex ("Bibs design", 2D) = "black" {}
    }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex, _Mask, _JerseyTex, _BibsTex;
            float _Mode;
            float4 _Jersey, _JerseyTrim, _Bibs, _BibsTrim, _Helmet, _HelmetTrim;   // LINEAR colours
            float _UseJersey, _UseBibs, _UseHelmet, _UseJerseyTex, _UseBibsTex;

            fixed4 frag(v2f_img i) : SV_Target
            {
                fixed4 baseCol = tex2D(_MainTex, i.uv);
                float4 m = tex2D(_Mask, i.uv);
                float rel = m.g * 2.0;

                if (_Mode > 0.5)
                {
                    if (_UseHelmet < 0.5) return baseCol;
                    float3 hs = LinearToGammaSpace(baseCol.rgb);
                    bool hskin = (hs.r - hs.b > 0.10) && (hs.r > 0.45) && (hs.g > 0.30);
                    if (hskin) return baseCol;                              // strap over the cheek
                    // Pale shells hit the display ceiling on the sunlit crown, erasing the
                    // sculpted vents. Preserve the baked dark folds and cap only their highlights.
                    float pale = step(0.45, min(_Helmet.r, min(_Helmet.g, _Helmet.b)));
                    float helmetRel = lerp(rel, min(rel, 1.14), pale);
                    return fixed4(saturate((m.a > 0.5 ? _HelmetTrim.rgb : _Helmet.rgb) * helmetRel), baseCol.a);
                }

                float zone = m.r * 255.0;                                   // 0 / 64 / 128 / 192
                float3 s = LinearToGammaSpace(baseCol.rgb);                 // thresholds are sRGB
                bool skin = (s.r - s.b > 0.10) && (s.r > 0.45) && (s.g > 0.30);
                float trim = m.b * 255.0;                                   // 255 jersey band, 128 bibs gripper

                if (zone > 32 && zone < 160)                                 // jersey (64) / collar (128)
                {
                    if (_UseJersey < 0.5 || (zone > 96 && skin)) return baseCol;
                    float3 jc = _UseJerseyTex > 0.5 ? tex2D(_JerseyTex, i.uv).rgb : (trim > 192 ? _JerseyTrim.rgb : _Jersey.rgb);
                    return fixed4(saturate(jc * rel), baseCol.a);
                }
                if (zone >= 160)                                             // bibs
                {
                    if (_UseBibs < 0.5) return baseCol;
                    float3 bc = _UseBibsTex > 0.5 ? tex2D(_BibsTex, i.uv).rgb : (trim > 64 && trim < 192 ? _BibsTrim.rgb : _Bibs.rgb);
                    return fixed4(saturate(bc * rel), baseCol.a);
                }
                return baseCol;                                              // arms, gloves, legs
            }
            ENDCG
        }
    }
}
