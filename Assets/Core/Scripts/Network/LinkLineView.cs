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

    [SerializeField, TitleGroup("Visual")]
    private Color laserizedEnergyColor = Color.red;

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
        if (!TryGetLinkManager(out LinkManager manager))
        {
            lineRenderer.enabled = false;
            return;
        }

        if (!manager.TryGetLinkPath(out Vector3 firstPosition, out Vector3 relayPosition, out Vector3 secondPosition, out bool usesRelay))
        {
            lineRenderer.enabled = false;
            return;
        }

        DrawPath(firstPosition, relayPosition, secondPosition, usesRelay, manager.Mode.Value, manager.IsEnergyLinkLaserized);
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

    private void DrawPath(Vector3 firstPosition, Vector3 relayPosition, Vector3 secondPosition, bool usesRelay, LinkMode mode, bool isLaserized)
    {
        Color color = GetColor(mode, isLaserized);
        Vector3 offset = Vector3.up * heightOffset;

        lineRenderer.enabled = true;
        lineRenderer.positionCount = usesRelay ? 3 : 2;
        lineRenderer.SetPosition(0, firstPosition + offset);

        if (usesRelay)
        {
            lineRenderer.SetPosition(1, relayPosition + offset);
            lineRenderer.SetPosition(2, secondPosition + offset);
        }
        else
        {
            lineRenderer.SetPosition(1, secondPosition + offset);
        }

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

    private bool TryGetLinkManager(out LinkManager manager)
    {
        if (linkManager != null)
        {
            manager = linkManager;
            return true;
        }

        linkManager = LinkManager.Instance;
        manager = linkManager;
        return manager != null;
    }

    private Color GetColor(LinkMode mode, bool isLaserized)
    {
        if (mode == LinkMode.Rope)
            return ropeColor;

        return isLaserized ? laserizedEnergyColor : energyColor;
    }
}