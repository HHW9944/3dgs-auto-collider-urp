Shader "Custom/GaussianSplat" {
    Properties {
        _SplatScale ("Splat Scale", Range(0.1, 50.0)) = 8.0
        _MinScreenRadius ("Min Screen Radius", Range(0.0, 64.0)) = 4.0
        _MaxScreenRadius ("Max Screen Radius", Range(8.0, 1024.0)) = 256.0
        [Toggle] _ForceScreenRadius ("Force Screen Radius", Float) = 0.0
        _ForcedScreenRadius ("Forced Screen Radius", Range(0.1, 64.0)) = 8.0
    }
    SubShader {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        ZTest LEqual
        Cull Off

        Pass {
            CGPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct Gaussian
            {
                float4 pos;
                float4 color;
                float4 axis0;
                float4 axis1;
                float4 axis2;
            };

            StructuredBuffer<Gaussian> _SplatBuffer;
            float4x4 _ObjectToWorld;
            float _SplatScale;
            float _MinScreenRadius;
            float _MaxScreenRadius;
            float _ForceScreenRadius;
            float _ForcedScreenRadius;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float4 color : COLOR;
                float2 localUv : TEXCOORD0;
                float2 pixelOffset : TEXCOORD1;
                float2 invCovDiag : TEXCOORD2;
                float invCovXY : TEXCOORD3;
            };

            float2 ClipToPixel(float4 clipPos)
            {
                float2 ndc = clipPos.xy / max(abs(clipPos.w), 1e-6);
                return ndc * 0.5 * _ScreenParams.xy;
            }

            float2 ProjectAxisToPixel(float3 worldCenter, float3 worldAxis, float4 centerClip)
            {
                float4 axisClip = mul(UNITY_MATRIX_VP, float4(worldCenter + worldAxis * _SplatScale, 1.0));
                return ClipToPixel(axisClip) - ClipToPixel(centerClip);
            }

            float2 GetQuadCorner(uint cornerId)
            {
                if (cornerId == 0) return float2(-1.0, -1.0);
                if (cornerId == 1) return float2(-1.0,  1.0);
                if (cornerId == 2) return float2( 1.0,  1.0);
                if (cornerId == 3) return float2(-1.0, -1.0);
                if (cornerId == 4) return float2( 1.0,  1.0);
                return float2(1.0, -1.0);
            }

            v2f vert(uint vertexId : SV_VertexID)
            {
                uint gaussianId = vertexId / 6;
                uint cornerId = vertexId - gaussianId * 6;
                Gaussian g = _SplatBuffer[gaussianId];

                float3 worldPos = mul(_ObjectToWorld, float4(g.pos.xyz, 1.0)).xyz;
                float3 worldAxis0 = mul((float3x3)_ObjectToWorld, g.axis0.xyz);
                float3 worldAxis1 = mul((float3x3)_ObjectToWorld, g.axis1.xyz);
                float3 worldAxis2 = mul((float3x3)_ObjectToWorld, g.axis2.xyz);

                float4 centerClip = mul(UNITY_MATRIX_VP, float4(worldPos, 1.0));
                float visible = centerClip.w > 0.0 ? 1.0 : 0.0;

                float2 a0 = ProjectAxisToPixel(worldPos, worldAxis0, centerClip);
                float2 a1 = ProjectAxisToPixel(worldPos, worldAxis1, centerClip);
                float2 a2 = ProjectAxisToPixel(worldPos, worldAxis2, centerClip);

                float covXX = dot(float3(a0.x, a1.x, a2.x), float3(a0.x, a1.x, a2.x));
                float covXY = dot(float3(a0.x, a1.x, a2.x), float3(a0.y, a1.y, a2.y));
                float covYY = dot(float3(a0.y, a1.y, a2.y), float3(a0.y, a1.y, a2.y));

                float axisEnergy = covXX + covYY;
                float fallbackRadius = max(_ForcedScreenRadius, _MinScreenRadius);
                float fallbackVariance = fallbackRadius * fallbackRadius / 9.0;
                float useFallback = max(_ForceScreenRadius, axisEnergy < 1e-4 ? 1.0 : 0.0);

                covXX = lerp(covXX, fallbackVariance, useFallback);
                covXY = lerp(covXY, 0.0, useFallback);
                covYY = lerp(covYY, fallbackVariance, useFallback);

                covXX += 0.3;
                covYY += 0.3;

                float detCov = max(covXX * covYY - covXY * covXY, 1e-5);
                float traceHalf = 0.5 * (covXX + covYY);
                float radiusTerm = sqrt(max(0.0, traceHalf * traceHalf - detCov));
                float lambdaMax = max(traceHalf + radiusTerm, 1.0);
                float radiusPixels = clamp(3.0 * sqrt(lambdaMax), _MinScreenRadius, _MaxScreenRadius);
                radiusPixels = lerp(radiusPixels, fallbackRadius, _ForceScreenRadius);

                float2 radiusClip = 2.0 * radiusPixels / _ScreenParams.xy;

                float invXX = covYY / detCov;
                float invXY = -covXY / detCov;
                float invYY = covXX / detCov;

                float2 corner = GetQuadCorner(cornerId);

                v2f o;
                o.pos = centerClip;
                o.pos.xy += corner * radiusClip * centerClip.w;
                o.pos.xy *= visible;
                o.color = g.color;
                o.color.a *= visible;
                o.localUv = corner;
                o.pixelOffset = corner * radiusPixels;
                o.invCovDiag = float2(invXX, invYY);
                o.invCovXY = invXY;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 d = i.pixelOffset;
                float power = -0.5 * (
                    i.invCovDiag.x * d.x * d.x +
                    2.0 * i.invCovXY * d.x * d.y +
                    i.invCovDiag.y * d.y * d.y
                );

                if (power < -9.0) discard;

                fixed4 color = i.color;
                color.a *= exp(power);
                if (color.a <= 0.001) discard;
                return color;
            }
            ENDCG
        }
    }
}
