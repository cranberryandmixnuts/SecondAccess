using System;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using Sirenix.OdinInspector;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public sealed class SceneLoader : NetworkSingleton<SceneLoader, GlobalScope>
{
    [Header("Fade UI")]
    [SerializeField, Required] private Image fadeImage;
    [SerializeField] private float fadeDuration = 1.0f;

    public bool IsTransitioning { get; private set; }
    public SceneType CurrentSceneType { get; private set; } = SceneType.None;

    public event Action<SceneType> TransitionCompleted;

    private Tween fadeTween;
    private Coroutine completionRoutine;
    private SceneType pendingScene = SceneType.None;
    private string loadingSceneName;

    protected override void NetworkSingletonAwake()
    {
        CurrentSceneType = GetCurrentSceneType();
        ResetFade();
    }

    protected override void NetworkSingletonOnNetworkSpawn()
    {
        if (NetworkManager.SceneManager != null)
            NetworkManager.SceneManager.OnSceneEvent += OnSceneEvent;
    }

    protected override void NetworkSingletonOnNetworkDespawn()
    {
        if (NetworkManager != null && NetworkManager.SceneManager != null)
            NetworkManager.SceneManager.OnSceneEvent -= OnSceneEvent;
    }

    protected override void NetworkSingletonOnDestroy()
    {
        if (NetworkManager != null && NetworkManager.SceneManager != null)
            NetworkManager.SceneManager.OnSceneEvent -= OnSceneEvent;

        fadeTween?.Kill(false);

        if (completionRoutine != null)
            StopCoroutine(completionRoutine);
    }

    public void LoadScene(SceneType scene)
    {
        if (!TryGetSceneName(scene, out string sceneName))
            return;

        if (!CanLoadNetworkScene())
            return;

        if (IsTransitioning)
        {
            pendingScene = scene;
            return;
        }

        StartCoroutine(ServerLoadSceneSequence(sceneName));
    }

    private IEnumerator ServerLoadSceneSequence(string sceneName)
    {
        IsTransitioning = true;
        loadingSceneName = sceneName;
        pendingScene = SceneType.None;

        BeginFade(1f);
        SendBeginFadeToClients(1f);

        yield return fadeTween.WaitForCompletion();

        SceneEventProgressStatus status = NetworkManager.SceneManager.LoadScene(sceneName, LoadSceneMode.Single);

        if (status == SceneEventProgressStatus.Started)
            yield break;

        Debug.LogError($"네트워크 씬 로드 실패: {sceneName}, Status={status}", this);
        StartCoroutine(CancelTransitionSequence());
        SendCancelTransitionToClients();
    }

    private IEnumerator CancelTransitionSequence()
    {
        loadingSceneName = null;
        pendingScene = SceneType.None;

        yield return BeginFade(0f).WaitForCompletion();

        fadeImage.gameObject.SetActive(false);
        IsTransitioning = false;
    }

    private void OnSceneEvent(SceneEvent sceneEvent)
    {
        switch (sceneEvent.SceneEventType)
        {
            case SceneEventType.Load:
                OnNetworkLoadStarted(sceneEvent);
                break;

            case SceneEventType.LoadEventCompleted:
                OnNetworkLoadCompleted(sceneEvent);
                break;
        }
    }

    private void OnNetworkLoadStarted(SceneEvent sceneEvent)
    {
        if (IsServer)
            return;

        IsTransitioning = true;
        loadingSceneName = sceneEvent.SceneName;

        if (Mathf.Approximately(fadeImage.color.a, 1f))
            return;

        BeginFade(1f);
    }

    private void OnNetworkLoadCompleted(SceneEvent sceneEvent)
    {
        if (!string.IsNullOrEmpty(loadingSceneName) && sceneEvent.SceneName != loadingSceneName)
            return;

        loadingSceneName = null;

        if (SceneTypeMap.TryGetTypeByName(sceneEvent.SceneName, out SceneType sceneType))
            CurrentSceneType = sceneType;
        else
        {
            Debug.LogError($"로드 완료된 씬 이름 '{sceneEvent.SceneName}' 이 SceneTypeMap과 일치하지 않습니다.", this);
            CurrentSceneType = SceneType.None;
        }

        if (completionRoutine != null)
            StopCoroutine(completionRoutine);

        completionRoutine = StartCoroutine(CompleteTransitionSequence(CurrentSceneType));
    }

    private IEnumerator CompleteTransitionSequence(SceneType completedScene)
    {
        yield return BeginFade(0f).WaitForCompletion();

        fadeImage.gameObject.SetActive(false);
        IsTransitioning = false;
        completionRoutine = null;
        TransitionCompleted?.Invoke(completedScene);

        if (!IsServer)
            yield break;

        if (pendingScene == SceneType.None)
            yield break;

        SceneType next = pendingScene;
        pendingScene = SceneType.None;
        LoadScene(next);
    }

    private bool CanLoadNetworkScene()
    {
        if (!IsSpawned)
        {
            Debug.LogError("SceneLoader가 아직 네트워크에 Spawn되지 않았습니다.", this);
            return false;
        }

        if (!IsServer)
        {
            Debug.LogWarning("네트워크 씬 전환은 서버 또는 호스트에서만 실행할 수 있습니다.", this);
            return false;
        }

        if (!NetworkManager.NetworkConfig.EnableSceneManagement)
        {
            Debug.LogError("NetworkManager의 Enable Scene Management가 꺼져 있습니다.", this);
            return false;
        }

        return true;
    }

    private bool TryGetSceneName(SceneType scene, out string sceneName)
    {
        sceneName = SceneTypeMap.GetName(scene);

        if (!string.IsNullOrEmpty(sceneName) && scene != SceneType.None)
            return true;

        Debug.LogError($"{sceneName}씬이 존재하지 않습니다.", this);
        return false;
    }

    private void SendBeginFadeToClients(float targetAlpha)
    {
        if (!TryGetRemoteClientRpcParams(out ClientRpcParams clientRpcParams))
            return;

        BeginFadeClientRpc(targetAlpha, clientRpcParams);
    }

    private void SendCancelTransitionToClients()
    {
        if (!TryGetRemoteClientRpcParams(out ClientRpcParams clientRpcParams))
            return;

        CancelTransitionClientRpc(clientRpcParams);
    }

    private bool TryGetRemoteClientRpcParams(out ClientRpcParams clientRpcParams)
    {
        List<ulong> clientIds = new();

        foreach (ulong clientId in NetworkManager.ConnectedClientsIds)
        {
            if (clientId == NetworkManager.ServerClientId)
                continue;

            clientIds.Add(clientId);
        }

        if (clientIds.Count > 0)
        {
            clientRpcParams = new ClientRpcParams
            {
                Send = new ClientRpcSendParams
                {
                    TargetClientIds = clientIds.ToArray()
                }
            };

            return true;
        }

        clientRpcParams = default;
        return false;
    }

    [ClientRpc]
    private void BeginFadeClientRpc(float targetAlpha, ClientRpcParams clientRpcParams = default)
    {
        if (IsServer)
            return;

        IsTransitioning = true;
        BeginFade(targetAlpha);
    }

    [ClientRpc]
    private void CancelTransitionClientRpc(ClientRpcParams clientRpcParams = default)
    {
        if (IsServer)
            return;

        StartCoroutine(CancelTransitionSequence());
    }

    private Tween BeginFade(float targetAlpha)
    {
        fadeImage.gameObject.SetActive(true);

        if (fadeTween != null && fadeTween.IsActive())
            fadeTween.Kill(false);

        fadeTween = fadeImage
            .DOFade(targetAlpha, fadeDuration)
            .SetEase(Ease.Linear)
            .SetUpdate(true);

        return fadeTween;
    }

    private void ResetFade()
    {
        Color imageColor = fadeImage.color;
        imageColor.a = 0f;
        fadeImage.color = imageColor;

        fadeImage.gameObject.SetActive(false);
    }

    private SceneType GetCurrentSceneType()
    {
        string sceneName = SceneManager.GetActiveScene().name;

        if (SceneTypeMap.TryGetTypeByName(sceneName, out SceneType sceneType))
            return sceneType;

        Debug.LogError($"현재 씬 이름 '{sceneName}' 이 SceneTypeMap과 일치하지 않습니다.", this);
        return SceneType.None;
    }
}