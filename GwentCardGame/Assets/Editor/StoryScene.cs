using System.Linq;
using Gwent.Story;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;

namespace Gwent.EditorTools
{
    // The story's map scene (Assets/Scenes/Story.unity), made here rather than by hand: a camera as the menu's (black,
    // orthographic), the EventSystem of the new input system, and the StoryScreen, which shows everything in UI Toolkit;
    // listed among the build's scenes. Menu Gwent > Story > Make the map scene (again).
    public static class StoryScene
    {
        public const string Path = "Assets/Scenes/Story.unity";

        [MenuItem("Gwent/Story/Make the map scene")]
        public static void Make()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            GameObject camera = new GameObject("Main Camera");
            camera.tag = "MainCamera";
            Camera view = camera.AddComponent<Camera>();
            view.clearFlags = CameraClearFlags.SolidColor;
            view.backgroundColor = Color.black;
            view.orthographic = true;
            view.depth = -1;

            GameObject events = new GameObject("EventSystem");
            events.AddComponent<EventSystem>();
            events.AddComponent<InputSystemUIInputModule>();

            new GameObject("Story").AddComponent<StoryScreen>();

            EditorSceneManager.SaveScene(scene, Path);
            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
            if (!scenes.Any(listed => listed.path == Path))
                EditorBuildSettings.scenes = scenes.Concat(new[] { new EditorBuildSettingsScene(Path, true) }).ToArray();
            AssetDatabase.SaveAssets();
            Debug.Log("The story's map scene: " + Path);
        }
    }
}
