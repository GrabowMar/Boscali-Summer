// Night vision, the tube: turns the world frame into one phosphor colour and adds the tube's own scintillation.
// Runs before the game's exposure, so the noise floor is amplified with the gain as in a real intensifier.
// Pass 1 reads one mip of the frame for the gain meter. Plain CG, no pipeline includes.
Shader "Boscali/NvgPhosphor"
{
    Properties { _MainTex ("Frame", 2D) = "black" {} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always

        CGINCLUDE
        #include "UnityCG.cginc"
        sampler2D _MainTex;
        float4 _NvgTint;   // rgb: phosphor colour
        float4 _NvgNoise;  // x: grain on the signal, y: noise floor, z: frame seed, w: meter mip

        struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

        v2f vert(appdata_img v)
        {
            v2f o;
            o.pos = UnityObjectToClipPos(v.vertex);
            o.uv = v.texcoord;
            return o;
        }

        float hash(float2 p)
        {
            p = frac(p * float2(443.897, 441.423));
            p += dot(p, p.yx + 19.19);
            return frac((p.x + p.y) * p.x);
        }
        ENDCG

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            float4 frag(v2f i) : SV_Target
            {
                float4 c = tex2Dlod(_MainTex, float4(i.uv, 0, 0));
                float l = dot(c.rgb, float3(0.2126, 0.7152, 0.0722));
                float2 cell = floor(i.pos.xy * 0.5);
                float n = hash(cell + _NvgNoise.z);
                float s = hash(cell.yx + _NvgNoise.z * 1.7 + 31.0);
                l = max(0.0, l * (1.0 + (n - 0.5) * _NvgNoise.x) + s * s * s * _NvgNoise.y);
                return float4(l * _NvgTint.rgb, c.a);
            }
            ENDCG
        }

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            float4 frag(v2f i) : SV_Target { return tex2Dlod(_MainTex, float4(i.uv, 0, _NvgNoise.w)); }
            ENDCG
        }
    }
}
