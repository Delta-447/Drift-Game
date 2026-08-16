using UnityEngine;

/// <summary>
/// Loads the generated SFX from Resources/Audio and plays them.
/// Engine pitch follows speed, skid fades in while drifting.
/// GameManager creates this automatically - no scene setup needed.
/// </summary>
public class AudioManager : MonoBehaviour
{
    [Header("Volumes")]
    [Range(0f, 1f)] public float engineVolume = 0.14f;
    [Range(0f, 1f)] public float skidVolume = 0.55f;
    [Range(0f, 1f)] public float coinVolume = 0.6f;
    [Range(0f, 1f)] public float musicVolume = 0.55f;
    [Range(0f, 1f)] public float oneShotVolume = 0.9f;

    [Header("Engine pitch range (maps to min/max speed)")]
    public float enginePitchMin = 0.75f;
    public float enginePitchMax = 2.1f;
    [Tooltip("How quickly engine pitch chases speed. Lower = smoother.")]
    public float enginePitchSmoothing = 3.5f;

    AudioClip engineClip, engineEvClip, skidClip, scrapeClip, crashClip, whooshClip, slipClip, tapClip, coinClip, musicClip, timeTravelClip, popClip;
    AudioClip gameMusicClip, beepClip, goClip, powerUpClip, boingClip, smashClip, finishWhooshClip;

    // --- music crossfading
    AudioClip queuedMusic;      // waiting for the current track to fade out
    float musicFade = 1f;       // 0 = silent, 1 = full
    bool musicSilent;           // held down between tracks
    public float musicFadeOutSpeed = 1.3f;
    public float musicFadeInSpeed = 1.1f;
    AudioSource engineSource, skidSource, oneShotSource, musicSource;
    CarController car;
    bool driving;
    [Tooltip("Silences the music without losing the volume setting - the intro " +
             "unmutes it so the theme starts exactly on cue.")]
    public bool musicMuted = true;

    void Awake()
    {
        engineClip = Resources.Load<AudioClip>("Audio/engine_loop");
        engineEvClip = Resources.Load<AudioClip>("Audio/engine_ev");
        scrapeClip = Resources.Load<AudioClip>("Audio/scrape_loop");
        timeTravelClip = Resources.Load<AudioClip>("Audio/time_travel");
        skidClip = Resources.Load<AudioClip>("Audio/skid_loop");
        crashClip = Resources.Load<AudioClip>("Audio/crash");
        whooshClip = Resources.Load<AudioClip>("Audio/whoosh");
        slipClip = Resources.Load<AudioClip>("Audio/slip");
        tapClip = Resources.Load<AudioClip>("Audio/tap");
        coinClip = Resources.Load<AudioClip>("Audio/coin");
        popClip = Resources.Load<AudioClip>("Audio/ui_pop");
        powerUpClip = Resources.Load<AudioClip>("Audio/powerup");
        boingClip = Resources.Load<AudioClip>("Audio/boing");
        smashClip = Resources.Load<AudioClip>("Audio/smash");
        finishWhooshClip = Resources.Load<AudioClip>("Audio/finish_whoosh");
        beepClip = Resources.Load<AudioClip>("Audio/count_beep");
        goClip = Resources.Load<AudioClip>("Audio/count_go");
        // the driving theme - a trimmed wav so the loop has no gap
        gameMusicClip = Resources.Load<AudioClip>("Audio/game_music");
        // the uploaded race track is the main theme; the synth loop is a fallback
        musicClip = Resources.Load<AudioClip>("Audio/intro_music");
        if (musicClip == null) musicClip = Resources.Load<AudioClip>("Audio/music_loop");

        engineSource = MakeSource(engineClip, true);
        skidSource = MakeSource(skidClip, true);
        oneShotSource = MakeSource(null, false);
        musicSource = MakeSource(musicClip, true);
        if (musicClip != null)
        {
            musicSource.volume = 0f;   // stays silent until the intro starts it
            musicSource.loop = true;
            musicSource.Play();
        }

        // start silent; loops start on demand
        engineSource.volume = 0f;
        skidSource.volume = 0f;
    }

    AudioSource MakeSource(AudioClip clip, bool loop)
    {
        var src = gameObject.AddComponent<AudioSource>();
        src.clip = clip;
        src.loop = loop;
        src.playOnAwake = false;
        src.spatialBlend = 0f;
        return src;
    }

    public void SetCar(CarController c)
    {
        car = c;
    }

    /// <summary>Switch between combustion/tire sounds and hover-car equivalents.</summary>
    public void SetEngineStyle(bool futuristic)
    {
        SwapLoop(engineSource, futuristic && engineEvClip != null ? engineEvClip : engineClip);
        SwapLoop(skidSource, futuristic && scrapeClip != null ? scrapeClip : skidClip);
    }

    static void SwapLoop(AudioSource src, AudioClip target)
    {
        if (src == null || src.clip == target) return;
        bool wasPlaying = src.isPlaying;
        src.clip = target;
        if (wasPlaying) src.Play();
    }

    public void StartDriving()
    {
        driving = true;
        if (car != null)
        {
            // begin at the right pitch rather than sweeping up from idle
            float t = Mathf.InverseLerp(car.baseSpeed, car.maxSpeed, car.CurrentSpeed);
            engineSource.pitch = Mathf.Lerp(enginePitchMin, enginePitchMax, t);
        }
        if (engineClip != null && !engineSource.isPlaying) engineSource.Play();
        if (skidClip != null && !skidSource.isPlaying) skidSource.Play();
    }

    public void StopDriving()
    {
        driving = false;
    }

    public void PlayCrash() { PlayOneShot(crashClip, 1f); }
    public void PlayNearMiss() { PlayOneShot(whooshClip, 0.8f); }
    public void PlayOilSlip() { PlayOneShot(slipClip, 0.9f); }
    public void PlayTap() { PlayOneShot(tapClip, 0.7f); }
    public void PlayPop() { PlayOneShot(popClip, 0.8f); }
    public void PlayPowerUp() { PlayOneShot(powerUpClip, 0.95f); }
    public void PlayBoing() { PlayOneShot(boingClip, 0.85f); }
    public void PlaySmash() { PlayOneShot(smashClip, 0.9f); }
    public void PlayFinishWhoosh() { PlayOneShot(finishWhooshClip, 1.2f); }
    public void PlayCountBeep() { PlayOneShot(beepClip, 0.85f); }
    public void PlayCountGo() { PlayOneShot(goClip, 1f); }
    public void PlayTimeTravel() { PlayOneShot(timeTravelClip, 1f); }

    public void PlayCoin()
    {
        if (coinClip != null) oneShotSource.PlayOneShot(coinClip, coinVolume);
    }

    void PlayOneShot(AudioClip clip, float scale)
    {
        if (clip != null) oneShotSource.PlayOneShot(clip, oneShotVolume * scale);
    }

    void Update()
    {
        // unscaled so volume fades still work while the game is paused
        float dt = Time.unscaledDeltaTime;

        float targetEngine = 0f;
        float targetSkid = 0f;

        if (driving && car != null)
        {
            float speedT = Mathf.InverseLerp(car.baseSpeed, car.maxSpeed, car.CurrentSpeed);
            float targetPitch = Mathf.Lerp(enginePitchMin, enginePitchMax, speedT);
            // ease toward the target so speed jumps (boosts, revives) glide
            // instead of stepping audibly
            engineSource.pitch = Mathf.Lerp(engineSource.pitch, targetPitch,
                1f - Mathf.Exp(-enginePitchSmoothing * dt));
            targetEngine = engineVolume;

            // starts as soon as the car steps out, not only on a scoring drift
            if (car.IsSliding || car.IsDrifting || car.IsSpinning)
            {
                float slide = Mathf.Clamp01(Mathf.Abs(car.LateralVelocity) / car.maxLateralSpeed);
                // quiet scrub at the start of a slide, full squeal in a drift
                targetSkid = skidVolume * Mathf.Lerp(0.45f, 1f, slide * 2f);
                skidSource.pitch = Mathf.Lerp(0.9f, 1.25f, slide);
            }
        }

        engineSource.volume = Mathf.MoveTowards(engineSource.volume, targetEngine, 2.5f * dt);
        skidSource.volume = Mathf.MoveTowards(skidSource.volume, targetSkid, 4f * dt);

        TickMusicFade(dt);
        musicSource.volume = musicMuted ? 0f : musicVolume * musicFade;
    }

    /// <summary>Fades the current track down, swaps it, then fades back up.</summary>
    void TickMusicFade(float dt)
    {
        if (queuedMusic != null)
        {
            musicFade = Mathf.MoveTowards(musicFade, 0f, musicFadeOutSpeed * dt);
            if (musicFade > 0f) return;

            if (queuedMusic.loadState != AudioDataLoadState.Loaded)
            {
                queuedMusic.LoadAudioData();
            }
            musicSource.Stop();
            musicSource.clip = queuedMusic;
            musicSource.loop = true;
            musicSource.time = 0f;
            musicSource.Play();
            queuedMusic = null;
            return;
        }

        float target = musicSilent ? 0f : 1f;
        float speed = target > musicFade ? musicFadeInSpeed : musicFadeOutSpeed;
        musicFade = Mathf.MoveTowards(musicFade, target, speed * dt);
    }

    /// <summary>Crossfade to a different track. Ignored if it is already playing.</summary>
    void SwitchMusic(AudioClip clip)
    {
        if (clip == null) return;
        if (queuedMusic == clip) return;
        musicSilent = false;
        if (queuedMusic == null && musicSource.clip == clip && musicSource.isPlaying)
        {
            musicMuted = false;
            return;   // already the right track, just fade it back up
        }
        queuedMusic = clip;
        musicMuted = false;
    }

    /// <summary>The driving theme, started when the countdown ends.</summary>
    public void PlayGameMusic() { SwitchMusic(gameMusicClip); }

    /// <summary>Back to the title/lobby theme.</summary>
    public void PlayMenuMusic() { SwitchMusic(musicClip); }

    /// <summary>Fade the music out and keep it out until something calls for a track.</summary>
    public void FadeMusicOut() { musicSilent = true; }

    /// <summary>Starts the theme from the top (called by the title intro).</summary>
    public bool RestartMusic()
    {
        musicMuted = false;
        musicSilent = false;
        musicFade = 1f;
        queuedMusic = null;
        if (musicSource == null || musicSource.clip == null) return false;

        // an mp3 may still be decoding on the first frames; force it in
        if (musicSource.clip.loadState != AudioDataLoadState.Loaded)
        {
            musicSource.clip.LoadAudioData();
        }

        musicSource.Stop();
        musicSource.time = 0f;
        musicSource.loop = true;
        musicSource.volume = musicVolume;
        musicSource.Play();
        return true;
    }
}
