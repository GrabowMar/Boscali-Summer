Shader "Hidden/Boscali/NativeDamageOwnershipFixture"
{
    Properties
    {
        _Color ("Colour", Color) = (1,1,1,1)
        _MainTex ("Display texture", 2D) = "white" {}
        _HitPoints ("Native damage", Float) = 1
        _Glossiness ("Native glossiness", Float) = .5
    }
    SubShader { Tags { "RenderType"="Opaque" } Pass { Color (1,1,1,1) } }
}
