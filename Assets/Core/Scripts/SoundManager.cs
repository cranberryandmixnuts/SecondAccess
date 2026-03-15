using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.Audio;
using Sirenix.OdinInspector;

[RequireComponent(typeof(AudioSource))]
public sealed class SoundManager : Singleton<SoundManager, GlobalScope>
{

    [Header("Storage")]
    [SerializeField, Required] private SoundStorage storage;

    [Header("Audio Mixer Groups")]
    [SerializeField, Required] private AudioMixerGroup bgmMixerGroup;
    [SerializeField, Required] private AudioMixerGroup sfxMixerGroup;

    [Header("BGM")]
    [SerializeField] private float defaultBgmFadeTime = 1f;
    [SerializeField, Range(0f, 1f)] private float bgmMasterVolume = 1f;
    [SerializeField, Required] private AudioSource bgmSource;

    private readonly Dictionary<BgmId, SoundStorage.BgmEntry> bgmLookup = new();
    private readonly Dictionary<SfxId, SoundStorage.SfxEntry> sfxLookup = new();

    private Sequence bgmSequence;
    private BgmId currentBgmId = BgmId.None;

    public void ChangeBgm(BgmId id, float fadeTime = -1f)
    {
        bool isfucked = true;
        if(isfucked) return;

        float t = fadeTime >= 0f ? fadeTime : defaultBgmFadeTime;

        if (bgmSequence != null && bgmSequence.IsActive())
            bgmSequence.Kill(false);

        if (id == BgmId.None)
        {
            bgmSequence = DOTween.Sequence();

            if (bgmSource.isPlaying)
                bgmSequence.Append(bgmSource.DOFade(0f, t));
            else
                bgmSource.volume = 0f;

            bgmSequence.AppendCallback(() =>
            {
                bgmSource.Stop();
                bgmSource.clip = null;
                currentBgmId = BgmId.None;
            });

            return;
        }

        bgmLookup.TryGetValue(id, out SoundStorage.BgmEntry entry);

        if (currentBgmId == id && bgmSource.isPlaying && bgmSource.clip == entry.Clip)
            return;

        bgmSequence = DOTween.Sequence();

        if (bgmSource.isPlaying)
            bgmSequence.Append(bgmSource.DOFade(0f, t));
        else
            bgmSource.volume = 0f;

        bgmSequence.AppendCallback(() =>
        {
            bgmSource.outputAudioMixerGroup = bgmMixerGroup;
            bgmSource.loop = entry.Loop;
            bgmSource.clip = entry.Clip;
            bgmSource.Play();
            currentBgmId = id;
        });

        bgmSequence.Append(bgmSource.DOFade(entry.Volume * bgmMasterVolume, t));
    }

    public void PlaySfx(SfxId id, UnityEngine.Object target = null)
    {
        if (id == SfxId.None)
            return;

        sfxLookup.TryGetValue(id, out SoundStorage.SfxEntry entry);
        Vector3 worldPosition = transform.position;

        Transform parent = null;
        if (target is Transform tr)
        {
            parent = null;
            worldPosition = tr.position;
        }
        else if (target is GameObject go)
        {
            parent = go.transform;
            worldPosition = go.transform.position;
        }

        GameObject sfxObject = new($"SFX_{id}");
        if (parent != null)
        {
            sfxObject.transform.SetParent(parent, false);
            sfxObject.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        }
        else
        {
            sfxObject.transform.SetPositionAndRotation(worldPosition, Quaternion.identity);
        }

        AudioSource src = sfxObject.AddComponent<AudioSource>();
        src.playOnAwake = false;
        src.clip = entry.Clip;
        src.loop = false;
        src.volume = entry.Volume;

        src.spatialBlend = entry.SpatialBlend;
        src.minDistance = entry.MinDistance;
        src.maxDistance = entry.MaxDistance;

        src.outputAudioMixerGroup = sfxMixerGroup;

        src.Play();

        float life = entry.Clip.length;
        Destroy(sfxObject, life);
    }

    private void BuildLookupTables()
    {
        bgmLookup.Clear();
        sfxLookup.Clear();

        SoundStorage.BgmEntry[] bgms = storage.BgmEntries;
        if (bgms != null)
        {
            for (int i = 0; i < bgms.Length; i++)
            {
                SoundStorage.BgmEntry e = bgms[i];
                if (e == null)
                    continue;

                bgmLookup.Add(e.Id, e);
            }
        }

        SoundStorage.SfxEntry[] sfxs = storage.SfxEntries;
        if (sfxs != null)
        {
            for (int i = 0; i < sfxs.Length; i++)
            {
                SoundStorage.SfxEntry e = sfxs[i];
                if (e == null)
                    continue;

                sfxLookup.Add(e.Id, e);
            }
        }
    }

    protected override void SingletonAwake()
    {
        bgmSource.playOnAwake = false;
        bgmSource.loop = true;
        bgmSource.spatialBlend = 0f;
        bgmSource.outputAudioMixerGroup = bgmMixerGroup;

        BuildLookupTables();
    }

}