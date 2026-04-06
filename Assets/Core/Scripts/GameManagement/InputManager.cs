using System;
using UnityEngine;
using UnityEngine.InputSystem;
using Sirenix.OdinInspector;

public enum InputMode
{
    Manual,
    Auto
}

public enum ActionKey
{
    Move,
    InterAction,
    Escape
}

[Serializable]
public struct InputModeState
{
    public InputMode Move;
    public InputMode InterActionDown;
    public InputMode InterActionHeld;
    public InputMode EscapeDown;
}

public sealed class InputManager : Singleton<InputManager, GlobalScope>
{
    private struct AutoButtonState
    {
        public bool Held;
        public bool PreviousHeld;
        public bool PulseDown;
    }

    [TabGroup("InputManager", "Setup"), Required, SerializeField]
    private InputActionAsset actions;

    [TabGroup("InputManager", "Setup"), SerializeField]
    private string playerMapName = "Player";

    [TabGroup("InputManager", "Setup"), SerializeField]
    private string UIMapName = "UI";

    [TabGroup("InputManager", "Action Names"), BoxGroup("InputManager/Action Names/Player"), SerializeField]
    private string moveActionName = "Move";

    [TabGroup("InputManager", "Action Names"), BoxGroup("InputManager/Action Names/Player"), SerializeField]
    private string interActionActionName = "InterAction";

    [TabGroup("InputManager", "Action Names"), BoxGroup("InputManager/Action Names/UI"), SerializeField]
    private string escapeActionName = "Escape";

    private InputAction moveAction;
    private InputAction interActionAction;
    private InputAction escapeAction;

    private InputModeState currentModes;

    private Vector2 autoMove;
    private AutoButtonState autoInterAction;
    private AutoButtonState autoEscape;

    public Vector2 Move { get; private set; }
    public float MoveAxis => Move.x;

    public bool InterActionDown { get; private set; }
    public bool InterActionHeld { get; private set; }

    public bool EscapeDown { get; private set; }

    public InputActionAsset Actions => actions;

    public InputAction FindAction(string mapName, string actionName) =>
        actions.FindAction(mapName + "/" + actionName);

    protected override void SingletonAwake() => InitializeActions();

    private void OnEnable()
    {
        if (Instance != this) return;
        EnableActions(true);
    }

    private void OnDisable()
    {
        if (Instance != this) return;
        EnableActions(false);
    }

    private void Update()
    {
        if (currentModes.Move == InputMode.Manual)
            Move = Vector2.ClampMagnitude(moveAction.ReadValue<Vector2>(), 1f);
        else
            Move = Vector2.ClampMagnitude(autoMove, 1f);

        bool interActionManualDown = interActionAction.WasPressedThisFrame();
        bool interActionManualHeld = interActionAction.IsPressed();

        bool interActionAutoDown = false;
        bool interActionAutoHeld = false;

        if (currentModes.InterActionDown == InputMode.Auto || currentModes.InterActionHeld == InputMode.Auto)
        {
            EvaluateAutoButton(ref autoInterAction, out bool down, out bool held);
            interActionAutoDown = down;
            interActionAutoHeld = held;
        }

        InterActionDown = currentModes.InterActionDown == InputMode.Manual ? interActionManualDown : interActionAutoDown;
        InterActionHeld = currentModes.InterActionHeld == InputMode.Manual ? interActionManualHeld : interActionAutoHeld;

        if (currentModes.EscapeDown == InputMode.Manual)
        {
            EscapeDown = escapeAction.WasPressedThisFrame();
        }
        else
        {
            EvaluateAutoButton(ref autoEscape, out bool down, out _);
            EscapeDown = down;
        }
    }

    public InputModeState GetModes() => currentModes;

    public void SetModes(InputModeState newModes)
    {
        if (currentModes.Move != newModes.Move)
            autoMove = Vector2.zero;

        if (currentModes.InterActionDown != newModes.InterActionDown || currentModes.InterActionHeld != newModes.InterActionHeld)
            ResetAutoButton(ref autoInterAction);

        if (currentModes.EscapeDown != newModes.EscapeDown)
            ResetAutoButton(ref autoEscape);

        currentModes = newModes;
    }

    public void SetMode(ActionKey key, InputMode mode)
    {
        switch (key)
        {
            case ActionKey.Move:
                currentModes.Move = mode;
                autoMove = Vector2.zero;
                break;

            case ActionKey.InterAction:
                currentModes.InterActionDown = mode;
                currentModes.InterActionHeld = mode;
                ResetAutoButton(ref autoInterAction);
                break;

            case ActionKey.Escape:
                currentModes.EscapeDown = mode;
                ResetAutoButton(ref autoEscape);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(key), key, null);
        }
    }

    public void SetAllModes(InputMode mode)
    {
        SetMode(ActionKey.Move, mode);
        SetMode(ActionKey.InterAction, mode);
        SetMode(ActionKey.Escape, mode);
    }

    public void SetCursorMode(bool mode)
    {
#if !UNITY_EDITOR
        if (mode)
            Cursor.lockState = CursorLockMode.None;
        else
            Cursor.lockState = CursorLockMode.Locked;

        Cursor.visible = mode;
#endif
    }

    public void SetAutoMove(Vector2 move) => autoMove = Vector2.ClampMagnitude(move, 1f);

    public void SetAutoMoveAxis(float axis) => autoMove = new Vector2(Mathf.Clamp(axis, -1f, 1f), 0f);

    public void SetAutoHeld(ActionKey key, bool held)
    {
        switch (key)
        {
            case ActionKey.InterAction:
                autoInterAction.Held = held;
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(key), key, null);
        }
    }

    public void TriggerAutoDown(ActionKey key)
    {
        switch (key)
        {
            case ActionKey.InterAction:
                autoInterAction.PulseDown = true;
                break;

            case ActionKey.Escape:
                autoEscape.PulseDown = true;
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(key), key, null);
        }
    }

    private static void ResetAutoButton(ref AutoButtonState state)
    {
        state.Held = false;
        state.PreviousHeld = false;
        state.PulseDown = false;
    }

    private static void EvaluateAutoButton(ref AutoButtonState state, out bool down, out bool held)
    {
        held = state.Held;
        down = state.PulseDown || (!state.PreviousHeld && held);
        state.PreviousHeld = held;
        state.PulseDown = false;
    }

    private void InitializeActions()
    {
        moveAction = GetRequiredAction(playerMapName, moveActionName);
        interActionAction = GetRequiredAction(playerMapName, interActionActionName);
        escapeAction = GetRequiredAction(UIMapName, escapeActionName);
    }

    private InputAction GetRequiredAction(string mapName, string actionName)
    {
        InputAction action = FindAction(mapName, actionName);

        if (action != null)
            return action;

        throw new InvalidOperationException($"Input action not found: {mapName}/{actionName}");
    }

    private void EnableActions(bool enable)
    {
        if (enable)
            actions.Enable();
        else
            actions.Disable();
    }
}