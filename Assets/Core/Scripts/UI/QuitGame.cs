using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;
using Sirenix.OdinInspector;

public sealed class QuitGame : MonoBehaviour
{
    [Header("Fade UI")]
    [SerializeField, Required] private Image fadeImage;
    [SerializeField] private float fadeDuration = 0.6f;

    private bool isQuitting;
    private Tween fadeTween;

    private void Awake()
    {
        Color imageColor = fadeImage.color;
        imageColor.a = 0f;
        fadeImage.color = imageColor;

        fadeImage.gameObject.SetActive(false);
    }

    public void Quit()
    {
        if (isQuitting) return;
        InputManager.Instance.SetAllModes(InputMode.Auto);
        StartCoroutine(QuitSequence());
    }

    private IEnumerator QuitSequence()
    {
        isQuitting = true;
        Time.timeScale = 0f;

        fadeImage.gameObject.SetActive(true);

        if (fadeTween != null && fadeTween.IsActive())
            fadeTween.Kill();

        fadeTween = fadeImage
            .DOFade(1f, fadeDuration)
            .SetEase(Ease.Linear)
            .SetUpdate(true);

        yield return fadeTween.WaitForCompletion();

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
        Application.Quit();
    }
}