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

        GD.Print("AudioManager initialized");
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
            GD.Print("Background music: OGG looping enabled");
        }
        else if (Stream is AudioStreamWav wavStream)
        {
            wavStream.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
            GD.Print("Background music: WAV looping enabled");
        }
        else if (Stream is AudioStreamMP3 mp3Stream)
        {
            mp3Stream.Loop = true;
            GD.Print("Background music: MP3 looping enabled");
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
            GD.Print("Background music started");
        }
        else
        {
            GD.PrintErr("No background music assigned to AudioManager");
        }
    }

    public void StopBackgroundMusic()
    {
        // Stop background music
        Stop();
        _isMusicPaused = false;
        GD.Print("Background music stopped");
    }

    public void PauseBackgroundMusic()
    {
        // Pause background music
        if (Playing)
        {
            StreamPaused = true;
            _isMusicPaused = true;
            GD.Print("Background music paused");
        }
    }

    public void ResumeBackgroundMusic()
    {
        // Resume paused background music
        if (_isMusicPaused)
        {
            StreamPaused = false;
            _isMusicPaused = false;
            GD.Print("Background music resumed");
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
            GD.Print("Background music restarted from beginning");
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
        GD.Print($"Music volume set to {volumeDb} dB");
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
        // Debug logging for audio state
        GD.Print("=== AudioManager Status ===");
        GD.Print($"Playing: {Playing}");
        GD.Print($"Paused: {_isMusicPaused}");
        GD.Print($"Volume: {VolumeDb} dB");
        GD.Print($"Current Music: {BackgroundMusic?.ResourcePath ?? "None"}");
        GD.Print($"Stream: {Stream?.ResourcePath ?? "None"}");
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