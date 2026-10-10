// A Roblox-style face drawn from a few numbers (LiveFace sets them), so an expression flows smoothly into the next one
// instead of switching decals: black oval eyes that close, squint and curve into happy arcs, look about with a white
// glint; brows that rise, frown or worry; a mouth that smiles, frowns and opens with a dark inside, teeth and tongue.
// Everything is a signed distance in the face's UV square (centre 0.5, 0.5), antialiased by its screen-space slope.
Shader "Roblox/LiveFace"
{
    Properties
    {
        _Ink ("Ink", Color) = (0.2, 0.12, 0.08, 1)
        _Sclera ("Whites of the eyes", Color) = (1, 0.97, 0.88, 1)
        _Inside ("Mouth inside", Color) = (0.32, 0.05, 0.08, 1)
        _Tongue ("Tongue", Color) = (0.86, 0.36, 0.42, 1)
        _Eye ("Eye: open, happy, look x, look y", Vector) = (1, 0, 0, 0)
        _Brow ("Brow: show, angle, raise, -", Vector) = (0, 0, 0, 0)
        _Mouth ("Mouth: smile, open, width, teeth", Vector) = (0.6, 0, 1, 0)
        _Style ("Style: white eyes, lids, thick brows, square eyes", Vector) = (0, 0, 0, 0)
        _Extra ("Extra: smirk, blush, gritted teeth, tears", Vector) = (0, 0, 0, 0)
        _More ("More: puffed cheeks, wobbly mouth, small pupils, sweat", Vector) = (0, 0, 0, 0)
        _Shape ("Shape: lid tilt, squeezed shut, creases, boxy mouth", Vector) = (0, 0, 0, 0)
        _Shape2 ("Shape: bold lines, gums, tongue out, -", Vector) = (0, 0, 0, 0)
        _Asym ("Asymmetry: brows, eyes, -, -", Vector) = (0, 0, 0, 0)
        _Ref ("Cartoon: round eyes, cross-eyed, arched thin brows, D mouth", Vector) = (0, 0, 0, 0)
        _Size ("Size of the features", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
            half4 _Ink, _Inside, _Tongue, _Sclera;
            float4 _Eye, _Brow, _Mouth, _Style, _Extra, _More, _Shape, _Shape2, _Asym, _Ref;
            float _Size;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                return o;
            }

            // Coverage of a shape from its signed distance (negative inside)
            static const float EyeSize = 1.2; // the user: eyes bigger (1.1, then 1.2)
            float Fill(float d) { return saturate(0.5 - d / max(fwidth(d), 1e-5)); }
            float Ellipse(float2 q, float2 r) { return (length(q / r) - 1.0) * min(r.x, r.y); }
            float Box(float2 q, float2 r, float round) { float2 d = abs(q) - r + round; return length(max(d, 0)) + min(max(d.x, d.y), 0) - round; }
            float Segment(float2 p, float2 a, float2 b)
            {
                float2 pa = p - a, ba = b - a;
                float h = saturate(dot(pa, ba) / dot(ba, ba));
                return length(pa - ba * h);
            }
            // A brush stroke from a to b, wa thick at a and wb at b
            float Taper(float2 p, float2 a, float2 b, float wa, float wb)
            {
                float2 pa = p - a, ba = b - a;
                float h = saturate(dot(pa, ba) / dot(ba, ba));
                return length(pa - ba * h) - lerp(wa, wb, h);
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 p = (i.uv - 0.5) / _Size;
                float open = max(_Eye.x, 0.07), happy = saturate(_Eye.y);
                float2 look = _Eye.zw;
                float ink = 0, glint = 0, white = 0, blush = 0, water = 0;
                float whiteEyes = saturate(_Style.x), lids = saturate(_Style.y), thick = saturate(_Style.z), square = saturate(_Style.w);
                float tilt = _Shape.x, squeeze = saturate(_Shape.y), creases = saturate(_Shape.z * 3.0), boxy = saturate(_Shape.w); // creases solid, never grey
                float weight = 1 + 0.6 * saturate(_Shape2.x); // bold lines
                float eyesShown = 1 - squeeze;
                float roundEyes = saturate(_Ref.x), cross = _Ref.y, arch = saturate(_Ref.z), dMouth = saturate(_Ref.w);
                float pupilShine = 0;

                float openBoth = open;
                for (int s = -1; s <= 1; s += 2)
                {
                    // One eye narrower than the other (asymmetry +: his left, the viewer's right)
                    open = openBoth * (1 - 0.55 * saturate(s * _Asym.y));
                    float2 c = float2(s * 0.165, 0.11) + look * float2(0.022, 0.014);
                    float2 q = p - c;
                    // Oval eye, squashed shut by `open`; morphing into an arc (^) as it gets happy
                    float oval = Ellipse(q, float2(0.052, 0.082 * open) * EyeSize);
                    float arc = max(abs(Ellipse(q + float2(0, 0.035) * EyeSize, float2(0.058, 0.06) * EyeSize)) - 0.013 * weight, -(q.y + 0.03 * EyeSize));
                    float eyeInk = Fill(lerp(oval, arc, happy)) * (1 - whiteEyes);
                    // White eyes: outlined, a black pupil, an upper lid drooping over them and slanting with the mood
                    // (tilt + : inner corner down, angry; - : inner corner up, sad)
                    float2 qe = p - float2(s * 0.165, 0.11);
                    float2 scleraR = lerp(float2(0.068, 0.046), float2(0.066, 0.078), roundEyes) * EyeSize * float2(1, open);
                    float sclera = lerp(Ellipse(qe, scleraR), Box(qe, float2(0.07, 0.042 * open) * EyeSize, 0.01), square);
                    float inward = -s * qe.x / (0.07 * EyeSize);
                    float lidY = scleraR.y * (1 - 1.6 * lids) - tilt * 0.032 * EyeSize * inward;
                    float shown = max(sclera, qe.y - lidY);
                    float2 pc = look * float2(0.03, 0.02) * (1 + 0.8 * roundEyes) + float2(-s * cross * 0.045, -0.006);
                    float pupilR = 0.024 * EyeSize * (1 - 0.3 * square) * (1 + 0.45 * roundEyes) * (1 - 0.6 * saturate(_More.z));
                    float pupil = length(qe - pc) - pupilR;
                    pupilShine = max(pupilShine, Fill(length(qe - pc - float2(-0.35, 0.35) * pupilR) - 0.3 * pupilR) * roundEyes * Fill(max(pupil, shown)) * eyesShown);
                    eyeInk = max(eyeInk, whiteEyes * max(Fill(abs(sclera) - 0.006 * weight) * Fill(qe.y - lidY - 0.004), Fill(max(pupil, shown))));
                    eyeInk = max(eyeInk, whiteEyes * saturate(max(lids, abs(tilt)) * 3) * Fill(max(abs(qe.y - lidY) - 0.009 * weight, abs(qe.x) - 0.075 * EyeSize)));
                    // Lower lid: a thin bag line under the white eye
                    float bag = Taper(p, float2(s * 0.115, 0.11 - 0.06 * EyeSize), float2(s * 0.21, 0.11 - 0.052 * EyeSize), 0.003, 0.006 * weight);
                    eyeInk = max(eyeInk, whiteEyes * (1 - roundEyes) * Fill(bag));
                    // Squeezed shut (> <): two brush strokes meeting at the inner corner
                    float2 tip = float2(s * 0.12, 0.11), top = float2(s * 0.215, 0.16), bottom = float2(s * 0.215, 0.065);
                    float chevron = min(Taper(p, top, tip, 0.006, 0.016 * weight), Taper(p, bottom, tip, 0.006, 0.016 * weight));
                    ink = max(ink, max(eyeInk * eyesShown, Fill(chevron) * squeeze));
                    white = max(white, Fill(shown) * whiteEyes * eyesShown);
                    // Tears streaming from the eye's outer corner
                    float2 tq = p - float2(s * 0.215, 0.045);
                    float tear = max(Ellipse(tq + float2(0, 0.05), float2(0.022, 0.07)), -tq.y - 0.11);
                    water = max(water, Fill(tear) * saturate(_Extra.w));
                    // Puffed cheeks: a round bulge line each side of the mouth
                    float2 cq = p - float2(s * 0.17, -0.13);
                    float cheekArc = max(abs(length(cq) - 0.075) - 0.008, -(cq.x * s) + 0.02);
                    ink = max(ink, Fill(cheekArc) * saturate(_More.x));
                    blush = max(blush, Fill(length(cq) - 0.06) * saturate(_More.x) * 0.6);
                    // Blush under the eye
                    blush = max(blush, Fill(Ellipse(p - float2(s * 0.2, -0.02), float2(0.07, 0.03))) * saturate(_Extra.y));
                    // The glint shows where he looks, gone as the eye closes or curves
                    float2 g = q - float2(-0.016, 0.03 * open) * EyeSize - look * float2(0.018, 0.012);
                    glint = max(glint, Fill(length(g) - 0.014 * EyeSize) * saturate(open * 1.6 - 0.6) * (1 - happy) * Fill(oval) * eyesShown * (1 - whiteEyes)); // white eyes have their own shine

                    // Brow: a brush stroke over the eye, thickest in the middle, inner end lowered by angle (+ angry, - worried)
                    float y = 0.245 + _Brow.z * 0.045 - happy * 0.01 + s * _Asym.x * 0.04; // asymmetry: one brow up
                    float2 inner = float2(s * 0.095, y - _Brow.y * 0.035);
                    float2 outer = float2(s * 0.225, y + _Brow.y * 0.02);
                    float2 mid = (inner + outer) * 0.5 + float2(0, 0.012 * thick + 0.03 * arch);
                    float slim = lerp(1, 0.5, arch);
                    float wIn = (0.013 + 0.01 * thick) * weight * slim, wMid = (0.017 + 0.014 * thick) * weight * slim, wOut = 0.007 * weight;
                    float browD = min(Taper(p, inner, mid, wIn, wMid), Taper(p, mid, outer, wMid, wOut));
                    ink = max(ink, Fill(browD) * saturate(_Brow.x * 4.0)); // shown solid, never grey
                    // Creases: between the brows and under the eyes
                    float crease = min(Taper(p, float2(s * 0.035, 0.3), float2(s * 0.055, 0.225), 0.004, 0.009 * weight),
                                       Taper(p, float2(s * 0.12, 0.025), float2(s * 0.215, 0.04), 0.009 * weight, 0.004));
                    ink = max(ink, Fill(crease) * creases);
                }

                // Mouth: a curve that smiles (corners up) or frowns, opening downward from it; or (boxy) a rounded trapezoid
                // hanging from it, wider at the top, for a scream
                float smile = _Mouth.x, mouthOpen = saturate(_Mouth.y), w = 0.15 * _Mouth.z;
                float t = clamp(p.x / w, -1, 1);
                float y0 = -0.16;
                float curve = y0 + smile * 0.07 * (t * t - 0.5) + _Extra.x * 0.06 * max(t, 0) * max(t, 0) // smirk: one corner up
                    + _More.y * 0.012 * sin(t * 9.42);                                                    // wobble
                float bulge = 1 - t * t;
                float boxTop = y0 + 0.012 - smile * 0.02, boxBottom = boxTop - mouthOpen * 0.19;
                float wy = w * lerp(0.72, 1.0, saturate((p.y - boxBottom) / max(boxTop - boxBottom, 1e-3)));
                float boxD = max(abs(p.x) - wy + 0.012, max(p.y - boxTop, boxBottom - p.y) + 0.012) - 0.012;
                float dTop = y0 + 0.03;
                float upper = lerp(lerp(curve + mouthOpen * 0.012 * bulge, boxTop, boxy), dTop, dMouth);
                float lower = lerp(lerp(curve - mouthOpen * 0.15 * bulge * (0.6 + 0.4 * saturate(smile + 0.5)), boxBottom, boxy),
                    dTop - mouthOpen * 0.21 * sqrt(bulge), dMouth);
                float across = abs(p.x) - w;
                float inside = lerp(lerp(max(across, max(p.y - upper, lower - p.y)), boxD, boxy), max(across, max(p.y - upper, lower - p.y)), dMouth);
                float stroke = length(float2(max(across, 0), p.y - curve)) - 0.013 * weight;
                ink = max(ink, Fill(min(stroke * (1 - boxy * saturate(mouthOpen * 4)) + boxy * saturate(mouthOpen * 4), inside - 0.012 * weight)));
                float hole = Fill(inside) * saturate(mouthOpen * 8);
                float teeth = hole * Fill(upper - 0.035 * _Mouth.w - p.y) * step(0.01, _Mouth.w);
                // Gritted teeth: the whole opening white, split along the middle and between the teeth; gums above them
                float grit = saturate(_Extra.z), gums = saturate(_Shape2.y);
                float midY = (upper + lower) * 0.5;
                float gumLine = upper - 0.024 * gums;
                float gaps = min(abs(p.y - midY) - 0.005, abs(frac(p.x / 0.05 + 0.5) - 0.5) * 0.05 - 0.004); // big separate teeth
                teeth = max(teeth, hole * grit);
                float gum = hole * gums * Fill(gumLine - p.y);
                float toothLines = hole * grit * Fill(gaps) * (1 - gum);
                float tongue = hole * (1 - grit) * Fill(Ellipse(p - float2(0, lower + 0.02), float2(w * 0.55, 0.045)))
                    * saturate((mouthOpen - 0.35) * 4.0) * saturate((_Mouth.z - 0.7) * 5.0); // only in a wide-open mouth
                // Tongue stuck out over the lower lip
                float tongueOut = saturate(_Shape2.z);
                float lick = max(Ellipse(p - float2(w * 0.25, lower - 0.035), float2(0.045, 0.055)), p.y - lower + 0.005);
                ink = max(ink, Fill(lick) * tongueOut);
                float lickInside = Fill(lick + 0.006 * weight) * tongueOut;

                half3 col = _Ink.rgb;
                ink = max(ink, 0);
                col = lerp(col, _Inside.rgb, hole);
                col = lerp(col, _Tongue.rgb, max(tongue, lickInside));
                col = lerp(col, half3(1, 1, 1), max(max(teeth, glint), pupilShine));
                col = lerp(col, half3(0.93, 0.5, 0.56), gum);
                col = lerp(col, _Ink.rgb, toothLines);
                // A sweat drop on his forehead
                float2 sq = p - float2(0.3, 0.27);
                float drop = min(Ellipse(sq, float2(0.028, 0.04)), max(Segment(sq, float2(0, 0.02), float2(0, 0.075)) - 0.006 - (0.075 - sq.y) * 0.25, -sq.y));
                water = max(water, Fill(drop) * saturate(_More.w));
                ink = max(ink, Fill(abs(drop) - 0.004) * saturate(_More.w));
                // White of the eyes under the ink, blush under everything
                float a = max(ink, max(hole, glint));
                col = lerp(_Sclera.rgb, col, saturate(a / max(max(a, white), 1e-4)));
                a = max(a, white);
                col = lerp(half3(0.95, 0.45, 0.5), col, saturate(a / max(max(a, blush * 0.55), 1e-4)));
                a = max(a, blush * 0.55);
                // Tears and sweat: light blue, over the skin but under the lines
                col = lerp(col, half3(0.55, 0.8, 1.0), saturate(water) * (1 - saturate(ink)));
                return half4(col, max(a, water * 0.9));
            }
            ENDHLSL
        }
    }
}
