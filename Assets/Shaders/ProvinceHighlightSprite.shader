Shader "Map/ProvinceHighlightSprite"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _ProvinceIdTex ("Province ID Texture (Point, Linear)", 2D) = "black" {}
        _CountryTex ("Country Texture (Point, Linear)", 2D) = "black" {}
        _FogOfWarTex ("Fog of War Texture (Point, Linear)", 2D) = "black" {}
        _AdjacentTex ("Adjacent Texture (Point, Linear)", 2D) = "black" {}
        
        _CountryBorderColor ("Country Border Color", Color) = (1,0.5,0,1)
        _CountryBorderThickness ("Country Border Thickness (px)", Range(1,12)) = 4
        _OwnerColorStrength ("Owner Color Strength", Range(0,1)) = 0.5
        
        _HoverEnabled ("Hover Enabled", Float) = 0
        _HoverKey ("Hover Key (RGBA 0..1)", Vector) = (0,0,0,0)
        _HoverTint ("Hover Tint", Color) = (1,1,1,1)
        _HoverTintStrength ("Hover Tint Strength", Range(0,1)) = 0.35
        _HoverOutlineColor ("Hover Outline Color", Color) = (1,1,0,1)
        _HoverOutlineThickness ("Hover Outline Thickness (px)", Range(1,8)) = 2

        _SelectEnabled ("Select Enabled", Float) = 0
        _SelectKey ("Select Key (RGBA 0..1)", Vector) = (0,0,0,0)
        _SelectOutlineColor ("Select Outline Color", Color) = (1,0.5,0,1)
        _SelectOutlineThickness ("Select Outline Thickness (px)", Range(1,12)) = 4
        
        _FogOfWarEnabled ("Fog of War Enabled", Float) = 0
        _FogOfWarTint ("Fog of War Tint", Color) = (1,1,1,1)
        _FogOfWarStrength ("Fog of War Strength", Range(0,1)) = 0.5
        
        _AdjacentEnabled ("Adjacent Enabled", Float) = 0
        _AdjacentTint ("Adjacent Tint", Color) = (1,1,1,1)
        _AdjacentStrength ("Adjacent Strength", Range(0,1)) = 0.5
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        Blend One OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.5
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex   : POSITION;
                float2 texcoord : TEXCOORD0;
                fixed4 color    : COLOR;
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                float2 uv       : TEXCOORD0;
                fixed4 color    : COLOR;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;

            sampler2D _ProvinceIdTex;
            float4 _ProvinceIdTex_TexelSize;
            
            sampler2D _CountryTex;
            float4 _CountryTex_TexelSize;

            sampler2D _FogOfWarTex;
            float4 _FogOfWarTex_TexelSize;
            
            sampler2D _AdjacentTex;
            float4 _AdjacentTex_TexelSize;
            
            fixed4 _CountryBorderColor;
            float _CountryBorderThickness;
            float _OwnerColorStrength;
            
            float _HoverEnabled;
            float4 _HoverKey;
            fixed4 _HoverTint;
            float _HoverTintStrength;
            fixed4 _HoverOutlineColor;
            float _HoverOutlineThickness;

            float _SelectEnabled;
            float4 _SelectKey;
            fixed4 _SelectOutlineColor;
            float _SelectOutlineThickness;
            
            float _FogOfWarEnabled;
            float _FogOfWarStrength;
            fixed4 _FogOfWarTint;
            
            float _AdjacentEnabled;
            fixed4 _AdjacentTint;
            float _AdjacentStrength;

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.texcoord, _MainTex);
                o.color = v.color * _Color;
                return o;
            }

            // Convert float RGBA to 0..255 integer-ish (rounded) to make comparisons stable.
            int4 ToByte4(float4 c)
            {
                // +0.5 for rounding; clamp to [0,255]
                int4 b = int4(saturate(c) * 255.0 + 0.5);
                return b;
            }

            bool ProvinceIdSameKey(float2 uv, int4 keyB)
            {
                float4 id = tex2D(_ProvinceIdTex, uv);
                int4 idB = ToByte4(id);
                return all(idB == keyB);
            }
            
            bool FogOfWarSameKey(float2 uv, int4 keyB)
            {
                float4 id = tex2D(_FogOfWarTex, uv);
                int4 idB = ToByte4(id);
                return all(idB == keyB);
            }
            
            bool SameKey(sampler2D tex, float2 uv, int4 keyB)
            {
                float4 id = tex2D(tex, uv);
                int4 idB = ToByte4(id);
                return all(idB == keyB);
            }

            // Returns 1 if this pixel is an outline pixel for the given key+thickness.
            float OutlineForKey(float2 uv, int4 keyB, float thicknessPx)
            {
                if (thicknessPx <= 0.0) return 0.0;

                // Only draw outline if we're inside the region
                if (!ProvinceIdSameKey(uv, keyB)) return 0.0;

                float2 t = _ProvinceIdTex_TexelSize.xy;

                // Check neighbors out to thickness in 4 directions.
                // (Cheap and works well for pixel maps)
                int steps = (int)round(thicknessPx);
                [loop]
                for (int s = 1; s <= steps; s++)
                {
                    float2 off = t * s;

                    // If any neighbor is not the same province, we're on the border
                    if (!ProvinceIdSameKey(uv + float2( off.x, 0), keyB)) return 1.0;
                    if (!ProvinceIdSameKey(uv + float2(-off.x, 0), keyB)) return 1.0;
                    if (!ProvinceIdSameKey(uv + float2(0,  off.y), keyB)) return 1.0;
                    if (!ProvinceIdSameKey(uv + float2(0, -off.y), keyB)) return 1.0;
                }

                return 0.0;
            }
            
            float AnyProvinceBorder(float2 uv, float thicknessPx)
            {
                float2 t = _ProvinceIdTex_TexelSize.xy;
                int steps = (int)round(thicknessPx);

                int4 center = ToByte4(tex2D(_ProvinceIdTex, uv));

                // optional: treat background as no border
                // if (all(center == int4(0,0,0,0))) return 0;

                [loop]
                for (int s = 1; s <= steps; s++)
                {
                    float2 off = t * s;

                    int4 r = ToByte4(tex2D(_ProvinceIdTex, uv + float2( off.x, 0)));
                    int4 l = ToByte4(tex2D(_ProvinceIdTex, uv + float2(-off.x, 0)));
                    int4 u = ToByte4(tex2D(_ProvinceIdTex, uv + float2(0,  off.y)));
                    int4 d = ToByte4(tex2D(_ProvinceIdTex, uv + float2(0, -off.y)));

                    if (!all(r == center)) return 1.0;
                    if (!all(l == center)) return 1.0;
                    if (!all(u == center)) return 1.0;
                    if (!all(d == center)) return 1.0;
                }

                return 0.0;
            }
            
            float AnyCountryBorder(float2 uv, float thicknessPx)
            {
                float2 t = _CountryTex_TexelSize.xy;
                int steps = (int)round(thicknessPx);

                int4 center = ToByte4(tex2D(_CountryTex, uv));

                // Treat transparent as "no owner / background" (optional)
                // if (center.a == 0) return 0.0;

                [loop]
                for (int s = 1; s <= steps; s++)
                {
                    float2 off = t * s;

                    int4 r = ToByte4(tex2D(_CountryTex, uv + float2( off.x, 0)));
                    int4 l = ToByte4(tex2D(_CountryTex, uv + float2(-off.x, 0)));
                    int4 u = ToByte4(tex2D(_CountryTex, uv + float2(0,  off.y)));
                    int4 d = ToByte4(tex2D(_CountryTex, uv + float2(0, -off.y)));

                    if (any(r != center)) return 1.0;
                    if (any(l != center)) return 1.0;
                    if (any(u != center)) return 1.0;
                    if (any(d != center)) return 1.0;
                }

                return 0.0;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 baseCol = tex2D(_MainTex, i.uv) * i.color;
                
                // Owner Color Overlay
                fixed4 ownerCol = tex2D(_CountryTex, i.uv);
                // Blend if owner color is valid (alpha > 0)
                if (ownerCol.a > 0.05)
                {
                     baseCol.rgb = lerp(baseCol.rgb, ownerCol.rgb, _OwnerColorStrength);
                }

                // Adjacent
                if (_AdjacentEnabled > 0.5)
                {
                    float adjacent = tex2D(_AdjacentTex, i.uv).r;
                    adjacent = step(0.5, adjacent);
                    baseCol.rgb = lerp(baseCol.rgb, _AdjacentTint, adjacent * _AdjacentStrength);
                }
                
                // Province Borders
                float provinceBorders = AnyProvinceBorder(i.uv, 1);
                if (provinceBorders > 0.5)
                {
                    fixed4 o = fixed4(0.04, 0.04, 0.04, 1);
                    o.rgb *= o.a;
                    baseCol.rgb = lerp(baseCol.rgb, o.rgb, o.a);
                    baseCol.a = max(baseCol.a, o.a);
                }
                
                // Country Borders
                float countryBorders = AnyCountryBorder(i.uv, _CountryBorderThickness);
                if (countryBorders > 0.5)
                {
                    fixed4 o = _CountryBorderColor;
                    o.rgb *= o.a; // premultiply
                    baseCol.rgb = lerp(baseCol.rgb, o.rgb, o.a);
                    baseCol.a = max(baseCol.a, o.a);
                }
                
                // Premultiply alpha for sprite blending style
                baseCol.rgb *= baseCol.a;

                int4 hoverB  = ToByte4(_HoverKey);
                int4 selectB = ToByte4(_SelectKey);

                float hoverInside = 0.0;
                if (_HoverEnabled > 0.5)
                    hoverInside = ProvinceIdSameKey(i.uv, hoverB) ? 1.0 : 0.0;

                // Tint hovered province (fill)
                if (hoverInside > 0.5)
                {
                    fixed3 tintRgb = _HoverTint.rgb * baseCol.a; // premultiplied
                    baseCol.rgb = lerp(baseCol.rgb, tintRgb, _HoverTintStrength);
                }

                // Outlines (draw selection on top of hover)
                float hoverOutline = 0.0;
                if (_HoverEnabled > 0.5)
                    hoverOutline = OutlineForKey(i.uv, hoverB, _HoverOutlineThickness);

                float selectOutline = 0.0;
                if (_SelectEnabled > 0.5)
                    selectOutline = OutlineForKey(i.uv, selectB, _SelectOutlineThickness);

                if (hoverOutline > 0.5)
                {
                    fixed4 o = _HoverOutlineColor;
                    o.rgb *= o.a;
                    baseCol.rgb = lerp(baseCol.rgb, o.rgb, o.a);
                    baseCol.a = max(baseCol.a, o.a);
                }

                if (selectOutline > 0.5)
                {
                    fixed4 o = _SelectOutlineColor;
                    o.rgb *= o.a;
                    baseCol.rgb = lerp(baseCol.rgb, o.rgb, o.a);
                    baseCol.a = max(baseCol.a, o.a);
                }
                
                // Fog of War (R8)
                if (_FogOfWarEnabled > 0.5)
                {
                    // 0..1 where: 1 = owned, ~0.5 = neighbor, 0 = unknown
                    float fog = tex2D(_FogOfWarTex, i.uv).r;

                    // Robust to filtering (Point/Bilinear)
                    float isOccupied = step(0.75, fog);                  // 1.0 only for owned (~1.0)
                    float isNeighbor = step(0.25, fog) * (1.0 - isOccupied); // 1.0 for neighbor (~0.5), not owned

                    if (isNeighbor > 0.5)
                    {
                        baseCol.rgb = lerp(baseCol.rgb, _FogOfWarTint, _FogOfWarStrength * 0.95);
                    }
                    else if (isOccupied < 0.5) // unknown
                    {
                        baseCol.rgb = lerp(baseCol.rgb, _FogOfWarTint, _FogOfWarStrength);
                    }
                }
                
                return baseCol;
            }
            ENDCG
        }
    }
}
