Shader "Hidden/WebcamRotateBlit"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Rotation ("Rotation", Float) = 0
        _FlipX ("FlipX", Float) = 0
        _FlipY ("FlipY", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        ZTest Always ZWrite Off Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize;
            float _Rotation; // 0, 90, 180, 270
            float _FlipX;    // 0 ou 1
            float _FlipY;    // 0 ou 1

            fixed4 frag(v2f_img i) : SV_Target
            {
                float2 uv = i.uv;

                // Flips
                if (_FlipX > 0.5) uv.x = 1.0 - uv.x;
                if (_FlipY > 0.5) uv.y = 1.0 - uv.y;

                // Rotação por quadrantes (evita trig)
                float2 uv2 = uv;
                if (_Rotation > 45 && _Rotation < 135)
                {
                    // 90 graus
                    uv2 = float2(uv.y, 1.0 - uv.x);
                }
                else if (_Rotation > 135 && _Rotation < 225)
                {
                    // 180 graus
                    uv2 = float2(1.0 - uv.x, 1.0 - uv.y);
                }
                else if (_Rotation > 225 && _Rotation < 315)
                {
                    // 270 graus
                    uv2 = float2(1.0 - uv.y, uv.x);
                }

                fixed4 col = tex2D(_MainTex, uv2);
                return col;
            }
            ENDCG
        }
    }

    Fallback Off
}