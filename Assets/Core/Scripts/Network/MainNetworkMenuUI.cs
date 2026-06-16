using DG.Tweening;
using Sirenix.OdinInspector;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class MainNetworkMenuUI : Singleton<MainNetworkMenuUI, SceneScope>
{
    [TabGroup("MainNetworkMenuUI", "References"), Required, SerializeField]
    private CanvasGroup rootCanvasGroup;

    [TabGroup("MainNetworkMenuUI", "References"), Required, SerializeField]
    private Button createRoomButton;

    [TabGroup("MainNetworkMenuUI", "References"), Required, SerializeField]
    private Button joinRoomButton;

    [TabGroup("MainNetworkMenuUI", "References"), Required, SerializeField]
    private TMP_InputField addressInputField;

    [TabGroup("MainNetworkMenuUI", "References"), SerializeField]
    private TMP_Text statusText;

    [TabGroup("MainNetworkMenuUI", "Tween"), SerializeField]
    private float fadeDuration = 1f;

    private Tween fadeTween;
    private bool listenersBound;

    private void Start()
    {
        Bind();
        ShowInstant();
    }

    protected override void SingletonOnDestroy()
    {
        Unbind();

        fadeTween?.Kill(false);

        if (MultiplayerRoomManager.HasInstance)
            MultiplayerRoomManager.Instance.StatusChanged -= SetStatus;
    }

    public void HideForGameStart()
    {
        SetInteractable(false);

        fadeTween?.Kill(false);

        fadeTween = rootCanvasGroup
            .DOFade(0f, fadeDuration)
            .SetEase(Ease.Linear)
            .SetUpdate(true)
            .OnComplete(() => rootCanvasGroup.gameObject.SetActive(false));
    }

    public void ShowAfterDisconnect(string message)
    {
        fadeTween?.Kill(false);

        rootCanvasGroup.gameObject.SetActive(true);
        rootCanvasGroup.alpha = 1f;

        SetInteractable(true);
        SetStatus(message);
    }

    private void Bind()
    {
        if (listenersBound)
            return;

        createRoomButton.onClick.AddListener(CreateRoom);
        joinRoomButton.onClick.AddListener(JoinRoom);
        MultiplayerRoomManager.Instance.StatusChanged += SetStatus;

        listenersBound = true;
    }

    private void Unbind()
    {
        if (!listenersBound)
            return;

        createRoomButton.onClick.RemoveListener(CreateRoom);
        joinRoomButton.onClick.RemoveListener(JoinRoom);

        listenersBound = false;
    }

    private void CreateRoom()
    {
        SetInteractable(false);
        SetStatus("방 생성 중...");

        if (!MultiplayerRoomManager.Instance.StartHostRoom())
            SetInteractable(true);
    }

    private void JoinRoom()
    {
        SetInteractable(false);
        SetStatus("참가 중...");

        if (!MultiplayerRoomManager.Instance.StartClientRoom(addressInputField.text))
            SetInteractable(true);
    }

    private void ShowInstant()
    {
        fadeTween?.Kill(false);

        rootCanvasGroup.gameObject.SetActive(true);
        rootCanvasGroup.alpha = 1f;

        SetInteractable(true);
    }

    private void SetInteractable(bool interactable)
    {
        rootCanvasGroup.interactable = interactable;
        rootCanvasGroup.blocksRaycasts = interactable;

        createRoomButton.interactable = interactable;
        joinRoomButton.interactable = interactable;
        addressInputField.interactable = interactable;
    }

    private void SetStatus(string message)
    {
        Debug.Log(message);

        if (statusText != null)
            statusText.text = message;
    }
}