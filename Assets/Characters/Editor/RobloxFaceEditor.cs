using UnityEditor;

// Shows RobloxFace.emotion as a named dropdown (the field stays an int so clips can animate it).
[CustomEditor(typeof(RobloxFace)), CanEditMultipleObjects]
public class RobloxFaceEditor : Editor
{
    static readonly string[] Names = System.Enum.GetNames(typeof(RobloxFace.Emotion));

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        var emotion = serializedObject.FindProperty("emotion");
        EditorGUI.showMixedValue = emotion.hasMultipleDifferentValues;
        EditorGUI.BeginChangeCheck();
        int value = EditorGUILayout.Popup("Emotion", emotion.intValue, Names);
        if (EditorGUI.EndChangeCheck()) emotion.intValue = value;
        EditorGUI.showMixedValue = false;
        EditorGUILayout.PropertyField(serializedObject.FindProperty("talk"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("blink"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("autoBlink"));
        serializedObject.ApplyModifiedProperties();
    }
}
