using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class KTHCalibratedTopDownSceneBuilder
{
    public const string ScenePath = "Assets/KTH/Scenes/KTHCalibratedTopDownPrototype.unity";

    [MenuItem("KTH/Create Calibrated TopDown Prototype Scene")]
    public static void CreateScene()
    {
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        GameObject bootstrapObject = new GameObject("KTH Calibrated TopDown Bootstrap");
        AdaptivePrototypeBootstrap bootstrap = bootstrapObject.AddComponent<AdaptivePrototypeBootstrap>();
        bootstrap.buildOnAwake = true;
        bootstrap.startGameOnPlay = false;
        bootstrap.runCalibrationBeforeGame = true;
        bootstrap.gameStartStage = 1;

        bootstrap.centerTapsPerButton = 8;
        bootstrap.reciprocalAlternationPairs = 10;
        bootstrap.boundaryTapsPerButton = 4;
        bootstrap.ambiguousTapsPerButton = 4;
        bootstrap.contextTapsPerState = 4;

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.ImportAsset(ScenePath);
        Debug.Log($"Created calibrated top-down prototype scene at {ScenePath}.");
    }

    public static void CreateSceneFromCommandLine()
    {
        CreateScene();
    }
}
