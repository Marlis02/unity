using UnityEditor;

// FBX files dropped into Assets/Characters/Mixamo import as Humanoid clips that play on main_hero (any humanoid).
// Download from mixamo.com as "FBX for Unity", "Without Skin", 30 fps; tick "In Place" for walks and runs.
// Only first imports are configured, so later changes made in the Inspector stick.
class MixamoImportPostprocessor : AssetPostprocessor
{
    const string Folder = "Assets/Characters/Mixamo/";
    static readonly string[] LoopingWords = { "idle", "walk", "run", "talk", "dance", "breath", "wave", "clap", "jog", "sit" };

    void OnPreprocessModel()
    {
        if (!assetPath.StartsWith(Folder) || !assetImporter.importSettingsMissing) return;
        var importer = (ModelImporter)assetImporter;
        importer.animationType = ModelImporterAnimationType.Human;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.materialImportMode = ModelImporterMaterialImportMode.None;
        importer.importCameras = false;
        importer.importLights = false;
    }

    void OnPreprocessAnimation()
    {
        if (!assetPath.StartsWith(Folder) || !assetImporter.importSettingsMissing) return;
        var importer = (ModelImporter)assetImporter;
        string name = System.IO.Path.GetFileNameWithoutExtension(assetPath);
        string lower = name.ToLowerInvariant();
        bool loop = false;
        foreach (var word in LoopingWords) loop |= lower.Contains(word);

        var clips = importer.defaultClipAnimations;
        foreach (var clip in clips)
        {
            clip.name = name; // Mixamo names every take "mixamo.com"
            clip.loopTime = loop;
            clip.lockRootRotation = true;
            clip.lockRootHeightY = true;
            clip.keepOriginalOrientation = true;
            clip.keepOriginalPositionY = true;
            clip.keepOriginalPositionXZ = true;
        }
        importer.clipAnimations = clips;
    }
}
