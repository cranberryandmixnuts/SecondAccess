using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using DG.Tweening;
using Sirenix.OdinInspector;

public sealed class SceneLoader : Singleton<SceneLoader, GlobalScope>
{
    [Header("Fade UI")]
    [SerializeField, Required] private Image fadeImage;
    [SerializeField] private float fadeDuration = 1.0f;

    public bool IsTransitioning { get; private set; } = false;

    public SceneType CurrentSceneType { get; private set; } = SceneType.None;

    private Tween fadeTween;
    private Tween pendingRequestTween;
    private SceneType pendingScene = SceneType.None;

    private void Start()
    {
        CurrentSceneType = GetCurrentSceneType();

        BgmId bgm = GetBgmForScene(CurrentSceneType);
        SoundManager.Instance.ChangeBgm(bgm, fadeDuration);

        Color imageColor = fadeImage.color;
        imageColor.a = 0f;
        fadeImage.color = imageColor;

        fadeImage.gameObject.SetActive(false);
    }

    public void LoadScene(SceneType scene)
    {
        string sceneName = SceneTypeMap.GetName(scene);
        if (string.IsNullOrEmpty(sceneName) || scene == SceneType.None)
        {
            Debug.LogError($"{sceneName}씬이 존재하지 않습니다.");
            return;
        }

        if (IsTransitioning)
        {
            ReserveSceneLoad(scene);
            return;
        }

        StartCoroutine(LoadSceneSequence(scene));
    }

    private void ReserveSceneLoad(SceneType scene)
    {
        pendingScene = scene;

        if (pendingRequestTween != null && pendingRequestTween.IsActive())
            pendingRequestTween.Kill(false);

        float delay = GetRemainingFadeTime();
        pendingRequestTween = DOVirtual.DelayedCall(delay, TryExecutePending, true).SetUpdate(true);
    }

    private void TryExecutePending()
    {
        if (IsTransitioning) return;
        if (pendingScene == SceneType.None) return;

        SceneType next = pendingScene;
        pendingScene = SceneType.None;
        LoadScene(next);
    }

    private IEnumerator LoadSceneSequence(SceneType scene)
    {
        IsTransitioning = true;

        fadeImage.gameObject.SetActive(true);

        BgmId bgm = GetBgmForScene(scene);
        SoundManager.Instance.ChangeBgm(bgm, fadeDuration);

        yield return FadeTo(1f).WaitForCompletion();

        string sceneName = SceneTypeMap.GetName(scene);
        if (string.IsNullOrEmpty(sceneName) || scene == SceneType.None)
        {
            Debug.LogError($"{sceneName}씬이 존재하지 않습니다.");
            IsTransitioning = false;
            fadeImage.gameObject.SetActive(false);
            yield break;
        }

        AsyncOperation asyncLoad = SceneManager.LoadSceneAsync(sceneName);
        yield return new WaitUntil(() => asyncLoad.isDone);

        CurrentSceneType = GetCurrentSceneType();

        yield return FadeTo(0f).WaitForCompletion();

        IsTransitioning = false;
        fadeImage.gameObject.SetActive(false);

        if (pendingScene != SceneType.None)
        {
            SceneType next = pendingScene;
            pendingScene = SceneType.None;
            LoadScene(next);
        }
    }

    private Tween FadeTo(float targetAlpha)
    {
        if (fadeTween != null && fadeTween.IsActive())
            fadeTween.Kill(false);

        fadeTween = fadeImage
            .DOFade(targetAlpha, fadeDuration)
            .SetEase(Ease.Linear)
            .SetUpdate(true);

        return fadeTween;
    }

    private float GetRemainingFadeTime()
    {
        if (fadeTween == null) return 0f;
        if (!fadeTween.IsActive()) return 0f;
        if (!fadeTween.IsPlaying()) return 0f;

        float remaining = fadeTween.Duration(false) - fadeTween.Elapsed(false);
        if (remaining < 0f) remaining = 0f;
        return remaining;
    }

    private SceneType GetCurrentSceneType()
    {
        string name = SceneManager.GetActiveScene().name;
        if (SceneTypeMap.TryGetTypeByName(name, out SceneType t))
            return t;

        Debug.LogError($"현재 씬 이름 '{name}' 이 SceneTypeMap과 일치하지 않습니다.");
        return SceneType.None;
    }

    private BgmId GetBgmForScene(SceneType scene)
    {
        return scene switch
        {
            SceneType.TitleScene => BgmId.Title,
            _ => BgmId.None,
        };
    }
}