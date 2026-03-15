using System;
using UnityEngine;

public enum BgmId
{
    None = 0,
    Title,
    Game,
}

public enum SfxId
{
    None = 0,
}

[CreateAssetMenu(fileName = "SoundStorage", menuName = "Scriptable Objects/SoundStorage")]
public sealed class SoundStorage : ScriptableObject
{
    [Serializable]
    public sealed class BgmEntry
    {
        public BgmId Id;
        public AudioClip Clip;
        [Range(0f, 1f)] public float Volume = 1f;
        public bool Loop = true;
    }

    [Serializable]
    public sealed class SfxEntry
    {
        public SfxId Id;
        public AudioClip Clip;
        [Range(0f, 1f)] public float Volume = 1f;

        [Header("3D Settings")]
        [Range(0f, 1f)] public float SpatialBlend = 0f;
        public float MinDistance = 1f;
        public float MaxDistance = 50f;
    }

    [Header("BGM")]
    [SerializeField] private BgmEntry[] bgmEntries;

    [Header("SFX")]
    [SerializeField] private SfxEntry[] sfxEntries;

    public BgmEntry[] BgmEntries => bgmEntries;
    public SfxEntry[] SfxEntries => sfxEntries;
}