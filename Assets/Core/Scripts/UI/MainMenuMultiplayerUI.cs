using Sirenix.OdinInspector;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class MainMenuMultiplayerUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField, Required] private MultiplayerLobbyManager lobbyManager;

    [Header("Buttons")]
    [SerializeField, Required] private Button createRoomButton;
    [SerializeField, Required] private Button joinRoomButton;

    [Header("Input")]
    [SerializeField, Required] private TMP_InputField joinCodeInputField;

    [Header("Texts")]
    [SerializeField, Required] private TMP_Text joinCodeText;
    [SerializeField, Required] private TMP_Text playerCountText;
    [SerializeField, Required] private TMP_Text statusText;

    private void OnEnable()
    {
        createRoomButton.onClick.AddListener(OnCreateRoomClicked);
        joinRoomButton.onClick.AddListener(OnJoinRoomClicked);
        joinCodeInputField.onValueChanged.AddListener(OnJoinCodeInputChanged);

        lobbyManager.JoinCodeChanged += OnJoinCodeChanged;
        lobbyManager.PlayerCountChanged += OnPlayerCountChanged;
        lobbyManager.StatusChanged += OnStatusChanged;
        lobbyManager.StateChanged += RefreshInteractable;

        RefreshAll();
    }

    private void OnDisable()
    {
        createRoomButton.onClick.RemoveListener(OnCreateRoomClicked);
        joinRoomButton.onClick.RemoveListener(OnJoinRoomClicked);
        joinCodeInputField.onValueChanged.RemoveListener(OnJoinCodeInputChanged);

        lobbyManager.JoinCodeChanged -= OnJoinCodeChanged;
        lobbyManager.PlayerCountChanged -= OnPlayerCountChanged;
        lobbyManager.StatusChanged -= OnStatusChanged;
        lobbyManager.StateChanged -= RefreshInteractable;
    }

    private void OnCreateRoomClicked() => lobbyManager.CreateRoom();

    private void OnJoinRoomClicked() => lobbyManager.JoinRoom(joinCodeInputField.text);

    private void OnJoinCodeInputChanged(string value) => RefreshInteractable();

    private void RefreshAll()
    {
        OnJoinCodeChanged(lobbyManager.JoinCode);
        OnPlayerCountChanged(lobbyManager.ConnectedPlayerCount, lobbyManager.MaxPlayers);
        OnStatusChanged(lobbyManager.CurrentStatus);
        RefreshInteractable();
    }

    private void RefreshInteractable()
    {
        bool canUseMenu = !lobbyManager.IsBusy && !lobbyManager.IsConnected;

        createRoomButton.interactable = canUseMenu;
        joinRoomButton.interactable = canUseMenu && !string.IsNullOrWhiteSpace(joinCodeInputField.text);
        joinCodeInputField.interactable = canUseMenu;
    }

    private void OnJoinCodeChanged(string joinCode)
    {
        joinCodeText.text = string.IsNullOrWhiteSpace(joinCode) ? "방 코드: -" : $"방 코드: {joinCode}";
    }

    private void OnPlayerCountChanged(int count, int max)
    {
        playerCountText.text = $"접속 인원: {count}/{max}";
    }

    private void OnStatusChanged(string status)
    {
        statusText.text = status;
    }
}
