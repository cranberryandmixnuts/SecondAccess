using System;
using System.Collections;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.Audio;

public readonly struct SfxHandle : IEquatable<SfxHandle>
{
    public static readonly SfxHandle Invalid = new(-1, 0);

    public int Id { get; }
    public uint Version { get; }

    public bool IsValid => Id >= 0;

    public SfxHandle(int id, uint version)
    {
        Id = id;
        Version = version;
    }

    public bool Equals(SfxHandle other) => Id == other.Id && Version == other.Version;

    public override bool Equals(object obj) => obj is SfxHandle other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Id, Version);

    public static bool operator ==(SfxHandle left, SfxHandle right) => left.Equals(right);

    public static bool operator !=(SfxHandle left, SfxHandle right) => !left.Equals(right);
}

public class AudioManager : Singleton<AudioManager, GlobalScope>
{
    private const string MasterKey = "Volume_Master_Db";
    private const string BgmKey = "Volume_BGM_Db";
    private const string SfxKey = "Volume_SFX_Db";

    private sealed class SfxVoice
    {
        public int Id;
        public uint Version;
        public AudioSource Source;
        public GameObject Owner;
        public string AudioName;
        public string Slot;
        public bool IsLoop;
        public bool IsActive;
        public Coroutine ReleaseRoutine;
        public Tween FadeTween;
    }

    private readonly struct LoopSlotKey : IEquatable<LoopSlotKey>
    {
        public int OwnerId { get; }
        public string Slot { get; }

        public LoopSlotKey(int ownerId, string slot)
        {
            OwnerId = ownerId;
            Slot = slot;
        }

        public bool Equals(LoopSlotKey other) => OwnerId == other.OwnerId && Slot == other.Slot;

        public override bool Equals(object obj) => obj is LoopSlotKey other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(OwnerId, Slot);

        public static bool operator ==(LoopSlotKey left, LoopSlotKey right) => left.Equals(right);

        public static bool operator !=(LoopSlotKey left, LoopSlotKey right) => !left.Equals(right);
    }

    [Header("References")]
    [SerializeField] private AudioRegistry audioRegistry;

    [Header("Prefabs")]
    [SerializeField] private AudioSource sfxPrefab;

    [Header("Hierarchy")]
    [SerializeField] private Transform sfxPoolRoot;

    [Header("BGM Sources")]
    [SerializeField] private AudioSource bgmSourceA;
    [SerializeField] private AudioSource bgmSourceB;

    [Header("Mixer")]
    [SerializeField] private AudioMixer audioMixer;
    [SerializeField] private AudioMixerGroup bgmMixerGroup;
    [SerializeField] private AudioMixerGroup sfxMixerGroup;

    [Header("Parameters")]
    [SerializeField] private string masterParameter = "Master";
    [SerializeField] private string bgmParameter = "BGM";
    [SerializeField] private string sfxParameter = "SFX";

    [Header("Default Volume (dB)")]
    [SerializeField] private float defaultMasterDb = 0.0f;
    [SerializeField] private float defaultBgmDb = 0.0f;
    [SerializeField] private float defaultSfxDb = 0.0f;

    private readonly List<SfxVoice> sfxVoices = new();
    private readonly Dictionary<int, SfxVoice> sfxVoicesById = new();
    private readonly Dictionary<LoopSlotKey, int> activeLoopVoiceIdsBySlot = new();
    private readonly Tween[] bgmFadeTweens = new Tween[2];
    private readonly ulong[] bgmFadeOrders = new ulong[2];

    private float masterVolumeDb = 0.0f;
    private float bgmVolumeDb = 0.0f;
    private float sfxVolumeDb = 0.0f;

    private int activeBgmSourceIndex = -1;
    private int nextSfxVoiceId = 1;
    private ulong bgmFadeSequence;
    private string currentBgmName;

    private bool isVolumeLoaded;
    private bool isBgmInitialized;
    private bool isRuntimeInitialized;

    public float MasterVolumeDb
    {
        get
        {
            EnsureRuntimeInitialized();
            return masterVolumeDb;
        }
        set
        {
            EnsureRuntimeInitialized();
            masterVolumeDb = value;
            ApplyMixerVolume(masterParameter, masterVolumeDb);
            SaveDb(MasterKey, masterVolumeDb);
        }
    }

    public float BGMVolumeDb
    {
        get
        {
            EnsureRuntimeInitialized();
            return bgmVolumeDb;
        }
        set
        {
            EnsureRuntimeInitialized();
            bgmVolumeDb = value;
            ApplyMixerVolume(bgmParameter, bgmVolumeDb);
            SaveDb(BgmKey, bgmVolumeDb);
        }
    }

    public float SFXVolumeDb
    {
        get
        {
            EnsureRuntimeInitialized();
            return sfxVolumeDb;
        }
        set
        {
            EnsureRuntimeInitialized();
            sfxVolumeDb = value;
            ApplyMixerVolume(sfxParameter, sfxVolumeDb);
            SaveDb(SfxKey, sfxVolumeDb);
        }
    }

    private void Start() => EnsureRuntimeInitialized();

    public void LoadVolumeSettings()
    {
        masterVolumeDb = LoadDb(MasterKey, masterParameter, defaultMasterDb);
        bgmVolumeDb = LoadDb(BgmKey, bgmParameter, defaultBgmDb);
        sfxVolumeDb = LoadDb(SfxKey, sfxParameter, defaultSfxDb);

        ApplyAllMixerVolumes();
        isVolumeLoaded = true;
    }

    public void ResetVolumesToDefault()
    {
        EnsureRuntimeInitialized();

        masterVolumeDb = defaultMasterDb;
        bgmVolumeDb = defaultBgmDb;
        sfxVolumeDb = defaultSfxDb;

        ApplyAllMixerVolumes();
        SaveAll();
        isVolumeLoaded = true;
    }

    public void SetBGM(string audioName, float fadeDuration = 1f)
    {
        EnsureRuntimeInitialized();

        if (string.IsNullOrEmpty(audioName))
        {
            StopBGM(fadeDuration);
            return;
        }

        if (audioName == currentBgmName && activeBgmSourceIndex >= 0 && GetBgmSource(activeBgmSourceIndex).isPlaying)
            return;

        AudioClip clip = audioRegistry.GetAudioClip(audioName);

        if (clip == null)
        {
            Debug.LogError($"BGM not found: {audioName}", this);
            return;
        }

        currentBgmName = audioName;

        if (HasAnyBgmFade())
        {
            int reuseSourceIndex = GetOldestFadingBgmSourceIndex();
            int otherSourceIndex = GetOtherBgmSourceIndex(reuseSourceIndex);

            StopBgmSourceImmediately(reuseSourceIndex);
            FadeOutBgmSource(otherSourceIndex, fadeDuration);
            PlayBgmSource(reuseSourceIndex, clip, fadeDuration);

            activeBgmSourceIndex = reuseSourceIndex;
            return;
        }

        if (activeBgmSourceIndex >= 0 && GetBgmSource(activeBgmSourceIndex).isPlaying)
        {
            int nextSourceIndex = GetOtherBgmSourceIndex(activeBgmSourceIndex);

            FadeOutBgmSource(activeBgmSourceIndex, fadeDuration);
            PlayBgmSource(nextSourceIndex, clip, fadeDuration);

            activeBgmSourceIndex = nextSourceIndex;
            return;
        }

        int sourceIndex = GetPreferredBgmSourceIndex();
        PlayBgmSource(sourceIndex, clip, fadeDuration);
        activeBgmSourceIndex = sourceIndex;
    }

    public SfxHandle PlayOneShotSFX(string audioName, GameObject parent = null) => PlaySFXInternal(audioName, parent, false, null);

    public SfxHandle PlayLoopSFX(string audioName, GameObject parent = null, string slot = null) => PlaySFXInternal(audioName, parent, true, slot);

    public void StopSFX(SfxHandle handle, float fadeDuration = 0f)
    {
        EnsureRuntimeInitialized();

        if (!TryGetVoice(handle, out SfxVoice voice))
            return;

        StopVoice(voice, fadeDuration);
    }

    public void StopSFX(GameObject parent, string slot, float fadeDuration = 0f)
    {
        EnsureRuntimeInitialized();

        if (string.IsNullOrWhiteSpace(slot))
            return;

        if (!TryGetVoiceBySlot(parent, slot, out SfxVoice voice))
            return;

        StopVoice(voice, fadeDuration);
    }

    public void StopAllSFX(GameObject parent, float fadeDuration = 0f)
    {
        EnsureRuntimeInitialized();

        int ownerId = GetOwnerId(parent);

        for (int i = 0; i < sfxVoices.Count; i++)
        {
            SfxVoice voice = sfxVoices[i];
            RepairVoiceIfNeeded(voice);

            if (!voice.IsActive)
                continue;

            if (GetOwnerId(voice.Owner) != ownerId)
                continue;

            StopVoice(voice, fadeDuration);
        }
    }

    private SfxHandle PlaySFXInternal(string audioName, GameObject parent, bool isLoop, string slot)
    {
        EnsureRuntimeInitialized();

        AudioClip clip = audioRegistry.GetAudioClip(audioName);

        if (clip == null)
        {
            Debug.LogError($"SFX not found: {audioName}", this);
            return SfxHandle.Invalid;
        }

        if (isLoop && !string.IsNullOrWhiteSpace(slot) && TryGetVoiceBySlot(parent, slot, out SfxVoice existingVoice))
        {
            if (existingVoice.AudioName == audioName)
                return new SfxHandle(existingVoice.Id, existingVoice.Version);

            StopVoice(existingVoice, 0f);
        }

        SfxVoice voice = GetAvailableVoice();
        PrepareVoice(voice, audioName, clip, parent, isLoop, slot);

        voice.Source.Play();

        if (!isLoop)
            voice.ReleaseRoutine = StartCoroutine(ReturnVoiceWhenFinished(voice.Id, voice.Version));

        return new SfxHandle(voice.Id, voice.Version);
    }

    private IEnumerator ReturnVoiceWhenFinished(int voiceId, uint version)
    {
        if (!sfxVoicesById.TryGetValue(voiceId, out SfxVoice voice))
            yield break;

        AudioSource source = voice.Source;

        while (source != null && source.isPlaying)
            yield return null;

        if (!TryGetVoice(new SfxHandle(voiceId, version), out SfxVoice activeVoice))
            yield break;

        activeVoice.ReleaseRoutine = null;
        RecycleVoice(activeVoice);
    }

    private SfxVoice GetAvailableVoice()
    {
        for (int i = 0; i < sfxVoices.Count; i++)
        {
            SfxVoice voice = sfxVoices[i];
            RepairVoiceIfNeeded(voice);

            if (!voice.IsActive)
                return voice;
        }

        return CreateVoice();
    }

    private SfxVoice CreateVoice()
    {
        SfxVoice voice = new()
        {
            Id = nextSfxVoiceId++,
            Version = 1,
            Source = CreateSFXSource()
        };

        sfxVoices.Add(voice);
        sfxVoicesById.Add(voice.Id, voice);
        return voice;
    }

    private AudioSource CreateSFXSource()
    {
        AudioSource source = Instantiate(sfxPrefab, sfxPoolRoot);

        if (sfxMixerGroup != null)
            source.outputAudioMixerGroup = sfxMixerGroup;

        source.playOnAwake = false;
        source.loop = false;
        source.volume = 1f;
        return source;
    }

    private void PrepareVoice(SfxVoice voice, string audioName, AudioClip clip, GameObject parent, bool isLoop, string slot)
    {
        if (voice.ReleaseRoutine != null)
        {
            StopCoroutine(voice.ReleaseRoutine);
            voice.ReleaseRoutine = null;
        }

        voice.FadeTween?.Kill();
        voice.FadeTween = null;

        RemoveVoiceSlot(voice);

        voice.AudioName = audioName;
        voice.Owner = parent;
        voice.Slot = isLoop ? slot : null;
        voice.IsLoop = isLoop;
        voice.IsActive = true;

        AttachVoiceToParent(voice, parent);

        voice.Source.Stop();
        voice.Source.clip = clip;
        voice.Source.loop = isLoop;
        voice.Source.volume = 1f;

        RegisterVoiceSlot(voice);
    }

    private void AttachVoiceToParent(SfxVoice voice, GameObject parent)
    {
        Transform target = parent != null ? parent.transform : sfxPoolRoot;
        Transform voiceTransform = voice.Source.transform;

        voiceTransform.SetParent(target, false);
        voiceTransform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        voiceTransform.localScale = Vector3.one;
    }

    private void StopVoice(SfxVoice voice, float fadeDuration)
    {
        RepairVoiceIfNeeded(voice);

        if (!voice.IsActive)
            return;

        if (voice.ReleaseRoutine != null)
        {
            StopCoroutine(voice.ReleaseRoutine);
            voice.ReleaseRoutine = null;
        }

        voice.FadeTween?.Kill();
        voice.FadeTween = null;

        if (voice.Source == null)
        {
            RecycleVoice(voice);
            return;
        }

        if (fadeDuration <= 0f)
        {
            RecycleVoice(voice);
            return;
        }

        voice.FadeTween = voice.Source.DOFade(0f, fadeDuration).OnComplete(() =>
        {
            voice.FadeTween = null;

            if (!voice.IsActive)
                return;

            RecycleVoice(voice);
        });
    }

    private void RecycleVoice(SfxVoice voice)
    {
        if (voice.ReleaseRoutine != null)
        {
            StopCoroutine(voice.ReleaseRoutine);
            voice.ReleaseRoutine = null;
        }

        voice.FadeTween?.Kill();
        voice.FadeTween = null;

        RemoveVoiceSlot(voice);

        if (voice.Source != null)
        {
            voice.Source.Stop();
            voice.Source.clip = null;
            voice.Source.loop = false;
            voice.Source.volume = 1f;

            Transform voiceTransform = voice.Source.transform;
            voiceTransform.SetParent(sfxPoolRoot, false);
            voiceTransform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            voiceTransform.localScale = Vector3.one;
        }

        voice.Owner = null;
        voice.AudioName = null;
        voice.Slot = null;
        voice.IsLoop = false;
        voice.IsActive = false;
        voice.Version++;
    }

    private void RegisterVoiceSlot(SfxVoice voice)
    {
        if (!voice.IsLoop || string.IsNullOrWhiteSpace(voice.Slot))
            return;

        LoopSlotKey key = new(GetOwnerId(voice.Owner), voice.Slot);
        activeLoopVoiceIdsBySlot[key] = voice.Id;
    }

    private void RemoveVoiceSlot(SfxVoice voice)
    {
        if (!voice.IsLoop || string.IsNullOrWhiteSpace(voice.Slot))
            return;

        LoopSlotKey key = new(GetOwnerId(voice.Owner), voice.Slot);

        if (!activeLoopVoiceIdsBySlot.TryGetValue(key, out int voiceId))
            return;

        if (voiceId != voice.Id)
            return;

        activeLoopVoiceIdsBySlot.Remove(key);
    }

    private bool TryGetVoiceBySlot(GameObject parent, string slot, out SfxVoice voice)
    {
        LoopSlotKey key = new(GetOwnerId(parent), slot);

        if (!activeLoopVoiceIdsBySlot.TryGetValue(key, out int voiceId))
        {
            voice = null;
            return false;
        }

        if (!sfxVoicesById.TryGetValue(voiceId, out voice))
        {
            activeLoopVoiceIdsBySlot.Remove(key);
            return false;
        }

        RepairVoiceIfNeeded(voice);

        if (voice.IsActive && voice.IsLoop && voice.Slot == slot && GetOwnerId(voice.Owner) == GetOwnerId(parent))
            return true;

        activeLoopVoiceIdsBySlot.Remove(key);
        voice = null;
        return false;
    }

    private bool TryGetVoice(SfxHandle handle, out SfxVoice voice)
    {
        if (!handle.IsValid)
        {
            voice = null;
            return false;
        }

        if (!sfxVoicesById.TryGetValue(handle.Id, out voice))
            return false;

        RepairVoiceIfNeeded(voice);

        if (!voice.IsActive)
        {
            voice = null;
            return false;
        }

        if (voice.Version != handle.Version)
        {
            voice = null;
            return false;
        }

        return true;
    }

    private void RepairVoiceIfNeeded(SfxVoice voice)
    {
        if (voice.Source != null)
            return;

        if (voice.ReleaseRoutine != null)
        {
            StopCoroutine(voice.ReleaseRoutine);
            voice.ReleaseRoutine = null;
        }

        voice.FadeTween?.Kill();
        voice.FadeTween = null;

        RemoveVoiceSlot(voice);

        voice.Source = CreateSFXSource();
        voice.Owner = null;
        voice.AudioName = null;
        voice.Slot = null;
        voice.IsLoop = false;
        voice.IsActive = false;
        voice.Version++;
    }

    private void EnsureRuntimeInitialized()
    {
        if (isRuntimeInitialized)
            return;

        EnsureSfxPoolRoot();
        InitializeBgmSources();
        EnsureVolumeLoaded();
        isRuntimeInitialized = true;
    }

    private void EnsureSfxPoolRoot()
    {
        if (sfxPoolRoot != null)
            return;

        GameObject root = new("SFX Pool");
        root.transform.SetParent(transform, false);
        sfxPoolRoot = root.transform;
    }

    private void InitializeBgmSources()
    {
        if (isBgmInitialized)
            return;

        ConfigureBgmSource(bgmSourceA);
        ConfigureBgmSource(bgmSourceB);
        isBgmInitialized = true;
    }

    private void ConfigureBgmSource(AudioSource source)
    {
        source.playOnAwake = false;
        source.loop = true;
        source.volume = 0f;

        if (bgmMixerGroup != null)
            source.outputAudioMixerGroup = bgmMixerGroup;
    }

    private void StopBGM(float fadeDuration)
    {
        currentBgmName = null;

        if (HasAnyBgmFade())
        {
            int oldestFadingSourceIndex = GetOldestFadingBgmSourceIndex();
            int otherSourceIndex = GetOtherBgmSourceIndex(oldestFadingSourceIndex);

            StopBgmSourceImmediately(oldestFadingSourceIndex);
            FadeOutBgmSource(otherSourceIndex, fadeDuration);
            activeBgmSourceIndex = -1;
            return;
        }

        if (activeBgmSourceIndex < 0)
            return;

        FadeOutBgmSource(activeBgmSourceIndex, fadeDuration);
        FadeOutBgmSource(GetOtherBgmSourceIndex(activeBgmSourceIndex), fadeDuration);
        activeBgmSourceIndex = -1;
    }

    private AudioSource GetBgmSource(int index) => index == 0 ? bgmSourceA : bgmSourceB;

    private int GetOtherBgmSourceIndex(int index) => index == 0 ? 1 : 0;

    private int GetPreferredBgmSourceIndex()
    {
        if (!bgmSourceA.isPlaying)
            return 0;

        if (!bgmSourceB.isPlaying)
            return 1;

        return 0;
    }

    private bool HasAnyBgmFade() => IsBgmFadeActive(0) || IsBgmFadeActive(1);

    private bool IsBgmFadeActive(int index) => bgmFadeTweens[index] != null && bgmFadeTweens[index].IsActive();

    private int GetOldestFadingBgmSourceIndex()
    {
        bool isFirstFading = IsBgmFadeActive(0);
        bool isSecondFading = IsBgmFadeActive(1);

        if (isFirstFading && isSecondFading)
            return bgmFadeOrders[0] <= bgmFadeOrders[1] ? 0 : 1;

        return isFirstFading ? 0 : 1;
    }

    private void KillBgmFade(int index)
    {
        if (bgmFadeTweens[index] == null)
            return;

        bgmFadeTweens[index].Kill();
        bgmFadeTweens[index] = null;
        bgmFadeOrders[index] = 0;
    }

    private void PlayBgmSource(int index, AudioClip clip, float fadeDuration)
    {
        AudioSource source = GetBgmSource(index);

        KillBgmFade(index);
        source.clip = clip;
        source.loop = true;

        if (fadeDuration <= 0f)
        {
            source.volume = 1f;
            source.Play();
            return;
        }

        source.volume = 0f;
        source.Play();

        bgmFadeOrders[index] = ++bgmFadeSequence;
        bgmFadeTweens[index] = source.DOFade(1f, fadeDuration).OnComplete(() =>
        {
            bgmFadeTweens[index] = null;
            bgmFadeOrders[index] = 0;
        });
    }

    private void FadeOutBgmSource(int index, float fadeDuration)
    {
        AudioSource source = GetBgmSource(index);

        if (!source.isPlaying)
        {
            StopBgmSourceImmediately(index);
            return;
        }

        KillBgmFade(index);

        if (fadeDuration <= 0f)
        {
            StopBgmSourceImmediately(index);
            return;
        }

        bgmFadeOrders[index] = ++bgmFadeSequence;
        bgmFadeTweens[index] = source.DOFade(0f, fadeDuration).OnComplete(() =>
        {
            source.Stop();
            source.clip = null;
            source.volume = 0f;
            bgmFadeTweens[index] = null;
            bgmFadeOrders[index] = 0;
        });
    }

    private void StopBgmSourceImmediately(int index)
    {
        AudioSource source = GetBgmSource(index);

        KillBgmFade(index);
        source.Stop();
        source.clip = null;
        source.volume = 0f;
    }

    private void EnsureVolumeLoaded()
    {
        if (isVolumeLoaded)
            return;

        LoadVolumeSettings();
    }

    private void ApplyAllMixerVolumes()
    {
        ApplyMixerVolume(masterParameter, masterVolumeDb);
        ApplyMixerVolume(bgmParameter, bgmVolumeDb);
        ApplyMixerVolume(sfxParameter, sfxVolumeDb);
    }

    private void ApplyMixerVolume(string parameterName, float db) => audioMixer.SetFloat(parameterName, db);

    private void SaveAll()
    {
        SaveDb(MasterKey, masterVolumeDb);
        SaveDb(BgmKey, bgmVolumeDb);
        SaveDb(SfxKey, sfxVolumeDb);
    }

    private void SaveDb(string key, float db)
    {
        PlayerPrefs.SetFloat(key, db);
        PlayerPrefs.Save();
    }

    private float LoadDb(string key, string parameter, float fallback)
    {
        if (PlayerPrefs.HasKey(key))
            return PlayerPrefs.GetFloat(key);

        if (audioMixer.GetFloat(parameter, out float db))
            return db;

        return fallback;
    }

    private int GetOwnerId(GameObject owner) => owner != null ? owner.GetInstanceID() : 0;
}