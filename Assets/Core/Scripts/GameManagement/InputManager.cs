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
    Jump,
    Dash,
    Parry,
    Heal,
    Escape
}

[Serializable]
public struct InputModeState
{
    public InputMode MoveAxis;
    public InputMode JumpDown;
    public InputMode JumpHeld;
    public InputMode DashDown;
    public InputMode ParryDown;
    public InputMode ParryHeld;
    public InputMode HealHeld;
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
    private string jumpActionName = "Jump";

    [TabGroup("InputManager", "Action Names"), BoxGroup("InputManager/Action Names/Player"), SerializeField]
    private string dashActionName = "Dash";

    [TabGroup("InputManager", "Action Names"), BoxGroup("InputManager/Action Names/Player"), SerializeField]
    private string parryActionName = "Parry";

    [TabGroup("InputManager", "Action Names"), BoxGroup("InputManager/Action Names/Player"), SerializeField]
    private string healActionName = "Heal";

    [TabGroup("InputManager", "Action Names"), BoxGroup("InputManager/Action Names/UI"), SerializeField]
    private string escapeActionName = "Escape";

    private static readonly string[] IgnoredRebindControlPaths =
    {
        "<Mouse>/scroll",
        "<Mouse>/scroll/x",
        "<Mouse>/scroll/y",
        "<Pointer>/scroll",
        "<Pointer>/scroll/x",
        "<Pointer>/scroll/y"
    };

    private InputAction moveAction;
    private InputAction jumpAction;
    private InputAction dashAction;
    private InputAction parryAction;
    private InputAction healAction;
    private InputAction escapeAction;

    private InputActionRebindingExtensions.RebindingOperation currentRebind;

    private InputAction currentRebindAction;
    private int currentRebindBindingIndex;
    private bool currentRebindExcludeMouse;
    private string currentRebindPreviousPath;

    private InputModeState currentModes;

    private float autoMoveAxis;
    private AutoButtonState autoJump;
    private AutoButtonState autoDash;
    private AutoButtonState autoParry;
    private AutoButtonState autoHeal;
    private AutoButtonState autoEscape;

    private const string RebindsKey = "InputService_Rebinds";

    public float MoveAxis { get; private set; }

    public bool JumpDown { get; private set; }
    public bool JumpHeld { get; private set; }

    public bool DashDown { get; private set; }

    public bool ParryDown { get; private set; }
    public bool ParryHeld { get; private set; }

    public bool HealHeld { get; private set; }

    public bool EscapeDown { get; private set; }

    public bool IsRebinding { get; private set; }

    public event Action OnRebindStarted;
    public event Action OnRebindCompleted;
    public event Action OnRebindCanceled;

    public InputActionAsset Actions => actions;

    public InputAction FindAction(string mapName, string actionName) =>
        actions.FindAction(mapName + "/" + actionName);

    protected override void SingletonAwake()
    {
        InitializeActions();
        LoadBindingOverrides();
    }

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
        if (IsRebinding)
        {
            MoveAxis = 0f;

            JumpDown = false;
            JumpHeld = false;

            DashDown = false;

            ParryDown = false;
            ParryHeld = false;

            HealHeld = false;

            EscapeDown = false;

            StabilizeAutoDuringRebind();
            return;
        }

        if (currentModes.MoveAxis == InputMode.Manual)
            MoveAxis = Mathf.Clamp(moveAction.ReadValue<Vector2>().x, -1f, 1f);
        else
            MoveAxis = Mathf.Clamp(autoMoveAxis, -1f, 1f);

        bool jumpManualDown = jumpAction.WasPressedThisFrame();
        bool jumpManualHeld = jumpAction.IsPressed();

        bool jumpAutoDown = false;
        bool jumpAutoHeld = false;

        if (currentModes.JumpDown == InputMode.Auto || currentModes.JumpHeld == InputMode.Auto)
        {
            EvaluateAutoButton(ref autoJump, out bool down, out bool held);
            jumpAutoDown = down;
            jumpAutoHeld = held;
        }

        JumpDown = currentModes.JumpDown == InputMode.Manual ? jumpManualDown : jumpAutoDown;
        JumpHeld = currentModes.JumpHeld == InputMode.Manual ? jumpManualHeld : jumpAutoHeld;

        if (currentModes.DashDown == InputMode.Manual)
        {
            DashDown = dashAction.WasPressedThisFrame();
        }
        else
        {
            EvaluateAutoButton(ref autoDash, out bool down, out _);
            DashDown = down;
        }

        bool parryManualDown = parryAction.WasPressedThisFrame();
        bool parryManualHeld = parryAction.IsPressed();

        bool parryAutoDown = false;
        bool parryAutoHeld = false;

        if (currentModes.ParryDown == InputMode.Auto || currentModes.ParryHeld == InputMode.Auto)
        {
            EvaluateAutoButton(ref autoParry, out bool down, out bool held);
            parryAutoDown = down;
            parryAutoHeld = held;
        }

        ParryDown = currentModes.ParryDown == InputMode.Manual ? parryManualDown : parryAutoDown;
        ParryHeld = currentModes.ParryHeld == InputMode.Manual ? parryManualHeld : parryAutoHeld;

        if (currentModes.HealHeld == InputMode.Manual)
        {
            HealHeld = healAction.IsPressed();
        }
        else
        {
            EvaluateAutoButton(ref autoHeal, out _, out bool held);
            HealHeld = held;
        }

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
        if (currentModes.MoveAxis != newModes.MoveAxis)
            autoMoveAxis = 0f;

        if (currentModes.JumpDown != newModes.JumpDown || currentModes.JumpHeld != newModes.JumpHeld)
            ResetAutoButton(ref autoJump);

        if (currentModes.DashDown != newModes.DashDown)
            ResetAutoButton(ref autoDash);

        if (currentModes.ParryDown != newModes.ParryDown || currentModes.ParryHeld != newModes.ParryHeld)
            ResetAutoButton(ref autoParry);

        if (currentModes.HealHeld != newModes.HealHeld)
            ResetAutoButton(ref autoHeal);

        if (currentModes.EscapeDown != newModes.EscapeDown)
            ResetAutoButton(ref autoEscape);

        currentModes = newModes;
    }

    public void SetMode(ActionKey key, InputMode mode)
    {
        switch (key)
        {
            case ActionKey.Move:
                currentModes.MoveAxis = mode;
                autoMoveAxis = 0f;
                break;
            case ActionKey.Jump:
                currentModes.JumpDown = mode;
                currentModes.JumpHeld = mode;
                ResetAutoButton(ref autoJump);
                break;
            case ActionKey.Dash:
                currentModes.DashDown = mode;
                ResetAutoButton(ref autoDash);
                break;
            case ActionKey.Parry:
                currentModes.ParryDown = mode;
                currentModes.ParryHeld = mode;
                ResetAutoButton(ref autoParry);
                break;
            case ActionKey.Heal:
                currentModes.HealHeld = mode;
                ResetAutoButton(ref autoHeal);
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
        SetMode(ActionKey.Jump, mode);
        SetMode(ActionKey.Dash, mode);
        SetMode(ActionKey.Parry, mode);
        SetMode(ActionKey.Heal, mode);
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

    public void SetAutoMoveAxis(float axis) => autoMoveAxis = Mathf.Clamp(axis, -1f, 1f);

    public void SetAutoHeld(ActionKey key, bool held)
    {
        switch (key)
        {
            case ActionKey.Jump:
                autoJump.Held = held;
                break;
            case ActionKey.Parry:
                autoParry.Held = held;
                break;
            case ActionKey.Heal:
                autoHeal.Held = held;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(key), key, null);
        }
    }

    public void TriggerAutoDown(ActionKey key)
    {
        switch (key)
        {
            case ActionKey.Jump:
                autoJump.PulseDown = true;
                break;
            case ActionKey.Dash:
                autoDash.PulseDown = true;
                break;
            case ActionKey.Parry:
                autoParry.PulseDown = true;
                break;
            case ActionKey.Escape:
                autoEscape.PulseDown = true;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(key), key, null);
        }
    }

    private void StabilizeAutoDuringRebind()
    {
        StabilizeAutoButtonDuringRebind(ref autoJump);
        StabilizeAutoButtonDuringRebind(ref autoDash);
        StabilizeAutoButtonDuringRebind(ref autoParry);
        StabilizeAutoButtonDuringRebind(ref autoHeal);
        StabilizeAutoButtonDuringRebind(ref autoEscape);
    }

    private static void ResetAutoButton(ref AutoButtonState state)
    {
        state.Held = false;
        state.PreviousHeld = false;
        state.PulseDown = false;
    }

    private static void StabilizeAutoButtonDuringRebind(ref AutoButtonState state)
    {
        state.PulseDown = false;
        state.PreviousHeld = state.Held;
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
        moveAction = FindAction(playerMapName, moveActionName);
        jumpAction = FindAction(playerMapName, jumpActionName);
        dashAction = FindAction(playerMapName, dashActionName);
        parryAction = FindAction(playerMapName, parryActionName);
        healAction = FindAction(playerMapName, healActionName);

        escapeAction = FindAction(UIMapName, escapeActionName);
    }

    private void EnableActions(bool enable)
    {
        if (enable) actions.Enable();
        else actions.Disable();
    }

    public void SaveBindingOverrides()
    {
        string json = actions.SaveBindingOverridesAsJson();
        PlayerPrefs.SetString(RebindsKey, json);
        PlayerPrefs.Save();
    }

    public void LoadBindingOverrides()
    {
        Debug.Log("Loading input binding overrides.");

        if (!PlayerPrefs.HasKey(RebindsKey)) return;

        string json = PlayerPrefs.GetString(RebindsKey);
        actions.LoadBindingOverridesFromJson(json);
    }

    public void ClearBindingOverrides()
    {
        actions.RemoveAllBindingOverrides();
        PlayerPrefs.DeleteKey(RebindsKey);
    }

    public void CancelCurrentRebind()
    {
        if (currentRebind == null)
            return;

        currentRebind.Cancel();
    }

    public void StartRebind(string mapName, string actionName, int bindingIndex)
    {
        InputAction action = FindAction(mapName, actionName);

        if (action == null)
            return;

        if (bindingIndex < 0 || bindingIndex >= action.bindings.Count)
            return;

        currentRebind?.Cancel();

        IsRebinding = true;

        currentRebindAction = action;
        currentRebindBindingIndex = bindingIndex;
        currentRebindExcludeMouse = false;
        currentRebindPreviousPath = action.bindings[bindingIndex].effectivePath;

        action.Disable();

        OnRebindStarted?.Invoke();

        currentRebind = BuildRebindOperation(action, bindingIndex, currentRebindExcludeMouse);
        currentRebind.Start();
    }

    public void SetCurrentRebindExcludeMouse(bool excludeMouse)
    {
        if (!IsRebinding)
            return;

        if (currentRebind == null)
            return;

        if (currentRebindAction == null)
            return;

        if (currentRebindExcludeMouse == excludeMouse)
            return;

        currentRebindExcludeMouse = excludeMouse;

        currentRebind.Dispose();
        currentRebind = null;

        currentRebind = BuildRebindOperation(currentRebindAction, currentRebindBindingIndex, currentRebindExcludeMouse);
        currentRebind.Start();
    }

    private InputActionRebindingExtensions.RebindingOperation BuildRebindOperation(InputAction action, int bindingIndex, bool excludeMouse)
    {
        InputActionRebindingExtensions.RebindingOperation operation = action.PerformInteractiveRebinding(bindingIndex);

        if (excludeMouse) operation.WithControlsExcluding("Mouse");

        ApplyIgnoredRebindControls(operation);
        operation.OnMatchWaitForAnother(0.1f);

        return operation
            .OnComplete(o => FinishRebind(action))
            .OnCancel(o => CancelRebind(action));
    }

    private static void ApplyIgnoredRebindControls(InputActionRebindingExtensions.RebindingOperation operation)
    {
        for (int i = 0; i < IgnoredRebindControlPaths.Length; i++)
            operation.WithControlsExcluding(IgnoredRebindControlPaths[i]);
    }

    private void FinishRebind(InputAction action)
    {
        action.Enable();

        string newPath = action.bindings[currentRebindBindingIndex].effectivePath;

        if (!string.IsNullOrEmpty(newPath) && newPath != currentRebindPreviousPath)
            SwapDuplicateBindingIfAny(action, currentRebindBindingIndex, newPath, currentRebindPreviousPath);

        currentRebind.Dispose();
        currentRebind = null;

        currentRebindAction = null;
        currentRebindPreviousPath = null;

        IsRebinding = false;

        SaveBindingOverrides();
        OnRebindCompleted?.Invoke();
    }

    private void CancelRebind(InputAction action)
    {
        action.Enable();

        currentRebind.Dispose();
        currentRebind = null;

        currentRebindAction = null;
        currentRebindPreviousPath = null;

        IsRebinding = false;

        OnRebindCanceled?.Invoke();
    }

    private void SwapDuplicateBindingIfAny(InputAction targetAction, int targetBindingIndex, string newPath, string previousPath)
    {
        if (string.IsNullOrEmpty(previousPath))
            return;

        string targetGroups = targetAction.bindings[targetBindingIndex].groups;

        if (TrySwapInAction(targetAction, targetBindingIndex, newPath, previousPath, targetGroups))
            return;

        foreach (InputActionMap map in actions.actionMaps)
        {
            foreach (InputAction action in map.actions)
            {
                if (action == targetAction)
                    continue;

                if (TrySwapInAction(action, -1, newPath, previousPath, targetGroups))
                    return;
            }
        }
    }

    private static bool TrySwapInAction(InputAction action, int skipBindingIndex, string newPath, string previousPath, string targetGroups)
    {
        for (int i = 0; i < action.bindings.Count; i++)
        {
            if (i == skipBindingIndex)
                continue;

            string otherPath = action.bindings[i].effectivePath;

            if (string.IsNullOrEmpty(otherPath))
                continue;

            if (otherPath != newPath)
                continue;

            if (!AreBindingGroupsCompatible(targetGroups, action.bindings[i].groups))
                continue;

            action.ApplyBindingOverride(i, previousPath);
            return true;
        }

        return false;
    }

    private static bool AreBindingGroupsCompatible(string a, string b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
            return true;

        string[] aParts = a.Split(';', StringSplitOptions.RemoveEmptyEntries);
        string[] bParts = b.Split(';', StringSplitOptions.RemoveEmptyEntries);

        for (int i = 0; i < aParts.Length; i++)
        {
            string left = aParts[i].Trim();

            for (int j = 0; j < bParts.Length; j++)
            {
                if (left == bParts[j].Trim())
                    return true;
            }
        }

        return false;
    }
}