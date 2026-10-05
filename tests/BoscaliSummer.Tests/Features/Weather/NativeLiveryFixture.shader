Shader "Hidden/Boscali/NativeLiveryOwnershipFixture"
{
    Properties
    {
        _Color ("Colour", Color) = (1,1,1,1)
        _MainTex ("Display texture", 2D) = "white" {}
        _Livery ("Native livery", Float) = 0
        _Glossiness ("Native glossiness", Float) = .5
    }
    SubShader { Tags { "RenderType"="Opaque" } Pass { Color (1,1,1,1) } }
}
