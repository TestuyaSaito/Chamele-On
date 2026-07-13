using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class ChameleonGardenAudit
{
    private const string GardenScenePath = "Assets/Scenes/Garden/GardenScene.unity";
    private const string PreviewPath = "/tmp/chameleon-urp-garden-original.png";

    public static void CaptureOriginalGarden()
    {
        Scene scene = EditorSceneManager.OpenScene(GardenScenePath, OpenSceneMode.Single);
        Physics.SyncTransforms();

        Camera[] cameras = Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        Camera selected = Camera.main;
        if (selected == null && cameras.Length > 0)
        {
            selected = cameras[0];
        }

        if (selected == null)
        {
            throw new System.InvalidOperationException("Garden scene contains no camera.");
        }

        int colliderCount = Object.FindObjectsByType<Collider>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
        int rendererCount = Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length;
        Debug.Log($"CHAMELEON_GARDEN_AUDIT: {scene.rootCount} roots, {rendererCount} renderers, " +
                  $"{colliderCount} colliders, {cameras.Length} cameras. Selected {selected.name} at {selected.transform.position}.");

        Capture(selected, PreviewPath, 1600, 900);
        Debug.Log($"CHAMELEON_GARDEN_CAPTURED: {PreviewPath}");
    }

    private static void Capture(Camera camera, string path, int width, int height)
    {
        var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
        {
            antiAliasing = 1
        };
        RenderTexture previousTarget = camera.targetTexture;
        RenderTexture previousActive = RenderTexture.active;
        camera.targetTexture = target;
        camera.Render();
        RenderTexture.active = target;

        var image = new Texture2D(width, height, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
        image.Apply(false, false);
        File.WriteAllBytes(path, image.EncodeToPNG());

        camera.targetTexture = previousTarget;
        RenderTexture.active = previousActive;
        Object.DestroyImmediate(image);
        Object.DestroyImmediate(target);
    }
}
