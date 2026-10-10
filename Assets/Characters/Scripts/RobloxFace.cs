using UnityEngine;

// Roblox-style face on a quad: shows one cell of Faces_Atlas.png (Tools/Characters/Build Face Atlas).
// Animate `emotion`, `talk` and `blink` from clips or Timeline; cell = emotion * 4 + talk * 2 + blink,
// column = cell % 8, row = cell / 8 counted from the bottom of the texture.
[ExecuteAlways]
[RequireComponent(typeof(Renderer))]
public class RobloxFace : MonoBehaviour
{
    public enum Emotion { Smile, Grin, Neutral, Shocked, Scared, Angry, Sad, Crying, Smug, Bruh, Laugh, Surprised, Love, Suspicious, Evil, Dizzy, Troll }
    public const int EmotionCount = 17, Columns = 8, Rows = (EmotionCount * 4 + Columns - 1) / Columns;

    [UnityEngine.Animations.DiscreteEvaluation]
    public int emotion;  // an Emotion; an int so clips can key it (stepped, never blended)
    public bool talk;    // mouth open: flap it on and off with the voice line
    public bool blink;
    [Tooltip("Blink on its own every few seconds in Play Mode (recordings run in Play Mode).")]
    public bool autoBlink = true;

    static readonly int BaseMapSt = Shader.PropertyToID("_BaseMap_ST");
    MaterialPropertyBlock block;
    Renderer rend;
    int appliedCell = -1;

    public Emotion Current { get => (Emotion)emotion; set => emotion = (int)value; }

    // Keyframe value for `emotion` in clips built from code: discrete curves store the int's bits, not the number
    public static float EmotionKey(Emotion e) => System.BitConverter.Int32BitsToSingle((int)e);

    void OnEnable() { appliedCell = -1; Apply(); }
    void OnValidate() => appliedCell = -1;
    void LateUpdate() => Apply();
    void OnDidApplyAnimationProperties() => Apply();

    // 0.12 s blink every 3.4 s, phase-shifted per character name so a group doesn't blink in sync
    bool AutoBlinkNow()
    {
        if (!autoBlink || !Application.isPlaying) return false;
        uint h = 2166136261;
        foreach (char c in transform.root.name) h = (h ^ c) * 16777619;
        return Mathf.Repeat(Time.time + h % 1000 * 0.0034f, 3.4f) < 0.12f;
    }

    // Called every frame; call it yourself after changing fields from an editor script
    public void Apply()
    {
        if (rend == null) rend = GetComponent<Renderer>();
        int cell = Mathf.Clamp(emotion, 0, EmotionCount - 1) * 4 + (talk ? 2 : 0) + (blink || AutoBlinkNow() ? 1 : 0);
        if (cell == appliedCell) return;
        appliedCell = cell;
        block ??= new MaterialPropertyBlock();
        rend.GetPropertyBlock(block);
        block.SetVector(BaseMapSt, new Vector4(1f / Columns, 1f / Rows, cell % Columns / (float)Columns, cell / Columns / (float)Rows));
        rend.SetPropertyBlock(block);
    }
}
