using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public sealed class RuntimeMannequin : MonoBehaviour, IMannequinVisual
{
    public const int PaintLayer = 8;
    // Prefer the imported character asset. If Unity cannot import it as a
    // paintable rigged mesh, Build() falls back to the procedural block body.
    private static readonly bool UseRiggedResource = true;

    private sealed class BodyPiece
    {
        public Transform Transform;
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 Scale;
    }

    private readonly Dictionary<string, BodyPiece> pieces = new Dictionary<string, BodyPiece>();
    private readonly List<PaintableBodyPart> paintableParts = new List<PaintableBodyPart>();
    private readonly List<Mesh> runtimePaintMeshes = new List<Mesh>();
    private Transform visualRoot;
    private Material paintTemplate;
    private RiggedRuntimeMannequin rigged;
    private int proceduralPoseIndex;

    public int PoseIndex => rigged != null && rigged.IsBuilt ? rigged.PoseIndex : proceduralPoseIndex;
    public IReadOnlyList<PaintableBodyPart> PaintableParts =>
        rigged != null && rigged.IsBuilt ? rigged.PaintableParts : paintableParts;

    public void Build()
    {
        if (UseRiggedResource)
        {
            rigged = gameObject.AddComponent<RiggedRuntimeMannequin>();
            rigged.Build();
            if (rigged.IsBuilt)
            {
                return;
            }

            Destroy(rigged);
            rigged = null;
        }

        visualRoot = new GameObject("White Body").transform;
        visualRoot.SetParent(transform, false);
        visualRoot.localScale = Vector3.one * 0.355f;

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            shader = Shader.Find("Standard");
        }

        if (shader == null)
        {
            Debug.LogError("No compatible lit shader is available for the fallback mannequin.");
            return;
        }

        paintTemplate = new Material(shader)
        {
            name = "White Body Paint Template"
        };
        if (paintTemplate.HasProperty("_BaseColor")) paintTemplate.SetColor("_BaseColor", Color.white);
        else if (paintTemplate.HasProperty("_Color")) paintTemplate.SetColor("_Color", Color.white);
        if (paintTemplate.HasProperty("_Metallic")) paintTemplate.SetFloat("_Metallic", 0f);
        if (paintTemplate.HasProperty("_Smoothness")) paintTemplate.SetFloat("_Smoothness", 0.24f);
        if (paintTemplate.HasProperty("_Glossiness")) paintTemplate.SetFloat("_Glossiness", 0.24f);

        // Unity's cube primitive is intentionally used for every piece.  With
        // the root scale of 0.355 this is a roughly 0.95 m tall, Minecraft-like
        // silhouette that stays readable when painted on a phone screen.
        CreatePart("Pelvis", PrimitiveType.Cube, new Vector3(0f, 0.98f, 0f), new Vector3(0.56f, 0.50f, 0.36f), Vector3.zero);
        CreatePart("Torso", PrimitiveType.Cube, new Vector3(0f, 1.63f, 0f), new Vector3(0.49f, 0.82f, 0.31f), Vector3.zero);
        // Keep the default silhouette as one connected Minecraft-style block
        // body: the head touches the torso, arms meet the shoulders, and hands
        // meet the lower ends of the arms.
        CreatePart("Head", PrimitiveType.Cube, new Vector3(0f, 2.32f, 0f), new Vector3(0.56f, 0.58f, 0.52f), Vector3.zero);

        CreatePart("ArmL", PrimitiveType.Cube, new Vector3(-0.33f, 1.62f, 0f), new Vector3(0.17f, 0.84f, 0.17f), Vector3.zero);
        CreatePart("ArmR", PrimitiveType.Cube, new Vector3(0.33f, 1.62f, 0f), new Vector3(0.17f, 0.84f, 0.17f), Vector3.zero);
        CreatePart("HandL", PrimitiveType.Cube, new Vector3(-0.33f, 1.095f, 0f), Vector3.one * 0.22f, Vector3.zero);
        CreatePart("HandR", PrimitiveType.Cube, new Vector3(0.33f, 1.095f, 0f), Vector3.one * 0.22f, Vector3.zero);

        CreatePart("LegL", PrimitiveType.Cube, new Vector3(-0.25f, 0.43f, 0f), new Vector3(0.20f, 0.72f, 0.21f), Vector3.zero);
        CreatePart("LegR", PrimitiveType.Cube, new Vector3(0.25f, 0.43f, 0f), new Vector3(0.20f, 0.72f, 0.21f), Vector3.zero);
        CreatePart("FootL", PrimitiveType.Cube, new Vector3(-0.25f, 0.10f, 0.18f), new Vector3(0.23f, 0.20f, 0.39f), Vector3.zero);
        CreatePart("FootR", PrimitiveType.Cube, new Vector3(0.25f, 0.10f, 0.18f), new Vector3(0.23f, 0.20f, 0.39f), Vector3.zero);

        ApplyPose(0);
    }

    public void ApplyPose(int index)
    {
        if (rigged != null && rigged.IsBuilt)
        {
            rigged.ApplyPose(index);
            return;
        }

        proceduralPoseIndex = Mathf.Abs(index) % 5;
        ResetPieces();

        switch (proceduralPoseIndex)
        {
            case 1:
                ApplyCrouch();
                break;
            case 2:
                ApplyCurl();
                break;
            case 3:
                ApplyFlat();
                break;
            case 4:
                ApplyStar();
                break;
        }
    }

    public string GetPoseName()
    {
        if (rigged != null && rigged.IsBuilt)
        {
            return rigged.GetPoseName();
        }

        switch (proceduralPoseIndex)
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
        if (rigged != null && rigged.IsBuilt)
        {
            rigged.ClearPaint();
            return;
        }

        foreach (PaintableBodyPart part in paintableParts)
        {
            part.ResetPaint();
        }
    }

    public void SetMaterialResponse(float metallic, float smoothness)
    {
        if (rigged != null && rigged.IsBuilt)
        {
            rigged.SetMaterialResponse(metallic, smoothness);
            return;
        }

        foreach (PaintableBodyPart part in paintableParts)
        {
            part.SetMaterialResponse(metallic, smoothness);
        }
    }

    public void SetLocomotion(float normalizedSpeed)
    {
        if (rigged != null && rigged.IsBuilt)
        {
            rigged.SetLocomotion(normalizedSpeed);
        }
    }

    private void CreatePart(string partName, PrimitiveType primitive, Vector3 position, Vector3 scale, Vector3 euler)
    {
        GameObject partObject = GameObject.CreatePrimitive(primitive);
        partObject.name = partName;
        partObject.layer = PaintLayer;
        partObject.transform.SetParent(visualRoot, false);
        partObject.transform.localPosition = position;
        partObject.transform.localRotation = Quaternion.Euler(euler);
        partObject.transform.localScale = scale;

        Collider primitiveCollider = partObject.GetComponent<Collider>();
        primitiveCollider.enabled = false;
        Destroy(primitiveCollider);

        MeshFilter filter = partObject.GetComponent<MeshFilter>();
        Mesh paintMesh = CreateFaceIsolatedPaintMesh(filter.sharedMesh, partName);
        if (paintMesh != null)
        {
            filter.sharedMesh = paintMesh;
            runtimePaintMeshes.Add(paintMesh);
        }

        var paintCollider = partObject.AddComponent<MeshCollider>();
        paintCollider.sharedMesh = filter.sharedMesh;
        paintCollider.convex = false;

        Renderer renderer = partObject.GetComponent<Renderer>();
        renderer.shadowCastingMode = ShadowCastingMode.On;
        renderer.receiveShadows = true;

        PaintableBodyPart paintable = partObject.AddComponent<PaintableBodyPart>();
        paintable.Initialize(256, paintTemplate);
        paintableParts.Add(paintable);

        pieces[partName] = new BodyPiece
        {
            Transform = partObject.transform,
            Position = position,
            Rotation = Quaternion.Euler(euler),
            Scale = scale
        };
    }

    private void ResetPieces()
    {
        foreach (BodyPiece piece in pieces.Values)
        {
            piece.Transform.localPosition = piece.Position;
            piece.Transform.localRotation = piece.Rotation;
            piece.Transform.localScale = piece.Scale;
        }
    }

    private void ApplyCrouch()
    {
        Set("Pelvis", new Vector3(0f, 0.58f, 0f), new Vector3(0f, 0f, 0f));
        Set("Torso", new Vector3(0f, 1.10f, 0f), new Vector3(16f, 0f, 0f));
        Set("Head", new Vector3(0f, 1.75f, 0.18f), Vector3.zero);
        Set("ArmL", new Vector3(-0.54f, 1.03f, 0.20f), new Vector3(30f, 0f, -14f));
        Set("ArmR", new Vector3(0.54f, 1.03f, 0.20f), new Vector3(30f, 0f, 14f));
        Set("HandL", new Vector3(-0.38f, 0.55f, 0.38f), Vector3.zero);
        Set("HandR", new Vector3(0.38f, 0.55f, 0.38f), Vector3.zero);
        Set("LegL", new Vector3(-0.34f, 0.22f, 0.24f), new Vector3(68f, 0f, -18f));
        Set("LegR", new Vector3(0.34f, 0.22f, 0.24f), new Vector3(68f, 0f, 18f));
        Set("FootL", new Vector3(-0.46f, 0.12f, 0.65f), Vector3.zero);
        Set("FootR", new Vector3(0.46f, 0.12f, 0.65f), Vector3.zero);
    }

    private void ApplyCurl()
    {
        Set("Pelvis", new Vector3(0f, 0.62f, 0f), new Vector3(90f, 0f, 0f));
        Set("Torso", new Vector3(0f, 0.90f, 0.05f), new Vector3(70f, 0f, 0f));
        Set("Head", new Vector3(0f, 0.72f, 0.62f), Vector3.zero);
        Set("ArmL", new Vector3(-0.42f, 0.78f, 0.43f), new Vector3(68f, 0f, -30f));
        Set("ArmR", new Vector3(0.42f, 0.78f, 0.43f), new Vector3(68f, 0f, 30f));
        Set("HandL", new Vector3(-0.20f, 0.48f, 0.68f), Vector3.zero);
        Set("HandR", new Vector3(0.20f, 0.48f, 0.68f), Vector3.zero);
        Set("LegL", new Vector3(-0.35f, 0.25f, 0.24f), new Vector3(75f, 0f, -32f));
        Set("LegR", new Vector3(0.35f, 0.25f, 0.24f), new Vector3(75f, 0f, 32f));
        Set("FootL", new Vector3(-0.22f, 0.20f, 0.55f), Vector3.zero);
        Set("FootR", new Vector3(0.22f, 0.20f, 0.55f), Vector3.zero);
    }

    private void ApplyFlat()
    {
        Set("Pelvis", new Vector3(0f, 1.12f, 0f), new Vector3(0f, 0f, 90f));
        Set("Torso", new Vector3(0f, 1.60f, 0f), new Vector3(0f, 0f, 90f));
        Set("Head", new Vector3(0.78f, 1.60f, 0f), Vector3.zero);
        Set("ArmL", new Vector3(-0.12f, 2.15f, 0f), new Vector3(0f, 0f, 90f));
        Set("ArmR", new Vector3(-0.12f, 1.02f, 0f), new Vector3(0f, 0f, 90f));
        Set("HandL", new Vector3(0.48f, 2.15f, 0f), Vector3.zero);
        Set("HandR", new Vector3(0.48f, 1.02f, 0f), Vector3.zero);
        Set("LegL", new Vector3(-0.74f, 1.35f, 0f), new Vector3(0f, 0f, 90f));
        Set("LegR", new Vector3(-0.74f, 1.85f, 0f), new Vector3(0f, 0f, 90f));
        Set("FootL", new Vector3(-1.27f, 1.35f, 0.12f), Vector3.zero);
        Set("FootR", new Vector3(-1.27f, 1.85f, 0.12f), Vector3.zero);
    }

    private void ApplyStar()
    {
        Set("ArmL", new Vector3(-0.78f, 1.77f, 0f), new Vector3(0f, 0f, -62f));
        Set("ArmR", new Vector3(0.78f, 1.77f, 0f), new Vector3(0f, 0f, 62f));
        Set("HandL", new Vector3(-1.22f, 2.02f, 0f), Vector3.zero);
        Set("HandR", new Vector3(1.22f, 2.02f, 0f), Vector3.zero);
        Set("LegL", new Vector3(-0.42f, 0.42f, 0f), new Vector3(0f, 0f, -25f));
        Set("LegR", new Vector3(0.42f, 0.42f, 0f), new Vector3(0f, 0f, 25f));
        Set("FootL", new Vector3(-0.70f, 0.05f, 0.16f), new Vector3(0f, -20f, 0f));
        Set("FootR", new Vector3(0.70f, 0.05f, 0.16f), new Vector3(0f, 20f, 0f));
    }

    private void Set(string partName, Vector3 position, Vector3 euler)
    {
        BodyPiece piece = pieces[partName];
        piece.Transform.localPosition = position;
        piece.Transform.localRotation = Quaternion.Euler(euler);
    }

    private static Mesh CreateFaceIsolatedPaintMesh(Mesh source, string partName)
    {
        if (source == null || source.vertexCount == 0 || source.triangles == null || source.triangles.Length < 6)
        {
            return source;
        }

        Vector2[] sourceUv = source.uv;
        Vector3[] sourceVertices = source.vertices;
        int[] sourceTriangles = source.triangles;
        if (sourceUv == null || sourceUv.Length != source.vertexCount ||
            sourceVertices == null || sourceVertices.Length != source.vertexCount)
        {
            return source;
        }

        Mesh mesh = Object.Instantiate(source);
        mesh.name = partName + " Paint Mesh";
        Vector2[] remappedUv = new Vector2[sourceUv.Length];
        int[] vertexFace = new int[sourceUv.Length];
        for (int i = 0; i < vertexFace.Length; i++)
        {
            vertexFace[i] = -1;
        }

        var faceNormals = new List<Vector3>(6);
        bool hasSharedFaceVertex = false;
        for (int triangle = 0; triangle + 2 < sourceTriangles.Length; triangle += 3)
        {
            int ia = sourceTriangles[triangle];
            int ib = sourceTriangles[triangle + 1];
            int ic = sourceTriangles[triangle + 2];
            if (ia < 0 || ib < 0 || ic < 0 || ia >= sourceVertices.Length ||
                ib >= sourceVertices.Length || ic >= sourceVertices.Length)
            {
                continue;
            }

            Vector3 faceNormal = Vector3.Cross(sourceVertices[ib] - sourceVertices[ia],
                sourceVertices[ic] - sourceVertices[ia]).normalized;
            int faceIndex = -1;
            for (int i = 0; i < faceNormals.Count; i++)
            {
                if (Vector3.Dot(faceNormals[i], faceNormal) > 0.995f)
                {
                    faceIndex = i;
                    break;
                }
            }

            if (faceIndex < 0)
            {
                faceIndex = faceNormals.Count;
                faceNormals.Add(faceNormal);
            }

            if (AssignFaceUv(ia, faceIndex, sourceUv, remappedUv, vertexFace) ||
                AssignFaceUv(ib, faceIndex, sourceUv, remappedUv, vertexFace) ||
                AssignFaceUv(ic, faceIndex, sourceUv, remappedUv, vertexFace))
            {
                hasSharedFaceVertex = true;
            }
        }

        // Unity's primitive cube has duplicated vertices per face, so this is
        // normally false. Do not mutate a mesh with shared vertices because one
        // vertex cannot carry two different face UVs.
        if (hasSharedFaceVertex)
        {
            Object.Destroy(mesh);
            return source;
        }

        mesh.uv = remappedUv;
        mesh.uv2 = remappedUv;
        mesh.UploadMeshData(false);
        return mesh;
    }

    private static bool AssignFaceUv(int vertexIndex, int faceIndex, Vector2[] sourceUv,
        Vector2[] destinationUv, int[] vertexFace)
    {
        if (vertexFace[vertexIndex] >= 0 && vertexFace[vertexIndex] != faceIndex)
        {
            return true;
        }

        vertexFace[vertexIndex] = faceIndex;
        int columns = 3;
        int rows = 2;
        int column = faceIndex % columns;
        int row = faceIndex / columns;
        // Point-filtered runtime atlases do not need a wide gutter. Keeping
        // the face tiles edge-to-edge removes the white seam at cube corners.
        float margin = 0.0f;
        Vector2 tileSize = new Vector2(1f / columns, 1f / rows);
        Vector2 tileMin = new Vector2(column * tileSize.x + margin, row * tileSize.y + margin);
        Vector2 usableSize = tileSize - Vector2.one * (margin * 2f);
        Vector2 localUv = Vector2.ClampMagnitude(sourceUv[vertexIndex], 1.4143f);
        localUv.x = Mathf.Clamp01(localUv.x);
        localUv.y = Mathf.Clamp01(localUv.y);
        destinationUv[vertexIndex] = tileMin + Vector2.Scale(localUv, usableSize);
        return false;
    }

    private void OnDestroy()
    {
        for (int i = 0; i < runtimePaintMeshes.Count; i++)
        {
            if (runtimePaintMeshes[i] != null)
            {
                Destroy(runtimePaintMeshes[i]);
            }
        }
        runtimePaintMeshes.Clear();

        if (paintTemplate != null)
        {
            Destroy(paintTemplate);
        }
    }
}
