using UnityEngine;

// A CPU-backed paint atlas shared by every material slot on a rigged character.
// The procedural fallback still creates one atlas per primitive body part.
internal sealed class SharedBodyPaintTexture
{
    private readonly int resolution;
    private readonly Color32[] pixels;

    public SharedBodyPaintTexture(int textureResolution, string textureName)
    {
        resolution = Mathf.Clamp(textureResolution, 64, 1024);
        pixels = new Color32[resolution * resolution];
        Texture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false)
        {
            name = textureName,
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            anisoLevel = 0
        };

        Reset();
    }

    public Texture2D Texture { get; private set; }

    public int Resolution => resolution;

    public void Reset()
    {
        if (Texture == null)
        {
            return;
        }

        var white = new Color32(242, 242, 238, 255);
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = white;
        }

        Upload();
    }

    public void PaintStroke(Vector2 fromUv, Vector2 toUv, Color color, int radius,
        bool[] allowedPixels = null)
    {
        if (Texture == null)
        {
            return;
        }

        Vector2 delta = toUv - fromUv;
        bool crossesSeam = Mathf.Abs(delta.x) > 0.45f || Mathf.Abs(delta.y) > 0.45f;
        // UI brush sizes were authored against the original 256 px prototype.
        // Preserve their apparent world size when a higher-resolution rigged atlas is used.
        int scaledRadius = Mathf.RoundToInt(radius * (resolution / 256f));
        int pixelRadius = Mathf.Clamp(scaledRadius, 2, resolution / 6);

        if (crossesSeam)
        {
            DrawCircle(toUv, color, pixelRadius, allowedPixels);
            Upload();
            return;
        }

        int steps = Mathf.Max(1, Mathf.CeilToInt(delta.magnitude * resolution / Mathf.Max(1f, pixelRadius * 0.42f)));
        for (int i = 0; i <= steps; i++)
        {
            DrawCircle(Vector2.Lerp(fromUv, toUv, i / (float)steps), color, pixelRadius, allowedPixels);
        }

        Upload();
    }

    public Color Sample(Vector2 uv)
    {
        int x = Mathf.Clamp(Mathf.RoundToInt(uv.x * (resolution - 1)), 0, resolution - 1);
        int y = Mathf.Clamp(Mathf.RoundToInt(uv.y * (resolution - 1)), 0, resolution - 1);
        return pixels[y * resolution + x];
    }

    public bool CopyPixelsTo(Color32[] destination)
    {
        if (destination == null || destination.Length != pixels.Length)
        {
            return false;
        }

        System.Array.Copy(pixels, destination, pixels.Length);
        return true;
    }

    public bool RestorePixels(Color32[] source)
    {
        if (Texture == null || source == null || source.Length != pixels.Length)
        {
            return false;
        }

        // Keep the CPU backing store authoritative. Restoring only Texture would
        // make an undone stroke reappear on the next PaintStroke upload.
        System.Array.Copy(source, pixels, pixels.Length);
        Upload();
        return true;
    }

    public void Dispose()
    {
        if (Texture != null)
        {
            Object.Destroy(Texture);
            Texture = null;
        }
    }

    private void DrawCircle(Vector2 uv, Color color, int radius, bool[] allowedPixels)
    {
        int centerX = Mathf.RoundToInt(Mathf.Clamp01(uv.x) * (resolution - 1));
        int centerY = Mathf.RoundToInt(Mathf.Clamp01(uv.y) * (resolution - 1));
        int radiusSquared = radius * radius;
        Color32 brush = color;

        for (int y = -radius; y <= radius; y++)
        {
            int py = centerY + y;
            if (py < 0 || py >= resolution)
            {
                continue;
            }

            for (int x = -radius; x <= radius; x++)
            {
                if (x * x + y * y > radiusSquared)
                {
                    continue;
                }

                int px = centerX + x;
                if (px >= 0 && px < resolution)
                {
                    int pixelIndex = py * resolution + px;
                    if (allowedPixels == null || allowedPixels.Length != pixels.Length || allowedPixels[pixelIndex])
                    {
                        pixels[pixelIndex] = brush;
                    }
                }
            }
        }
    }

    private void Upload()
    {
        Texture.SetPixels32(pixels);
        Texture.Apply(false, false);
    }
}

public sealed class PaintableBodyPart : MonoBehaviour
{
    private const float CharacterShadowLift = 0.10f;

    private SharedBodyPaintTexture paintData;
    private Material[] runtimeMaterials;
    private Renderer paintRenderer;
    private MaterialPropertyBlock paintProperties;
    // Shared paint textures are sampled in UV space.  Keep a per-renderer
    // triangle mask so a circular brush cannot cross from one UV island (for
    // example a cube's front face) into a neighbouring island (its side face).
    private bool[] paintMask;
    private bool[] surfacePaintMask;
    private Mesh paintMesh;
    private Vector2[] paintUv;
    private int[] paintTriangles;
    private Vector3[] paintVertices;
    private Vector3 surfaceMaskNormal;
    private bool surfaceMaskValid;
    private bool ownsPaintData;

    public Texture2D PaintTexture => paintData != null ? paintData.Texture : null;

    public void Initialize(int textureResolution, Material template)
    {
        var shared = new SharedBodyPaintTexture(textureResolution, name + " Body Paint");
        InitializeInternal(shared, GetComponent<Renderer>(), template, true);
    }

    internal void InitializeShared(SharedBodyPaintTexture shared, Renderer targetRenderer, Material template)
    {
        InitializeInternal(shared, targetRenderer, template, false);
    }

    public void ResetPaint()
    {
        paintData?.Reset();
    }

    public void PaintStroke(Vector2 fromUv, Vector2 toUv, Color color, int radius)
    {
        paintData?.PaintStroke(fromUv, toUv, color, radius, paintMask);
    }

    // The procedural mannequin uses a cube UV layout where every face occupies
    // the same 0..1 square. A UV-only brush would therefore paint the front,
    // back, and side faces together. Restrict this stroke to triangles whose
    // surface normal matches the face currently under the pointer.
    public void PaintStroke(Vector2 fromUv, Vector2 toUv, Color color, int radius, Vector3 worldNormal)
    {
        if (paintData == null)
        {
            return;
        }

        bool[] mask = paintMask;
        if (paintRenderer is MeshRenderer && BuildSurfacePaintMask(worldNormal))
        {
            mask = surfacePaintMask;
        }

        paintData.PaintStroke(fromUv, toUv, color, radius, mask);
    }

    public Color Sample(Vector2 uv)
    {
        return paintData != null ? paintData.Sample(uv) : Color.white;
    }

    public bool CopyPaintPixelsTo(Color32[] destination)
    {
        return paintData != null && paintData.CopyPixelsTo(destination);
    }

    public bool RestorePaintPixels(Color32[] pixels)
    {
        return paintData != null && paintData.RestorePixels(pixels);
    }

    public string GetPaintCoverageDiagnostic()
    {
        Mesh mesh = null;
        if (paintRenderer is SkinnedMeshRenderer skinned)
        {
            mesh = skinned.sharedMesh;
        }
        else if (paintRenderer != null)
        {
            MeshFilter filter = paintRenderer.GetComponent<MeshFilter>();
            mesh = filter != null ? filter.sharedMesh : null;
        }

        Vector2[] uv = mesh != null ? mesh.uv : null;
        if (uv == null || uv.Length == 0 || paintData == null)
        {
            return "uvSamples=none";
        }

        int painted = 0;
        for (int i = 0; i < uv.Length; i++)
        {
            Color sample = paintData.Sample(uv[i]);
            if (Mathf.Abs(sample.r - 0.949f) + Mathf.Abs(sample.g - 0.949f) + Mathf.Abs(sample.b - 0.933f) > 0.10f)
            {
                painted++;
            }
        }

        Material material = runtimeMaterials != null && runtimeMaterials.Length > 0 ? runtimeMaterials[0] : null;
        Vector2 scale = material != null ? material.GetTextureScale("_BaseMap") : Vector2.zero;
        Vector2 offset = material != null ? material.GetTextureOffset("_BaseMap") : Vector2.zero;
        return "uvSamples=" + uv.Length + ", paintedVertices=" + painted +
               " (" + (painted * 100f / uv.Length).ToString("F1") + "%), baseMapST=" + scale + "/" + offset;
    }

    public void SetMaterialResponse(float metallic, float smoothness)
    {
        if (runtimeMaterials == null)
        {
            return;
        }

        for (int i = 0; i < runtimeMaterials.Length; i++)
        {
            Material material = runtimeMaterials[i];
            if (material == null)
            {
                continue;
            }

            if (material.HasProperty("_Metallic"))
            {
                material.SetFloat("_Metallic", Mathf.Clamp01(metallic));
            }

            if (material.HasProperty("_Glossiness"))
            {
                material.SetFloat("_Glossiness", Mathf.Clamp01(smoothness));
            }

            if (material.HasProperty("_Smoothness"))
            {
                material.SetFloat("_Smoothness", Mathf.Clamp01(smoothness));
            }
        }
    }

    private void InitializeInternal(SharedBodyPaintTexture shared, Renderer targetRenderer, Material template, bool ownsShared)
    {
        paintData = shared;
        ownsPaintData = ownsShared;
        paintRenderer = targetRenderer;
        BuildPaintMask();

        if (targetRenderer == null || template == null)
        {
            return;
        }

        // Keep the painted character readable in the darker side of the map,
        // while leaving its cast shadow and the environment lighting intact.
        targetRenderer.receiveShadows = false;

        int slotCount = Mathf.Max(1, targetRenderer.sharedMaterials.Length);
        runtimeMaterials = new Material[slotCount];
        for (int i = 0; i < slotCount; i++)
        {
            Material material = new Material(template)
            {
                name = name + " Paint Material " + i
            };
            if (material.HasProperty("_BaseMap"))
            {
                material.SetTexture("_BaseMap", paintData.Texture);
            }
            else if (material.HasProperty("_MainTex"))
            {
                material.SetTexture("_MainTex", paintData.Texture);
            }

            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", Color.white);
            }
            else if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", Color.white);
            }

            ApplyCharacterShadowLift(material);

            runtimeMaterials[i] = material;
        }

        targetRenderer.sharedMaterials = runtimeMaterials;

        // Imported character prefabs and animation systems can leave per-renderer
        // overrides behind. A property block is the final authority at draw time,
        // so bind the live atlas there as well as on every material slot.
        bool replacedPropertyBlock = targetRenderer.HasPropertyBlock();
        paintProperties = new MaterialPropertyBlock();
        paintProperties.SetTexture("_BaseMap", paintData.Texture);
        paintProperties.SetTexture("_MainTex", paintData.Texture);
        paintProperties.SetColor("_BaseColor", Color.white);
        paintProperties.SetColor("_Color", Color.white);
        paintProperties.SetTexture("_EmissionMap", paintData.Texture);
        paintProperties.SetColor("_EmissionColor", new Color(CharacterShadowLift,
            CharacterShadowLift, CharacterShadowLift, 1f));
        targetRenderer.SetPropertyBlock(paintProperties);

        Debug.Log("CHAMELEON_PAINT_BINDING_READY: renderer=" + targetRenderer.name +
                  ", slots=" + runtimeMaterials.Length +
                  ", shader=" + runtimeMaterials[0].shader.name +
                  ", replacedPropertyBlock=" + replacedPropertyBlock + ".");
    }

    private static void ApplyCharacterShadowLift(Material material)
    {
        if (material == null)
        {
            return;
        }

        if (material.HasProperty("_ReceiveShadows"))
        {
            material.SetFloat("_ReceiveShadows", 0f);
        }

        if (material.HasProperty("_Surface"))
        {
            material.SetFloat("_Surface", 0f);
        }

        if (material.HasProperty("_Blend"))
        {
            material.SetFloat("_Blend", 0f);
        }

        if (material.HasProperty("_AlphaClip"))
        {
            material.SetFloat("_AlphaClip", 0f);
        }

        if (material.HasProperty("_SrcBlend"))
        {
            material.SetFloat("_SrcBlend", 1f);
        }

        if (material.HasProperty("_DstBlend"))
        {
            material.SetFloat("_DstBlend", 0f);
        }

        if (material.HasProperty("_ZWrite"))
        {
            material.SetFloat("_ZWrite", 1f);
        }

        if (material.HasProperty("_Cull"))
        {
            // Some imported stick-man meshes are almost flat. Rendering both
            // sides prevents them from disappearing when the camera sees the
            // reverse face.
            material.SetFloat("_Cull", 0f);
        }

        material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.DisableKeyword("_ALPHATEST_ON");
        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        material.DisableKeyword("_ALPHAMODULATE_ON");
        material.renderQueue = -1;
        material.doubleSidedGI = true;

        if (material.HasProperty("_EmissionMap"))
        {
            Texture paintTexture = null;
            if (material.HasProperty("_BaseMap"))
            {
                paintTexture = material.GetTexture("_BaseMap");
            }
            else if (material.HasProperty("_MainTex"))
            {
                paintTexture = material.GetTexture("_MainTex");
            }

            if (paintTexture != null)
            {
                material.SetTexture("_EmissionMap", paintTexture);
                if (material.HasProperty("_EmissionColor"))
                {
                    material.SetColor("_EmissionColor", new Color(CharacterShadowLift,
                        CharacterShadowLift, CharacterShadowLift, 1f));
                    material.EnableKeyword("_EMISSION");
                }
            }
        }
    }

    private void OnDestroy()
    {
        if (runtimeMaterials != null)
        {
            for (int i = 0; i < runtimeMaterials.Length; i++)
            {
                if (runtimeMaterials[i] != null)
                {
                    Destroy(runtimeMaterials[i]);
                }
            }
        }

        if (ownsPaintData && paintData != null)
        {
            paintData.Dispose();
        }

        paintData = null;
        paintRenderer = null;
        paintProperties = null;
        paintMask = null;
        surfacePaintMask = null;
        paintMesh = null;
        paintUv = null;
        paintTriangles = null;
        paintVertices = null;
        surfaceMaskValid = false;
    }

    private void BuildPaintMask()
    {
        paintMask = null;
        if (paintData == null || paintRenderer == null)
        {
            return;
        }

        Mesh mesh = null;
        if (paintRenderer is SkinnedMeshRenderer skinned)
        {
            mesh = skinned.sharedMesh;
        }
        else
        {
            MeshFilter filter = paintRenderer.GetComponent<MeshFilter>();
            mesh = filter != null ? filter.sharedMesh : null;
        }

        if (mesh == null || mesh.vertexCount == 0)
        {
            return;
        }

        Vector2[] uv = mesh.uv;
        int[] triangles = mesh.triangles;
        Vector3[] vertices = mesh.vertices;
        int resolution = paintData.Resolution;
        if (uv == null || uv.Length != mesh.vertexCount || vertices == null || vertices.Length != mesh.vertexCount ||
            triangles == null || triangles.Length < 3 || resolution < 2)
        {
            return;
        }

        paintMesh = mesh;
        paintUv = uv;
        paintTriangles = triangles;
        paintVertices = vertices;
        paintMask = new bool[resolution * resolution];
        for (int triangle = 0; triangle + 2 < triangles.Length; triangle += 3)
        {
            RasterizeTriangle(paintMask, triangle, resolution);
        }
    }

    private bool BuildSurfacePaintMask(Vector3 worldNormal)
    {
        if (paintData == null || paintMesh == null || paintUv == null || paintTriangles == null || paintVertices == null)
        {
            return false;
        }

        Vector3 localNormal = paintRenderer.transform.InverseTransformDirection(worldNormal).normalized;
        if (localNormal.sqrMagnitude < 0.25f)
        {
            return false;
        }

        if (surfaceMaskValid && Vector3.Dot(surfaceMaskNormal, localNormal) > 0.995f)
        {
            return true;
        }

        int pixelCount = paintData.Resolution * paintData.Resolution;
        if (surfacePaintMask == null || surfacePaintMask.Length != pixelCount)
        {
            surfacePaintMask = new bool[pixelCount];
        }
        System.Array.Clear(surfacePaintMask, 0, surfacePaintMask.Length);

        int matchingTriangles = RasterizeMatchingSurface(surfacePaintMask, localNormal, 0.90f);
        // A few imported meshes have reversed winding. If no triangle matched,
        // try the opposite orientation without weakening the side isolation.
        if (matchingTriangles == 0)
        {
            System.Array.Clear(surfacePaintMask, 0, surfacePaintMask.Length);
            matchingTriangles = RasterizeMatchingSurface(surfacePaintMask, -localNormal, 0.90f);
        }

        if (matchingTriangles == 0)
        {
            surfaceMaskValid = false;
            return false;
        }

        surfaceMaskNormal = localNormal;
        surfaceMaskValid = true;
        return true;
    }

    private int RasterizeMatchingSurface(bool[] destination, Vector3 normal, float threshold)
    {
        int matchingTriangles = 0;
        for (int triangle = 0; triangle + 2 < paintTriangles.Length; triangle += 3)
        {
            int ia = paintTriangles[triangle];
            int ib = paintTriangles[triangle + 1];
            int ic = paintTriangles[triangle + 2];
            if (ia < 0 || ib < 0 || ic < 0 || ia >= paintVertices.Length ||
                ib >= paintVertices.Length || ic >= paintVertices.Length)
            {
                continue;
            }

            Vector3 triangleNormal = Vector3.Cross(paintVertices[ib] - paintVertices[ia],
                paintVertices[ic] - paintVertices[ia]).normalized;
            if (Vector3.Dot(triangleNormal, normal) < threshold)
            {
                continue;
            }

            RasterizeTriangle(destination, triangle, paintData.Resolution);
            matchingTriangles++;
        }

        return matchingTriangles;
    }

    private void RasterizeTriangle(bool[] destination, int triangle, int resolution)
    {
        int ia = paintTriangles[triangle];
        int ib = paintTriangles[triangle + 1];
        int ic = paintTriangles[triangle + 2];
        if (ia < 0 || ib < 0 || ic < 0 || ia >= paintUv.Length || ib >= paintUv.Length || ic >= paintUv.Length)
        {
            return;
        }

        Vector2 a = paintUv[ia];
        Vector2 b = paintUv[ib];
        Vector2 c = paintUv[ic];
        float area = Cross(b - a, c - a);
        if (Mathf.Abs(area) < 0.000001f)
        {
            return;
        }

        float maxPixel = resolution - 1f;
        Vector2 minimum = Vector2.Min(a, Vector2.Min(b, c));
        Vector2 maximum = Vector2.Max(a, Vector2.Max(b, c));
        int minX = Mathf.Clamp(Mathf.FloorToInt(minimum.x * maxPixel), 0, resolution - 1);
        int maxX = Mathf.Clamp(Mathf.CeilToInt(maximum.x * maxPixel), 0, resolution - 1);
        int minY = Mathf.Clamp(Mathf.FloorToInt(minimum.y * maxPixel), 0, resolution - 1);
        int maxY = Mathf.Clamp(Mathf.CeilToInt(maximum.y * maxPixel), 0, resolution - 1);

        for (int y = minY; y <= maxY; y++)
        {
            float v = y / maxPixel;
            for (int x = minX; x <= maxX; x++)
            {
                float u = x / maxPixel;
                if (PointInTriangle(new Vector2(u, v), a, b, c, area))
                {
                    destination[y * resolution + x] = true;
                }
            }
        }
    }

    private static float Cross(Vector2 left, Vector2 right)
    {
        return left.x * right.y - left.y * right.x;
    }

    private static bool PointInTriangle(Vector2 point, Vector2 a, Vector2 b, Vector2 c, float signedArea)
    {
        const float edgeTolerance = 0.0001f;
        float ab = Cross(b - a, point - a);
        float bc = Cross(c - b, point - b);
        float ca = Cross(a - c, point - c);
        if (signedArea > 0f)
        {
            return ab >= -edgeTolerance && bc >= -edgeTolerance && ca >= -edgeTolerance;
        }

        return ab <= edgeTolerance && bc <= edgeTolerance && ca <= edgeTolerance;
    }
}
