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
    
    [SerializeField] private AudioClip _oilCollectedSound;

    [SerializeField] private float maxSpeedInput;
    [SerializeField] private float _minLoopedSoundVolumeMultiplier;
    [SerializeField] private float _maxLoopedSoundVolumeMultiplier;
    [SerializeField] private float _minLoopedSoundPitch = 0.8f;
    [SerializeField] private float _maxLoopedSoundPitch = 1.2f;
    [SerializeField] private float _oilCollectedPitchDelta = 0.1f;
    
    private float defaultLoopedSourceVolume;
    private float defaultOneShotSourcePitch;

    private void Awake()
    {
        defaultLoopedSourceVolume = _loopedAudioSource.volume;
        defaultOneShotSourcePitch = _oneShotAudioSource.pitch;
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
        float loopedSoundVolumeMultiplierBySpeed = Helper.RemapRange(_playerDirectionTracker.GetSpeed(), 0f, maxSpeedInput, _minLoopedSoundVolumeMultiplier, _maxLoopedSoundVolumeMultiplier);
        loopedSoundVolumeMultiplierBySpeed = Mathf.Clamp(loopedSoundVolumeMultiplierBySpeed, _minLoopedSoundVolumeMultiplier, _maxLoopedSoundVolumeMultiplier);
        
        _loopedAudioSource.volume = loopedSoundVolumeMultiplierBySpeed * defaultLoopedSourceVolume;
        
        float loopedSoundPitchBySpeed = Helper.RemapRange(_playerDirectionTracker.GetSpeed(), 0f, maxSpeedInput, _minLoopedSoundPitch, _maxLoopedSoundPitch);
        loopedSoundPitchBySpeed = Mathf.Clamp(loopedSoundPitchBySpeed, _minLoopedSoundPitch, _maxLoopedSoundPitch);
        
        _loopedAudioSource.pitch = loopedSoundPitchBySpeed;
    }
    
    public void PlayOilCollectedSound()
    {
        _oneShotAudioSource.pitch = Random.Range(defaultOneShotSourcePitch - _oilCollectedPitchDelta, defaultOneShotSourcePitch + _oilCollectedPitchDelta);
        _oneShotAudioSource.PlayOneShot(_oilCollectedSound);
    }

    private async void StartAudioDelayed() // This method is called to ensure the audio starts after a short delay, otherwise a short and loud buzz is heard when a new player spawns
    {
        await Task.Delay(500);
        if (_loopedAudioSource != null && !_loopedAudioSource.isPlaying)
        {
            _loopedAudioSource.Play();
        }
        
    }
    
}
