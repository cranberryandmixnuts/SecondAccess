using Sirenix.OdinInspector;
using UnityEngine;
using UnityEngine.Rendering;

[RequireComponent(typeof(LineRenderer))]
public sealed class LinkLineView : MonoBehaviour
{
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    [SerializeField, Required, TitleGroup("References")]
    private LineRenderer lineRenderer;

    [SerializeField, TitleGroup("References")]
    private LinkManager linkManager;

    [SerializeField, TitleGroup("Debug")]
    private Transform debugStart;

    [SerializeField, TitleGroup("Debug")]
    private Transform debugEnd;

    [SerializeField, TitleGroup("Debug")]
    private bool drawDebugLineWhenUnavailable = true;

    [SerializeField, MinValue(0.1f), TitleGroup("Debug")]
    private float debugLineLength = 3f;

    [SerializeField, TitleGroup("Debug")]
    private bool logUnavailableReason = true;

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
    private float width = 0.12f;

    [SerializeField, MinValue(0f), TitleGroup("Visual")]
    private float heightOffset = 0.25f;

    [SerializeField, MinValue(0), TitleGroup("Visual")]
    private int capVertices = 4;

    private MaterialPropertyBlock propertyBlock;
    private Material runtimeRopeMaterial;
    private Material runtimeEnergyMaterial;
    private string lastUnavailableReason;

    private void Reset()
    {
        lineRenderer = GetComponent<LineRenderer>();
        linkManager = FindAnyObjectByType<LinkManager>();
    }

    private void Awake()
    {
        lineRenderer = GetComponent<LineRenderer>();

        if (linkManager == null)
            linkManager = LinkManager.Instance;

        propertyBlock = new MaterialPropertyBlock();
        ConfigureRenderer();
    }

    private void OnDestroy()
    {
        DestroyRuntimeMaterial(runtimeRopeMaterial);
        DestroyRuntimeMaterial(runtimeEnergyMaterial);
    }

    private void LateUpdate()
    {
        if (linkManager == null)
            linkManager = LinkManager.Instance;

        if (linkManager != null && linkManager.TryGetDirectLine(out Vector3 start, out Vector3 end))
        {
            DrawLine(start, end, linkManager.Mode.Value);
            lastUnavailableReason = null;
            return;
        }

        DrawUnavailableLine();
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
        lineRenderer.generateLightingData = generateLightingData;
        lineRenderer.shadowCastingMode = ShadowCastingMode.Off;
        lineRenderer.receiveShadows = false;
    }

    private void DrawUnavailableLine()
    {
        string reason = GetUnavailableReason();

        if (logUnavailableReason && lastUnavailableReason != reason)
        {
            lastUnavailableReason = reason;
            Debug.Log($"[SecondAccess] LinkLineView is using debug line. {reason}", this);
        }

        if (!drawDebugLineWhenUnavailable)
            return;

        if (debugStart != null && debugEnd != null)
        {
            DrawLine(debugStart.position, debugEnd.position, LinkMode.Rope);
            return;
        }

        Vector3 center = transform.position;
        Vector3 half = transform.right * (debugLineLength * 0.5f);
        DrawLine(center - half, center + half, LinkMode.Rope);
    }

    private string GetUnavailableReason()
    {
        int scenePlayerCount = CountSpawnedScenePlayers();

        if (linkManager == null)
            return $"LinkManager is missing. SpawnedScenePlayers={scenePlayerCount}";

        return $"Direct line unavailable. RegisteredPlayers={linkManager.RegisteredPlayerCount}, HasTwoPlayers={linkManager.HasTwoPlayers}, SpawnedScenePlayers={scenePlayerCount}";
    }

    private int CountSpawnedScenePlayers()
    {
        int count = 0;
        NetworkPlayer[] players = FindObjectsByType<NetworkPlayer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);

        for (int i = 0; i < players.Length; i++)
        {
            if (players[i].IsSpawned)
                count++;
        }

        return count;
    }

    private void DrawLine(Vector3 start, Vector3 end, LinkMode mode)
    {
        Color color = GetColor(mode);
        Material material = GetMaterial(mode, color);
        Vector3 offset = Vector3.up * heightOffset;

        lineRenderer.sharedMaterial = material;
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
        lineRenderer.generateLightingData = generateLightingData;

        propertyBlock.Clear();
        propertyBlock.SetColor(BaseColorId, color);
        propertyBlock.SetColor(ColorId, color);
        lineRenderer.SetPropertyBlock(propertyBlock);
    }

    private Color GetColor(LinkMode mode) => mode == LinkMode.Rope ? ropeColor : energyColor;

    private Material GetMaterial(LinkMode mode, Color color)
    {
        if (mode == LinkMode.Rope)
        {
            if (ropeMaterial != null)
                return ropeMaterial;

            if (runtimeRopeMaterial == null)
                runtimeRopeMaterial = CreateRuntimeMaterial("Runtime Rope Link Material", color);

            return runtimeRopeMaterial;
        }

        if (energyMaterial != null)
            return energyMaterial;

        if (runtimeEnergyMaterial == null)
            runtimeEnergyMaterial = CreateRuntimeMaterial("Runtime Energy Link Material", color);

        return runtimeEnergyMaterial;
    }

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