using System.Collections.Generic;
using DG.Tweening;
using Sirenix.OdinInspector;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;

[RequireComponent(typeof(NetworkObject))]
public sealed class TriggerSignalEffect : NetworkBehaviour
{
    private const int MaximumPathResolution = 24;

    [SerializeField]
    private Material signalMaterial;

    [SerializeField]
    private Color signalColor = new(1f, 1f, 1f);

    [SerializeField, Min(0.01f)]
    private float signalLength = 5f;

    [SerializeField, Min(0.01f)]
    private float signalThickness = 0.1f;

    [SerializeField, Min(0f)]
    private float heightOffset = 0.3f;

    [SerializeField, Range(-85f, 85f)]
    private float startAngle = 15f;

    [SerializeField, Min(0.01f)]
    private float duration = 0.2f;

    [SerializeField, Min(0f)]
    private float emissionIntensity = 1.2f;

    public void Play(Transform origin, IReadOnlyList<TriggerTarget> targets)
    {
        Vector3 startPosition = origin.position + Vector3.up * heightOffset;
        Vector3[] endPositions = CreateEndPositions(targets);

        if (endPositions.Length == 0)
            return;

        if (!IsSpawned)
        {
            PlayLocal(startPosition, endPositions);
            return;
        }

        if (!IsServer)
            return;

        PlayClientRpc(startPosition, endPositions);
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void PlayClientRpc(Vector3 startPosition, Vector3[] endPositions)
    {
        PlayLocal(startPosition, endPositions);
    }

    private Vector3[] CreateEndPositions(IReadOnlyList<TriggerTarget> targets)
    {
        List<Vector3> endPositions = new(targets.Count);

        for (int i = 0; i < targets.Count; i++)
        {
            TriggerTarget target = targets[i];

            if (target == null)
                continue;

            endPositions.Add(target.transform.position + Vector3.up * heightOffset);
        }

        return endPositions.ToArray();
    }

    private void PlayLocal(Vector3 startPosition, IReadOnlyList<Vector3> endPositions)
    {
        for (int i = 0; i < endPositions.Count; i++)
            PlayPulse(startPosition, endPositions[i]);
    }

    private void PlayPulse(Vector3 startPosition, Vector3 endPosition)
    {
        Vector3 difference = endPosition - startPosition;
        float distance = difference.magnitude;

        if (distance <= Mathf.Epsilon)
            return;

        float visibleLength = Mathf.Min(signalLength, distance);
        float tailDuration = duration * visibleLength / distance;

        GameObject pulse = new("Trigger Signal Effect");

        LineRenderer lineRenderer = pulse.AddComponent<LineRenderer>();
        Material material = CreateSignalMaterial();
        ConfigureLineRenderer(lineRenderer, material);

        Sequence sequence = DOTween.Sequence()
            .Append(DOTween.To(() => 0f, trimDistance => UpdateTrimmedPulse(lineRenderer, startPosition, endPosition, distance, visibleLength, trimDistance), distance, duration).SetEase(Ease.OutSine))
            .Append(DOTween.To(() => distance, trimDistance => UpdateTrimmedPulse(lineRenderer, startPosition, endPosition, distance, visibleLength, trimDistance), distance + visibleLength, tailDuration).SetEase(Ease.Linear))
            .SetLink(pulse);

        sequence.OnKill(() => ReleasePulse(pulse, material));
    }

    private void UpdateTrimmedPulse(LineRenderer lineRenderer, Vector3 startPosition, Vector3 endPosition, float distance, float visibleLength, float trimDistance)
    {
        float headDistance = Mathf.Clamp(trimDistance, 0f, distance);
        float tailDistance = Mathf.Clamp(trimDistance - visibleLength, 0f, distance);
        float segmentLength = headDistance - tailDistance;

        if (segmentLength <= 0.001f)
        {
            lineRenderer.enabled = false;
            return;
        }

        lineRenderer.enabled = true;

        float tailProgress = tailDistance / distance;
        float headProgress = headDistance / distance;
        float segmentProgress = headProgress - tailProgress;
        int positionCount = Mathf.Clamp(Mathf.CeilToInt(segmentProgress * MaximumPathResolution) + 2, 2, MaximumPathResolution + 1);

        lineRenderer.positionCount = positionCount;

        for (int i = 0; i < positionCount; i++)
        {
            float ratio = positionCount == 1 ? 0f : i / (positionCount - 1f);
            float progress = Mathf.Lerp(tailProgress, headProgress, ratio);
            lineRenderer.SetPosition(i, EvaluateParabola(startPosition, endPosition, distance, progress));
        }
    }

    private Vector3 EvaluateParabola(Vector3 startPosition, Vector3 endPosition, float distance, float progress)
    {
        Vector3 basePosition = Vector3.Lerp(startPosition, endPosition, progress);
        float angleRadian = startAngle * Mathf.Deg2Rad;
        float height = distance * Mathf.Tan(angleRadian) * progress * (1f - progress);
        return basePosition + Vector3.up * height;
    }

    private void ConfigureLineRenderer(LineRenderer lineRenderer, Material material)
    {
        lineRenderer.sharedMaterial = material;
        lineRenderer.useWorldSpace = true;
        lineRenderer.widthMultiplier = signalThickness;
        lineRenderer.numCapVertices = 8;
        lineRenderer.numCornerVertices = 4;
        lineRenderer.positionCount = 0;
        lineRenderer.shadowCastingMode = ShadowCastingMode.Off;
        lineRenderer.receiveShadows = false;
        lineRenderer.startColor = signalColor;
        lineRenderer.endColor = signalColor;
        lineRenderer.enabled = false;
    }

    private Material CreateSignalMaterial()
    {
        Material material = signalMaterial != null ? new Material(signalMaterial) : new Material(GetDefaultShader());

        ConfigureTransparentMaterial(material);
        ApplyMaterialColor(material, signalColor);

        return material;
    }

    private Shader GetDefaultShader()
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

        return Shader.Find("Standard");
    }

    private void ConfigureTransparentMaterial(Material material)
    {
        material.renderQueue = (int)RenderQueue.Transparent;

        if (material.HasProperty("_Surface"))
            material.SetFloat("_Surface", 1f);

        if (material.HasProperty("_Blend"))
            material.SetFloat("_Blend", 0f);

        if (material.HasProperty("_SrcBlend"))
            material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);

        if (material.HasProperty("_DstBlend"))
            material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);

        if (material.HasProperty("_ZWrite"))
            material.SetInt("_ZWrite", 0);

        material.DisableKeyword("_ALPHATEST_ON");
        material.EnableKeyword("_ALPHABLEND_ON");
        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.EnableKeyword("_EMISSION");
    }

    private void ApplyMaterialColor(Material material, Color color)
    {
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", color);

        if (material.HasProperty("_Color"))
            material.SetColor("_Color", color);

        if (material.HasProperty("_EmissionColor"))
            material.SetColor("_EmissionColor", color * emissionIntensity);
    }

    private static void ReleasePulse(GameObject pulse, Material material)
    {
        if (pulse != null)
            Destroy(pulse);

        if (material != null)
            Destroy(material);
    }
}