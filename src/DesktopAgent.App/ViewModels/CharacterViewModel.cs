using DesktopAgent.Core.Enums;
using DesktopAgent.Core.Interfaces;

namespace DesktopAgent.App.ViewModels;

public sealed class CharacterViewModel : ObservableObject, IDisposable
{
    private readonly ICharacterService _characterService;
    private CharacterState _state;
    private bool _animationEnabled;
    private double _animationIntensity;

    public CharacterViewModel(ICharacterService characterService, ISettingsService settingsService)
    {
        _characterService = characterService;
        _state = characterService.State;
        _animationEnabled = settingsService.Current.AnimationEnabled;
        _animationIntensity = settingsService.Current.AnimationIntensity;
        characterService.StateChanged += OnStateChanged;
    }

    public CharacterState State { get => _state; private set => SetProperty(ref _state, value); }
    public bool AnimationEnabled { get => _animationEnabled; set => SetProperty(ref _animationEnabled, value); }
    public double AnimationIntensity { get => _animationIntensity; set => SetProperty(ref _animationIntensity, value); }
    public Array States => Enum.GetValues(typeof(CharacterState));
    public void SetState(CharacterState state) => _characterService.SetState(state);
    private void OnStateChanged(object? sender, CharacterState state) => State = state;
    public void Dispose() => _characterService.StateChanged -= OnStateChanged;
}
