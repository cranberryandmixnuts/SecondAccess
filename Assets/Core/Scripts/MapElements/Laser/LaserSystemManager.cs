using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[DefaultExecutionOrder(20000)]
public sealed class LaserSystemManager : Singleton<LaserSystemManager, GlobalScope>
{
    internal readonly struct ReceiverInput : IEquatable<ReceiverInput>
    {
        public ILaserReceiver Receiver { get; }
        public Component Source { get; }
        public string Key { get; }

        public ReceiverInput(ILaserReceiver receiver, Component source, string key)
        {
            Receiver = receiver;
            Source = source;
            Key = key ?? string.Empty;
        }

        public bool Equals(ReceiverInput other) => ReferenceEquals(Receiver, other.Receiver) && Source == other.Source && Key == other.Key;

        public override bool Equals(object obj) => obj is ReceiverInput other && Equals(other);

        public override int GetHashCode()
        {
            EntityId receiverId = default;
            EntityId sourceId = default;

            if (Receiver is UnityEngine.Object receiverObject && receiverObject != null)
                receiverId = receiverObject.GetEntityId();

            if (Source != null)
                sourceId = Source.GetEntityId();

            return HashCode.Combine(receiverId, sourceId, Key);
        }
    }

    private const string DirectInputKey = "DirectLaser";
    private const string EnergyLinkInputKey = "EnergyLinkLaser";

    private static readonly List<LaserEmitterRuntime> emitters = new();

    private readonly HashSet<ReceiverInput> currentInputs = new();
    private readonly HashSet<ReceiverInput> previousInputs = new();
    private readonly List<ReceiverInput> endedInputs = new();
    private readonly List<ReceiverInput> startedInputs = new();
    private readonly Collider[] linkReceiverBuffer = new Collider[64];

    public static bool CanWriteGameplay
    {
        get
        {
            if (NetworkManager.Singleton == null)
                return true;

            return NetworkManager.Singleton.IsServer;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        emitters.Clear();
    }

    public static void RegisterEmitter(LaserEmitterRuntime emitter)
    {
        if (emitters.Contains(emitter))
            return;

        emitters.Add(emitter);
    }

    public static void UnregisterEmitter(LaserEmitterRuntime emitter)
    {
        emitters.Remove(emitter);
    }

    internal static void AddDirectInput(HashSet<ReceiverInput> inputs, ILaserReceiver receiver, Component source)
    {
        inputs.Add(new ReceiverInput(receiver, source, DirectInputKey));
    }

    internal static bool TryResolveReceiver(Collider targetCollider, out ILaserReceiver receiver)
    {
        receiver = targetCollider.GetComponentInParent<LaserReceiverRuntime>();

        if (receiver != null)
            return true;

        return receiver != null;
    }

    internal static bool TryResolveMirror(Collider targetCollider, out LaserMirror mirror)
    {
        mirror = targetCollider.GetComponentInParent<LaserMirror>();
        return mirror != null && mirror.ReflectionEnabled;
    }

    private void LateUpdate()
    {
        currentInputs.Clear();
        bool energyLinkLaserized = false;

        for (int i = emitters.Count - 1; i >= 0; i--)
        {
            LaserEmitterRuntime emitter = emitters[i];

            if (emitter == null)
            {
                emitters.RemoveAt(i);
                continue;
            }

            if (!emitter.isActiveAndEnabled)
            {
                emitter.ClearVisual();
                continue;
            }

            if (emitter.Simulate(currentInputs))
                energyLinkLaserized = true;
        }

        LinkManager linkManager = LinkManager.Instance;

        if (energyLinkLaserized && linkManager != null && linkManager.Mode.Value == LinkMode.Energy)
            AddEnergyLinkInputs(linkManager);

        if (CanWriteGameplay)
        {
            ApplyInputDiff();

            if (linkManager != null)
                linkManager.SetEnergyLinkLaserized(energyLinkLaserized);
        }
    }

    private void AddEnergyLinkInputs(LinkManager linkManager)
    {
        if (!linkManager.TryGetLinkPath(out Vector3 firstPosition, out Vector3 relayPosition, out Vector3 secondPosition, out bool usesRelay))
            return;

        AddEnergyLinkSegmentInputs(linkManager, firstPosition, usesRelay ? relayPosition : secondPosition);

        if (usesRelay)
            AddEnergyLinkSegmentInputs(linkManager, relayPosition, secondPosition);
    }

    private void AddEnergyLinkSegmentInputs(LinkManager linkManager, Vector3 start, Vector3 end)
    {
        float radius = Mathf.Max(0.01f, GetLargestEnergyLinkInputRadius());
        int hitCount = Physics.OverlapCapsuleNonAlloc(start, end, radius, linkReceiverBuffer, GetEnergyLinkReceiverMask(), QueryTriggerInteraction.Collide);
        AddEnergyLinkInputsFromColliders(linkManager, hitCount);
    }

    private void AddEnergyLinkInputsFromColliders(LinkManager linkManager, int count)
    {
        for (int i = 0; i < count; i++)
        {
            Collider targetCollider = linkReceiverBuffer[i];

            if (targetCollider == null)
                continue;

            if (!TryResolveReceiver(targetCollider, out ILaserReceiver receiver))
                continue;

            currentInputs.Add(new ReceiverInput(receiver, linkManager, EnergyLinkInputKey));
        }
    }

    private int GetEnergyLinkReceiverMask()
    {
        int mask = 0;

        for (int i = 0; i < emitters.Count; i++)
        {
            LaserEmitterRuntime emitter = emitters[i];

            if (emitter == null)
                continue;

            mask |= emitter.CollisionMask.value;
        }

        return mask == 0 ? ~0 : mask;
    }

    private float GetLargestEnergyLinkInputRadius()
    {
        float radius = 0.12f;

        for (int i = 0; i < emitters.Count; i++)
        {
            LaserEmitterRuntime emitter = emitters[i];

            if (emitter == null)
                continue;

            radius = Mathf.Max(radius, emitter.EnergyLinkInputRadius);
        }

        return radius;
    }

    private void ApplyInputDiff()
    {
        endedInputs.Clear();
        startedInputs.Clear();

        foreach (ReceiverInput input in previousInputs)
        {
            if (!currentInputs.Contains(input))
                endedInputs.Add(input);
        }

        foreach (ReceiverInput input in currentInputs)
        {
            if (!previousInputs.Contains(input))
                startedInputs.Add(input);
        }

        for (int i = 0; i < endedInputs.Count; i++)
            SetReceiverInput(endedInputs[i], false);

        for (int i = 0; i < startedInputs.Count; i++)
            SetReceiverInput(startedInputs[i], true);

        previousInputs.Clear();

        foreach (ReceiverInput input in currentInputs)
            previousInputs.Add(input);
    }

    private void SetReceiverInput(ReceiverInput input, bool active)
    {
        if (input.Receiver is not UnityEngine.Object receiverObject)
            return;

        if (receiverObject == null)
            return;

        if (input.Source == null)
            return;

        input.Receiver.SetLaserInput(input.Source, input.Key, active);
    }
}