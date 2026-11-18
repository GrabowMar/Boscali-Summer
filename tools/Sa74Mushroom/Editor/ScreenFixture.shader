Shader "Hidden/SA74ScreenFixture"
{
    Properties { _FixtureWeather ("Weather", 2D) = "black" {} }
    SubShader
    {
        Pass
        {
            ZTest Always ZWrite Off Cull Off Blend Off
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"
            sampler2D _FixtureWeather;
            float4 _FixtureTargetSize, _FixtureBackA, _FixtureBackB;
            struct Varyings { float4 pos : SV_POSITION; };
            Varyings Vert(uint id : SV_VertexID)
            {
                Varyings o;
                float2 p = float2((id << 1) & 2, id & 2);
                o.pos = float4(p * 2.0 - 1.0, 0.5, 1.0);
                return o;
            }
            float4 Frag(Varyings i) : SV_Target
            {
                // Match native Weather's SV_POSITION addressing, independently of SA74's varying UV.
                float2 uv = i.pos.xy * _FixtureTargetSize.zw;
                float4 weather = tex2D(_FixtureWeather, uv);
                float3 scene = uv.y < 0.5 ? _FixtureBackA.rgb : _FixtureBackB.rgb;
                return float4(weather.rgb + (1.0 - weather.a) * scene, 1.0);
            }
            ENDHLSL
        }
    }
}
