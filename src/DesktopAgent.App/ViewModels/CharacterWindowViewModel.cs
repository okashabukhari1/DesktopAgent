using DesktopAgent.Core.Interfaces;

namespace DesktopAgent.App.ViewModels;

public sealed class CharacterWindowViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    public CharacterWindowViewModel(CharacterViewModel character, ISettingsService settings)
    {
        Character = character;
        _settings = settings;
    }
    public CharacterViewModel Character { get; }
    public double CharacterSize => _settings.Current.CharacterSize;
    public void Refresh()
    {
        Character.AnimationEnabled = _settings.Current.AnimationEnabled;
        Character.AnimationIntensity = _settings.Current.AnimationIntensity;
        OnPropertyChanged(nameof(CharacterSize));
    }
}
