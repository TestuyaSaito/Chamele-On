using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Playables;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

public static class ChameleonURPBuildTools
{
    private const string SourceScenePath = "Assets/Scenes/Garden/GardenScene.unity";
    private const string PrototypeScenePath = "Assets/ChameleON/Scenes/GardenPrototype.unity";
    private const string AppIconPath = "Assets/ChameleON/Branding/ChameleON-AppIcon-1024.png";
    private const string BundleId = "com.chameleon.prototype";
    private const string TeamId = "PY57847KYX";
    private const string IOSQualityPlatformName = "iPhone";

    [MenuItem("ChameleON URP/Prepare Garden Prototype")]
    public static void PrepareGardenPrototype()
    {
        Directory.CreateDirectory("Assets/ChameleON/Scenes");
        Scene scene = EditorSceneManager.OpenScene(SourceScenePath, OpenSceneMode.Single);

        GameObject sampleController = GameObject.Find("FPS_Controller");
        Vector3 samplePosition = sampleController != null
            ? sampleController.transform.position
            : new Vector3(-18.8f, 0.9f, 77.55f);
        Quaternion sampleRotation = sampleController != null
            ? sampleController.transform.rotation
            : Quaternion.Euler(0f, 110f, 0f);

        if (sampleController != null)
        {
            Object.DestroyImmediate(sampleController);
        }

        DestroyIfPresent("Cameras");
        DestroyIfPresent("SceneSetup");
        DestroyIfPresent("RuntimeDataCanvas");
        DestroyIfPresent("ChameleON Prototype");
        DestroyIfPresent("PlayerSpawn");
        DestroyIfPresent("Mobile Interface");
        DestroyIfPresent("White Hider");
        DestroyIfPresent("Prototype Spawn Floor Collider");

        GameObject gardenRoot = FindSceneObject("Root");
        if (gardenRoot != null)
        {
            DestroyChildIfPresent(gardenRoot.transform, "Media");
            DestroyChildIfPresent(gardenRoot.transform, "TerminalLoader_01_Prefab");
            DestroyChildIfPresent(gardenRoot.transform, "ScreensB Variant");
        }

        foreach (PlayableDirector director in Object.FindObjectsByType<PlayableDirector>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            director.enabled = false;
        }

        foreach (Camera camera in Object.FindObjectsByType<Camera>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            Object.DestroyImmediate(camera.gameObject);
        }

        Physics.SyncTransforms();
        Quaternion spawnRotation = Quaternion.Euler(0f, sampleRotation.eulerAngles.y, 0f);
        // The sample FPS start leaves a third-person camera almost against the
        // room behind it. Give the imported Garden camera real rear clearance.
        Vector3 spawnPosition = FindGroundedSpawn(samplePosition) + spawnRotation * Vector3.forward * 1.2f;
        var spawn = new GameObject("PlayerSpawn");
        spawn.transform.SetPositionAndRotation(spawnPosition, spawnRotation);

        var spawnFloor = new GameObject("Prototype Spawn Floor Collider", typeof(BoxCollider));
        spawnFloor.transform.position = spawnPosition - Vector3.up * 0.065f;
        BoxCollider spawnFloorCollider = spawnFloor.GetComponent<BoxCollider>();
        spawnFloorCollider.size = new Vector3(11f, 0.12f, 11f);

        var cameraObject = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener),
            typeof(UniversalAdditionalCameraData));
        cameraObject.tag = "MainCamera";
        cameraObject.transform.position = spawnPosition + spawn.transform.rotation * new Vector3(0.65f, 1.65f, -3.4f);
        cameraObject.transform.LookAt(spawnPosition + Vector3.up * 1.25f);
        Camera gameCamera = cameraObject.GetComponent<Camera>();
        gameCamera.fieldOfView = 55f;
        gameCamera.nearClipPlane = 0.06f;
        gameCamera.farClipPlane = 300f;
        gameCamera.allowHDR = true;
        gameCamera.allowMSAA = true;

        UniversalAdditionalCameraData cameraData = cameraObject.GetComponent<UniversalAdditionalCameraData>();
        cameraData.renderPostProcessing = true;
        cameraData.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
        cameraData.stopNaN = true;
        cameraData.dithering = true;

        if (Object.FindFirstObjectByType<EventSystem>() == null)
        {
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        }

        var controller = new GameObject("ChameleON Prototype");
        controller.AddComponent<ChameleonPrototypeController>();

        EditorSceneManager.SaveScene(scene, PrototypeScenePath, true);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(PrototypeScenePath, true) };
        ConfigurePlayerSettings();
        AssetDatabase.SaveAssets();
        Debug.Log($"CHAMELEON_URP_SCENE_READY: Garden prototype saved at {PrototypeScenePath}; spawn {spawnPosition}.");
    }

    private static Vector3 FindGroundedSpawn(Vector3 samplePosition)
    {
        // The official FPS controller root is authored at character-foot height.
        // A downward ray at this X/Z can see the decorative terrain below the
        // elevated tatami room and incorrectly sink the character into the floor.
        return samplePosition;
    }

    private static void DestroyIfPresent(string objectName)
    {
        GameObject instance = FindSceneObject(objectName);
        if (instance != null)
        {
            Object.DestroyImmediate(instance);
        }
    }

    private static GameObject FindSceneObject(string objectName)
    {
        foreach (Transform transform in Object.FindObjectsByType<Transform>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (transform != null && transform.gameObject.scene.IsValid() && transform.name == objectName)
            {
                return transform.gameObject;
            }
        }

        return null;
    }

    private static void DestroyChildIfPresent(Transform parent, string childName)
    {
        Transform child = parent.Find(childName);
        if (child != null)
        {
            Object.DestroyImmediate(child.gameObject);
        }
    }

    private static void ConfigurePlayerSettings()
    {
        PlayerSettings.companyName = "Chamele-ON";
        PlayerSettings.productName = "Chamele-ON Prototype";
        PlayerSettings.bundleVersion = "0.1.0";
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.iOS, BundleId);
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.LandscapeLeft;
        PlayerSettings.allowedAutorotateToPortrait = false;
        PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
        PlayerSettings.allowedAutorotateToLandscapeLeft = true;
        PlayerSettings.allowedAutorotateToLandscapeRight = true;
        PlayerSettings.SetScriptingBackend(NamedBuildTarget.iOS, ScriptingImplementation.IL2CPP);
        PlayerSettings.iOS.appleDeveloperTeamID = TeamId;
        PlayerSettings.iOS.appleEnableAutomaticSigning = true;
        if (!int.TryParse(PlayerSettings.iOS.buildNumber, out int buildNumber) || buildNumber < 1)
        {
            PlayerSettings.iOS.buildNumber = "1";
        }
        PlayerSettings.iOS.targetDevice = iOSTargetDevice.iPhoneOnly;
        PlayerSettings.iOS.targetOSVersionString = "15.0";
        PlayerSettings.SetStaticBatchingForPlatform(BuildTarget.iOS, true);
        PlayerSettings.SetDynamicBatchingForPlatform(BuildTarget.iOS, true);
        ConfigureApplicationIcon();
        ConfigureIOSRenderPipelineBatching();

        Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
        if (assets.Length > 0)
        {
            var serializedSettings = new SerializedObject(assets[0]);
            SerializedProperty inputHandler = serializedSettings.FindProperty("activeInputHandler");
            if (inputHandler != null)
            {
                inputHandler.intValue = 2;
                serializedSettings.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        AuditIOSReadiness();
    }

    private static void ConfigureApplicationIcon()
    {
        Texture2D icon = AssetDatabase.LoadAssetAtPath<Texture2D>(AppIconPath);
        if (icon == null)
        {
            Debug.LogWarning("CHAMELEON_APP_ICON_PENDING: add the opaque 1024px icon at " + AppIconPath + ".");
            return;
        }

        int[] sizes = PlayerSettings.GetIconSizes(NamedBuildTarget.iOS, IconKind.Application);
        var icons = new Texture2D[sizes.Length];
        for (int i = 0; i < icons.Length; i++) icons[i] = icon;
        PlayerSettings.SetIcons(NamedBuildTarget.iOS, icons, IconKind.Application);
        Debug.Log("CHAMELEON_APP_ICON_READY: assigned " + icon.name + " to " + sizes.Length + " iOS icon slots.");
    }

    private static void ConfigureIOSRenderPipelineBatching()
    {
        int[] activeQualityLevels = QualitySettings.GetActiveQualityLevelsForPlatform(IOSQualityPlatformName);
        for (int i = 0; i < activeQualityLevels.Length; i++)
        {
            int qualityIndex = activeQualityLevels[i];
            if (QualitySettings.GetRenderPipelineAssetAt(qualityIndex) is not UniversalRenderPipelineAsset pipeline)
            {
                continue;
            }

            if (!pipeline.supportsDynamicBatching)
            {
                pipeline.supportsDynamicBatching = true;
                EditorUtility.SetDirty(pipeline);
            }
        }
    }

    private static void AuditIOSReadiness()
    {
        int[] activeQualityLevels = QualitySettings.GetActiveQualityLevelsForPlatform(IOSQualityPlatformName);
        string qualitySummary = string.Empty;
        for (int i = 0; i < activeQualityLevels.Length; i++)
        {
            int qualityIndex = activeQualityLevels[i];
            string qualityName = qualityIndex >= 0 && qualityIndex < QualitySettings.names.Length
                ? QualitySettings.names[qualityIndex]
                : $"index {qualityIndex}";
            UniversalRenderPipelineAsset pipeline = QualitySettings.GetRenderPipelineAssetAt(qualityIndex)
                as UniversalRenderPipelineAsset;
            string pipelineSummary = pipeline == null
                ? "no URP override"
                : $"{pipeline.name}, scale {pipeline.renderScale:0.00}, HDR {pipeline.supportsHDR}, " +
                  $"MSAA {pipeline.msaaSampleCount}x, dynamic batching {pipeline.supportsDynamicBatching}";
            qualitySummary += (i == 0 ? string.Empty : "; ") + $"{qualityName} ({pipelineSummary})";
        }

        if (activeQualityLevels.Length == 0)
        {
            Debug.LogError("CHAMELEON_IOS_QUALITY_AUDIT: iOS has no active quality level.");
            return;
        }

        // The official Mobile High profile is the least disruptive first-device
        // choice: it preserves the imported Garden look while already rendering
        // at 0.8 scale and 1x MSAA. Do not silently downgrade visual quality here;
        // profile the TestFlight build before enabling Mobile Low.
        bool hasMobileHigh = false;
        for (int i = 0; i < activeQualityLevels.Length; i++)
        {
            int qualityIndex = activeQualityLevels[i];
            if (qualityIndex >= 0 && qualityIndex < QualitySettings.names.Length &&
                QualitySettings.names[qualityIndex] == "Mobile High")
            {
                hasMobileHigh = true;
                break;
            }
        }

        if (!hasMobileHigh)
        {
            Debug.LogWarning("CHAMELEON_IOS_QUALITY_AUDIT: Mobile High is not active for iOS; " +
                             "verify visual quality before the first device build.");
        }

        Debug.Log($"CHAMELEON_IOS_READY: bundle {PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.iOS)}, " +
                  $"team {PlayerSettings.iOS.appleDeveloperTeamID}, build {PlayerSettings.iOS.buildNumber}, " +
                  $"static batching {PlayerSettings.GetStaticBatchingForPlatform(BuildTarget.iOS)}, " +
                  $"dynamic batching {PlayerSettings.GetDynamicBatchingForPlatform(BuildTarget.iOS)}. " +
                  $"Active quality: {qualitySummary}.");
    }

    [MenuItem("ChameleON URP/Build iOS Xcode Project")]
    public static void BuildIOS()
    {
        PrepareGardenPrototype();
        const string outputPath = "Builds/iOS-URP";
        Directory.CreateDirectory(outputPath);
        BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { PrototypeScenePath },
            locationPathName = outputPath,
            target = BuildTarget.iOS,
            options = BuildOptions.None
        });

        if (report.summary.result != BuildResult.Succeeded)
        {
            throw new System.Exception("URP iOS build failed: " + report.summary.result);
        }
    }

    [MenuItem("ChameleON URP/Run Garden Play Mode Smoke Test")]
    public static void RunGardenSmokeTest()
    {
        PrepareGardenPrototype();
        ChameleonURPSmokeTestRunner.Start(PrototypeScenePath);
    }
}

[InitializeOnLoad]
internal static class ChameleonURPSmokeTestRunner
{
    private const string PendingKey = "Chameleon.URP.Smoke.Pending";
    private const string ExitRequestedKey = "Chameleon.URP.Smoke.ExitRequested";
    private const string FailedKey = "Chameleon.URP.Smoke.Failed";
    private const string StartTicksKey = "Chameleon.URP.Smoke.StartTicks";
    private const string InitialPreview = "/tmp/chameleon-3d-preview.png";
    private const string PaintPreview = "/tmp/chameleon-paint-preview.png";
    private const string AttachPreview = "/tmp/chameleon-attach-preview.png";

    static ChameleonURPSmokeTestRunner()
    {
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
    }

    public static void Start(string scenePath)
    {
        DeleteIfPresent(InitialPreview);
        DeleteIfPresent(PaintPreview);
        DeleteIfPresent(AttachPreview);
        SessionState.SetBool(PendingKey, true);
        SessionState.SetBool(ExitRequestedKey, false);
        SessionState.SetBool(FailedKey, false);
        SessionState.SetString(StartTicksKey, System.DateTime.UtcNow.Ticks.ToString());
        EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
        EditorSettings.enterPlayModeOptionsEnabled = false;
        Debug.Log("CHAMELEON_URP_SMOKE_START: entering Garden Play Mode.");
        EditorApplication.isPlaying = true;
    }

    private static void Tick()
    {
        if (!SessionState.GetBool(PendingKey, false))
        {
            return;
        }

        if (SessionState.GetBool(ExitRequestedKey, false) && !EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Complete(!SessionState.GetBool(FailedKey, false));
            return;
        }

        if (EditorApplication.isPlaying && File.Exists(InitialPreview) &&
            File.Exists(PaintPreview) && File.Exists(AttachPreview))
        {
            SessionState.SetBool(ExitRequestedKey, true);
            EditorApplication.isPlaying = false;
            return;
        }

        if (ElapsedSeconds() <= 180d)
        {
            return;
        }

        Debug.LogError("CHAMELEON_URP_SMOKE_FAIL: timed out waiting for Garden previews.");
        SessionState.SetBool(FailedKey, true);
        SessionState.SetBool(ExitRequestedKey, true);
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorApplication.isPlaying = false;
        }
        else
        {
            Complete(false);
        }
    }

    private static double ElapsedSeconds()
    {
        string value = SessionState.GetString(StartTicksKey, "0");
        return long.TryParse(value, out long ticks) && ticks > 0
            ? new System.TimeSpan(System.DateTime.UtcNow.Ticks - ticks).TotalSeconds
            : 0d;
    }

    private static void Complete(bool succeeded)
    {
        SessionState.SetBool(PendingKey, false);
        SessionState.SetBool(ExitRequestedKey, false);
        SessionState.SetBool(FailedKey, false);
        Debug.Log(succeeded
            ? "CHAMELEON_URP_SMOKE_COMPLETE: all Garden runtime previews were captured."
            : "CHAMELEON_URP_SMOKE_COMPLETE: Garden runtime validation failed.");
        if (Application.isBatchMode)
        {
            EditorApplication.Exit(succeeded ? 0 : 1);
        }
    }

    private static void DeleteIfPresent(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
