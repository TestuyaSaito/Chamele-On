using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering;

public sealed class RiggedRuntimeMannequin : MonoBehaviour, IMannequinVisual
{
    private const float TargetHeight = 0.91f;
    private const float CrossfadeSeconds = 0.18f;
    private static readonly string[] CharacterResourcePaths =
    {
        "Characters/PolyOneStickMan/Free Pack - Stick Man",
        "Characters/UAL1_Standard",
        "Characters/UAL2_Standard"
    };

    private readonly List<PaintableBodyPart> paintableParts = new List<PaintableBodyPart>();
    private readonly List<SkinnedMeshRenderer> skinnedRenderers = new List<SkinnedMeshRenderer>();
    private readonly List<MeshCollider> paintColliders = new List<MeshCollider>();
    private readonly List<Mesh> bakedMeshes = new List<Mesh>();
    private readonly List<Vector2[]> paintColliderUvs = new List<Vector2[]>();
    private readonly List<Mesh> runtimeSkinnedMeshes = new List<Mesh>();
    private readonly List<AnimationClip> clips = new List<AnimationClip>();

    private GameObject visualInstance;
    private Material paintTemplate;
    private SharedBodyPaintTexture sharedPaint;
    private Animator animator;
    private string activeResourcePath;
    private Transform proceduralLeftArm;
    private Transform proceduralRightArm;
    private Transform proceduralLeftForeArm;
    private Transform proceduralRightForeArm;
    private Transform proceduralLeftUpLeg;
    private Transform proceduralRightUpLeg;
    private Transform proceduralLeftLeg;
    private Transform proceduralRightLeg;
    private Transform proceduralLeftFoot;
    private Transform proceduralRightFoot;
    private Quaternion proceduralLeftArmRest;
    private Quaternion proceduralRightArmRest;
    private Quaternion proceduralLeftForeArmRest;
    private Quaternion proceduralRightForeArmRest;
    private Quaternion proceduralLeftUpLegRest;
    private Quaternion proceduralRightUpLegRest;
    private Quaternion proceduralLeftLegRest;
    private Quaternion proceduralRightLegRest;
    private Quaternion proceduralLeftFootRest;
    private Quaternion proceduralRightFootRest;
    private float proceduralWalkTime;
    private bool proceduralMotionReady;

    private PlayableGraph animationGraph;
    private AnimationMixerPlayable animationMixer;
    private AnimationClipPlayable activePlayable;
    private AnimationClip activeClip;
    private int activeInput = -1;
    private int fadingInput = -1;
    private float fadeElapsed;
    private bool activeLoops;
    private string activeState = string.Empty;
    private float locomotion;
    private bool colliderUvLogged;

    public bool IsBuilt { get; private set; }
    public int PoseIndex { get; private set; }
    public IReadOnlyList<PaintableBodyPart> PaintableParts => paintableParts;

    public void Build()
    {
        if (IsBuilt)
        {
            return;
        }

        GameObject source = LoadCharacterResource();
        if (source == null)
        {
            Debug.LogWarning("Rigged mannequin assets were not imported; using procedural fallback.");
            return;
        }

        visualInstance = Instantiate(source, transform);
        visualInstance.name = "Rigged White Mannequin";
        visualInstance.transform.localPosition = Vector3.zero;
        visualInstance.transform.localRotation = Quaternion.identity;
        visualInstance.transform.localScale = Vector3.one;

        CollectRenderableMeshes();
        if (skinnedRenderers.Count == 0)
        {
            Debug.LogWarning("Rigged mannequin has no usable SkinnedMeshRenderer; using procedural fallback.");
            Destroy(visualInstance);
            visualInstance = null;
            return;
        }

        NormalizeModelScaleAndOrigin();
        CollectProceduralMotionBones();
        CreatePaintMaterialsAndColliders();
        CollectAnimationClips();
        ConfigureAnimation();

        IsBuilt = paintableParts.Count > 0;
        if (!IsBuilt)
        {
            Debug.LogWarning("Rigged mannequin could not create UV paint colliders; using procedural fallback.");
            Destroy(visualInstance);
            visualInstance = null;
            return;
        }

        ApplyPose(0);
        RefreshPaintColliders();
        Debug.Log("CHAMELEON_RIGGED_BODY_READY: " + skinnedRenderers.Count +
                  " skinned mesh(es), " + clips.Count + " animation clip(s).");
    }

    private GameObject LoadCharacterResource()
    {
        activeResourcePath = null;
        for (int i = 0; i < CharacterResourcePaths.Length; i++)
        {
            string path = CharacterResourcePaths[i];
            GameObject source = Resources.Load<GameObject>(path);
            if (source == null)
            {
                continue;
            }

            activeResourcePath = path;
            Debug.Log("CHAMELEON_RIGGED_RESOURCE: " + path);
            return source;
        }

        return null;
    }

    public void ApplyPose(int index)
    {
        if (!IsBuilt)
        {
            return;
        }

        PoseIndex = Mathf.Abs(index) % 5;
        AnimationClip clip;
        bool loop = true;
        float normalizedTime = 0f;

        switch (PoseIndex)
        {
            case 1:
                clip = FindClip("Crouch_Idle_Loop", "Fixing_Kneeling", "Crouch");
                StartClip(clip, "pose-crouch", true, 0f, 1f);
                break;
            case 2:
                clip = FindClip("Sitting_Idle_Loop", "Fixing_Kneeling", "LayToIdle", "Crouch_Idle_Loop");
                StartClip(clip, "pose-curl", true, 0.15f, 1f);
                break;
            case 3:
                clip = FindClip("Push_Loop", "Idle_Rail_Loop", "Idle_FoldArms_Loop", "A_TPose");
                StartClip(clip, "pose-wall", clip != null && clip.name.IndexOf("Loop", StringComparison.OrdinalIgnoreCase) >= 0, 0.25f, 1f);
                break;
            case 4:
                clip = FindClip("Dance_Loop", "NinjaJump_Idle_Loop", "Idle_Rail_Loop", "Idle_FoldArms_Loop");
                if (clip != null && Normalize(clip.name).Contains("tpose"))
                {
                    clip = FindFirstNonTPoseClip();
                }
                loop = clip != null && clip.name.IndexOf("Loop", StringComparison.OrdinalIgnoreCase) >= 0;
                normalizedTime = loop ? 0.20f : 0f;
                StartClip(clip, "pose-star", loop, normalizedTime, loop ? 0.75f : 0f);
                break;
            default:
                SetLocomotion(locomotion);
                break;
        }

        if (animationGraph.IsValid())
        {
            animationGraph.Evaluate(0f);
        }
        RefreshPaintColliders();
    }

    public string GetPoseName()
    {
        switch (PoseIndex)
        {
            case 1: return "CROUCH";
            case 2: return "CURL";
            case 3: return "WALL FLAT";
            case 4: return "STAR";
            default: return "STAND";
        }
    }

    public void ClearPaint()
    {
        sharedPaint?.Reset();
    }

    public void SetMaterialResponse(float metallic, float smoothness)
    {
        for (int i = 0; i < paintableParts.Count; i++)
        {
            paintableParts[i].SetMaterialResponse(metallic, smoothness);
        }
    }

    public void SetLocomotion(float normalizedSpeed)
    {
        locomotion = Mathf.Clamp01(normalizedSpeed);
        if (!IsBuilt || PoseIndex != 0)
        {
            return;
        }

        if (clips.Count == 0)
        {
            return;
        }

        if (locomotion > 0.08f)
        {
            AnimationClip walk = FindClip("Walk_Loop", "Jog_Fwd_Loop", "Walk_Formal_Loop", "Walk_Carry_Loop", "Zombie_Walk_Fwd_Loop");
            StartClip(walk, "locomotion-walk", true, 0f, Mathf.Lerp(0.78f, 1.28f, locomotion));
        }
        else
        {
            AnimationClip idle = FindClip("Idle_Loop", "Idle_FoldArms_Loop", "Zombie_Idle_Loop", "Idle_No_Loop");
            StartClip(idle, "locomotion-idle", true, 0f, 1f);
        }
    }

    private void CollectRenderableMeshes()
    {
        SkinnedMeshRenderer[] renderers = visualInstance.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            SkinnedMeshRenderer renderer = renderers[i];
            if (renderer.sharedMesh == null || renderer.sharedMesh.vertexCount == 0)
            {
                continue;
            }

            if (IsSeparateFacialFeature(renderer.name))
            {
                renderer.enabled = false;
                continue;
            }

            renderer.enabled = true;
            renderer.updateWhenOffscreen = true;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
            renderer.gameObject.layer = RuntimeMannequin.PaintLayer;
            skinnedRenderers.Add(renderer);
        }
    }

    private void NormalizeModelScaleAndOrigin()
    {
        Bounds bounds = CalculateBounds();
        if (bounds.size.y > 0.001f)
        {
            float scale = TargetHeight / bounds.size.y;
            visualInstance.transform.localScale = Vector3.one * scale;
        }

        bounds = CalculateBounds();
        Vector3 desiredBase = transform.position;
        Vector3 currentBase = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
        visualInstance.transform.position += desiredBase - currentBase;
    }

    private Bounds CalculateBounds()
    {
        Bounds bounds = skinnedRenderers[0].bounds;
        for (int i = 1; i < skinnedRenderers.Count; i++)
        {
            bounds.Encapsulate(skinnedRenderers[i].bounds);
        }
        return bounds;
    }

    private void CreatePaintMaterialsAndColliders()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            shader = Shader.Find("Standard");
        }

        if (shader == null)
        {
            Debug.LogError("No lit shader is available for the rigged mannequin.");
            return;
        }

        paintTemplate = new Material(shader)
        {
            name = "Rigged White Body Paint Template",
            enableInstancing = true
        };
        if (paintTemplate.HasProperty("_BaseColor")) paintTemplate.SetColor("_BaseColor", Color.white);
        else if (paintTemplate.HasProperty("_Color")) paintTemplate.SetColor("_Color", Color.white);
        if (paintTemplate.HasProperty("_Metallic")) paintTemplate.SetFloat("_Metallic", 0f);
        if (paintTemplate.HasProperty("_Glossiness")) paintTemplate.SetFloat("_Glossiness", 0.24f);
        if (paintTemplate.HasProperty("_Smoothness")) paintTemplate.SetFloat("_Smoothness", 0.24f);

        sharedPaint = new SharedBodyPaintTexture(512, "Rigged Body Paint Atlas");

        for (int i = 0; i < skinnedRenderers.Count; i++)
        {
            SkinnedMeshRenderer renderer = skinnedRenderers[i];
            PreparePaintUv(renderer);
            MeshCollider collider = renderer.GetComponent<MeshCollider>();
            if (collider == null)
            {
                collider = renderer.gameObject.AddComponent<MeshCollider>();
            }
            collider.convex = false;

            Mesh bakedMesh = new Mesh
            {
                name = renderer.name + " Baked Paint Collider"
            };
            bakedMesh.MarkDynamic();

            PaintableBodyPart paintable = renderer.GetComponent<PaintableBodyPart>();
            if (paintable == null)
            {
                paintable = renderer.gameObject.AddComponent<PaintableBodyPart>();
            }
            paintable.InitializeShared(sharedPaint, renderer, paintTemplate);

            paintColliders.Add(collider);
            bakedMeshes.Add(bakedMesh);
            paintColliderUvs.Add(renderer.sharedMesh != null ? renderer.sharedMesh.uv : null);
            paintableParts.Add(paintable);
        }
    }

    private void PreparePaintUv(SkinnedMeshRenderer renderer)
    {
        Mesh source = renderer.sharedMesh;
        Debug.Log("CHAMELEON_UV_SOURCE: " + DescribeUv(source));
        if (source == null || HasUsableUv(source.uv))
        {
            return;
        }

        Vector2[] secondaryUv = source.uv2;
        Vector2[] paintUv;
        string uvSource;
        int collapsedUvCount = 0;
        if (HasUsableUv(secondaryUv) && secondaryUv.Length == source.vertexCount)
        {
            collapsedUvCount = CountCollapsedUvs(secondaryUv);
            paintUv = collapsedUvCount > secondaryUv.Length / 20
                ? BuildCylindricalPaintUv(source)
                : secondaryUv;
            uvSource = ReferenceEquals(paintUv, secondaryUv) ? "copied UV2" : "generated complete cylindrical UVs";
        }
        else
        {
            paintUv = BuildCylindricalPaintUv(source);
            uvSource = "generated fallback cylindrical UVs";
        }

        Mesh runtimeMesh = RebuildMeshWithPaintUv(source, paintUv);
        runtimeMesh.UploadMeshData(false);
        renderer.sharedMesh = null;
        renderer.sharedMesh = runtimeMesh;
        renderer.enabled = false;
        renderer.enabled = true;
        runtimeSkinnedMeshes.Add(runtimeMesh);
        Debug.Log("CHAMELEON_PAINT_UV_READY: " +
                  uvSource +
                  " into UV0 for " + renderer.name + "; collapsedSourceUvs=" + collapsedUvCount + "/" +
                  (secondaryUv != null ? secondaryUv.Length : 0) + "; " +
                  DescribeUv(runtimeMesh));
    }

    private static int CountCollapsedUvs(Vector2[] uv)
    {
        int collapsed = 0;
        for (int i = 0; i < uv.Length; i++)
        {
            if (uv[i].sqrMagnitude < 0.000001f)
            {
                collapsed++;
            }
        }
        return collapsed;
    }

    private static Vector2[] BuildCylindricalPaintUv(Mesh source)
    {
        Vector3[] vertices = source.vertices;
        Bounds bounds = source.bounds;
        Vector3 size = bounds.size;
        int heightAxis = size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;
        float minimumHeight = heightAxis == 0 ? bounds.min.x : heightAxis == 1 ? bounds.min.y : bounds.min.z;
        float height = Mathf.Max(0.0001f, heightAxis == 0 ? size.x : heightAxis == 1 ? size.y : size.z);
        var uv = new Vector2[vertices.Length];

        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 vertex = vertices[i];
            float vertical;
            float radialA;
            float radialB;
            if (heightAxis == 0)
            {
                vertical = vertex.x;
                radialA = vertex.y - bounds.center.y;
                radialB = vertex.z - bounds.center.z;
            }
            else if (heightAxis == 1)
            {
                vertical = vertex.y;
                radialA = vertex.x - bounds.center.x;
                radialB = vertex.z - bounds.center.z;
            }
            else
            {
                vertical = vertex.z;
                radialA = vertex.x - bounds.center.x;
                radialB = vertex.y - bounds.center.y;
            }

            float u = Mathf.Atan2(radialB, radialA) / (Mathf.PI * 2f) + 0.5f;
            float v = Mathf.Clamp01((vertical - minimumHeight) / height);
            uv[i] = new Vector2(Mathf.Lerp(0.015f, 0.985f, u), Mathf.Lerp(0.015f, 0.985f, v));
        }

        return uv;
    }

    private static Mesh RebuildMeshWithPaintUv(Mesh source, Vector2[] paintUv)
    {
        // Rebuilding the vertex buffers is intentional. Some compressed FBX
        // skinned meshes retain their original GPU vertex declaration when a
        // cloned mesh's UV array is replaced; the CPU mesh then reports the new
        // UVs while URP continues sampling the original all-zero UV0 stream.
        var mesh = new Mesh
        {
            name = source.name + " Paint UV",
            indexFormat = source.indexFormat
        };

        mesh.vertices = source.vertices;
        if (source.normals != null && source.normals.Length == source.vertexCount) mesh.normals = source.normals;
        if (source.tangents != null && source.tangents.Length == source.vertexCount) mesh.tangents = source.tangents;
        if (source.colors32 != null && source.colors32.Length == source.vertexCount) mesh.colors32 = source.colors32;
        mesh.uv = paintUv;
        mesh.uv2 = paintUv;
        mesh.bindposes = source.bindposes;
        mesh.boneWeights = source.boneWeights;

        mesh.subMeshCount = source.subMeshCount;
        for (int subMesh = 0; subMesh < source.subMeshCount; subMesh++)
        {
            mesh.SetIndices(source.GetIndices(subMesh), source.GetTopology(subMesh), subMesh, false);
        }

        CopyBlendShapes(source, mesh);
        mesh.bounds = source.bounds;
        mesh.RecalculateBounds();
        return mesh;
    }

    private static void CopyBlendShapes(Mesh source, Mesh target)
    {
        if (source.blendShapeCount == 0)
        {
            return;
        }

        var deltaVertices = new Vector3[source.vertexCount];
        var deltaNormals = new Vector3[source.vertexCount];
        var deltaTangents = new Vector3[source.vertexCount];
        for (int shape = 0; shape < source.blendShapeCount; shape++)
        {
            string shapeName = source.GetBlendShapeName(shape);
            int frameCount = source.GetBlendShapeFrameCount(shape);
            for (int frame = 0; frame < frameCount; frame++)
            {
                source.GetBlendShapeFrameVertices(shape, frame, deltaVertices, deltaNormals, deltaTangents);
                target.AddBlendShapeFrame(shapeName, source.GetBlendShapeFrameWeight(shape, frame),
                    deltaVertices, deltaNormals, deltaTangents);
            }
        }
    }

    private static string DescribeUv(Mesh mesh)
    {
        if (mesh == null)
        {
            return "mesh=null";
        }

        return mesh.name + " vertices=" + mesh.vertexCount +
               " uv0=" + DescribeUvRange(mesh.uv) +
               " uv1=" + DescribeUvRange(mesh.uv2) +
               " uv2=" + DescribeUvRange(mesh.uv3) +
               " uv3=" + DescribeUvRange(mesh.uv4);
    }

    private static string DescribeUvRange(Vector2[] uv)
    {
        if (uv == null || uv.Length == 0)
        {
            return "none";
        }

        Vector2 minimum = uv[0];
        Vector2 maximum = uv[0];
        for (int i = 1; i < uv.Length; i++)
        {
            minimum = Vector2.Min(minimum, uv[i]);
            maximum = Vector2.Max(maximum, uv[i]);
        }

        return uv.Length + "[" + minimum.ToString("F3") + ".." + maximum.ToString("F3") + "]";
    }

    private static bool HasUsableUv(Vector2[] uv)
    {
        if (uv == null || uv.Length < 3)
        {
            return false;
        }

        Vector2 minimum = uv[0];
        Vector2 maximum = uv[0];
        for (int i = 1; i < uv.Length; i++)
        {
            minimum = Vector2.Min(minimum, uv[i]);
            maximum = Vector2.Max(maximum, uv[i]);
        }

        Vector2 range = maximum - minimum;
        return range.x > 0.05f && range.y > 0.05f;
    }

    private void CollectAnimationClips()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrEmpty(activeResourcePath))
        {
            AddClips(Resources.LoadAll<AnimationClip>(activeResourcePath), names);
        }
    }

    private void AddClips(AnimationClip[] sourceClips, HashSet<string> names)
    {
        for (int i = 0; i < sourceClips.Length; i++)
        {
            AnimationClip clip = sourceClips[i];
            if (clip == null || clip.name.StartsWith("__preview__", StringComparison.OrdinalIgnoreCase) || !names.Add(clip.name))
            {
                continue;
            }
            clips.Add(clip);
        }
    }

    private void ConfigureAnimation()
    {
        animator = visualInstance.GetComponentInChildren<Animator>();
        if (animator == null)
        {
            animator = visualInstance.AddComponent<Animator>();
        }
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        animator.updateMode = AnimatorUpdateMode.Normal;

        animationGraph = PlayableGraph.Create("ChameleON Rigged Mannequin Animation");
        animationGraph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
        animationMixer = AnimationMixerPlayable.Create(animationGraph, 2);
        AnimationPlayableOutput output = AnimationPlayableOutput.Create(animationGraph, "Body Animation", animator);
        output.SetSourcePlayable(animationMixer);
        animationGraph.Play();
    }

    private AnimationClip FindClip(params string[] preferences)
    {
        AnimationClip best = null;
        int bestScore = int.MinValue;

        for (int clipIndex = 0; clipIndex < clips.Count; clipIndex++)
        {
            AnimationClip clip = clips[clipIndex];
            string clipName = Normalize(clip.name);
            for (int preferenceIndex = 0; preferenceIndex < preferences.Length; preferenceIndex++)
            {
                string wanted = Normalize(preferences[preferenceIndex]);
                int score;
                if (clipName == wanted)
                {
                    score = 10000 - preferenceIndex * 100;
                }
                else if (clipName.Contains(wanted))
                {
                    score = 7000 - preferenceIndex * 100 - (clipName.Length - wanted.Length);
                }
                else if (wanted.Contains(clipName))
                {
                    score = 5000 - preferenceIndex * 100 - (wanted.Length - clipName.Length);
                }
                else
                {
                    continue;
                }

                if (score > bestScore)
                {
                    bestScore = score;
                    best = clip;
                }
            }
        }

        return best ?? (clips.Count > 0 ? clips[0] : null);
    }

    private AnimationClip FindFirstNonTPoseClip()
    {
        for (int i = 0; i < clips.Count; i++)
        {
            AnimationClip candidate = clips[i];
            if (candidate != null && !Normalize(candidate.name).Contains("tpose"))
            {
                return candidate;
            }
        }

        return null;
    }

    private void StartClip(AnimationClip clip, string stateName, bool loop, float normalizedTime, float speed)
    {
        if (clip == null || !animationGraph.IsValid())
        {
            return;
        }

        if (activeState == stateName && activePlayable.IsValid())
        {
            activePlayable.SetSpeed(speed);
            return;
        }

        Debug.Log($"CHAMELEON_ANIMATION: {stateName} -> {clip.name}.");

        FinishCrossfade();
        int nextInput = activeInput == 0 ? 1 : 0;
        Playable stale = animationMixer.GetInput(nextInput);
        if (stale.IsValid())
        {
            animationGraph.Disconnect(animationMixer, nextInput);
            animationGraph.DestroyPlayable(stale);
        }

        AnimationClipPlayable nextPlayable = AnimationClipPlayable.Create(animationGraph, clip);
        nextPlayable.SetApplyFootIK(false);
        nextPlayable.SetApplyPlayableIK(false);
        nextPlayable.SetTime(Mathf.Clamp01(normalizedTime) * Mathf.Max(0.01f, clip.length));
        nextPlayable.SetSpeed(speed);
        animationGraph.Connect(nextPlayable, 0, animationMixer, nextInput);

        if (activeInput < 0)
        {
            animationMixer.SetInputWeight(nextInput, 1f);
            fadingInput = -1;
        }
        else
        {
            fadingInput = activeInput;
            animationMixer.SetInputWeight(fadingInput, 1f);
            animationMixer.SetInputWeight(nextInput, 0f);
            fadeElapsed = 0f;
        }

        activeInput = nextInput;
        activePlayable = nextPlayable;
        activeClip = clip;
        activeLoops = loop;
        activeState = stateName;
    }

    private void Update()
    {
        if (fadingInput >= 0 && animationGraph.IsValid())
        {
            fadeElapsed += Time.deltaTime;
            float blend = Mathf.Clamp01(fadeElapsed / CrossfadeSeconds);
            animationMixer.SetInputWeight(fadingInput, 1f - blend);
            animationMixer.SetInputWeight(activeInput, blend);
            if (blend >= 1f)
            {
                FinishCrossfade();
            }
        }

        if (activeLoops && activePlayable.IsValid() && activeClip != null && activeClip.length > 0.01f)
        {
            double time = activePlayable.GetTime();
            if (time >= activeClip.length)
            {
                activePlayable.SetTime(time % activeClip.length);
            }
        }
    }

    private void LateUpdate()
    {
        if (!IsBuilt)
        {
            return;
        }

        ApplyProceduralMotion();

        if ((Time.frameCount & 1) == 0)
        {
            RefreshPaintColliders();
        }
    }

    private void CollectProceduralMotionBones()
    {
        proceduralLeftArm = FindBone("LeftArm");
        proceduralRightArm = FindBone("RightArm");
        proceduralLeftForeArm = FindBone("LeftForeArm");
        proceduralRightForeArm = FindBone("RightForeArm");
        proceduralLeftUpLeg = FindBone("LeftUpLeg");
        proceduralRightUpLeg = FindBone("RightUpLeg");
        proceduralLeftLeg = FindBone("LeftLeg");
        proceduralRightLeg = FindBone("RightLeg");
        proceduralLeftFoot = FindBone("LeftFoot");
        proceduralRightFoot = FindBone("RightFoot");

        proceduralMotionReady = proceduralLeftArm != null && proceduralRightArm != null &&
                                proceduralLeftUpLeg != null && proceduralRightUpLeg != null;
        if (!proceduralMotionReady)
        {
            return;
        }

        proceduralLeftArmRest = proceduralLeftArm.localRotation;
        proceduralRightArmRest = proceduralRightArm.localRotation;
        proceduralLeftForeArmRest = proceduralLeftForeArm != null ? proceduralLeftForeArm.localRotation : Quaternion.identity;
        proceduralRightForeArmRest = proceduralRightForeArm != null ? proceduralRightForeArm.localRotation : Quaternion.identity;
        proceduralLeftUpLegRest = proceduralLeftUpLeg.localRotation;
        proceduralRightUpLegRest = proceduralRightUpLeg.localRotation;
        proceduralLeftLegRest = proceduralLeftLeg != null ? proceduralLeftLeg.localRotation : Quaternion.identity;
        proceduralRightLegRest = proceduralRightLeg != null ? proceduralRightLeg.localRotation : Quaternion.identity;
        proceduralLeftFootRest = proceduralLeftFoot != null ? proceduralLeftFoot.localRotation : Quaternion.identity;
        proceduralRightFootRest = proceduralRightFoot != null ? proceduralRightFoot.localRotation : Quaternion.identity;
        Debug.Log("CHAMELEON_PROCEDURAL_WALK_READY: stick-man bones were found.");
    }

    private Transform FindBone(string boneName)
    {
        if (visualInstance == null)
        {
            return null;
        }

        string wanted = Normalize(boneName);
        Transform[] transforms = visualInstance.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
        {
            Transform candidate = transforms[i];
            if (candidate != null && Normalize(candidate.name) == wanted)
            {
                return candidate;
            }
        }

        return null;
    }

    private void ApplyProceduralMotion()
    {
        if (!proceduralMotionReady || clips.Count > 0)
        {
            return;
        }

        float speed = PoseIndex == 0 ? locomotion : 0f;
        proceduralWalkTime += Time.deltaTime * Mathf.Lerp(5.2f, 8.8f, speed);
        float fade = Mathf.Clamp01(speed * 1.35f);
        float swing = Mathf.Sin(proceduralWalkTime) * 28f * fade;
        float counterSwing = -swing;
        float leftKneeBend = Mathf.Max(0f, Mathf.Sin(proceduralWalkTime + Mathf.PI * 0.5f)) * 18f * fade;
        float rightKneeBend = Mathf.Max(0f, Mathf.Sin(proceduralWalkTime - Mathf.PI * 0.5f)) * 18f * fade;
        float footLift = Mathf.Sin(proceduralWalkTime) * 8f * fade;

        proceduralLeftArm.localRotation = proceduralLeftArmRest * Quaternion.Euler(counterSwing, 0f, 0f);
        proceduralRightArm.localRotation = proceduralRightArmRest * Quaternion.Euler(swing, 0f, 0f);
        proceduralLeftUpLeg.localRotation = proceduralLeftUpLegRest * Quaternion.Euler(swing, 0f, 0f);
        proceduralRightUpLeg.localRotation = proceduralRightUpLegRest * Quaternion.Euler(counterSwing, 0f, 0f);

        if (proceduralLeftForeArm != null)
        {
            proceduralLeftForeArm.localRotation = proceduralLeftForeArmRest * Quaternion.Euler(-8f * fade, 0f, 0f);
        }
        if (proceduralRightForeArm != null)
        {
            proceduralRightForeArm.localRotation = proceduralRightForeArmRest * Quaternion.Euler(-8f * fade, 0f, 0f);
        }
        if (proceduralLeftLeg != null)
        {
            proceduralLeftLeg.localRotation = proceduralLeftLegRest * Quaternion.Euler(leftKneeBend, 0f, 0f);
        }
        if (proceduralRightLeg != null)
        {
            proceduralRightLeg.localRotation = proceduralRightLegRest * Quaternion.Euler(rightKneeBend, 0f, 0f);
        }
        if (proceduralLeftFoot != null)
        {
            proceduralLeftFoot.localRotation = proceduralLeftFootRest * Quaternion.Euler(-footLift, 0f, 0f);
        }
        if (proceduralRightFoot != null)
        {
            proceduralRightFoot.localRotation = proceduralRightFootRest * Quaternion.Euler(footLift, 0f, 0f);
        }
    }

    private void RefreshPaintColliders()
    {
        int count = Mathf.Min(skinnedRenderers.Count, Mathf.Min(paintColliders.Count, bakedMeshes.Count));
        for (int i = 0; i < count; i++)
        {
            SkinnedMeshRenderer renderer = skinnedRenderers[i];
            MeshCollider collider = paintColliders[i];
            Mesh bakedMesh = bakedMeshes[i];
            if (renderer == null || collider == null || bakedMesh == null)
            {
                continue;
            }

            collider.sharedMesh = null;
            renderer.BakeMesh(bakedMesh);
            Vector2[] paintUv = i < paintColliderUvs.Count ? paintColliderUvs[i] : null;
            if (paintUv != null && paintUv.Length == bakedMesh.vertexCount)
            {
                // BakeMesh can omit or retain the FBX's original collapsed UV0
                // depending on backend. Force the exact render UVs onto the
                // raycast mesh so RaycastHit.textureCoord addresses the atlas.
                bakedMesh.uv = paintUv;
            }
            collider.sharedMesh = bakedMesh;

            if (!colliderUvLogged)
            {
                colliderUvLogged = true;
                Debug.Log("CHAMELEON_PAINT_COLLIDER_UV_READY: " + DescribeUvRange(bakedMesh.uv));
            }
        }
    }

    private void FinishCrossfade()
    {
        if (fadingInput < 0 || !animationGraph.IsValid())
        {
            return;
        }

        Playable previous = animationMixer.GetInput(fadingInput);
        animationGraph.Disconnect(animationMixer, fadingInput);
        if (previous.IsValid())
        {
            animationGraph.DestroyPlayable(previous);
        }
        animationMixer.SetInputWeight(activeInput, 1f);
        fadingInput = -1;
    }

    private static string Normalize(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        char[] buffer = new char[value.Length];
        int length = 0;
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (char.IsLetterOrDigit(c))
            {
                buffer[length++] = char.ToLowerInvariant(c);
            }
        }
        return new string(buffer, 0, length);
    }

    private static bool IsSeparateFacialFeature(string objectName)
    {
        string normalized = Normalize(objectName);
        return normalized.Contains("eyelash") || normalized.Contains("eyebrow") ||
               normalized.Contains("eyeball") || normalized.Contains("teeth") ||
               normalized.Contains("tongue") || normalized.Contains("beard");
    }

    private void OnDestroy()
    {
        if (animationGraph.IsValid())
        {
            animationGraph.Destroy();
        }

        sharedPaint?.Dispose();
        sharedPaint = null;

        for (int i = 0; i < bakedMeshes.Count; i++)
        {
            if (bakedMeshes[i] != null)
            {
                Destroy(bakedMeshes[i]);
            }
        }

        for (int i = 0; i < runtimeSkinnedMeshes.Count; i++)
        {
            if (runtimeSkinnedMeshes[i] != null)
            {
                Destroy(runtimeSkinnedMeshes[i]);
            }
        }

        if (paintTemplate != null)
        {
            Destroy(paintTemplate);
        }
    }
}
