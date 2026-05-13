using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Rendering;

[RequireComponent(typeof(LineRenderer))]
public sealed class LinkLineView : MonoBehaviour
{
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    private LinkManager linkManager;

    [SerializeField, Required, TitleGroup("References")]
    private LineRenderer lineRenderer;

    [SerializeField, TitleGroup("Visual")]
    private Color ropeColor = Color.white;

    [SerializeField, TitleGroup("Visual")]
    private Color energyColor = Color.cyan;

    [SerializeField, MinValue(0f), TitleGroup("Visual")]
    private float width = 0.12f;

    [SerializeField, MinValue(0f), TitleGroup("Visual")]
    private float heightOffset = 0.25f;

    [SerializeField, MinValue(0), TitleGroup("Visual")]
    private int capVertices = 4;

    private MaterialPropertyBlock propertyBlock;

    private void Awake()
    {
        linkManager = LinkManager.Instance;

        propertyBlock = new MaterialPropertyBlock();
        ConfigureRenderer();
    }

    private void LateUpdate()
    {
        if (linkManager.TryGetDirectLine(out Vector3 start, out Vector3 end))
        {
            DrawLine(start, end, linkManager.Mode.Value);
            return;
        }
    }

    private void ConfigureRenderer()
    {
        lineRenderer.enabled = true;
        lineRenderer.useWorldSpace = true;
        lineRenderer.startWidth = width;
        lineRenderer.endWidth = width;
        lineRenderer.numCapVertices = capVertices;
        lineRenderer.numCornerVertices = capVertices;
        lineRenderer.alignment = LineAlignment.View;
        lineRenderer.textureMode = LineTextureMode.Stretch;
        lineRenderer.shadowCastingMode = ShadowCastingMode.Off;
        lineRenderer.receiveShadows = false;
    }

    private void DrawLine(Vector3 start, Vector3 end, LinkMode mode)
    {
        Color color = GetColor(mode);
        Vector3 offset = Vector3.up * heightOffset;

        lineRenderer.enabled = true;
        lineRenderer.positionCount = 2;
        lineRenderer.SetPosition(0, start + offset);
        lineRenderer.SetPosition(1, end + offset);
        lineRenderer.startColor = color;
        lineRenderer.endColor = color;
        lineRenderer.startWidth = width;
        lineRenderer.endWidth = width;
        lineRenderer.numCapVertices = capVertices;
        lineRenderer.numCornerVertices = capVertices;

        propertyBlock.Clear();
        propertyBlock.SetColor(BaseColorId, color);
        propertyBlock.SetColor(ColorId, color);
        lineRenderer.SetPropertyBlock(propertyBlock);
    }

    private Color GetColor(LinkMode mode) => mode == LinkMode.Rope ? ropeColor : energyColor;

    private Material CreateRuntimeMaterial(string materialName, Color color)
    {
        Shader shader = FindUnlitShader();

        Material material = new(shader)
        {
            name = materialName
        };

        if (material.HasProperty(BaseColorId))
            material.SetColor(BaseColorId, color);

        if (material.HasProperty(ColorId))
            material.SetColor(ColorId, color);

        return material;
    }

    private Shader FindUnlitShader()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");

        if (shader != null)
            return shader;

        shader = Shader.Find("Unlit/Color");

        if (shader != null)
            return shader;

        shader = Shader.Find("Sprites/Default");

        if (shader != null)
            return shader;

        return Shader.Find("Hidden/Internal-Colored");
    }

    private void DestroyRuntimeMaterial(Material material)
    {
        if (material == null)
            return;

        if (Application.isPlaying)
            Destroy(material);
        else
            DestroyImmediate(material);
    }
}