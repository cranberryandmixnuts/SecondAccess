using UnityEngine;

[ExecuteAlways]
public sealed class OutlineMaskCameraSync : MonoBehaviour
{
    [SerializeField] private Camera sourceCamera = default;
    [SerializeField] private Camera maskCamera = default;
    [SerializeField] private Material compositeMaterial = default;
    [SerializeField] private int depthBufferBits = 24;

    private RenderTexture maskTexture = default;
    private int cachedWidth;
    private int cachedHeight;

    private void OnEnable()
    {
        SyncCamera();
        RecreateMaskTextureIfNeeded();
        ApplyMaterialTexture();
    }

    private void LateUpdate()
    {
        SyncCamera();
        RecreateMaskTextureIfNeeded();
        ApplyMaterialTexture();
    }

    private void OnDisable() => ReleaseMaskTexture();

    private void SyncCamera()
    {
        Transform sourceTransform = sourceCamera.transform;
        Transform maskTransform = maskCamera.transform;

        maskTransform.SetPositionAndRotation(sourceTransform.position, sourceTransform.rotation);

        maskCamera.orthographic = sourceCamera.orthographic;
        maskCamera.fieldOfView = sourceCamera.fieldOfView;
        maskCamera.orthographicSize = sourceCamera.orthographicSize;
        maskCamera.nearClipPlane = sourceCamera.nearClipPlane;
        maskCamera.farClipPlane = sourceCamera.farClipPlane;
        maskCamera.aspect = sourceCamera.aspect;
        maskCamera.allowHDR = false;
        maskCamera.allowMSAA = false;
        maskCamera.clearFlags = CameraClearFlags.SolidColor;
        maskCamera.backgroundColor = Color.black;
    }

    private void RecreateMaskTextureIfNeeded()
    {
        int width = Mathf.Max(1, sourceCamera.pixelWidth);
        int height = Mathf.Max(1, sourceCamera.pixelHeight);

        if (maskTexture != null && width == cachedWidth && height == cachedHeight)
            return;

        ReleaseMaskTexture();

        RenderTextureFormat format = GetMaskRenderTextureFormat();

        maskTexture = new RenderTexture(width, height, depthBufferBits, format, RenderTextureReadWrite.Linear)
        {
            name = "_OutlineMaskRT",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            useMipMap = false,
            autoGenerateMips = false
        };

        maskTexture.Create();

        cachedWidth = width;
        cachedHeight = height;

        maskCamera.targetTexture = maskTexture;
    }

    private RenderTextureFormat GetMaskRenderTextureFormat()
    {
        if (SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.R8))
            return RenderTextureFormat.R8;

        return RenderTextureFormat.ARGB32;
    }

    private void ApplyMaterialTexture() => compositeMaterial.SetTexture("_OutlineMaskTex", maskTexture);

    private void ReleaseMaskTexture()
    {
        if (maskTexture == null)
            return;

        maskCamera.targetTexture = null;

        if (Application.isPlaying)
            Destroy(maskTexture);
        else
            DestroyImmediate(maskTexture);

        maskTexture = null;
        cachedWidth = 0;
        cachedHeight = 0;
    }
}