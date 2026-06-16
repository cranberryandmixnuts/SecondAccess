using DG.Tweening;
using UnityEngine;

public sealed class OpeningDirection : MonoBehaviour
{
    [SerializeField] private float delay = 0f;
    [SerializeField] private float targetXScale = 0.4f;
    [SerializeField] private float duration = 2f;
    [SerializeField] private Ease ease = Ease.OutExpo;
    private void Start()
    {
        Vector3 scale = transform.localScale;
        scale.x = 0f;
        transform.localScale = scale;

        transform.DOScaleX(targetXScale, duration)
            .SetDelay(delay)
            .SetEase(ease);
    }
}