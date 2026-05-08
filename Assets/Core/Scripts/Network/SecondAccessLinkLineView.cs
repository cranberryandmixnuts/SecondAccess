using Sirenix.OdinInspector;
using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public sealed class SecondAccessLinkLineView : MonoBehaviour
{
    [SerializeField, Required, TitleGroup("References")]
    private LineRenderer lineRenderer;

    [SerializeField, TitleGroup("References")]
    private SecondAccessLinkManager linkManager;

    [SerializeField, TitleGroup("Visual")]
    private Color ropeColor = Color.white;

    [SerializeField, TitleGroup("Visual")]
    private Color energyColor = Color.cyan;

    [SerializeField, MinValue(0f), TitleGroup("Visual")]
    private float width = 0.08f;

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

        lineRenderer.useWorldSpace = true;
        lineRenderer.positionCount = 0;
        lineRenderer.startWidth = width;
        lineRenderer.endWidth = width;
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

        Color color = linkManager.Mode.Value == SecondAccessLinkMode.Rope ? ropeColor : energyColor;

        lineRenderer.positionCount = 2;
        lineRenderer.SetPosition(0, start);
        lineRenderer.SetPosition(1, end);
        lineRenderer.startColor = color;
        lineRenderer.endColor = color;
        lineRenderer.startWidth = width;
        lineRenderer.endWidth = width;
    }
}
