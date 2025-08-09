using System;
using System.Threading.Tasks;
using KBCore.Refs;
using UnityEngine;
using Random = UnityEngine.Random;

public class PlayerSound : ValidatedMonoBehaviour
{
    [SerializeField, Self] PlayerDirectionTracker _playerDirectionTracker;
    [SerializeField] private AudioSource _loopedAudioSource;
    [SerializeField] private AudioSource _oneShotAudioSource;

    [SerializeField] private float maxSpeedInput;
    [SerializeField] private float _minLoopedSoundVolumeMultiplier;
    [SerializeField] private float _maxLoopedSoundVolumeMultiplier;
    [SerializeField] private float _minLoopedSoundPitch = 0.8f;
    [SerializeField] private float _maxLoopedSoundPitch = 1.2f;
    [SerializeField] private float _oilCollectedPitchDelta = 0.1f;

    private float defaultLoopedSourceVolume;

    private void Awake()
    {
        defaultLoopedSourceVolume = _loopedAudioSource.volume;
    }

    private void OnEnable()
    {
        StartAudioDelayed();
    }

    private void OnDisable()
    {
        if (_loopedAudioSource != null && _loopedAudioSource.isPlaying)
        {
            _loopedAudioSource.Stop();
        }
    }

    private void FixedUpdate()
    {
        float loopedSoundVolumeMultiplierBySpeed = Helper.RemapRange(_playerDirectionTracker.GetSpeed(), 0f,
            maxSpeedInput, _minLoopedSoundVolumeMultiplier, _maxLoopedSoundVolumeMultiplier);
        loopedSoundVolumeMultiplierBySpeed = Mathf.Clamp(loopedSoundVolumeMultiplierBySpeed,
            _minLoopedSoundVolumeMultiplier, _maxLoopedSoundVolumeMultiplier);

        float targetVolume = loopedSoundVolumeMultiplierBySpeed * defaultLoopedSourceVolume;
        float lerpedVolume = Mathf.Lerp(_loopedAudioSource.volume, targetVolume, Time.fixedTime * 0.1f);
        _loopedAudioSource.volume = lerpedVolume;


        float loopedSoundPitchBySpeed = Helper.RemapRange(_playerDirectionTracker.GetSpeed(), 0f, maxSpeedInput,
            _minLoopedSoundPitch, _maxLoopedSoundPitch);
        loopedSoundPitchBySpeed = Mathf.Clamp(loopedSoundPitchBySpeed, _minLoopedSoundPitch, _maxLoopedSoundPitch);

        float lerpedPitch = Mathf.Lerp(_loopedAudioSource.pitch, loopedSoundPitchBySpeed, Time.fixedTime * 0.1f);
        _loopedAudioSource.pitch = lerpedPitch;
    }

    private async void
        StartAudioDelayed() // This method is called to ensure the audio starts after a short delay, otherwise a short and loud buzz is heard when a new player spawns
    {
        await Task.Delay(500);
        if (_loopedAudioSource != null && !_loopedAudioSource.isPlaying)
        {
            _loopedAudioSource.Play();
        }
    }

    public void PlayOilCollectedSound()
    {
        if (_oneShotAudioSource)
        {
            _oneShotAudioSource.pitch = Random.Range(1f - _oilCollectedPitchDelta, 1f + _oilCollectedPitchDelta);
            _oneShotAudioSource.PlayOneShot(_oneShotAudioSource.clip);
        }
    }
}