using System.Collections;
using UnityEngine;
using UnityEngine.Audio;
using UnityUtils;
using Random = UnityEngine.Random;

public class SoundManager : PersistentSingleton<SoundManager>
{
    public AudioMixer audioMixer;
    public AudioMixerGroup gameMusicGroup;
    public AudioMixerGroup menuMusicGroup;
    public AudioSource musicAudioSource;
    public AudioSource sfxMenuAudioSource;

    public string masterVolume = "MasterVolume";
    public string musicVolume = "MusicVolume";
    public string sfxVolume = "SFXVolume";

    [Header("Music")]
    [SerializeField] private AudioClip _menuMusic;
    [SerializeField] private AudioClip[] _gameMusic;

    [Header("SFX")]
    public AudioClip gameOverSound;

    [Header("UI")]
    public AudioClip buttonHoverSound;
    public AudioClip buttonClickSound;
    public AudioClip interactSound;

    private Coroutine _musicCoroutine;
    private bool _isPlayingGameMusic;

    private void PlayMusic(AudioClip clip, AudioMixerGroup mixerGroup)
    {
        musicAudioSource.outputAudioMixerGroup = mixerGroup;
        musicAudioSource.clip = clip;
        musicAudioSource.Play();
    }

    public void PlayGameSfx3D(AudioClip clip, Vector3 worldPosition, float volumeScale = 1f)
    {
        AudioSource.PlayClipAtPoint(clip, worldPosition, volumeScale);
    }

    private static float DecibelToLinear(float dB)
    {
        float linear = Mathf.Pow(10.0f, dB / 20.0f);
        return linear;
    }

    public float GetSfxVolume()
    {
        audioMixer.GetFloat(sfxVolume, out var value);
        return value;
    }

    public void PlayMenuSfx(AudioClip clip)
    {
        sfxMenuAudioSource.PlayOneShot(clip);
    }

    public void PlayMenuMusic()
    {
        musicAudioSource.loop = true;

        if (_musicCoroutine != null)
        {
            StopCoroutine(_musicCoroutine);
            _musicCoroutine = null;
        }

        if (musicAudioSource.clip != _menuMusic)
        {
            PlayMusic(_menuMusic, menuMusicGroup);
        }
        else if (!musicAudioSource.isPlaying)
        {
            musicAudioSource.Play();
        }
    }

    public void PlayGameMusic()
    {
        _musicCoroutine ??= StartCoroutine(PlayGameMusicPlaylist());
    }

    public void StopMusic()
    {
        if (_musicCoroutine != null)
        {
            StopCoroutine(_musicCoroutine);
            _musicCoroutine = null;
        }

        musicAudioSource.Stop();
    }

    private IEnumerator PlayGameMusicPlaylist()
    {
        musicAudioSource.loop = false;
        int startingIndex = Random.Range(0, _gameMusic.Length);

        //1.Loop through each AudioClip
        for (int i = startingIndex; i < _gameMusic.Length; i++)
        {
            //3.Play Audio
            PlayMusic(_gameMusic[i], gameMusicGroup);

            //4.Wait for it to finish playing
            while (musicAudioSource.isPlaying)
            {
                yield return null;
            }

            if (i == _gameMusic.Length - 1)
            {
                i = -1;
            }
        }
    }

    public void SetMasterVolume(float volume)
    {
        audioMixer.SetFloat(masterVolume, volume);
    }

    public void SetSfxVolume(float volume)
    {
        audioMixer.SetFloat(sfxVolume, volume);
    }

    public void SetMusicVolume(float volume)
    {
        audioMixer.SetFloat(musicVolume, volume);
    }

    public void MuteAll()
    {
        audioMixer.SetFloat(masterVolume, -80);
    }

    public void MuteSfx()
    {
        audioMixer.SetFloat(sfxVolume, -80);
    }

    public void MuteMusic()
    {
        audioMixer.SetFloat(musicVolume, -80);
    }

    public void GameOver()
    {
        sfxMenuAudioSource.PlayOneShot(gameOverSound);
    }

    #region UI Sounds

    public void ButtonHover()
    {
        sfxMenuAudioSource.PlayOneShot(buttonHoverSound);
    }

    public void ButtonClick()
    {
        sfxMenuAudioSource.PlayOneShot(buttonClickSound);
    }

    public void Interact()
    {
        sfxMenuAudioSource.PlayOneShot(interactSound);
    }

    #endregion
}