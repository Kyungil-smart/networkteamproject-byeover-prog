Shader "Custom/StencilWriter"
{
    Properties {    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry-10"
        }

        Pass
        {
            ColorMask 0
            ZWrite Off
            ZTest Always
            Cull Off

            Stencil
            {
                Ref 1
                Comp Always
                Pass Replace
            }
        }
    }
}
