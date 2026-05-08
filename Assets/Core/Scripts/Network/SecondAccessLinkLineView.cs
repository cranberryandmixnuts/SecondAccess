using Sirenix.OdinInspector;
using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public sealed class SecondAccessLinkLineView : MonoBehaviour
{
    [SerializeField, Required, TitleGroup("References")]
    private LineRenderer lineRenderer;

    [SerializeField, TitleGroup("References")]
    private SecondAccessLinkManager linkManager;

    [SerializeField, TitleGroup("Material")]
    private Material ropeMaterial;

    [SerializeField, TitleGroup("Material")]
    private Material energyMaterial;

    [SerializeField, TitleGroup("Material")]
    private bool generateLightingData;

    [SerializeField, TitleGroup("Visual")]
    private Color ropeColor = Color.white;

    [SerializeField, TitleGroup("Visual")]
    private Color energyColor = Color.cyan;

    [SerializeField, MinValue(0f), TitleGroup("Visual")]
    private float width = 0.08f;

    private MaterialPropertyBlock propertyBlock;

    private void Reset()
    {
        lineRenderer = GetComponent<LineRenderer>();
        linkManager = FindAnyObjectByType<SecondAccessLinkManager>();
    }

    private void Awake()
    {
        lineRenderer = GetComponent<LineRenderer>();

        if (linkManager == null)
            linkManager = SecondAccessLinkManager.Instance;

        propertyBlock = new MaterialPropertyBlock();

        lineRenderer.useWorldSpace = true;
        lineRenderer.positionCount = 0;
        lineRenderer.startWidth = width;
        lineRenderer.endWidth = width;
        lineRenderer.generateLightingData = generateLightingData;
    }

    private void Update()
    {
        if (linkManager == null)
            linkManager = SecondAccessLinkManager.Instance;

        if (linkManager == null || !linkManager.TryGetDirectLine(out Vector3 start, out Vector3 end))
        {
            lineRenderer.positionCount = 0;
            return;
        }

        SecondAccessLinkMode mode = linkManager.Mode.Value;
        Color color = mode == SecondAccessLinkMode.Rope ? ropeColor : energyColor;
        Material material = mode == SecondAccessLinkMode.Rope ? ropeMaterial : energyMaterial;

        if (material != null)
            lineRenderer.sharedMaterial = material;

        lineRenderer.positionCount = 2;
        lineRenderer.SetPosition(0, start);
        lineRenderer.SetPosition(1, end);
        lineRenderer.startColor = color;
        lineRenderer.endColor = color;
        lineRenderer.startWidth = width;
        lineRenderer.endWidth = width;
        lineRenderer.generateLightingData = generateLightingData;

        propertyBlock.Clear();
        propertyBlock.SetColor("_BaseColor", color);
        propertyBlock.SetColor("_Color", color);
        lineRenderer.SetPropertyBlock(propertyBlock);
    }
}