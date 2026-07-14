using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

public sealed class ChameleonPrototypeController : MonoBehaviour
{
    private const float CharacterScale = 0.5f;
    private const float ExploreMovementSpeed = 3.2f * CharacterScale;
    private const float AttachedMovementSpeed = 2.1f * CharacterScale;
    private const int MinimumBrushRadius = 3;
    private const int MaximumBrushRadius = 34;
    private const int MaximumPaintUndoSteps = 12;

    private sealed class PaintTextureSnapshot
    {
        public PaintableBodyPart Part;
        public Color32[] Pixels;
    }

    private sealed class PaintUndoState
    {
        public readonly List<PaintTextureSnapshot> Textures = new List<PaintTextureSnapshot>();
    }

    private enum PlayMode
    {
        Explore,
        Paint
    }

    private Camera gameCamera;
    private ThirdPersonCameraRig cameraRig;
    private CharacterController characterController;
    private RuntimeMannequin mannequin;
    private Transform playerRoot;
    private Transform environmentRoot;
    private Canvas canvas;
    private Camera interfaceCamera;

    private HoldInputButton moveLeft;
    private HoldInputButton moveRight;
    private HoldInputButton moveForward;
    private HoldInputButton moveBack;
    private HoldInputButton orbitLeft;
    private HoldInputButton orbitRight;
    private HoldInputButton orbitUp;
    private HoldInputButton orbitDown;
    private HoldInputButton paintOrbitLeft;
    private HoldInputButton paintOrbitRight;
    private VirtualJoystick movementJoystick;

    private GameObject exploreHud;
    private GameObject paintHud;
    private GameObject posePanel;
    private Text statusText;
    private Text modeText;
    private Text poseText;
    private Text brushSizeText;
    private Text brushHexText;
    private Text surfaceText;
    private Text attachButtonText;
    private Image brushPreview;
    private Image brushSizePreview;
    private Slider brushSizeSlider;
    private Button sampleButton;
    private Text sampleButtonText;
    private Button undoButton;
    private Button redoButton;
    private Text historyText;
    private Button brushModeButton;
    private Text brushModeButtonText;

    private PlayMode playMode;
    private Color brushColor = new Color(0.12f, 0.16f, 0.20f);
    private int brushRadius = 15;
    private float metallic;
    private float smoothness = 0.24f;
    private float cameraYaw;
    private float cameraPitch = 10f;
    private float cameraDistance = 3.5f;
    private float verticalVelocity;
    private bool attached;
    private bool paintBrushMode = true;
    private Vector3 attachedNormal;
    private PaintableBodyPart lastPaintPart;
    private Vector2 lastPaintUv;
    private Vector3 lastPaintNormal;
    private bool hasLastPaintNormal;
    private bool sampleEnvironment;
    private Coroutine screenColorSampleRoutine;
    private Texture2D screenColorSamplePixel;
    private int activeTouchId = -1;
    private bool touchStartedOnBody;
    private bool pointerStartedAsSample;
    private bool pointerStartedOnUi;
    private float lastManualCameraInputTime = -10f;
    private Material floorMaterial;
    private Material wallMaterial;
    private Material accentMaterial;
    private Material darkMaterial;
    private Material pictureMaterialA;
    private Material pictureMaterialB;
    private readonly List<Material> runtimeMaterials = new List<Material>();
    private readonly List<Color> swatchColors = new List<Color>();
    private readonly List<Outline> swatchOutlines = new List<Outline>();
    private readonly List<PaintUndoState> paintUndoHistory = new List<PaintUndoState>();
    private readonly List<PaintUndoState> paintRedoHistory = new List<PaintUndoState>();
    private readonly Stack<PaintUndoState> paintUndoPool = new Stack<PaintUndoState>();
    private bool authoredEnvironment;

    private void Start()
    {
        Application.targetFrameRate = 60;
        Screen.sleepTimeout = SleepTimeout.NeverSleep;
        Physics.IgnoreLayerCollision(0, RuntimeMannequin.PaintLayer, true);
        Physics.IgnoreLayerCollision(2, RuntimeMannequin.PaintLayer, true);

        EnsureInfrastructure();
        BuildRoom();
        BuildPlayer();
        BuildInterface();
        SetPlayMode(PlayMode.Explore);
        UpdateCamera(true);

#if UNITY_EDITOR
        if (HasCommandLineFlag("-chameleonSmoke"))
        {
            StartCoroutine(RunEditorSmokeSequence());
        }
        else
        {
            StartCoroutine(CaptureEditorPreview());
        }
#endif
    }

    private void Update()
    {
        UpdateOrbitInput();

        if (playMode == PlayMode.Explore)
        {
            UpdateMovement();
            UpdateExploreTouchCamera();
        }
        else
        {
            UpdatePaintingInput();
        }

        if (cameraRig == null)
        {
            UpdateCamera(false);
        }
    }

    private void EnsureInfrastructure()
    {
        gameCamera = Camera.main;
        if (gameCamera == null)
        {
            Camera[] sceneCameras = Object.FindObjectsByType<Camera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < sceneCameras.Length; i++)
            {
                if (sceneCameras[i] != null && sceneCameras[i].enabled)
                {
                    gameCamera = sceneCameras[i];
                    break;
                }
            }
        }

        if (gameCamera == null)
        {
            var cameraObject = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            cameraObject.tag = "MainCamera";
            gameCamera = cameraObject.GetComponent<Camera>();
            gameCamera.clearFlags = CameraClearFlags.Skybox;
        }

        gameCamera.gameObject.tag = "MainCamera";

        gameCamera.orthographic = false;
        gameCamera.clearFlags = CameraClearFlags.SolidColor;
        gameCamera.backgroundColor = new Color(0.42f, 0.70f, 0.90f);
        gameCamera.fieldOfView = 55f;
        // The prototype always owns its lightweight block map.  The previous
        // Garden scene used a much larger near/far range and a baked lighting
        // setup; those values make the small mobile map look washed out.
        gameCamera.nearClipPlane = 0.08f;
        gameCamera.farClipPlane = 100f;
        gameCamera.allowHDR = true;

        UniversalAdditionalCameraData cameraData = gameCamera.GetComponent<UniversalAdditionalCameraData>();
        if (cameraData == null)
        {
            cameraData = gameCamera.gameObject.AddComponent<UniversalAdditionalCameraData>();
        }
        cameraData.renderPostProcessing = true;

        cameraRig = gameCamera.GetComponent<ThirdPersonCameraRig>();
        if (cameraRig == null)
        {
            cameraRig = gameCamera.gameObject.AddComponent<ThirdPersonCameraRig>();
        }

        if (Object.FindFirstObjectByType<EventSystem>() == null)
        {
            var eventSystemObject = new GameObject("EventSystem", typeof(EventSystem));
#if ENABLE_INPUT_SYSTEM
            eventSystemObject.AddComponent<InputSystemUIInputModule>();
#else
            eventSystemObject.AddComponent<StandaloneInputModule>();
#endif
        }

        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.48f, 0.70f, 0.86f);
        RenderSettings.ambientEquatorColor = new Color(0.29f, 0.42f, 0.30f);
        RenderSettings.ambientGroundColor = new Color(0.10f, 0.15f, 0.09f);
        RenderSettings.ambientIntensity = 0.82f;
        RenderSettings.reflectionIntensity = 0.45f;
        RenderSettings.skybox = null;
    }

    private void CreateMaterials()
    {
        // Flat, saturated materials keep the scene readable on a phone and
        // give the map a friendly voxel/toy-game silhouette.
        floorMaterial = MakeMaterial("Voxel Grass", new Color(0.28f, 0.62f, 0.25f), 0f, 0.08f);
        wallMaterial = MakeMaterial("Voxel Dirt", new Color(0.48f, 0.28f, 0.15f), 0f, 0.10f);
        accentMaterial = MakeMaterial("Voxel Leaves", new Color(0.12f, 0.48f, 0.18f), 0f, 0.08f);
        darkMaterial = MakeMaterial("Voxel Wood", new Color(0.30f, 0.16f, 0.075f), 0f, 0.12f);
        pictureMaterialA = MakeMaterial("Voxel Water", new Color(0.12f, 0.48f, 0.82f), 0f, 0.20f);
        pictureMaterialB = MakeMaterial("Voxel Gold", new Color(0.95f, 0.69f, 0.12f), 0f, 0.14f);
    }

    private void BuildRoom()
    {
        // Do not render the imported Garden/Sponza scene in the mobile build.
        // It is useful as an art reference, but it is far too heavy and visually
        // detailed for this short, blocky prototype.
        DisableImportedSceneRoots();
        CreateMaterials();
        environmentRoot = new GameObject("Voxel Hide-and-Seek Map").transform;
        authoredEnvironment = true;
        BuildBlockyMap();
        CreateLighting();
    }

    private void DisableImportedSceneRoots()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        GameObject[] roots = activeScene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            GameObject root = roots[i];
            if (root == null || root == gameObject || root == gameCamera.gameObject ||
                root.GetComponentInChildren<EventSystem>(true) != null ||
                gameObject.transform.IsChildOf(root.transform) ||
                gameCamera.transform.IsChildOf(root.transform))
            {
                continue;
            }

            // Disable rather than destroy so the authored Garden scene remains
            // available as a reference when the project is opened in the editor.
            root.SetActive(false);
        }
    }

    private void BuildBlockyMap()
    {
        Material grassAlt = MakeMaterial("Voxel Grass Light", new Color(0.38f, 0.72f, 0.28f), 0f, 0.08f);
        Material stone = MakeMaterial("Voxel Stone", new Color(0.42f, 0.48f, 0.52f), 0f, 0.12f);
        Material roof = MakeMaterial("Voxel Roof", new Color(0.72f, 0.18f, 0.16f), 0f, 0.11f);
        Material flower = MakeMaterial("Voxel Flower", new Color(0.95f, 0.30f, 0.48f), 0f, 0.08f);

        // 8×8 two-metre tiles give the player a clear, inexpensive play space.
        for (int x = -4; x < 4; x++)
        {
            for (int z = -4; z < 4; z++)
            {
                Material tile = ((x + z) & 1) == 0 ? floorMaterial : grassAlt;
                CreateBlock("Grass Block", new Vector3(x * 2f + 1f, -0.12f, z * 2f + 1f),
                    new Vector3(2f, 0.24f, 2f), tile);
            }
        }

        // Low boundary walls keep the camera from seeing an empty horizon while
        // leaving the map open enough for a small child-friendly playground.
        CreateBlock("Back Boundary", new Vector3(0f, 1.1f, 8.1f), new Vector3(16.4f, 2.2f, 0.4f), stone);
        CreateBlock("Left Boundary", new Vector3(-8.1f, 1.1f, 0f), new Vector3(0.4f, 2.2f, 16.4f), stone);
        CreateBlock("Right Boundary", new Vector3(8.1f, 1.1f, 0f), new Vector3(0.4f, 2.2f, 16.4f), stone);
        CreateBlock("Boundary Cap", new Vector3(0f, 2.28f, 8.1f), new Vector3(16.4f, 0.16f, 0.52f), accentMaterial);

        // A tiny block house creates a recognizable hiding landmark.
        CreateBlock("House Floor", new Vector3(-3.8f, 0.10f, 2.8f), new Vector3(4.2f, 0.20f, 3.2f), darkMaterial);
        CreateBlock("House Wall Left", new Vector3(-5.6f, 1.15f, 2.8f), new Vector3(0.35f, 2.1f, 3.2f), wallMaterial);
        CreateBlock("House Wall Back", new Vector3(-3.8f, 1.15f, 4.2f), new Vector3(3.3f, 2.1f, 0.35f), wallMaterial);
        CreateBlock("House Pillar", new Vector3(-2.1f, 1.15f, 1.45f), new Vector3(0.35f, 2.1f, 0.35f), darkMaterial);
        CreateBlock("House Roof", new Vector3(-3.8f, 2.42f, 2.8f), new Vector3(4.7f, 0.35f, 3.7f), roof);

        // Simple cover pieces double as paint targets and wall-attachment tests.
        CreateBlock("Paint Wall", new Vector3(1.8f, 0.90f, 1.15f), new Vector3(3.2f, 1.8f, 0.32f), wallMaterial);
        CreateBlock("Paint Wall Cap", new Vector3(1.8f, 1.88f, 1.15f), new Vector3(3.35f, 0.16f, 0.40f), accentMaterial);
        CreateBlock("Blue Pond", new Vector3(-4.5f, 0.025f, -3.5f), new Vector3(3.5f, 0.05f, 2.2f), pictureMaterialA,
            Vector3.zero, false);

        CreateVoxelTree(new Vector3(5.2f, 0f, 4.7f), darkMaterial, accentMaterial);
        CreateVoxelTree(new Vector3(-6.0f, 0f, -1.2f), darkMaterial, accentMaterial);

        // A handful of crates and flowers add scale without importing furniture
        // meshes or generating thousands of polygons.
        for (int i = 0; i < 4; i++)
        {
            float x = 3.4f + (i % 2) * 0.72f;
            float z = -3.8f + (i / 2) * 0.72f;
            CreateBlock("Crate", new Vector3(x, 0.34f, z), new Vector3(0.62f, 0.68f, 0.62f), darkMaterial,
                new Vector3(0f, i * 9f, 0f));
        }

        for (int i = 0; i < 5; i++)
        {
            float x = -1.0f + i * 0.54f;
            CreateBlock("Flower Block", new Vector3(x, 0.18f, -1.35f), new Vector3(0.28f, 0.36f, 0.28f), flower);
        }

        GameObject spawn = new GameObject("PlayerSpawn");
        spawn.transform.SetParent(environmentRoot, false);
        spawn.transform.position = new Vector3(0f, 0.02f, -5.4f);
        spawn.transform.rotation = Quaternion.identity;
    }

    private void CreateVoxelTree(Vector3 basePosition, Material trunk, Material leaves)
    {
        CreateBlock("Tree Trunk", basePosition + new Vector3(0f, 1.15f, 0f), new Vector3(0.75f, 2.3f, 0.75f), trunk);
        CreateBlock("Tree Leaves Lower", basePosition + new Vector3(0f, 2.25f, 0f), new Vector3(2.6f, 1.0f, 2.6f), leaves);
        CreateBlock("Tree Leaves Upper", basePosition + new Vector3(0f, 3.25f, 0f), new Vector3(1.85f, 1.0f, 1.85f), leaves);
    }

    private static bool IsAuthoredUrpScene()
    {
        string sceneName = SceneManager.GetActiveScene().name;
        return sceneName.IndexOf("Garden", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
               sceneName.IndexOf("Oasis", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
               sceneName.IndexOf("Cockpit", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
               sceneName.IndexOf("Terminal", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private void CreateCheckeredFloor()
    {
        Material cream = MakeMaterial("Tile Cream", new Color(0.76f, 0.72f, 0.62f), 0f, 0.34f);
        Material charcoal = MakeMaterial("Tile Charcoal", new Color(0.10f, 0.11f, 0.12f), 0f, 0.30f);
        const float tileSize = 1f;
        for (int z = -5; z <= 5; z++)
        {
            for (int x = -6; x <= 6; x++)
            {
                Material material = ((x + z) & 1) == 0 ? cream : charcoal;
                CreateBlock("Floor Tile", new Vector3(x * tileSize, 0.015f, z * tileSize),
                    new Vector3(0.98f, 0.03f, 0.98f), material, Vector3.zero, false);
            }
        }
    }

    private void CreateWallpaperPattern()
    {
        for (int x = -6; x <= 6; x++)
        {
            float offset = (x & 1) == 0 ? 0f : 0.36f;
            for (int y = 0; y < 4; y++)
            {
                CreateBlock("Wallpaper Diamond", new Vector3(x + offset, 1.55f + y * 0.72f, 5.84f),
                    new Vector3(0.34f, 0.34f, 0.035f), accentMaterial, new Vector3(0f, 0f, 45f), false);
            }
        }

        Material leftAccent = MakeMaterial("Left Wall Accent", new Color(0.36f, 0.16f, 0.18f), 0f, 0.22f);
        for (int i = 0; i < 7; i++)
        {
            CreateBlock("Left Panel", new Vector3(-6.84f, 1.45f + i * 0.45f, -3.8f + i * 1.15f),
                new Vector3(0.035f, 0.22f, 0.75f), leftAccent, Vector3.zero, false);
        }
    }

    private void CreatePictureWall()
    {
        CreatePicture(new Vector3(-3.9f, 2.75f, 5.70f), new Vector2(1.25f, 1.65f), pictureMaterialA);
        CreatePicture(new Vector3(-1.9f, 2.65f, 5.70f), new Vector2(1.05f, 1.25f), pictureMaterialB);
        CreatePicture(new Vector3(0.2f, 2.82f, 5.70f), new Vector2(1.55f, 1.85f), pictureMaterialA);
        CreatePicture(new Vector3(2.6f, 2.58f, 5.70f), new Vector2(1.15f, 1.15f), pictureMaterialB);
        CreatePicture(new Vector3(4.6f, 2.78f, 5.70f), new Vector2(1.45f, 1.68f), pictureMaterialA);
    }

    private void CreatePicture(Vector3 position, Vector2 size, Material canvasMaterial)
    {
        CreateBlock("Picture Frame", position, new Vector3(size.x + 0.16f, size.y + 0.16f, 0.13f), darkMaterial,
            Vector3.zero, false);
        CreateBlock("Picture Canvas", position + new Vector3(0f, 0f, -0.075f), new Vector3(size.x, size.y, 0.055f),
            canvasMaterial, Vector3.zero, true);
    }

    private void CreateFurnitureLayout()
    {
        PlaceFurniture("loungeSofaLong", new Vector3(-4.65f, 0f, 3.65f), 180f, 1.25f);
        PlaceFurniture("loungeChair", new Vector3(-2.55f, 0f, 3.85f), 180f, 1.15f);
        PlaceFurniture("rugRectangle", new Vector3(-3.55f, 0.025f, 2.80f), 0f, 0.05f);
        PlaceFurniture("table", new Vector3(0.25f, 0f, 2.90f), 0f, 1.05f);
        PlaceFurniture("chair", new Vector3(-0.75f, 0f, 2.90f), 90f, 1.05f);
        PlaceFurniture("chair", new Vector3(1.25f, 0f, 2.90f), -90f, 1.05f);
        PlaceFurniture("bookcaseOpen", new Vector3(5.55f, 0f, 4.95f), 180f, 2.45f);
        PlaceFurniture("books", new Vector3(5.55f, 1.15f, 4.40f), 180f, 0.30f);
        PlaceFurniture("cabinetTelevision", new Vector3(4.65f, 0f, -0.15f), -90f, 1.15f);
        PlaceFurniture("televisionModern", new Vector3(4.67f, 1.10f, -0.15f), -90f, 0.72f);
        PlaceFurniture("pottedPlant", new Vector3(-5.85f, 0f, -3.95f), 30f, 1.55f);
        PlaceFurniture("coatRackStanding", new Vector3(5.65f, 0f, -4.25f), 0f, 1.85f);
        PlaceFurniture("lampRoundFloor", new Vector3(-5.8f, 0f, 4.8f), 0f, 1.75f);
        PlaceFurniture("bear", new Vector3(2.55f, 0f, 5.15f), 170f, 0.82f);

        for (int i = 0; i < 6; i++)
        {
            float x = 2.7f + (i % 3) * 0.72f;
            float z = -4.35f + (i / 3) * 0.72f;
            PlaceFurniture("cardboardBoxClosed", new Vector3(x, 0f, z), i * 11f, 0.65f + (i % 2) * 0.22f);
        }
    }

    private void CreateLighting()
    {
        var sunObject = new GameObject("Voxel Sun", typeof(Light));
        sunObject.transform.SetParent(environmentRoot, false);
        sunObject.transform.rotation = Quaternion.Euler(48f, -32f, 0f);
        Light sun = sunObject.GetComponent<Light>();
        sun.type = LightType.Directional;
        sun.color = new Color(1f, 0.94f, 0.82f);
        sun.intensity = 1.0f;
        sun.shadows = LightShadows.Soft;
        sun.shadowStrength = 0.58f;

        CreatePointLight("Sky Fill", new Vector3(-3.5f, 4.0f, -2.5f), new Color(0.45f, 0.75f, 1f), 2.4f, 12f);
        CreatePointLight("House Glow", new Vector3(-3.8f, 2.0f, 2.8f), new Color(1f, 0.58f, 0.25f), 1.7f, 6f);
    }

    private void BuildPlayer()
    {
        var player = new GameObject("White Hider");
        player.layer = 2;
        playerRoot = player.transform;
        GameObject spawn = GameObject.Find("PlayerSpawn");
        if (spawn == null && authoredEnvironment)
        {
            spawn = GameObject.Find("Spawn");
        }
        playerRoot.position = spawn != null ? spawn.transform.position : new Vector3(0f, 0.02f, -3.2f);
        playerRoot.rotation = spawn != null ? spawn.transform.rotation : Quaternion.identity;

        characterController = player.AddComponent<CharacterController>();
        characterController.height = 1.95f * CharacterScale;
        characterController.radius = 0.34f * CharacterScale;
        characterController.center = new Vector3(0f, 0.975f * CharacterScale, 0f);
        characterController.stepOffset = 0.25f * CharacterScale;
        characterController.skinWidth = 0.08f * CharacterScale;
        characterController.slopeLimit = 48f;

        mannequin = player.AddComponent<RuntimeMannequin>();
        mannequin.Build();
        mannequin.SetMaterialResponse(metallic, smoothness);
        Vector3 groundedPosition = playerRoot.position;
        groundedPosition.y = FindGroundedRootY(groundedPosition);
        playerRoot.position = groundedPosition;
        PreparePaintUndoBuffers();

        cameraRig.Initialize(gameCamera, playerRoot);
        // Meccha Chameleon keeps the orbit camera under the player's manual
        // control.  Movement changes the character heading, not the camera
        // heading; a right-side swipe/mouse drag is the only yaw input.
        cameraRig.SetMovementFollowEnabled(false);
        cameraRig.SetOrbitAngles(playerRoot.eulerAngles.y, 10f, true);
    }

    private void BuildInterface()
    {
        var canvasObject = new GameObject("Mobile Interface", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvas = canvasObject.GetComponent<Canvas>();
        ConfigureInterfaceCamera();
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = interfaceCamera != null ? interfaceCamera : gameCamera;
        canvas.planeDistance = 1f;
        canvas.sortingOrder = 50;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        var safeAreaObject = new GameObject("Safe Area", typeof(RectTransform));
        safeAreaObject.transform.SetParent(canvasObject.transform, false);
        Stretch(safeAreaObject.GetComponent<RectTransform>(), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        safeAreaObject.AddComponent<SafeAreaFitter>();

        CreateTopHud(safeAreaObject.transform);
        CreateExploreHud(safeAreaObject.transform);
        CreatePaintHud(safeAreaObject.transform);
        CreatePosePanel(safeAreaObject.transform);
        SetLayerRecursively(canvasObject, LayerMask.NameToLayer("UI"));
    }

    private void ConfigureInterfaceCamera()
    {
        int uiLayer = LayerMask.NameToLayer("UI");
        if (uiLayer < 0 || gameCamera == null)
        {
            return;
        }

        var cameraObject = new GameObject("Interface Camera", typeof(Camera), typeof(UniversalAdditionalCameraData));
        cameraObject.transform.SetParent(transform, false);
        interfaceCamera = cameraObject.GetComponent<Camera>();
        interfaceCamera.clearFlags = CameraClearFlags.Depth;
        interfaceCamera.cullingMask = 1 << uiLayer;
        interfaceCamera.allowHDR = false;
        interfaceCamera.allowMSAA = false;
        interfaceCamera.depth = gameCamera.depth + 1f;

        UniversalAdditionalCameraData baseData = gameCamera.GetComponent<UniversalAdditionalCameraData>();
        UniversalAdditionalCameraData interfaceData = interfaceCamera.GetComponent<UniversalAdditionalCameraData>();
        interfaceData.renderType = CameraRenderType.Overlay;
        interfaceData.renderPostProcessing = false;

        if (baseData != null && baseData.renderType == CameraRenderType.Base)
        {
            baseData.cameraStack.RemoveAll(candidate => candidate == null || candidate == interfaceCamera);
            baseData.cameraStack.Add(interfaceCamera);
            gameCamera.cullingMask &= ~(1 << uiLayer);
        }
    }

    private void CreateTopHud(Transform parent)
    {
        var topBar = CreatePanel("Top Bar", parent, new Color(0.015f, 0.025f, 0.03f, 0.82f));
        Stretch(topBar.rectTransform, new Vector2(0f, 0.925f), Vector2.one, Vector2.zero, Vector2.zero);

        var title = CreateText("Title", topBar.transform, "Chamele-ON", 28, TextAnchor.MiddleLeft);
        title.fontStyle = FontStyle.Bold;
        SetRect(title.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(340f, 54f), new Vector2(32f, 13f));

        modeText = CreateText("Mode", topBar.transform, "EXPLORE", 19, TextAnchor.MiddleLeft);
        modeText.color = new Color(0.42f, 1f, 0.70f);
        SetRect(modeText.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(360f, 36f), new Vector2(34f, -22f));

        statusText = CreateText("Status", topBar.transform, "", 20, TextAnchor.MiddleRight);
        SetRect(statusText.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(820f, 58f), new Vector2(-32f, 0f));
    }

    private void CreateExploreHud(Transform parent)
    {
        exploreHud = new GameObject("Explore HUD", typeof(RectTransform));
        exploreHud.transform.SetParent(parent, false);
        Stretch(exploreHud.GetComponent<RectTransform>(), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        Image moveBase = CreateImage("Movement Joystick", exploreHud.transform, new Color(0.015f, 0.025f, 0.03f, 0.56f));
        moveBase.sprite = CircleSprite();
        moveBase.raycastTarget = true;
        SetRect(moveBase.rectTransform, new Vector2(0.105f, 0.16f), new Vector2(0.105f, 0.16f), new Vector2(232f, 232f), Vector2.zero);

        Image moveRing = CreateImage("Joystick Ring", moveBase.transform, new Color(0.42f, 0.93f, 0.72f, 0.22f));
        moveRing.sprite = CircleSprite();
        SetRect(moveRing.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(176f, 176f), Vector2.zero);

        Image moveHandle = CreateImage("Joystick Handle", moveBase.transform, new Color(0.42f, 0.93f, 0.72f, 0.94f));
        moveHandle.sprite = CircleSprite();
        SetRect(moveHandle.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(80f, 80f), Vector2.zero);
        movementJoystick = moveBase.gameObject.AddComponent<VirtualJoystick>();
        movementJoystick.Initialize(moveBase.rectTransform, moveHandle.rectTransform, canvas);

        Text lookHint = CreateText("Look Hint", exploreHud.transform, "Swipe anywhere to look", 17, TextAnchor.MiddleCenter);
        lookHint.color = new Color(1f, 1f, 1f, 0.48f);
        SetRect(lookHint.rectTransform, new Vector2(0.83f, 0.245f), new Vector2(0.83f, 0.245f), new Vector2(330f, 42f), Vector2.zero);

        var actions = CreatePanel("Actions", exploreHud.transform, new Color(0.02f, 0.035f, 0.04f, 0.76f));
        SetRect(actions.rectTransform, new Vector2(0.82f, 0.12f), new Vector2(0.82f, 0.12f), new Vector2(480f, 132f), Vector2.zero);

        Button attachButton = CreateButton("Attach", actions.transform, new Color(0.18f, 0.48f, 0.70f), "STICK", ToggleAttach);
        SetRect(attachButton.GetComponent<RectTransform>(), new Vector2(0.17f, 0.5f), new Vector2(0.17f, 0.5f), new Vector2(132f, 88f), Vector2.zero);
        attachButtonText = attachButton.GetComponentInChildren<Text>();

        Button poseButton = CreateButton("Pose", actions.transform, new Color(0.56f, 0.35f, 0.74f), "POSE", TogglePosePanel);
        SetRect(poseButton.GetComponent<RectTransform>(), new Vector2(0.50f, 0.5f), new Vector2(0.50f, 0.5f), new Vector2(132f, 88f), Vector2.zero);

        Button paintButton = CreateButton("Paint", actions.transform, new Color(0.92f, 0.31f, 0.37f), "PAINT", () => SetPlayMode(PlayMode.Paint));
        SetRect(paintButton.GetComponent<RectTransform>(), new Vector2(0.83f, 0.5f), new Vector2(0.83f, 0.5f), new Vector2(132f, 88f), Vector2.zero);

        poseText = CreateText("Pose Name", exploreHud.transform, "POSE: STAND", 23, TextAnchor.MiddleCenter);
        poseText.color = new Color(0.78f, 0.70f, 1f);
        SetRect(poseText.rectTransform, new Vector2(0.82f, 0.205f), new Vector2(0.82f, 0.205f), new Vector2(300f, 38f), Vector2.zero);
    }

    private void CreatePaintHud(Transform parent)
    {
        paintHud = new GameObject("Paint HUD", typeof(RectTransform));
        paintHud.transform.SetParent(parent, false);
        Stretch(paintHud.GetComponent<RectTransform>(), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        // The paint menu is deliberately composed like a compact art-tool
        // palette: a clear current-colour card, a swatch tray, a brush card,
        // surface response, and a dedicated history row.  The controls stay
        // large enough for a phone thumb, but the hierarchy no longer reads as
        // a collection of prototype buttons.
        Image toolPanel = CreatePanel("Paint Tools", paintHud.transform, new Color(0.012f, 0.020f, 0.026f, 0.975f));
        SetRect(toolPanel.rectTransform, new Vector2(0f, 0.50f), new Vector2(0f, 0.50f),
            new Vector2(470f, 780f), new Vector2(24f, -4f));
        Outline panelOutline = toolPanel.gameObject.AddComponent<Outline>();
        panelOutline.effectColor = new Color(0.26f, 0.92f, 0.68f, 0.42f);
        panelOutline.effectDistance = new Vector2(2f, -2f);

        Image accentLine = CreateImage("Paint Accent Line", toolPanel.transform, new Color(0.31f, 0.94f, 0.70f, 0.92f));
        SetRect(accentLine.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
            new Vector2(424f, 5f), new Vector2(0f, -10f));

        Text heading = CreateText("Paint Heading", toolPanel.transform, "CAMO LAB", 30, TextAnchor.MiddleCenter);
        heading.fontStyle = FontStyle.Bold;
        heading.color = new Color(0.76f, 1f, 0.89f);
        SetRect(heading.rectTransform, new Vector2(0.5f, 0.947f), new Vector2(0.5f, 0.947f),
            new Vector2(400f, 52f), Vector2.zero);

        Text subtitle = CreateText("Paint Subtitle", toolPanel.transform, "BODY PAINT  /  MATCH THE WORLD", 13,
            TextAnchor.MiddleCenter);
        subtitle.color = new Color(1f, 1f, 1f, 0.48f);
        subtitle.fontStyle = FontStyle.Bold;
        SetRect(subtitle.rectTransform, new Vector2(0.5f, 0.895f), new Vector2(0.5f, 0.895f),
            new Vector2(400f, 24f), Vector2.zero);

        Image currentCard = CreatePanel("Current Swatch Card", toolPanel.transform, new Color(0.055f, 0.095f, 0.105f, 0.96f));
        SetRect(currentCard.rectTransform, new Vector2(0.5f, 0.785f), new Vector2(0.5f, 0.785f),
            new Vector2(430f, 92f), Vector2.zero);
        Outline currentOutline = currentCard.gameObject.AddComponent<Outline>();
        currentOutline.effectColor = new Color(0.31f, 0.94f, 0.70f, 0.22f);
        currentOutline.effectDistance = new Vector2(1f, -1f);

        Text currentColorLabel = CreateText("Current Color Label", currentCard.transform, "CURRENT SWATCH", 14,
            TextAnchor.MiddleLeft);
        currentColorLabel.color = new Color(1f, 1f, 1f, 0.56f);
        currentColorLabel.fontStyle = FontStyle.Bold;
        SetRect(currentColorLabel.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2(230f, 24f), new Vector2(102f, 22f));

        brushPreview = CreateImage("Current Color Preview", currentCard.transform, brushColor);
        brushPreview.sprite = CircleSprite();
        SetRect(brushPreview.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2(68f, 68f), new Vector2(52f, 0f));
        Outline previewOutline = brushPreview.gameObject.AddComponent<Outline>();
        previewOutline.effectColor = new Color(1f, 1f, 1f, 0.92f);
        previewOutline.effectDistance = new Vector2(2f, -2f);

        Image hexCard = CreatePanel("Color Hex Card", currentCard.transform, new Color(1f, 1f, 1f, 0.075f));
        SetRect(hexCard.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2(238f, 42f), new Vector2(245f, -13f));
        brushHexText = CreateText("Color Hex", hexCard.transform, "#1F2933", 24, TextAnchor.MiddleCenter);
        brushHexText.fontStyle = FontStyle.Bold;
        Stretch(brushHexText.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

        Text currentHint = CreateText("Current Color Hint", currentCard.transform, "eyedropper or palette", 12,
            TextAnchor.MiddleLeft);
        currentHint.color = new Color(1f, 1f, 1f, 0.38f);
        SetRect(currentHint.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2(230f, 20f), new Vector2(102f, -28f));

        Text paletteTitle = CreateText("Palette Title", toolPanel.transform, "QUICK PALETTE", 14, TextAnchor.MiddleLeft);
        paletteTitle.color = new Color(0.42f, 1f, 0.72f, 0.86f);
        paletteTitle.fontStyle = FontStyle.Bold;
        SetRect(paletteTitle.rectTransform, new Vector2(0f, 0.713f), new Vector2(0f, 0.713f),
            new Vector2(220f, 20f), new Vector2(28f, 0f));

        Image paletteCard = CreatePanel("Palette Card", toolPanel.transform, new Color(0.035f, 0.058f, 0.066f, 0.96f));
        SetRect(paletteCard.rectTransform, new Vector2(0.5f, 0.605f), new Vector2(0.5f, 0.605f),
            new Vector2(430f, 148f), Vector2.zero);
        Outline paletteOutline = paletteCard.gameObject.AddComponent<Outline>();
        paletteOutline.effectColor = new Color(1f, 1f, 1f, 0.08f);
        paletteOutline.effectDistance = new Vector2(1f, -1f);

        Color[] swatches =
        {
            new Color(0.10f, 0.12f, 0.14f), new Color(0.28f, 0.32f, 0.34f),
            new Color(0.68f, 0.72f, 0.69f), new Color(0.96f, 0.95f, 0.88f),
            new Color(0.77f, 0.20f, 0.18f), new Color(0.94f, 0.42f, 0.22f),
            new Color(0.95f, 0.69f, 0.12f), new Color(0.97f, 0.86f, 0.32f),
            new Color(0.18f, 0.63f, 0.30f), new Color(0.49f, 0.78f, 0.25f),
            new Color(0.13f, 0.42f, 0.80f), new Color(0.25f, 0.72f, 0.87f),
            new Color(0.48f, 0.20f, 0.68f), new Color(0.80f, 0.35f, 0.67f),
            new Color(0.42f, 0.23f, 0.12f), new Color(0.78f, 0.49f, 0.24f)
        };

        swatchColors.Clear();
        swatchOutlines.Clear();
        for (int i = 0; i < swatches.Length; i++)
        {
            Color selected = swatches[i];
            Button swatch = CreateButton("Swatch " + i, paletteCard.transform, selected, string.Empty, () => SetBrushColor(selected));
            swatch.image.sprite = CircleSprite();
            float x = 0.105f + (i % 8) * 0.113f;
            float y = i < 8 ? 0.68f : 0.32f;
            SetRect(swatch.GetComponent<RectTransform>(), new Vector2(x, y), new Vector2(x, y),
                new Vector2(44f, 44f), Vector2.zero);
            Outline swatchOutline = swatch.gameObject.AddComponent<Outline>();
            swatchOutline.effectDistance = new Vector2(2f, -2f);
            swatchColors.Add(selected);
            swatchOutlines.Add(swatchOutline);
        }

        Text brushSection = CreateText("Brush Section", toolPanel.transform, "BRUSH / DETAIL", 14, TextAnchor.MiddleLeft);
        brushSection.color = new Color(0.42f, 1f, 0.72f, 0.86f);
        brushSection.fontStyle = FontStyle.Bold;
        SetRect(brushSection.rectTransform, new Vector2(0f, 0.483f), new Vector2(0f, 0.483f),
            new Vector2(220f, 20f), new Vector2(28f, 0f));

        Image brushCard = CreatePanel("Brush Card", toolPanel.transform, new Color(0.035f, 0.058f, 0.066f, 0.96f));
        SetRect(brushCard.rectTransform, new Vector2(0.5f, 0.365f), new Vector2(0.5f, 0.365f),
            new Vector2(430f, 142f), Vector2.zero);
        Outline brushCardOutline = brushCard.gameObject.AddComponent<Outline>();
        brushCardOutline.effectColor = new Color(1f, 1f, 1f, 0.08f);
        brushCardOutline.effectDistance = new Vector2(1f, -1f);

        Text brushSizeLabel = CreateText("Brush Size Label", brushCard.transform, "BRUSH SIZE", 13, TextAnchor.MiddleLeft);
        brushSizeLabel.color = new Color(1f, 1f, 1f, 0.56f);
        brushSizeLabel.fontStyle = FontStyle.Bold;
        SetRect(brushSizeLabel.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2(130f, 24f), new Vector2(22f, 38f));

        Button smallerBrush = CreateButton("Brush Smaller", brushCard.transform, new Color(0.12f, 0.17f, 0.20f),
            "-", () => AdjustBrushSize(-1));
        SetRect(smallerBrush.GetComponent<RectTransform>(), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2(64f, 68f), Vector2.zero);
        smallerBrush.GetComponentInChildren<Text>().fontSize = 38;

        Image sizeCard = CreatePanel("Brush Size Preview Card", brushCard.transform, new Color(1f, 1f, 1f, 0.075f));
        SetRect(sizeCard.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(205f, 86f), Vector2.zero);
        Outline sizeCardOutline = sizeCard.gameObject.AddComponent<Outline>();
        sizeCardOutline.effectColor = new Color(1f, 1f, 1f, 0.16f);
        sizeCardOutline.effectDistance = new Vector2(1f, -1f);
        brushSizePreview = CreateImage("Brush Diameter Preview", sizeCard.transform, brushColor);
        brushSizePreview.sprite = CircleSprite();
        SetRect(brushSizePreview.rectTransform, new Vector2(0.27f, 0.5f), new Vector2(0.27f, 0.5f),
            new Vector2(36f, 36f), Vector2.zero);
        brushSizeText = CreateText("Brush Pixel Size", sizeCard.transform, "30 px", 24, TextAnchor.MiddleCenter);
        brushSizeText.fontStyle = FontStyle.Bold;
        SetRect(brushSizeText.rectTransform, new Vector2(0.70f, 0.5f), new Vector2(0.70f, 0.5f),
            new Vector2(100f, 50f), Vector2.zero);

        Button largerBrush = CreateButton("Brush Larger", brushCard.transform, new Color(0.12f, 0.17f, 0.20f),
            "+", () => AdjustBrushSize(1));
        SetRect(largerBrush.GetComponent<RectTransform>(), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
            new Vector2(64f, 68f), Vector2.zero);
        largerBrush.GetComponentInChildren<Text>().fontSize = 34;

        brushSizeSlider = CreateSlider("Brush Size Slider", brushCard.transform, new Vector2(0.5f, 0.16f),
            new Vector2(360f, 42f), MinimumBrushRadius, MaximumBrushRadius, brushRadius, SetBrushSize);
        brushSizeSlider.wholeNumbers = true;

        Image surfaceCard = CreatePanel("Surface Card", toolPanel.transform, new Color(0.035f, 0.058f, 0.066f, 0.96f));
        SetRect(surfaceCard.rectTransform, new Vector2(0.5f, 0.205f), new Vector2(0.5f, 0.205f),
            new Vector2(430f, 48f), Vector2.zero);
        surfaceText = CreateText("Surface Label", surfaceCard.transform, "GLOSS 24%", 14, TextAnchor.MiddleLeft);
        surfaceText.fontStyle = FontStyle.Bold;
        SetRect(surfaceText.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2(130f, 32f), new Vector2(22f, 0f));
        CreateSlider("Surface", surfaceCard.transform, new Vector2(0.68f, 0.5f), new Vector2(230f, 34f), 0f, 1f, smoothness,
            value =>
            {
                smoothness = value;
                surfaceText.text = "GLOSS " + Mathf.RoundToInt(value * 100f) + "%";
                mannequin.SetMaterialResponse(metallic, smoothness);
            });

        Image historyCard = CreatePanel("Paint History Card", toolPanel.transform, new Color(0.035f, 0.058f, 0.066f, 0.96f));
        SetRect(historyCard.rectTransform, new Vector2(0.5f, 0.075f), new Vector2(0.5f, 0.075f),
            new Vector2(430f, 82f), Vector2.zero);
        Outline historyOutline = historyCard.gameObject.AddComponent<Outline>();
        historyOutline.effectColor = new Color(1f, 1f, 1f, 0.08f);
        historyOutline.effectDistance = new Vector2(1f, -1f);

        sampleButton = CreateButton("3D Eyedropper", historyCard.transform, new Color(0.12f, 0.48f, 0.58f),
            "EYEDROPPER\nPICK 3D COLOR", BeginSample);
        SetRect(sampleButton.GetComponent<RectTransform>(), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2(174f, 62f), new Vector2(16f, 0f));
        sampleButtonText = sampleButton.GetComponentInChildren<Text>();
        sampleButtonText.fontSize = 16;

        undoButton = CreateButton("Undo Paint", historyCard.transform, new Color(0.15f, 0.20f, 0.22f),
            "↶\nUNDO", UndoPaint);
        SetRect(undoButton.GetComponent<RectTransform>(), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2(66f, 62f), new Vector2(204f, 0f));
        undoButton.GetComponentInChildren<Text>().fontSize = 18;

        redoButton = CreateButton("Redo Paint", historyCard.transform, new Color(0.15f, 0.20f, 0.22f),
            "↷\nREDO", RedoPaint);
        SetRect(redoButton.GetComponent<RectTransform>(), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2(66f, 62f), new Vector2(276f, 0f));
        redoButton.GetComponentInChildren<Text>().fontSize = 18;

        Button clearButton = CreateButton("Clear Body", historyCard.transform, new Color(0.38f, 0.25f, 0.27f),
            "CLEAR", ClearBody);
        SetRect(clearButton.GetComponent<RectTransform>(), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2(76f, 62f), new Vector2(348f, 0f));
        clearButton.GetComponentInChildren<Text>().fontSize = 14;

        historyText = CreateText("History Count", toolPanel.transform, "0 / " + MaximumPaintUndoSteps, 12, TextAnchor.MiddleRight);
        historyText.color = new Color(1f, 1f, 1f, 0.38f);
        historyText.fontStyle = FontStyle.Bold;
        SetRect(historyText.rectTransform, new Vector2(1f, 0.135f), new Vector2(1f, 0.135f),
            new Vector2(120f, 22f), new Vector2(-26f, 0f));

        brushModeButton = CreateButton("Brush Lock", paintHud.transform, new Color(0.16f, 0.70f, 0.42f),
            "BRUSH\nLOCKED", TogglePaintBrushMode);
        SetRect(brushModeButton.GetComponent<RectTransform>(), new Vector2(0.64f, 0.065f),
            new Vector2(0.64f, 0.065f), new Vector2(170f, 80f), Vector2.zero);
        brushModeButtonText = brushModeButton.GetComponentInChildren<Text>();
        brushModeButtonText.fontSize = 17;

        Button pose = CreateButton("Paint Pose", paintHud.transform, new Color(0.53f, 0.35f, 0.72f), "POSE", TogglePosePanel);
        SetRect(pose.GetComponent<RectTransform>(), new Vector2(0.77f, 0.065f), new Vector2(0.77f, 0.065f), new Vector2(170f, 80f), Vector2.zero);
        Button done = CreateButton("Finish Painting", paintHud.transform, new Color(0.16f, 0.70f, 0.42f), "DONE", () => SetPlayMode(PlayMode.Explore));
        SetRect(done.GetComponent<RectTransform>(), new Vector2(0.89f, 0.065f), new Vector2(0.89f, 0.065f), new Vector2(170f, 80f), Vector2.zero);

        var instruction = CreateText("Paint Instruction", paintHud.transform,
            "BRUSH LOCKED: DRAG BODY TO PAINT   ·   TAP BRUSH TO UNLOCK CAMERA   ·   HISTORY ARROWS RESTORE YOUR LAST MOVES", 17,
            TextAnchor.MiddleCenter);
        instruction.color = new Color(1f, 1f, 1f, 0.58f);
        SetRect(instruction.rectTransform, new Vector2(0.64f, 0.87f), new Vector2(0.64f, 0.87f),
            new Vector2(820f, 44f), Vector2.zero);

        RefreshPaintToolUi();
    }

    private void CreatePosePanel(Transform parent)
    {
        posePanel = new GameObject("Pose Wheel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        posePanel.transform.SetParent(parent, false);
        RectTransform panelRect = posePanel.GetComponent<RectTransform>();
        Stretch(panelRect, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        posePanel.GetComponent<Image>().color = new Color(0.01f, 0.015f, 0.02f, 0.72f);
        posePanel.GetComponent<Image>().raycastTarget = true;

        var wheel = CreatePanel("Pose Card", posePanel.transform, new Color(0.035f, 0.055f, 0.065f, 0.98f));
        SetRect(wheel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(920f, 510f), Vector2.zero);
        var heading = CreateText("Pose Heading", wheel.transform, "CHOOSE A SILHOUETTE", 38, TextAnchor.MiddleCenter);
        heading.fontStyle = FontStyle.Bold;
        SetRect(heading.rectTransform, new Vector2(0.5f, 0.84f), new Vector2(0.5f, 0.84f), new Vector2(820f, 70f), Vector2.zero);

        string[] names = { "STAND", "CROUCH", "CURL", "WALL FLAT", "STAR" };
        Color[] colors =
        {
            new Color(0.24f, 0.46f, 0.64f), new Color(0.23f, 0.60f, 0.48f), new Color(0.72f, 0.45f, 0.22f),
            new Color(0.58f, 0.34f, 0.72f), new Color(0.72f, 0.28f, 0.42f)
        };

        for (int i = 0; i < names.Length; i++)
        {
            int poseIndex = i;
            Button button = CreateButton("Pose " + names[i], wheel.transform, colors[i], names[i], () => SelectPose(poseIndex));
            SetRect(button.GetComponent<RectTransform>(), new Vector2(0.10f + i * 0.20f, 0.48f), new Vector2(0.10f + i * 0.20f, 0.48f),
                new Vector2(155f, 190f), Vector2.zero);
        }

        Button close = CreateButton("Close Pose Wheel", wheel.transform, new Color(0.22f, 0.25f, 0.27f), "CLOSE", TogglePosePanel);
        SetRect(close.GetComponent<RectTransform>(), new Vector2(0.5f, 0.13f), new Vector2(0.5f, 0.13f), new Vector2(260f, 88f), Vector2.zero);
        posePanel.SetActive(false);
    }

    private void UpdateMovement()
    {
        Vector2 stick = movementJoystick != null ? movementJoystick.Value : Vector2.zero;
        float horizontal = Mathf.Clamp(stick.x + GetAxis(moveLeft, moveRight, KeyCode.A, KeyCode.D, KeyCode.LeftArrow, KeyCode.RightArrow), -1f, 1f);
        float vertical = Mathf.Clamp(stick.y + GetAxis(moveBack, moveForward, KeyCode.S, KeyCode.W, KeyCode.DownArrow, KeyCode.UpArrow), -1f, 1f);

        if (attached)
        {
            if (cameraRig != null)
            {
                cameraRig.ClearMovementHeading();
            }
            mannequin.SetLocomotion(0f);
            Vector3 surfaceRight = Vector3.Cross(Vector3.up, attachedNormal).normalized;
            Vector3 delta = (surfaceRight * horizontal + Vector3.up * vertical) *
                            (AttachedMovementSpeed * Time.deltaTime);
            Vector3 next = playerRoot.position + delta;
            next.y = Mathf.Clamp(next.y, 0.02f, 2.1f);
            playerRoot.position = next;
            return;
        }

        Vector3 cameraForward = cameraRig != null
            ? cameraRig.PlanarForward
            : Vector3.ProjectOnPlane(gameCamera.transform.forward, Vector3.up).normalized;
        Vector2 movementInput = Vector2.ClampMagnitude(new Vector2(horizontal, vertical), 1f);
        float movementStrength = movementInput.magnitude;
        Vector3 cameraRight = cameraRig != null
            ? cameraRig.PlanarRight
            : Vector3.Cross(Vector3.up, cameraForward).normalized;
        // Rebuild the travel vector every frame so held WASD input follows a
        // camera that is being orbited at the same time.
        Vector3 movement = movementStrength > 0.14f
            ? cameraRight * movementInput.x + cameraForward * movementInput.y
            : Vector3.zero;
        if (movement.sqrMagnitude > 1f)
        {
            movement.Normalize();
        }

        if (characterController.isGrounded)
        {
            verticalVelocity = -1.8f;
        }
        else
        {
            verticalVelocity += Physics.gravity.y * Time.deltaTime;
        }

        Vector3 velocity = movement * ExploreMovementSpeed + Vector3.up * verticalVelocity;
        characterController.Move(velocity * Time.deltaTime);
        mannequin.SetLocomotion(Mathf.Clamp01(movement.magnitude));

        if (movement.sqrMagnitude > 0.02f)
        {
            Quaternion target = Quaternion.LookRotation(movement, Vector3.up);
            playerRoot.rotation = Quaternion.Slerp(playerRoot.rotation, target, Time.deltaTime * 10f);
            if (cameraRig != null)
            {
                cameraRig.SetMovementHeading(Mathf.Atan2(movement.x, movement.z) * Mathf.Rad2Deg, movementStrength);
            }
            else if (Time.unscaledTime - lastManualCameraInputTime > 1.15f)
            {
                cameraYaw = Mathf.LerpAngle(cameraYaw, target.eulerAngles.y, Time.deltaTime * 2.3f);
            }
        }
        else if (cameraRig != null)
        {
            cameraRig.ClearMovementHeading();
        }
    }

    private void UpdateOrbitInput()
    {
        HoldInputButton left = playMode == PlayMode.Paint ? paintOrbitLeft : orbitLeft;
        HoldInputButton right = playMode == PlayMode.Paint ? paintOrbitRight : orbitRight;
        float yawDelta = 0f;
        float pitchDelta = 0f;
        if (left != null && left.IsPressed) yawDelta -= 72f * Time.deltaTime;
        if (right != null && right.IsPressed) yawDelta += 72f * Time.deltaTime;

        if (playMode == PlayMode.Explore)
        {
            if (orbitUp != null && orbitUp.IsPressed) pitchDelta += 45f * Time.deltaTime;
            if (orbitDown != null && orbitDown.IsPressed) pitchDelta -= 45f * Time.deltaTime;
        }

        bool paintLocksCamera = playMode == PlayMode.Paint && paintBrushMode;
        if (cameraRig != null && !paintLocksCamera)
        {
            cameraRig.AddOrbitInput(new Vector2(yawDelta, pitchDelta));
        }
        else if (cameraRig == null && !paintLocksCamera)
        {
            cameraYaw += yawDelta;
            cameraPitch -= pitchDelta;
        }

        if (cameraRig == null && ChameleonInput.GetMouseButton(1))
        {
            Vector2 mouseDelta = ChameleonInput.MouseDelta;
            cameraYaw += mouseDelta.x * 0.18f;
            cameraPitch -= mouseDelta.y * 0.15f;
            lastManualCameraInputTime = Time.unscaledTime;
        }

        cameraPitch = Mathf.Clamp(cameraPitch, -8f, 48f);
    }

    private void UpdateExploreTouchCamera()
    {
        if (cameraRig != null)
        {
            return;
        }

        for (int i = 0; i < ChameleonInput.TouchCount; i++)
        {
            ChameleonTouch touch = ChameleonInput.GetTouch(i);
            if (touch.Phase != TouchPhase.Moved || ChameleonInput.IsPointerOverUi(touch.Position))
            {
                continue;
            }

            cameraYaw += touch.DeltaPosition.x * 0.12f;
            cameraPitch -= touch.DeltaPosition.y * 0.10f;
            cameraPitch = Mathf.Clamp(cameraPitch, -8f, 48f);
            lastManualCameraInputTime = Time.unscaledTime;
            break;
        }
    }

    private void UpdatePaintingInput()
    {
        if (!paintBrushMode)
        {
            // In camera mode every non-UI swipe is owned by the orbit rig. A
            // brush stroke cannot start until the BRUSH button is locked again.
            EndStroke();
            return;
        }

        if (ChameleonInput.TouchCount > 0)
        {
            if (activeTouchId < 0)
            {
                for (int i = 0; i < ChameleonInput.TouchCount; i++)
                {
                    ChameleonTouch beganTouch = ChameleonInput.GetTouch(i);
                    if (beganTouch.Phase != TouchPhase.Began)
                    {
                        continue;
                    }

                    activeTouchId = beganTouch.FingerId;
                    pointerStartedOnUi = ChameleonInput.IsPointerOverUi(beganTouch.Position);
                    if (!pointerStartedOnUi)
                    {
                        pointerStartedAsSample = sampleEnvironment;
                        touchStartedOnBody = TryPaintAt(beganTouch.Position, true);
                    }
                    return;
                }

                return;
            }

            bool foundActiveTouch = false;
            for (int i = 0; i < ChameleonInput.TouchCount; i++)
            {
                ChameleonTouch touch = ChameleonInput.GetTouch(i);
                if (touch.FingerId != activeTouchId)
                {
                    continue;
                }

                foundActiveTouch = true;
                if (touch.Phase == TouchPhase.Ended || touch.Phase == TouchPhase.Canceled)
                {
                    EndStroke();
                    return;
                }

                if (pointerStartedOnUi || touch.Phase != TouchPhase.Moved)
                {
                    return;
                }

                if (ChameleonInput.IsPointerOverUi(touch.Position))
                {
                    // Break the UV connection while crossing the tool panel so a
                    // return to the body cannot draw one long accidental line.
                    lastPaintPart = null;
                    return;
                }

                if (touchStartedOnBody)
                {
                    TryPaintAt(touch.Position, false);
                }
                return;
            }

            if (!foundActiveTouch)
            {
                EndStroke();
            }
            return;
        }

        if (activeTouchId >= 0)
        {
            EndStroke();
        }

        if (ChameleonInput.GetMouseButtonDown(0))
        {
            EndStroke();
            pointerStartedOnUi = ChameleonInput.IsPointerOverUi(ChameleonInput.MousePosition);
            if (!pointerStartedOnUi)
            {
                pointerStartedAsSample = sampleEnvironment;
                touchStartedOnBody = TryPaintAt(ChameleonInput.MousePosition, true);
            }
        }
        else if (ChameleonInput.GetMouseButton(0) && pointerStartedOnUi)
        {
            // A drag that started on a slider/button remains UI-owned even after
            // the cursor leaves the panel.
            return;
        }
        else if (ChameleonInput.GetMouseButton(0) && ChameleonInput.IsPointerOverUi(ChameleonInput.MousePosition))
        {
            lastPaintPart = null;
            return;
        }
        else if (ChameleonInput.GetMouseButton(0) && touchStartedOnBody)
        {
            TryPaintAt(ChameleonInput.MousePosition, false);
        }
        else if (ChameleonInput.GetMouseButtonUp(0))
        {
            EndStroke();
        }
    }

    private bool TryPaintAt(Vector2 screenPosition, bool newStroke)
    {
        Ray ray = gameCamera.ScreenPointToRay(screenPosition);

        if (sampleEnvironment)
        {
            if (!TryGetEyedropperSurface(ray, out RaycastHit sampleHit))
            {
                statusText.text = "EYEDROPPER — aim away from your body and tap a visible object.";
                return false;
            }

            sampleEnvironment = false;
            if (TrySampleSurfaceColor(sampleHit, out Color sampledColor))
            {
                SetBrushColor(sampledColor);
                statusText.text = "SURFACE COLOR PICKED — paint now uses the tapped material color.";
                return false;
            }

            if (screenColorSampleRoutine != null)
            {
                StopCoroutine(screenColorSampleRoutine);
            }
            screenColorSampleRoutine = StartCoroutine(SampleRenderedWorldColor(screenPosition));
            return false;
        }

        if (!Physics.Raycast(ray, out RaycastHit hit, 50f, 1 << RuntimeMannequin.PaintLayer, QueryTriggerInteraction.Collide))
        {
            if (!newStroke)
            {
                lastPaintPart = null;
                hasLastPaintNormal = false;
            }
            return false;
        }

        PaintableBodyPart part = hit.collider.GetComponent<PaintableBodyPart>();
        if (part == null)
        {
            if (!newStroke)
            {
                lastPaintPart = null;
                hasLastPaintNormal = false;
            }
            return false;
        }

        Vector2 uv = hit.textureCoord;
        if (!newStroke && lastPaintPart == part && (uv - lastPaintUv).sqrMagnitude < 0.0000001f)
        {
            return true;
        }

        bool continuesOnSurface = !newStroke && lastPaintPart == part && hasLastPaintNormal &&
                                  Vector3.Dot(lastPaintNormal, hit.normal) > 0.92f;

        if (newStroke)
        {
            CapturePaintUndoState();
        }

        if (!continuesOnSurface)
        {
            lastPaintPart = part;
            lastPaintUv = uv;
        }

        // Pass the hit normal so a cube's overlapping face UVs cannot color
        // its side/back faces along with the face currently under the pointer.
        // A sharp edge starts a new UV segment instead of connecting unrelated
        // face tiles through the atlas.
        part.PaintStroke(continuesOnSurface ? lastPaintUv : uv, uv, brushColor, brushRadius, hit.normal);
        PaintVisibleScreenNeighbors(screenPosition, part, brushColor, brushRadius);
        lastPaintPart = part;
        lastPaintUv = uv;
        lastPaintNormal = hit.normal;
        hasLastPaintNormal = true;
        return true;
    }

    private bool TryGetEyedropperSurface(Ray ray, out RaycastHit surfaceHit)
    {
        surfaceHit = default;
        RaycastHit[] hits = Physics.RaycastAll(ray, 75f, ~(1 << 2), QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
        for (int i = 0; i < hits.Length; i++)
        {
            Collider collider = hits[i].collider;
            if (collider == null || collider.isTrigger)
            {
                continue;
            }

            Transform hitTransform = collider.transform;
            bool hitOwnCharacter = collider.gameObject.layer == RuntimeMannequin.PaintLayer ||
                                   (playerRoot != null &&
                                    (hitTransform == playerRoot || hitTransform.IsChildOf(playerRoot)));
            if (hitOwnCharacter)
            {
                // Do not ray through the character and then sample the character's
                // rendered foreground pixel while claiming a wall was selected.
                return false;
            }

            surfaceHit = hits[i];
            return true;
        }

        return false;
    }

    private bool TrySampleSurfaceColor(RaycastHit surfaceHit, out Color sampledColor)
    {
        sampledColor = Color.white;
        Renderer renderer = surfaceHit.collider.GetComponent<Renderer>();
        if (renderer == null)
        {
            renderer = surfaceHit.collider.GetComponentInParent<Renderer>();
        }

        if (renderer == null)
        {
            return false;
        }

        Material material = renderer.sharedMaterial;
        if (material == null)
        {
            return false;
        }

        Color baseColor = Color.white;
        if (material.HasProperty("_BaseColor"))
        {
            baseColor = material.GetColor("_BaseColor");
        }
        else if (material.HasProperty("_Color"))
        {
            baseColor = material.GetColor("_Color");
        }

        Texture texture = null;
        if (material.HasProperty("_BaseMap"))
        {
            texture = material.GetTexture("_BaseMap");
        }
        else if (material.HasProperty("_MainTex"))
        {
            texture = material.GetTexture("_MainTex");
        }

        if (texture is Texture2D texture2D && texture2D.isReadable)
        {
            Color texel = texture2D.GetPixelBilinear(surfaceHit.textureCoord.x, surfaceHit.textureCoord.y);
            sampledColor = new Color(baseColor.r * texel.r, baseColor.g * texel.g, baseColor.b * texel.b, 1f);
            return true;
        }

        sampledColor = new Color(baseColor.r, baseColor.g, baseColor.b, 1f);
        return true;
    }

    private void PaintVisibleScreenNeighbors(Vector2 screenPosition, PaintableBodyPart centerPart,
        Color color, int radius)
    {
        float normalizedSize = Mathf.InverseLerp(MinimumBrushRadius, MaximumBrushRadius, radius);
        float screenRadius = Mathf.Lerp(4f, 16f, normalizedSize);
        float diagonal = screenRadius * 0.7071f;
        Vector2[] offsets =
        {
            new Vector2(screenRadius, 0f),
            new Vector2(-screenRadius, 0f),
            new Vector2(0f, screenRadius),
            new Vector2(0f, -screenRadius),
            new Vector2(diagonal, diagonal),
            new Vector2(diagonal, -diagonal),
            new Vector2(-diagonal, diagonal),
            new Vector2(-diagonal, -diagonal)
        };

        int dotRadius = Mathf.Max(2, Mathf.RoundToInt(radius * 0.34f));
        for (int i = 0; i < offsets.Length; i++)
        {
            Ray sampleRay = gameCamera.ScreenPointToRay(screenPosition + offsets[i]);
            if (!Physics.Raycast(sampleRay, out RaycastHit hit, 50f, 1 << RuntimeMannequin.PaintLayer,
                    QueryTriggerInteraction.Collide))
            {
                continue;
            }

            if (hit.collider == null)
            {
                continue;
            }

            PaintableBodyPart part = hit.collider.GetComponent<PaintableBodyPart>();
            if (part == null || part != centerPart)
            {
                continue;
            }

            part.PaintStroke(hit.textureCoord, hit.textureCoord, color, dotRadius, hit.normal);
        }
    }

    private void EndStroke()
    {
        activeTouchId = -1;
        touchStartedOnBody = false;
        pointerStartedAsSample = false;
        pointerStartedOnUi = false;
        lastPaintPart = null;
        hasLastPaintNormal = false;
    }

    private void UpdateCamera(bool immediate)
    {
        if (playerRoot == null)
        {
            return;
        }

        if (cameraRig != null)
        {
            if (immediate)
            {
                cameraRig.SnapToTarget();
            }
            return;
        }

        Vector3 target = playerRoot.position + Vector3.up * (1.15f * CharacterScale);
        float distance = playMode == PlayMode.Paint ? 2.05f : cameraDistance;
        Quaternion orbit = Quaternion.Euler(cameraPitch, cameraYaw, 0f);
        Vector3 desiredPosition = target - orbit * Vector3.forward * distance;

        if (immediate)
        {
            gameCamera.transform.position = desiredPosition;
        }
        else
        {
            gameCamera.transform.position = Vector3.Lerp(gameCamera.transform.position, desiredPosition, Time.deltaTime * 12f);
        }

        gameCamera.transform.rotation = Quaternion.LookRotation(target - gameCamera.transform.position, Vector3.up);
    }

    private void RefreshCameraComposition(bool snapImmediately)
    {
        if (cameraRig == null || playerRoot == null)
        {
            return;
        }

        cameraRig.SetAttachedMode(attached);
        Vector3 targetOffset = attached
            ? GetMannequinVisualCenterOffset()
            : Vector3.up * (1.15f * CharacterScale);
        if (attached)
        {
            targetOffset += attachedNormal * (0.12f * CharacterScale);
        }
        cameraRig.SetTargetOffset(targetOffset, snapImmediately);
    }

    private Vector3 GetMannequinVisualCenterOffset()
    {
        SkinnedMeshRenderer[] renderers = playerRoot.GetComponentsInChildren<SkinnedMeshRenderer>();
        bool hasBounds = false;
        Bounds combined = default;
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] == null || !renderers[i].enabled)
            {
                continue;
            }

            if (!hasBounds)
            {
                combined = renderers[i].bounds;
                hasBounds = true;
            }
            else
            {
                combined.Encapsulate(renderers[i].bounds);
            }
        }

        return hasBounds ? combined.center - playerRoot.position : Vector3.up * (1.05f * CharacterScale);
    }

    private float GetVisualSurfaceOffset(Vector3 surfaceNormal, float fallbackOffset)
    {
        if (playerRoot == null)
        {
            return fallbackOffset;
        }

        Vector3 normal = Vector3.ProjectOnPlane(surfaceNormal, Vector3.up);
        if (normal.sqrMagnitude < 0.0001f)
        {
            normal = surfaceNormal;
        }
        normal.Normalize();

        Renderer[] renderers = playerRoot.GetComponentsInChildren<Renderer>();
        bool hasProjection = false;
        float minimumProjection = 0f;
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || !renderer.enabled)
            {
                continue;
            }

            Bounds bounds = renderer.bounds;
            Vector3 minimum = bounds.min;
            Vector3 maximum = bounds.max;
            for (int x = 0; x < 2; x++)
            {
                for (int y = 0; y < 2; y++)
                {
                    for (int z = 0; z < 2; z++)
                    {
                        Vector3 corner = new Vector3(
                            x == 0 ? minimum.x : maximum.x,
                            y == 0 ? minimum.y : maximum.y,
                            z == 0 ? minimum.z : maximum.z);
                        float projection = Vector3.Dot(corner - playerRoot.position, normal);
                        if (!hasProjection || projection < minimumProjection)
                        {
                            minimumProjection = projection;
                            hasProjection = true;
                        }
                    }
                }
            }
        }

        return hasProjection ? Mathf.Max(0.018f * CharacterScale, -minimumProjection + 0.018f * CharacterScale) : fallbackOffset;
    }

    private float FindGroundedRootY(Vector3 nearPosition)
    {
        int mask = ~((1 << RuntimeMannequin.PaintLayer) | (1 << 2));
        Vector3 origin = nearPosition + Vector3.up * (2.2f * CharacterScale + 0.8f);
        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 4f, mask, QueryTriggerInteraction.Ignore) &&
            hit.normal.y > 0.45f)
        {
            return hit.point.y + 0.018f;
        }

        return Mathf.Max(0.02f, nearPosition.y);
    }

    private void ToggleAttach()
    {
        if (attached)
        {
            DetachFromSurface();
            return;
        }

        Vector3 origin = playerRoot.position + Vector3.up * (1.02f * CharacterScale);
        Vector3[] directions =
        {
            playerRoot.forward, -playerRoot.forward, playerRoot.right, -playerRoot.right
        };

        bool found = false;
        RaycastHit bestHit = default;
        float bestDistance = 1.45f * CharacterScale;
        int mask = ~((1 << RuntimeMannequin.PaintLayer) | (1 << 2));
        foreach (Vector3 direction in directions)
        {
            if (Physics.Raycast(origin, direction, out RaycastHit hit, bestDistance, mask, QueryTriggerInteraction.Ignore) &&
                Mathf.Abs(hit.normal.y) < 0.55f)
            {
                found = true;
                bestHit = hit;
                bestDistance = hit.distance;
            }
        }

        if (!found)
        {
            statusText.text = "Move right next to a wall or large piece of furniture first.";
            return;
        }

        attached = true;
        attachedNormal = Vector3.ProjectOnPlane(bestHit.normal, Vector3.up);
        if (attachedNormal.sqrMagnitude < 0.0001f)
        {
            attachedNormal = bestHit.normal;
        }
        attachedNormal.Normalize();
        characterController.enabled = false;
        playerRoot.rotation = Quaternion.LookRotation(-attachedNormal, Vector3.up);
        mannequin.ApplyPose(3);
        Vector3 provisionalPosition = bestHit.point + attachedNormal * (0.34f * CharacterScale);
        provisionalPosition.y = FindGroundedRootY(provisionalPosition);
        playerRoot.position = provisionalPosition;
        Physics.SyncTransforms();

        float surfaceOffset = GetVisualSurfaceOffset(attachedNormal, 0.34f * CharacterScale);
        Vector3 attachedPosition = bestHit.point + attachedNormal * surfaceOffset;
        attachedPosition.y = FindGroundedRootY(attachedPosition);
        playerRoot.position = attachedPosition;
        Physics.SyncTransforms();
        cameraYaw = playerRoot.eulerAngles.y;
        if (cameraRig != null)
        {
            RefreshCameraComposition(false);
            cameraRig.SetOrbitAngles(playerRoot.eulerAngles.y, 9f, true);
        }
        attachButtonText.text = "DETACH";
        poseText.text = "POSE: " + mannequin.GetPoseName();
        modeText.text = "HIDING / ATTACHED";
        statusText.text = "STUCK TO SURFACE — use the joystick to slide, then paint.";
    }

    private void DetachFromSurface()
    {
        playerRoot.position += attachedNormal * (0.55f * CharacterScale);
        attached = false;
        characterController.enabled = true;
        attachButtonText.text = "STICK";
        if (cameraRig != null)
        {
            RefreshCameraComposition(true);
        }
        modeText.text = "EXPLORE 3D MAP";
        statusText.text = "Detached from surface.";
    }

    private void TogglePosePanel()
    {
        CancelSample();
        posePanel.SetActive(!posePanel.activeSelf);
        EndStroke();
    }

    private void SelectPose(int index)
    {
        mannequin.ApplyPose(index);
        poseText.text = "POSE: " + mannequin.GetPoseName();
        posePanel.SetActive(false);
        statusText.text = mannequin.GetPoseName() + " pose selected. Paint for this silhouette.";
    }

    private void SetPlayMode(PlayMode mode)
    {
        CancelSample();
        playMode = mode;
        bool painting = mode == PlayMode.Paint;
        exploreHud.SetActive(!painting);
        paintHud.SetActive(painting);
        posePanel.SetActive(false);
        EndStroke();

        if (painting)
        {
            paintBrushMode = true;
            if (cameraRig != null)
            {
                cameraRig.ClearMovementHeading();
                cameraRig.SetLegacyPointerInputEnabled(false);
            }
            modeText.text = "PAINT MODE — BODY SURFACE";
            statusText.text = "BRUSH LOCKED — choose a color and drag the body to paint. Tap BRUSH to orbit.";
        }
        else
        {
            paintBrushMode = false;
            if (cameraRig != null)
            {
                cameraRig.SetLegacyPointerInputEnabled(true);
            }
            modeText.text = attached ? "HIDING / ATTACHED" : "EXPLORE 3D MAP";
            statusText.text = attached
                ? "Paint saved. Stay attached or detach to move."
                : "Find a surface, choose a pose, then paint your body.";
        }
        RefreshPaintToolUi();
    }

    private void TogglePaintBrushMode()
    {
        if (playMode != PlayMode.Paint)
        {
            return;
        }

        CancelSample();
        EndStroke();
        paintBrushMode = !paintBrushMode;
        if (cameraRig != null)
        {
            cameraRig.SetLegacyPointerInputEnabled(!paintBrushMode);
        }

        RefreshPaintToolUi();
        statusText.text = paintBrushMode
            ? "BRUSH LOCKED — drag the body to paint."
            : "CAMERA FREE — swipe the world to orbit; tap BRUSH to paint again.";
    }

    private void BeginSample()
    {
        if (sampleEnvironment)
        {
            CancelSample();
            statusText.text = "Eyedropper canceled.";
            return;
        }

        CancelSample();
        sampleEnvironment = true;
        RefreshPaintToolUi();
        statusText.text = "EYEDROPPER ACTIVE — tap a wall, floor, or object to copy its visible color.";
    }

    private IEnumerator SampleRenderedWorldColor(Vector2 screenPosition)
    {
        // Sample after the scene and post-processing have rendered. This copies the
        // colour the player actually sees, including textures, lighting and shadow,
        // instead of only reading a material's flat BaseColor value.
        yield return new WaitForEndOfFrame();

        const int sampleSize = 5;
        if (screenColorSamplePixel == null || screenColorSamplePixel.width != sampleSize)
        {
            if (screenColorSamplePixel != null)
            {
                Destroy(screenColorSamplePixel);
            }

            screenColorSamplePixel = new Texture2D(sampleSize, sampleSize, TextureFormat.RGB24, false)
            {
                name = "Chamele-ON Screen Eyedropper"
            };
        }

        int x = Mathf.Clamp(Mathf.RoundToInt(screenPosition.x) - sampleSize / 2, 0,
            Mathf.Max(0, Screen.width - sampleSize));
        int y = Mathf.Clamp(Mathf.RoundToInt(screenPosition.y) - sampleSize / 2, 0,
            Mathf.Max(0, Screen.height - sampleSize));
        screenColorSamplePixel.ReadPixels(new Rect(x, y, sampleSize, sampleSize), 0, 0, false);
        screenColorSamplePixel.Apply(false, false);

        Color32[] sampledPixels = screenColorSamplePixel.GetPixels32();
        var reds = new byte[sampledPixels.Length];
        var greens = new byte[sampledPixels.Length];
        var blues = new byte[sampledPixels.Length];
        for (int i = 0; i < sampledPixels.Length; i++)
        {
            reds[i] = sampledPixels[i].r;
            greens[i] = sampledPixels[i].g;
            blues[i] = sampledPixels[i].b;
        }

        System.Array.Sort(reds);
        System.Array.Sort(greens);
        System.Array.Sort(blues);
        int medianIndex = sampledPixels.Length / 2;
        Color32 median = new Color32(reds[medianIndex], greens[medianIndex], blues[medianIndex], 255);
        int closestPixelIndex = 0;
        int closestDistance = int.MaxValue;
        for (int i = 0; i < sampledPixels.Length; i++)
        {
            Color32 pixel = sampledPixels[i];
            int distance = Mathf.Abs(pixel.r - median.r) + Mathf.Abs(pixel.g - median.g) +
                           Mathf.Abs(pixel.b - median.b);
            if (distance < closestDistance)
            {
                closestDistance = distance;
                closestPixelIndex = i;
            }
        }

        // Pick a real rendered pixel nearest the robust 5x5 median instead of
        // averaging two object edges into a colour that exists on neither.
        Color sampled = sampledPixels[closestPixelIndex];
        screenColorSampleRoutine = null;
        sampleEnvironment = false;
        brushColor = sampled;
        RefreshPaintToolUi();
        statusText.text = "VISIBLE COLOR PICKED — paint the body to match this exact spot.";
    }

    private void SetBrushColor(Color color)
    {
        CancelSample();
        brushColor = new Color(color.r, color.g, color.b, 1f);
        RefreshPaintToolUi();
        statusText.text = "Paint color set to #" + ColorUtility.ToHtmlStringRGB(brushColor) + ".";
    }

    private void SetBrushSize(float value)
    {
        brushRadius = Mathf.Clamp(Mathf.RoundToInt(value), MinimumBrushRadius, MaximumBrushRadius);
        RefreshPaintToolUi();
    }

    private void AdjustBrushSize(int delta)
    {
        SetBrushSize(brushRadius + delta);
        statusText.text = "Brush size: " + (brushRadius * 2) + " px diameter.";
    }

    private void RefreshPaintToolUi()
    {
        if (brushPreview != null)
        {
            brushPreview.color = brushColor;
        }

        if (brushHexText != null)
        {
            brushHexText.text = "#" + ColorUtility.ToHtmlStringRGB(brushColor);
        }

        if (brushSizeText != null)
        {
            // The engine stores a radius. Show the reference-atlas diameter,
            // matching the familiar Photoshop-style brush size label.
            brushSizeText.text = (brushRadius * 2) + " px";
        }

        if (brushSizePreview != null)
        {
            float normalizedSize = Mathf.InverseLerp(MinimumBrushRadius, MaximumBrushRadius, brushRadius);
            float previewDiameter = Mathf.Lerp(18f, 58f, normalizedSize);
            brushSizePreview.rectTransform.sizeDelta = Vector2.one * previewDiameter;
            brushSizePreview.color = brushColor;
        }

        if (brushSizeSlider != null && !Mathf.Approximately(brushSizeSlider.value, brushRadius))
        {
            brushSizeSlider.SetValueWithoutNotify(brushRadius);
        }

        for (int i = 0; i < swatchOutlines.Count && i < swatchColors.Count; i++)
        {
            Color swatch = swatchColors[i];
            float difference = Mathf.Abs(swatch.r - brushColor.r) + Mathf.Abs(swatch.g - brushColor.g) +
                               Mathf.Abs(swatch.b - brushColor.b);
            swatchOutlines[i].effectColor = difference < 0.025f
                ? new Color(1f, 1f, 1f, 0.98f)
                : new Color(1f, 1f, 1f, 0.12f);
        }

        if (sampleButton != null)
        {
            sampleButton.image.color = sampleEnvironment
                ? new Color(0.95f, 0.65f, 0.12f)
                : new Color(0.12f, 0.48f, 0.58f);
        }

        if (sampleButtonText != null)
        {
            sampleButtonText.text = sampleEnvironment
                ? "TAP OBJECT..."
                : "EYEDROPPER\nPICK 3D COLOR";
        }

        if (undoButton != null)
        {
            undoButton.interactable = paintUndoHistory.Count > 0;
        }

        if (redoButton != null)
        {
            redoButton.interactable = paintRedoHistory.Count > 0;
        }

        if (brushModeButton != null)
        {
            brushModeButton.image.color = paintBrushMode
                ? new Color(0.16f, 0.70f, 0.42f)
                : new Color(0.18f, 0.42f, 0.70f);
        }

        if (brushModeButtonText != null)
        {
            brushModeButtonText.text = paintBrushMode
                ? "BRUSH\nLOCKED"
                : "CAMERA\nFREE";
        }

        if (historyText != null)
        {
            historyText.text = paintUndoHistory.Count + " / " + MaximumPaintUndoSteps + " HISTORY";
        }
    }

    private void CancelSample()
    {
        sampleEnvironment = false;
        if (screenColorSampleRoutine != null)
        {
            StopCoroutine(screenColorSampleRoutine);
            screenColorSampleRoutine = null;
        }
        RefreshPaintToolUi();
    }

    private void CapturePaintUndoState()
    {
        PaintUndoState state = CaptureCurrentPaintState();
        if (state == null)
        {
            return;
        }

        // A new stroke creates a new branch. Photoshop-style editors discard
        // the forward history as soon as the user paints after an undo.
        ClearRedoHistory();
        paintUndoHistory.Add(state);
        TrimPaintHistory(paintUndoHistory);
        RefreshPaintToolUi();
    }

    private PaintUndoState CaptureCurrentPaintState()
    {
        if (mannequin == null)
        {
            return null;
        }

        PaintUndoState state = paintUndoPool.Count > 0
            ? paintUndoPool.Pop()
            : CreatePaintUndoState();
        bool capturedAny = false;
        for (int i = 0; i < state.Textures.Count; i++)
        {
            PaintTextureSnapshot snapshot = state.Textures[i];
            if (snapshot.Part != null && snapshot.Part.CopyPaintPixelsTo(snapshot.Pixels))
            {
                capturedAny = true;
            }
        }

        if (!capturedAny)
        {
            ReleasePaintUndoState(state);
            return null;
        }

        return state;
    }

    private void TrimPaintHistory(List<PaintUndoState> history)
    {
        while (history.Count > MaximumPaintUndoSteps)
        {
            PaintUndoState oldest = history[0];
            history.RemoveAt(0);
            ReleasePaintUndoState(oldest);
        }
    }

    private void ClearRedoHistory()
    {
        for (int i = 0; i < paintRedoHistory.Count; i++)
        {
            ReleasePaintUndoState(paintRedoHistory[i]);
        }

        paintRedoHistory.Clear();
    }

    private void ReleasePaintUndoState(PaintUndoState state)
    {
        if (state != null)
        {
            paintUndoPool.Push(state);
        }
    }

    private static void RestorePaintUndoState(PaintUndoState state)
    {
        if (state == null)
        {
            return;
        }

        for (int i = 0; i < state.Textures.Count; i++)
        {
            PaintTextureSnapshot snapshot = state.Textures[i];
            if (snapshot.Part == null || snapshot.Pixels == null)
            {
                continue;
            }

            snapshot.Part.RestorePaintPixels(snapshot.Pixels);
        }
    }

    private void PreparePaintUndoBuffers()
    {
        paintUndoHistory.Clear();
        paintRedoHistory.Clear();
        paintUndoPool.Clear();
        for (int i = 0; i < MaximumPaintUndoSteps; i++)
        {
            PaintUndoState state = CreatePaintUndoState();
            if (state.Textures.Count > 0)
            {
                paintUndoPool.Push(state);
            }
        }
    }

    private PaintUndoState CreatePaintUndoState()
    {
        var state = new PaintUndoState();
        if (mannequin == null)
        {
            return state;
        }

        var captured = new HashSet<Texture2D>();
        for (int i = 0; i < mannequin.PaintableParts.Count; i++)
        {
            PaintableBodyPart part = mannequin.PaintableParts[i];
            Texture2D texture = part != null ? part.PaintTexture : null;
            if (texture == null || !captured.Add(texture))
            {
                continue;
            }

            state.Textures.Add(new PaintTextureSnapshot
            {
                Part = part,
                Pixels = new Color32[texture.width * texture.height]
            });
        }

        return state;
    }

    private void UndoPaint()
    {
        CancelSample();
        EndStroke();
        if (paintUndoHistory.Count == 0)
        {
            statusText.text = "Nothing to undo.";
            return;
        }

        PaintUndoState current = CaptureCurrentPaintState();
        if (current == null)
        {
            statusText.text = "Undo is unavailable while the paint atlas is busy.";
            return;
        }

        int lastIndex = paintUndoHistory.Count - 1;
        PaintUndoState state = paintUndoHistory[lastIndex];
        paintUndoHistory.RemoveAt(lastIndex);
        RestorePaintUndoState(state);
        paintRedoHistory.Add(current);
        TrimPaintHistory(paintRedoHistory);
        ReleasePaintUndoState(state);
        RefreshPaintToolUi();
        statusText.text = "Undo — last paint stroke restored.";
    }

    private void RedoPaint()
    {
        CancelSample();
        EndStroke();
        if (paintRedoHistory.Count == 0)
        {
            statusText.text = "Nothing to redo.";
            return;
        }

        PaintUndoState current = CaptureCurrentPaintState();
        if (current == null)
        {
            statusText.text = "Redo is unavailable while the paint atlas is busy.";
            return;
        }

        int lastIndex = paintRedoHistory.Count - 1;
        PaintUndoState state = paintRedoHistory[lastIndex];
        paintRedoHistory.RemoveAt(lastIndex);
        RestorePaintUndoState(state);
        paintUndoHistory.Add(current);
        TrimPaintHistory(paintUndoHistory);
        ReleasePaintUndoState(state);
        RefreshPaintToolUi();
        statusText.text = "Redo — paint stroke restored.";
    }

    private void ClearBody()
    {
        CancelSample();
        CapturePaintUndoState();
        mannequin.ClearPaint();
        statusText.text = "Body paint cleared to white.";
        EndStroke();
    }

    private GameObject CreateBlock(string objectName, Vector3 position, Vector3 scale, Material material,
        Vector3 euler = default, bool colliderEnabled = true)
    {
        GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
        block.name = objectName;
        block.transform.SetParent(environmentRoot, false);
        block.transform.position = position;
        block.transform.rotation = Quaternion.Euler(euler);
        block.transform.localScale = scale;
        block.GetComponent<Renderer>().material = material;
        Collider collider = block.GetComponent<Collider>();
        collider.enabled = colliderEnabled;
        return block;
    }

    private GameObject PlaceFurniture(string resourceName, Vector3 position, float yaw, float targetHeight)
    {
        GameObject prefab = Resources.Load<GameObject>("Kenney3D/Furniture/" + resourceName);
        if (prefab == null)
        {
            return null;
        }

        GameObject instance = Instantiate(prefab, environmentRoot);
        instance.name = "Kenney " + resourceName;
        instance.transform.position = position;
        instance.transform.rotation = Quaternion.Euler(0f, yaw, 0f);

        Bounds bounds = CalculateBounds(instance);
        if (bounds.size.y > 0.001f)
        {
            float factor = targetHeight / bounds.size.y;
            instance.transform.localScale *= factor;
        }

        bounds = CalculateBounds(instance);
        instance.transform.position += Vector3.up * (position.y - bounds.min.y);

        foreach (MeshFilter filter in instance.GetComponentsInChildren<MeshFilter>())
        {
            if (filter.sharedMesh == null || filter.GetComponent<Collider>() != null)
            {
                continue;
            }

            var collider = filter.gameObject.AddComponent<MeshCollider>();
            collider.sharedMesh = filter.sharedMesh;
        }

        return instance;
    }

    private static Bounds CalculateBounds(GameObject root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
        {
            return new Bounds(root.transform.position, Vector3.one);
        }

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            bounds.Encapsulate(renderers[i].bounds);
        }

        return bounds;
    }

    private void CreatePointLight(string lightName, Vector3 position, Color color, float intensity, float range)
    {
        var lightObject = new GameObject(lightName, typeof(Light));
        lightObject.transform.SetParent(environmentRoot, false);
        lightObject.transform.position = position;
        Light light = lightObject.GetComponent<Light>();
        light.type = LightType.Point;
        light.color = color;
        light.intensity = intensity;
        light.range = range;
        light.shadows = LightShadows.None;
    }

    private Material MakeMaterial(string materialName, Color color, float metal, float gloss)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            shader = Shader.Find("Standard");
        }

        var material = new Material(shader) { name = materialName };
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        else material.color = color;
        if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", metal);
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", gloss);
        if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", gloss);
        runtimeMaterials.Add(material);
        return material;
    }

    private static float GetAxis(HoldInputButton negativeButton, HoldInputButton positiveButton,
        KeyCode negativeKey, KeyCode positiveKey, KeyCode alternateNegative, KeyCode alternatePositive)
    {
        bool negative = (negativeButton != null && negativeButton.IsPressed) || ChameleonInput.GetKey(negativeKey) || ChameleonInput.GetKey(alternateNegative);
        bool positive = (positiveButton != null && positiveButton.IsPressed) || ChameleonInput.GetKey(positiveKey) || ChameleonInput.GetKey(alternatePositive);
        return (positive ? 1f : 0f) - (negative ? 1f : 0f);
    }

    private static Image CreateImage(string objectName, Transform parent, Color color)
    {
        var obj = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        obj.transform.SetParent(parent, false);
        Image image = obj.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static Image CreatePanel(string objectName, Transform parent, Color color)
    {
        Image panel = CreateImage(objectName, parent, color);
        panel.sprite = RoundedSprite();
        panel.type = Image.Type.Sliced;
        return panel;
    }

    private static Text CreateText(string objectName, Transform parent, string value, int size, TextAnchor anchor)
    {
        var obj = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        obj.transform.SetParent(parent, false);
        Text text = obj.GetComponent<Text>();
        text.text = value;
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = size;
        text.alignment = anchor;
        text.color = Color.white;
        text.resizeTextForBestFit = false;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        text.raycastTarget = false;
        return text;
    }

    private static Button CreateButton(string objectName, Transform parent, Color color, string label,
        UnityEngine.Events.UnityAction action)
    {
        var obj = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        obj.transform.SetParent(parent, false);
        Image image = obj.GetComponent<Image>();
        image.color = color;
        image.sprite = RoundedSprite();
        image.type = Image.Type.Sliced;
        image.raycastTarget = true;

        Button button = obj.GetComponent<Button>();
        button.targetGraphic = image;
        button.onClick.AddListener(action);

        if (!string.IsNullOrEmpty(label))
        {
            Text text = CreateText("Label", obj.transform, label, 24, TextAnchor.MiddleCenter);
            text.fontStyle = FontStyle.Bold;
            Stretch(text.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        }

        return button;
    }

    private static HoldInputButton CreateHoldButton(string objectName, Transform parent, string label,
        Vector2 anchor, Vector2 size)
    {
        Image image = CreatePanel(objectName, parent, new Color(0.13f, 0.18f, 0.20f, 0.95f));
        image.raycastTarget = true;
        SetRect(image.rectTransform, anchor, anchor, size, Vector2.zero);
        HoldInputButton hold = image.gameObject.AddComponent<HoldInputButton>();
        Text text = CreateText("Label", image.transform, label, 20, TextAnchor.MiddleCenter);
        text.fontStyle = FontStyle.Bold;
        Stretch(text.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        return hold;
    }

    private static Slider CreateSlider(string objectName, Transform parent, Vector2 anchor, Vector2 size,
        float min, float max, float value, UnityEngine.Events.UnityAction<float> onChanged)
    {
        var sliderObject = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Slider));
        sliderObject.transform.SetParent(parent, false);
        SetRect(sliderObject.GetComponent<RectTransform>(), anchor, anchor, size, Vector2.zero);
        Image background = sliderObject.GetComponent<Image>();
        background.color = new Color(1f, 1f, 1f, 0.14f);
        background.sprite = RoundedSprite();
        background.type = Image.Type.Sliced;
        background.raycastTarget = true;

        Image fill = CreateImage("Fill", sliderObject.transform, new Color(0.30f, 0.86f, 0.58f));
        fill.sprite = RoundedSprite();
        fill.type = Image.Type.Sliced;
        Stretch(fill.rectTransform, new Vector2(0f, 0.30f), new Vector2(1f, 0.70f), new Vector2(8f, 0f), new Vector2(-8f, 0f));

        var handleAreaObject = new GameObject("Handle Slide Area", typeof(RectTransform));
        handleAreaObject.transform.SetParent(sliderObject.transform, false);
        Stretch(handleAreaObject.GetComponent<RectTransform>(), Vector2.zero, Vector2.one,
            new Vector2(24f, 0f), new Vector2(-24f, 0f));

        var handlePositionObject = new GameObject("Handle Position", typeof(RectTransform));
        handlePositionObject.transform.SetParent(handleAreaObject.transform, false);
        RectTransform handlePosition = handlePositionObject.GetComponent<RectTransform>();
        SetRect(handlePosition, new Vector2(0.5f, 0f), new Vector2(0.5f, 1f), Vector2.zero, Vector2.zero);

        Image handle = CreateImage("Handle", handlePosition, Color.white);
        handle.sprite = CircleSprite();
        handle.raycastTarget = true;
        SetRect(handle.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(42f, 42f), Vector2.zero);

        Slider slider = sliderObject.GetComponent<Slider>();
        slider.minValue = min;
        slider.maxValue = max;
        slider.value = value;
        slider.fillRect = fill.rectTransform;
        slider.handleRect = handlePosition;
        slider.targetGraphic = handle;
        slider.onValueChanged.AddListener(onChanged);
        return slider;
    }

    private static void SetLayerRecursively(GameObject root, int layer)
    {
        if (root == null || layer < 0)
        {
            return;
        }

        root.layer = layer;
        for (int i = 0; i < root.transform.childCount; i++)
        {
            SetLayerRecursively(root.transform.GetChild(i).gameObject, layer);
        }
    }

    private static void SetRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 size, Vector2 position)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = new Vector2(
            Mathf.Approximately(anchorMin.x, 0f) ? 0f : Mathf.Approximately(anchorMin.x, 1f) ? 1f : 0.5f,
            Mathf.Approximately(anchorMin.y, 0f) ? 0f : Mathf.Approximately(anchorMin.y, 1f) ? 1f : 0.5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
    }

    private static void Stretch(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
    }

    private static Sprite roundedSprite;
    private static Sprite circleSprite;

    private static Sprite RoundedSprite()
    {
        if (roundedSprite != null) return roundedSprite;
        const int size = 64;
        const int radius = 14;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = Mathf.Max(Mathf.Max(radius - x, 0f), x - (size - radius - 1));
                float dy = Mathf.Max(Mathf.Max(radius - y, 0f), y - (size - radius - 1));
                byte alpha = dx * dx + dy * dy <= radius * radius ? (byte)255 : (byte)0;
                pixels[y * size + x] = new Color32(255, 255, 255, alpha);
            }
        }
        texture.SetPixels32(pixels);
        texture.Apply();
        roundedSprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), Vector2.one * 0.5f, 100f, 0,
            SpriteMeshType.FullRect, Vector4.one * radius);
        return roundedSprite;
    }

    private static Sprite CircleSprite()
    {
        if (circleSprite != null) return circleSprite;
        const int size = 64;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var pixels = new Color32[size * size];
        Vector2 center = Vector2.one * ((size - 1) * 0.5f);
        float radius = size * 0.49f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                byte alpha = Vector2.Distance(new Vector2(x, y), center) <= radius ? (byte)255 : (byte)0;
                pixels[y * size + x] = new Color32(255, 255, 255, alpha);
            }
        }
        texture.SetPixels32(pixels);
        texture.Apply();
        circleSprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), Vector2.one * 0.5f, 100f);
        return circleSprite;
    }

#if UNITY_EDITOR
    private static bool HasCommandLineFlag(string flag)
    {
        foreach (string argument in System.Environment.GetCommandLineArgs())
        {
            if (string.Equals(argument, flag, System.StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private IEnumerator CaptureEditorPreview()
    {
        yield return new WaitForSecondsRealtime(2.0f);
        CaptureEditorFrame("/tmp/chameleon-3d-preview.png");
    }

    private IEnumerator RunEditorSmokeSequence()
    {
        yield return new WaitForSecondsRealtime(2.0f);
        CaptureEditorFrame("/tmp/chameleon-3d-preview.png");

        SetPlayMode(PlayMode.Paint);
        int savedBrushRadius = brushRadius;
        Color savedBrushColor = brushColor;
        SetBrushSize(MinimumBrushRadius);
        float smallPreviewDiameter = brushSizePreview.rectTransform.sizeDelta.x;
        SetBrushSize(MaximumBrushRadius);
        float largePreviewDiameter = brushSizePreview.rectTransform.sizeDelta.x;
        SetBrushColor(new Color32(35, 126, 201, 255));
        bool brushUiValid = brushSizeSlider != null && brushSizeSlider.wholeNumbers &&
                            Mathf.Approximately(brushSizeSlider.minValue, MinimumBrushRadius) &&
                            Mathf.Approximately(brushSizeSlider.maxValue, MaximumBrushRadius) &&
                            largePreviewDiameter > smallPreviewDiameter && brushHexText.text == "#237EC9";
        SetBrushSize(savedBrushRadius);
        SetBrushColor(savedBrushColor);
        if (!brushUiValid)
        {
            throw new System.InvalidOperationException("CHAMELEON_PAINT_TOOLS_AUDIT_FAIL: brush UI did not synchronize.");
        }

        if (mannequin.PaintableParts.Count > 0)
        {
            PaintableBodyPart undoAuditPart = mannequin.PaintableParts[0];
            Texture2D undoAuditTexture = undoAuditPart.PaintTexture;
            Color32 beforeUndoAudit = undoAuditTexture.GetPixel(undoAuditTexture.width / 2,
                undoAuditTexture.height / 2);
            CapturePaintUndoState();
            undoAuditPart.PaintStroke(Vector2.one * 0.5f, Vector2.one * 0.5f, Color.magenta, 8);
            Color32 paintedUndoAudit = undoAuditTexture.GetPixel(undoAuditTexture.width / 2,
                undoAuditTexture.height / 2);
            UndoPaint();
            Color32 restoredUndoAudit = undoAuditTexture.GetPixel(undoAuditTexture.width / 2,
                undoAuditTexture.height / 2);
            RedoPaint();
            Color32 redoneUndoAudit = undoAuditTexture.GetPixel(undoAuditTexture.width / 2,
                undoAuditTexture.height / 2);
            UndoPaint();
            undoAuditPart.PaintStroke(Vector2.one * 0.10f, Vector2.one * 0.10f, Color.cyan, 4);
            Color32 restoredAfterNextStroke = undoAuditTexture.GetPixel(undoAuditTexture.width / 2,
                undoAuditTexture.height / 2);
            if (paintedUndoAudit.Equals(beforeUndoAudit) || !restoredUndoAudit.Equals(beforeUndoAudit) ||
                !redoneUndoAudit.Equals(paintedUndoAudit) || !restoredAfterNextStroke.Equals(beforeUndoAudit))
            {
                throw new System.InvalidOperationException(
                    "CHAMELEON_PAINT_TOOLS_AUDIT_FAIL: undo/redo did not persist in the CPU-backed paint atlas.");
            }
        }

        Debug.Log("CHAMELEON_PAINT_TOOLS_AUDIT_PASS: brush range, live preview, HEX color, and undo/redo are synchronized.");
        Color[] smokeColors =
        {
            new Color(0.10f, 0.18f, 0.29f),
            new Color(0.82f, 0.25f, 0.19f),
            new Color(0.91f, 0.68f, 0.12f),
            new Color(0.16f, 0.55f, 0.32f)
        };

        for (int i = 0; i < mannequin.PaintableParts.Count; i++)
        {
            PaintableBodyPart part = mannequin.PaintableParts[i];
            for (int row = 0; row < 9; row++)
            {
                float y = Mathf.Lerp(0.08f, 0.92f, row / 8f);
                Color color = smokeColors[(row + i) % smokeColors.Length];
                part.PaintStroke(new Vector2(0.04f, y), new Vector2(0.36f, y), color, 18);
                part.PaintStroke(new Vector2(0.36f, y), new Vector2(0.68f, y), color, 18);
                part.PaintStroke(new Vector2(0.68f, y), new Vector2(0.96f, y), color, 18);
            }

            Texture2D atlas = part.PaintTexture;
            Renderer paintRenderer = part.GetComponent<Renderer>();
            Material activeMaterial = paintRenderer != null && paintRenderer.sharedMaterials.Length > 0
                ? paintRenderer.sharedMaterials[0]
                : null;
            Texture baseMap = activeMaterial != null && activeMaterial.HasProperty("_BaseMap")
                ? activeMaterial.GetTexture("_BaseMap")
                : null;
            Debug.Log($"CHAMELEON_PAINT_AUDIT: atlasCenter={atlas?.GetPixel(atlas.width / 2, atlas.height / 2)}, " +
                      $"shader={activeMaterial?.shader?.name}, baseMap={baseMap?.name}.");
        }

        Renderer[] bodyRenderers = playerRoot.GetComponentsInChildren<Renderer>();
        if (bodyRenderers.Length == 0)
        {
            throw new System.InvalidOperationException(
                "CHAMELEON_CHARACTER_SCALE_AUDIT_FAIL: the player has no body renderer.");
        }

        if (bodyRenderers.Length > 0)
        {
            Bounds bodyBounds = bodyRenderers[0].bounds;
            for (int i = 1; i < bodyRenderers.Length; i++) bodyBounds.Encapsulate(bodyRenderers[i].bounds);
            Debug.Log($"CHAMELEON_BODY_BOUNDS: root={playerRoot.position}, min={bodyBounds.min}, max={bodyBounds.max}.");
            bool scaleValid = bodyBounds.size.y >= 0.84f && bodyBounds.size.y <= 0.98f &&
                              Mathf.Abs(characterController.height - 0.975f) < 0.001f &&
                              Mathf.Abs(characterController.radius - 0.17f) < 0.001f &&
                              Mathf.Abs(characterController.center.y - 0.4875f) < 0.001f;
            if (!scaleValid)
            {
                throw new System.InvalidOperationException(
                    $"CHAMELEON_CHARACTER_SCALE_AUDIT_FAIL: bodyHeight={bodyBounds.size.y:F3}, " +
                    $"capsule={characterController.height:F3}/{characterController.radius:F3}.");
            }
            Debug.Log($"CHAMELEON_CHARACTER_SCALE_AUDIT_PASS: bodyHeight={bodyBounds.size.y:F3}, " +
                      $"capsule={characterController.height:F3}/{characterController.radius:F3}.");
        }

        mannequin.ApplyPose(4);
        poseText.text = "POSE: " + mannequin.GetPoseName();
        statusText.text = "Paint stays locked to the body while the pose changes.";
        UpdateCamera(true);
        yield return new WaitForSecondsRealtime(0.5f);
        CaptureEditorFrame("/tmp/chameleon-paint-preview.png");

        SetPlayMode(PlayMode.Explore);
        if (!PositionNearSmokeSurface())
        {
            throw new System.InvalidOperationException(
                "CHAMELEON_SMOKE_ATTACH_FAIL: could not position the player near a test surface.");
        }

        ToggleAttach();
        if (!attached || characterController.enabled)
        {
            throw new System.InvalidOperationException(
                "CHAMELEON_SMOKE_ATTACH_FAIL: STICK did not enter the attached state.");
        }

        ToggleAttach();
        if (attached || !characterController.enabled)
        {
            throw new System.InvalidOperationException(
                "CHAMELEON_SMOKE_DETACH_FAIL: DETACH did not restore the CharacterController.");
        }

        // Reattach so the captured frame continues to exercise and document the
        // final wall-hiding composition after the detach assertion.
        ToggleAttach();
        if (!attached || characterController.enabled)
        {
            throw new System.InvalidOperationException(
                "CHAMELEON_SMOKE_REATTACH_FAIL: the player could not attach again after detaching.");
        }
        cameraPitch = 10f;
        UpdateCamera(true);
        yield return new WaitForSecondsRealtime(0.5f);
        CaptureEditorFrame("/tmp/chameleon-attach-preview.png");

        Debug.Log("CHAMELEON_SMOKE_PASS: room, body paint, pose change, and surface attachment were exercised.");
    }

    private bool PositionNearSmokeSurface()
    {
        Vector3 origin = playerRoot.position + Vector3.up * (1.02f * CharacterScale);
        int mask = ~((1 << RuntimeMannequin.PaintLayer) | (1 << 2));
        bool found = false;
        RaycastHit nearest = default;
        float nearestDistance = 20f;

        for (int i = 0; i < 16; i++)
        {
            float angle = i * (360f / 16f);
            Vector3 direction = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
            if (!Physics.Raycast(origin, direction, out RaycastHit hit, nearestDistance, mask, QueryTriggerInteraction.Ignore) ||
                Mathf.Abs(hit.normal.y) >= 0.55f)
            {
                continue;
            }

            found = true;
            nearest = hit;
            nearestDistance = hit.distance;
        }

        if (!found)
        {
            Debug.LogError("CHAMELEON_SMOKE_ATTACH_FAIL: no vertical block-map surface was found near PlayerSpawn.");
            return false;
        }

        characterController.enabled = false;
        playerRoot.position = nearest.point + nearest.normal.normalized * (0.72f * CharacterScale) -
                              Vector3.up * (1.02f * CharacterScale);
        playerRoot.rotation = Quaternion.LookRotation(-nearest.normal.normalized, Vector3.up);
        characterController.enabled = true;
        Physics.SyncTransforms();
        return true;
    }

    private void CaptureEditorFrame(string path)
    {
        const int width = 1280;
        const int height = 720;
        Canvas.ForceUpdateCanvases();
        var renderTexture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
        RenderTexture previousTarget = gameCamera.targetTexture;
        RenderTexture previousActive = RenderTexture.active;
        gameCamera.targetTexture = renderTexture;
        gameCamera.Render();

        // Camera.Render does not execute an URP camera stack in every batch-mode
        // editor configuration. Render the crisp UI to a transparent texture and
        // composite it below so the smoke image still matches an on-device frame.
        RenderTexture interfaceTexture = null;
        Texture2D interfaceImage = null;
        if (interfaceCamera != null)
        {
            RenderTexture previousInterfaceTarget = interfaceCamera.targetTexture;
            CameraClearFlags previousClearFlags = interfaceCamera.clearFlags;
            Color previousBackground = interfaceCamera.backgroundColor;
            UniversalAdditionalCameraData interfaceData = interfaceCamera.GetComponent<UniversalAdditionalCameraData>();
            if (interfaceData != null) interfaceData.renderType = CameraRenderType.Base;
            interfaceTexture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            interfaceCamera.clearFlags = CameraClearFlags.SolidColor;
            interfaceCamera.backgroundColor = Color.clear;
            interfaceCamera.targetTexture = interfaceTexture;
            interfaceCamera.Render();
            interfaceCamera.targetTexture = previousInterfaceTarget;
            interfaceCamera.clearFlags = previousClearFlags;
            interfaceCamera.backgroundColor = previousBackground;
            if (interfaceData != null) interfaceData.renderType = CameraRenderType.Overlay;

            RenderTexture.active = interfaceTexture;
            interfaceImage = new Texture2D(width, height, TextureFormat.RGBA32, false);
            interfaceImage.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
            interfaceImage.Apply();
        }

        RenderTexture.active = renderTexture;
        var image = new Texture2D(width, height, TextureFormat.RGB24, false);
        image.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
        image.Apply();

        if (interfaceImage != null)
        {
            Color32[] scenePixels = image.GetPixels32();
            Color32[] interfacePixels = interfaceImage.GetPixels32();
            for (int i = 0; i < scenePixels.Length && i < interfacePixels.Length; i++)
            {
                int alpha = interfacePixels[i].a;
                if (alpha == 0) continue;
                int inverse = 255 - alpha;
                scenePixels[i] = new Color32(
                    (byte)((interfacePixels[i].r * alpha + scenePixels[i].r * inverse + 127) / 255),
                    (byte)((interfacePixels[i].g * alpha + scenePixels[i].g * inverse + 127) / 255),
                    (byte)((interfacePixels[i].b * alpha + scenePixels[i].b * inverse + 127) / 255),
                    255);
            }
            image.SetPixels32(scenePixels);
            image.Apply();
        }

        File.WriteAllBytes(path, image.EncodeToPNG());
        gameCamera.targetTexture = previousTarget;
        RenderTexture.active = previousActive;
        Destroy(renderTexture);
        if (interfaceTexture != null) Destroy(interfaceTexture);
        if (interfaceImage != null) Destroy(interfaceImage);
        Destroy(image);
    }
#endif

    private void OnDestroy()
    {
        if (screenColorSamplePixel != null)
        {
            Destroy(screenColorSamplePixel);
        }

        foreach (Material material in runtimeMaterials)
        {
            if (material != null) Destroy(material);
        }
    }
}
