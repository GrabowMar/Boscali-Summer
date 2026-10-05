Shader "Hidden/Boscali/DisplayOwnershipFixture"
{
    Properties
    {
        _Color ("Colour", Color) = (1,1,1,1)
        _BaseColor ("Base colour", Color) = (1,1,1,1)
        _EmissionColor ("Emission", Color) = (0,0,0,1)
        _MainTex ("Main", 2D) = "white" {}
        _BaseMap ("Base", 2D) = "white" {}
        _EmissionMap ("Emission", 2D) = "black" {}
    }
    SubShader { Tags { "RenderType"="Opaque" } Pass { Color (1,1,1,1) } }
}
