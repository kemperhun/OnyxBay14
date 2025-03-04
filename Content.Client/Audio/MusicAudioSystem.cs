using System.Threading;
using Content.Client.Gameplay;
using Content.Client.GameTicking.Managers;
using Content.Client.Lobby;
using Content.Shared.CCVar;
using JetBrains.Annotations;
using Robust.Client;
using Robust.Client.GameObjects;
using Robust.Client.Player;
using Robust.Client.ResourceManagement;
using Robust.Client.State;
using Robust.Shared.Audio;
using Robust.Shared.Configuration;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Timer = Robust.Shared.Timing.Timer;

namespace Content.Client.Audio;

[UsedImplicitly]
public sealed class MusicAudioSystem : EntitySystem
{
    private readonly AudioParams _ambientParams = new(-10f, 1, "Master", 0, 0, 0, false, 0f);
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly IBaseClient _client = default!;
    [Dependency] private readonly IConfigurationManager _configManager = default!;
    [Dependency] private readonly ClientGameTicker _gameTicker = default!;
    private readonly AudioParams _lobbyParams = new(-5f, 1, "Master", 0, 0, 0, false, 0f);
    [Dependency] private readonly IPlayerManager _playMan = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly IRobustRandom _robustRandom = default!;
    [Dependency] private readonly IStateManager _stateManager = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private AudioSystem.PlayingStream? _ambientStream;
    private AudioSystem.PlayingStream? _lobbyStream;

    /// <summary>
    ///     What the ambience has been set to.
    /// </summary>
    private SoundCollectionPrototype? _currentCollection;


    /// <summary>
    ///     What is currently playing.
    /// </summary>
    private SoundCollectionPrototype? _playingCollection;

    private SoundCollectionPrototype _spaceAmbience = default!;
    private SoundCollectionPrototype _stationAmbience = default!;
    private CancellationTokenSource _timerCancelTokenSource = new();
    private float _ambienceMusicVolume;
    private bool _ambienceMusicEnabled;
    private bool _lobbyMusicEnabled;

    public override void Initialize()
    {
        base.Initialize();

        _stationAmbience = _prototypeManager.Index<SoundCollectionPrototype>("StationAmbienceBase");
        _spaceAmbience = _prototypeManager.Index<SoundCollectionPrototype>("SpaceAmbienceBase");
        _currentCollection = _stationAmbience;

        _ambienceMusicEnabled = _configManager.GetCVar(CCVars.AmbienceMusicEnabled);
        _ambienceMusicVolume = _configManager.GetCVar(CCVars.AmbienceMusicVolume);
        _lobbyMusicEnabled = _configManager.GetCVar(CCVars.LobbyMusicEnabled);

        // TOOD: Ideally audio loading streamed better / we have more robust audio but this is quite annoying
        var cache = IoCManager.Resolve<IResourceCache>();

        foreach (var audio in _spaceAmbience.PickFiles)
        {
            cache.GetResource<AudioResource>(audio.ToString());
        }

        foreach (var audio in _stationAmbience.PickFiles)
        {
            cache.GetResource<AudioResource>(audio.ToString());
        }

        _configManager.OnValueChanged(CCVars.AmbienceMusicVolume, AmbienceMusicVolumeCVarChanged);
        _configManager.OnValueChanged(CCVars.LobbyMusicEnabled, LobbyMusicCVarChanged);
        _configManager.OnValueChanged(CCVars.AmbienceMusicEnabled, AmbienceMusicCVarChanged);

        SubscribeLocalEvent<PlayerAttachedEvent>(OnPlayerAttached);
        SubscribeLocalEvent<EntParentChangedMessage>(EntParentChanged);
        SubscribeLocalEvent<PlayerDetachedEvent>(OnPlayerDetached);

        _stateManager.OnStateChanged += StateManagerOnStateChanged;

        _client.PlayerJoinedServer += OnJoin;
        _client.PlayerLeaveServer += OnLeave;

        _gameTicker.LobbyStatusUpdated += LobbySongReceived;
    }

    // I wish an audio stream had OnDone event.
    public override void Update(float frameTime)
    {
        switch (_stateManager.CurrentState)
        {
            case GameplayState:
                if (_ambientStream is null || _ambientStream.Done)
                    StartAmbience();
                break;
            case LobbyState:
                if (_lobbyStream is null || _lobbyStream.Done)
                    StartLobbyMusic();
                break;
            default:
                return;
        }
    }

    private void OnPlayerAttached(PlayerAttachedEvent ev)
    {
        if (!TryComp<TransformComponent>(ev.Entity, out var xform))
            return;

        CheckAmbience(xform);
    }

    private void OnPlayerDetached(PlayerDetachedEvent ev)
    {
        EndAmbience();
    }

    public override void Shutdown()
    {
        base.Shutdown();

        _configManager.UnsubValueChanged(CCVars.AmbienceVolume, AmbienceMusicVolumeCVarChanged);
        _configManager.UnsubValueChanged(CCVars.LobbyMusicEnabled, LobbyMusicCVarChanged);
        _configManager.UnsubValueChanged(CCVars.AmbienceMusicEnabled, AmbienceMusicCVarChanged);

        _stateManager.OnStateChanged -= StateManagerOnStateChanged;

        _client.PlayerJoinedServer -= OnJoin;
        _client.PlayerLeaveServer -= OnLeave;

        _gameTicker.LobbyStatusUpdated -= LobbySongReceived;

        EndAmbience();
        EndLobbyMusic();
    }

    private void CheckAmbience(TransformComponent xform)
    {
        if (xform.GridUid != null)
        {
            if (_currentCollection == _stationAmbience)
                return;
            ChangeAmbience(_stationAmbience);
        }
        else
            ChangeAmbience(_spaceAmbience);
    }

    private void EntParentChanged(ref EntParentChangedMessage message)
    {
        if (_playMan.LocalPlayer is null || _playMan.LocalPlayer.ControlledEntity != message.Entity ||
            !_timing.IsFirstTimePredicted)
            return;

        // Check if we traversed to grid.
        CheckAmbience(message.Transform);
    }

    private void ChangeAmbience(SoundCollectionPrototype newAmbience)
    {
        if (_currentCollection == newAmbience)
            return;
        _timerCancelTokenSource.Cancel();
        _currentCollection = newAmbience;
        _timerCancelTokenSource = new CancellationTokenSource();
        Timer.Spawn(1500, () =>
        {
            // If we are not in gameplay, don't call StartAmbience because of player movement
            if (_playingCollection == _currentCollection || _stateManager.CurrentState is not GameplayState)
                return;
            EndAmbience();
            StartAmbience();
        }, _timerCancelTokenSource.Token);
    }

    private void StateManagerOnStateChanged(StateChangedEventArgs args)
    {
        EndAmbience();

        switch (args.NewState)
        {
            case LobbyState:
                StartLobbyMusic();
                return;
            case GameplayState:
                StartAmbience();
                break;
        }

        EndLobbyMusic();
    }

    private void OnJoin(object? sender, PlayerEventArgs args)
    {
        if (_stateManager.CurrentState is LobbyState)
        {
            EndAmbience();
            StartLobbyMusic();
        }
        else
        {
            EndLobbyMusic();
            StartAmbience();
        }
    }

    private void OnLeave(object? sender, PlayerEventArgs args)
    {
        EndAmbience();
        EndLobbyMusic();
    }

    private void AmbienceMusicVolumeCVarChanged(float volume)
    {
        _ambienceMusicVolume = volume;

        if (_stateManager.CurrentState is GameplayState)
            StartAmbience();
        else
            EndAmbience();
    }

    private void StartAmbience()
    {
        EndAmbience();

        if (_currentCollection == null || !_ambienceMusicEnabled)
            return;

        _playingCollection = _currentCollection;

        var file = _robustRandom.Pick(_currentCollection.PickFiles).ToString();
        _ambientStream = _audio.PlayGlobal(file, Filter.Local(),
            _ambientParams.WithVolume(_ambientParams.Volume + _ambienceMusicVolume)) as AudioSystem.PlayingStream;
    }

    private void EndAmbience()
    {
        _playingCollection = null;
        _ambientStream?.Stop();
        _ambientStream = null;
    }

    private void AmbienceMusicCVarChanged(bool enabled)
    {
        _ambienceMusicEnabled = enabled;

        if (_currentCollection == null)
            return;

        if (enabled && _stateManager.CurrentState is GameplayState)
            StartAmbience();
        else
            EndAmbience();
    }

    private void LobbyMusicCVarChanged(bool musicEnabled)
    {
        _lobbyMusicEnabled = musicEnabled;

        if (!musicEnabled)
            EndLobbyMusic();
        else if (_stateManager.CurrentState is LobbyState)
            StartLobbyMusic();
        else
            EndLobbyMusic();
    }

    private void LobbySongReceived()
    {
        if (_lobbyStream !=
            null) //Toggling Ready status fires this method. This check ensures we only start the lobby music if it's not playing.
            return;
        if (_stateManager.CurrentState is LobbyState)
            StartLobbyMusic();
    }

    public void StartLobbyMusic()
    {
        if (_lobbyStream != null || !_lobbyMusicEnabled)
            return;

        var file = _gameTicker.LobbySong;
        if (file == null) // We have not received the lobby song yet.
            return;

        _lobbyStream = _audio.PlayGlobal(file, Filter.Local(), _lobbyParams) as AudioSystem.PlayingStream;
    }

    private void EndLobbyMusic()
    {
        _lobbyStream?.Stop();
        _lobbyStream = null;
    }
}
