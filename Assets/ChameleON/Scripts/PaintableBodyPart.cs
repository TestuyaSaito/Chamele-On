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
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            anisoLevel = 4
        };

        Reset();
    }

    public Texture2D Texture { get; private set; }

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

    public void PaintStroke(Vector2 fromUv, Vector2 toUv, Color color, int radius)
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
            DrawCircle(toUv, color, pixelRadius);
            Upload();
            return;
        }

        int steps = Mathf.Max(1, Mathf.CeilToInt(delta.magnitude * resolution / Mathf.Max(1f, pixelRadius * 0.42f)));
        for (int i = 0; i <= steps; i++)
        {
            DrawCircle(Vector2.Lerp(fromUv, toUv, i / (float)steps), color, pixelRadius);
        }

        Upload();
    }

    public Color Sample(Vector2 uv)
    {
        int x = Mathf.Clamp(Mathf.RoundToInt(uv.x * (resolution - 1)), 0, resolution - 1);
        int y = Mathf.Clamp(Mathf.RoundToInt(uv.y * (resolution - 1)), 0, resolution - 1);
        return pixels[y * resolution + x];
    }

    public void Dispose()
    {
        if (Texture != null)
        {
            Object.Destroy(Texture);
            Texture = null;
        }
    }

    private void DrawCircle(Vector2 uv, Color color, int radius)
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
                    pixels[py * resolution + px] = brush;
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
    private SharedBodyPaintTexture paintData;
    private Material[] runtimeMaterials;
    private Renderer paintRenderer;
    private MaterialPropertyBlock paintProperties;
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
        paintData?.PaintStroke(fromUv, toUv, color, radius);
    }

    public Color Sample(Vector2 uv)
    {
        return paintData != null ? paintData.Sample(uv) : Color.white;
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

        if (targetRenderer == null || template == null)
        {
            return;
        }

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
        targetRenderer.SetPropertyBlock(paintProperties);

        Debug.Log("CHAMELEON_PAINT_BINDING_READY: renderer=" + targetRenderer.name +
                  ", slots=" + runtimeMaterials.Length +
                  ", shader=" + runtimeMaterials[0].shader.name +
                  ", replacedPropertyBlock=" + replacedPropertyBlock + ".");
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
    }
}
