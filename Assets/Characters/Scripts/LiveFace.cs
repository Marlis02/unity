using UnityEngine;

// A Roblox-style face drawn by the Roblox/LiveFace shader from these numbers, so one expression flows into the next
// frame by frame instead of switching decals (RobloxFace). Clips key the fields; the face's renderer follows them.
// The defaults are his resting face, Chill Face (the user's pick, 2026-10-09): sleepy lidded eyes, an easy half smile.
[ExecuteAlways]
public class LiveFace : MonoBehaviour
{
    [Range(0f, 1f)] public float eyeOpen = 1f;     // 0: shut (a blink)
    [Range(0f, 1f)] public float eyeHappy;         // 1: curved into happy arcs (^ ^)
    [Range(-1f, 1f)] public float lookX, lookY;    // where he looks, his right is -X from the front
    [Range(0f, 1f)] public float brow = 0.6f;      // how much the brows show
    [Range(-1f, 1f)] public float browAngle;       // +1 angry (inner ends down), -1 worried (inner ends up)
    [Range(-1f, 1f)] public float browRaise = -0.2f;
    [Range(-1f, 1f)] public float smile = 0.7f;    // -1 frown, +1 big smile
    [Range(0f, 1f)] public float mouthOpen;
    [Range(0.4f, 1.6f)] public float mouthWidth = 1f;
    [Range(0f, 1f)] public float teeth;
    [Range(0f, 1f)] public float eyeWhite = 1f;    // white eyes with a pupil; 0: black ovals
    [Range(0f, 1f)] public float lids = 0.55f;     // upper lids drooping over them (Chill, Man Face)
    [Range(0f, 1f)] public float browThick;        // thick bent brows (Man Face)
    [Range(0f, 1f)] public float squareEyes;       // square eyes (Lol)
    [Range(-1f, 1f)] public float smirk = 0.35f;   // one mouth corner up
    [Range(0f, 1f)] public float blush;
    [Range(0f, 1f)] public float gritted;          // clenched teeth (angry)
    [Range(0f, 1f)] public float tears;            // tears from the eyes (laughing, crying)
    [Range(0f, 1f)] public float cheeks;           // puffed cheeks (mouth full)
    [Range(0f, 1f)] public float wobble;           // a wobbly mouth (scared, mouth full)
    [Range(0f, 1f)] public float pupilSmall;       // tiny pupils (scared)
    [Range(0f, 1f)] public float sweat;            // a sweat drop
    [Range(-1f, 1f)] public float lidTilt;         // lids slanting: + angry (inner corners down), - sad
    [Range(0f, 1f)] public float squeeze;          // eyes squeezed shut (> <)
    [Range(0f, 1f)] public float creases;          // creases between the brows, under the eyes, beside the mouth
    [Range(0f, 1f)] public float mouthSquare;      // a boxy screaming mouth
    [Range(0f, 1f)] public float bold;             // heavier brush lines
    [Range(0f, 1f)] public float gums;             // gums over the teeth (a cringe grin)
    [Range(0f, 1f)] public float tongueOut;        // tongue stuck out (disgust)
    [Range(-1f, 1f)] public float browAsym;        // one brow up, the other down
    [Range(-1f, 1f)] public float eyeAsym;         // one eye narrower
    [Range(0f, 1f)] public float eyeRound;         // big round eyes with big shiny pupils (the cartoon references)
    [Range(-1f, 1f)] public float eyeCross;        // pupils crossing in (silly)
    [Range(0f, 1f)] public float browArc;          // thin arched brows
    [Range(0f, 1f)] public float mouthD;           // a D mouth: flat top, round bottom (a big laugh)

    static readonly int EyeId = Shader.PropertyToID("_Eye"), BrowId = Shader.PropertyToID("_Brow"), MouthId = Shader.PropertyToID("_Mouth"),
        StyleId = Shader.PropertyToID("_Style"), ExtraId = Shader.PropertyToID("_Extra"), MoreId = Shader.PropertyToID("_More"),
        ShapeId = Shader.PropertyToID("_Shape"), Shape2Id = Shader.PropertyToID("_Shape2"), AsymId = Shader.PropertyToID("_Asym"),
        RefId = Shader.PropertyToID("_Ref");
    MaterialPropertyBlock block;

    void OnEnable() => Apply();
    void OnValidate() => Apply();
    void LateUpdate() => Apply();
    void OnDidApplyAnimationProperties() => Apply();

    public void Apply()
    {
        var target = GetComponent<Renderer>();
        if (target == null) return;
        if (block == null) block = new MaterialPropertyBlock();
        target.GetPropertyBlock(block);
        block.SetVector(EyeId, new Vector4(eyeOpen, eyeHappy, lookX, lookY));
        block.SetVector(BrowId, new Vector4(brow, browAngle, browRaise, 0f));
        block.SetVector(MouthId, new Vector4(smile, mouthOpen, mouthWidth, teeth));
        block.SetVector(StyleId, new Vector4(eyeWhite, lids, browThick, squareEyes));
        block.SetVector(ExtraId, new Vector4(smirk, blush, gritted, tears));
        block.SetVector(MoreId, new Vector4(cheeks, wobble, pupilSmall, sweat));
        block.SetVector(ShapeId, new Vector4(lidTilt, squeeze, creases, mouthSquare));
        block.SetVector(Shape2Id, new Vector4(bold, gums, tongueOut, 0f));
        block.SetVector(AsymId, new Vector4(browAsym, eyeAsym, 0f, 0f));
        block.SetVector(RefId, new Vector4(eyeRound, eyeCross, browArc, mouthD));
        target.SetPropertyBlock(block);
    }
}
