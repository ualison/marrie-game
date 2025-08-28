using Godot;

// Audio management system for background music and sound effects
public partial class AudioManager : AudioStreamPlayer2D
{
    #region Singleton
    public static AudioManager Instance { get; private set; }
    #endregion

    #region Exported Properties
    [Export] public AudioStream BackgroundMusic;
    [Export] public float MusicVolume = 0.0f; // 0 dB = normal volume
    [Export] public bool StartMusicOnReady = true;
    #endregion

    #region Audio State
    private bool _isMusicPaused = false;
    private float _originalVolume;
    #endregion

    #region Initialization
    public override void _Ready()
    {
        // Setup singleton
        if (Instance == null)
        {
            Instance = this;
            ProcessMode = ProcessModeEnum.Always; // Persist through scene changes
        }
        else if (Instance != this)
        {
            QueueFree();
            return;
        }

        // Add to group for GameManager to find
        AddToGroup("background_music");
        
        // Configure audio settings
        SetupAudioConfiguration();
        
        // Start background music if enabled
        if (StartMusicOnReady && BackgroundMusic != null)
        {
            PlayBackgroundMusic();
        }

        // Debug level - system initialization is routine
        Log.Debug("AudioManager initialized", "AudioManager");
    }

    private void SetupAudioConfiguration()
    {
        // Configure basic audio settings
        VolumeDb = MusicVolume;
        _originalVolume = MusicVolume;
        
        // Set the background music stream
        if (BackgroundMusic != null)
        {
            Stream = BackgroundMusic;
            ConfigureLooping();
        }
    }

    private void ConfigureLooping()
    {
        // Enable looping based on audio format
        if (Stream is AudioStreamOggVorbis oggStream)
        {
            oggStream.Loop = true;
            Log.Audio("Background music: OGG looping enabled", "AudioManager");
        }
        else if (Stream is AudioStreamWav wavStream)
        {
            wavStream.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
            Log.Audio("Background music: WAV looping enabled", "AudioManager");
        }
        else if (Stream is AudioStreamMP3 mp3Stream)
        {
            mp3Stream.Loop = true;
            Log.Audio("Background music: MP3 looping enabled", "AudioManager");
        }
    }
    #endregion

    #region Music Control
    public void PlayBackgroundMusic()
    {
        // Start playing background music from beginning
        if (BackgroundMusic != null)
        {
            Stream = BackgroundMusic;
            ConfigureLooping();
            Stop(); // Ensure we start from beginning
            Play();
            _isMusicPaused = false;
            // Debug level - routine audio operations
            Log.Debug("Background music started", "AudioManager");
        }
        else
        {
            Log.Error("No background music assigned to AudioManager", "AudioManager");
        }
    }

    public void StopBackgroundMusic()
    {
        // Stop background music
        Stop();
        _isMusicPaused = false;
        Log.Audio("Background music stopped", "AudioManager");
    }

    public void PauseBackgroundMusic()
    {
        // Pause background music
        if (Playing)
        {
            StreamPaused = true;
            _isMusicPaused = true;
            Log.Audio("Background music paused", "AudioManager");
        }
    }

    public void ResumeBackgroundMusic()
    {
        // Resume paused background music
        if (_isMusicPaused)
        {
            StreamPaused = false;
            _isMusicPaused = false;
            Log.Audio("Background music resumed", "AudioManager");
        }
    }

    public void RestartBackgroundMusic()
    {
        // Restart music from beginning (for game restarts)
        if (BackgroundMusic != null)
        {
            Stop();
            Play();
            _isMusicPaused = false;
            Log.Audio("Background music restarted from beginning", "AudioManager");
        }
    }

    public void SetBackgroundMusic(AudioStream newMusic)
    {
        // Change background music
        bool wasPlaying = Playing;
        
        BackgroundMusic = newMusic;
        Stream = newMusic;
        
        if (newMusic != null)
        {
            ConfigureLooping();
            if (wasPlaying || StartMusicOnReady)
            {
                PlayBackgroundMusic();
            }
        }
    }
    #endregion

    #region Volume Control
    public void SetMusicVolume(float volumeDb)
    {
        // Set music volume in decibels
        MusicVolume = volumeDb;
        VolumeDb = volumeDb;
        _originalVolume = volumeDb;
        Log.Audio($"Music volume set to {volumeDb} dB", "AudioManager");
    }

    public void FadeOut(float duration = 1.0f)
    {
        // Fade out music over specified duration
        var tween = CreateTween();
        tween.TweenProperty(this, "volume_db", -80.0f, duration);
        tween.TweenCallback(Callable.From(StopBackgroundMusic));
    }

    public void FadeIn(float duration = 1.0f)
    {
        // Fade in music over specified duration
        VolumeDb = -80.0f;
        if (!Playing) PlayBackgroundMusic();
        
        var tween = CreateTween();
        tween.TweenProperty(this, "volume_db", _originalVolume, duration);
    }
    #endregion

    #region Public API

    public bool IsPaused() => _isMusicPaused;
    public float GetCurrentVolume() => VolumeDb;
    public AudioStream GetCurrentMusic() => BackgroundMusic;
    #endregion

    #region Debug
    public void LogStatus()
    {
        // Only log audio status in debug builds
        #if DEBUG
        if (Log.IsCategoryEnabled(MarrieGame.Core.Logging.LogCategory.Debug))
        {
            Log.Debug("=== AudioManager Status ===", "AudioManager");
            Log.Debug($"Playing: {Playing}", "AudioManager");
            Log.Debug($"Paused: {_isMusicPaused}", "AudioManager");
            Log.Debug($"Volume: {VolumeDb} dB", "AudioManager");
            Log.Debug($"Current Music: {BackgroundMusic?.ResourcePath ?? "None"}", "AudioManager");
            Log.Debug($"Stream: {Stream?.ResourcePath ?? "None"}", "AudioManager");
        }
        #endif
    }
    #endregion

    #region Cleanup
    public override void _ExitTree()
    {
        // Clean up singleton reference
        if (Instance == this)
        {
            Instance = null;
        }
    }
    #endregion
}