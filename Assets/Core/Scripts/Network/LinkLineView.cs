using System.Collections.Generic;
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

    private readonly List<Vector3> pathPoints = new();
    private readonly List<Vector3> renderPoints = new();

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

        if (!manager.TryGetLinkPath(pathPoints))
        {
            lineRenderer.enabled = false;
            return;
        }

        DrawPath(pathPoints, manager.Mode.Value, manager.IsEnergyLinkLaserized);
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

    private void DrawPath(IReadOnlyList<Vector3> path, LinkMode mode, bool isLaserized)
    {
        if (path.Count < 2)
        {
            lineRenderer.enabled = false;
            return;
        }

        Color color = GetColor(mode, isLaserized);
        Vector3 offset = Vector3.up * heightOffset;

        renderPoints.Clear();

        for (int i = 0; i < path.Count; i++)
            renderPoints.Add(path[i] + offset);

        lineRenderer.enabled = true;
        lineRenderer.positionCount = renderPoints.Count;

        for (int i = 0; i < renderPoints.Count; i++)
            lineRenderer.SetPosition(i, renderPoints[i]);
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
