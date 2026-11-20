using System.Windows;
using DesktopAgent.Core.Ai;
using DesktopAgent.Core.Interfaces;
using DesktopAgent.Core.Models;
using DesktopAgent.Infrastructure.Ai;

namespace DesktopAgent.App.Views;

public partial class AiSettingsView : System.Windows.Controls.UserControl
{
    private sealed record ProviderPreset(string Name, string BaseUrl, string DefaultModel);
    private readonly ProviderPreset[] _providers =
    [
        new("OpenAI", "https://api.openai.com/v1", "gpt-4o-mini"),
        new("OpenRouter", "https://openrouter.ai/api/v1", "openai/gpt-4o-mini"),
        new("Groq", "https://api.groq.com/openai/v1", "llama-3.3-70b-versatile"),
        new("Together AI", "https://api.together.xyz/v1", "meta-llama/Llama-3.3-70B-Instruct-Turbo"),
        new("Custom OpenAI-compatible", string.Empty, string.Empty)
    ];
    private readonly ISettingsService _settings;
    private readonly ICredentialStore _credentials;
    private readonly IAiProvider _provider;
    private readonly IAiConnectionService _connection;
    private bool _loading;
    private bool _initialized;

    public AiSettingsView(ISettingsService settings, ICredentialStore credentials, IAiProvider provider, IAiConnectionService connection)
    {
        InitializeComponent();
        _settings = settings;
        _credentials = credentials;
        _provider = provider;
        _connection = connection;
        ProviderBox.ItemsSource = _providers;
        LoadSettings();
        Loaded += async (_, _) =>
        {
            if (_initialized) return;
            _initialized = true;
            try
            {
                if (_settings.Current.Ai.Enabled && _credentials.Exists(ProviderCredentialNames.ForBaseUrl(_settings.Current.Ai.BaseUrl)))
                    await FetchModelsAsync();
            }
            catch (Exception exception) { StatusText.Text = $"Provider setup is unavailable: {exception.Message}"; }
        };
    }

    private void LoadSettings()
    {
        _loading = true;
        var saved = _settings.Current.Ai;
        ProviderBox.SelectedItem = _providers.FirstOrDefault(p => p.Name.Equals(saved.Provider, StringComparison.OrdinalIgnoreCase))
            ?? _providers[^1];
        BaseUrlBox.Text = saved.BaseUrl;
        ModelBox.Text = saved.Model;
        AiEnabledBox.IsChecked = saved.Enabled;
        ToolActivityBox.IsChecked = _settings.Current.Ai.ShowToolActivity;
        TimeoutBox.Text = saved.TimeoutSeconds.ToString();
        try { UpdateCredentialStatus(); }
        catch (Exception exception) { CredentialStatus.Text = $"Credential status unavailable: {exception.Message}"; }
        _loading = false;
    }

    private void Provider_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_loading || ProviderBox.SelectedItem is not ProviderPreset selected) return;
        if (!string.IsNullOrWhiteSpace(selected.BaseUrl)) BaseUrlBox.Text = selected.BaseUrl;
        if (!string.IsNullOrWhiteSpace(selected.DefaultModel)) ModelBox.Text = selected.DefaultModel;
        StatusText.Text = "Not tested. Save and test this provider.";
        try { UpdateCredentialStatus(); }
        catch (Exception exception) { CredentialStatus.Text = $"Credential status unavailable: {exception.Message}"; }
    }

    private async void FetchModels_Click(object sender, RoutedEventArgs e) => await SaveAndFetchModelsAsync();

    private async Task SaveAndFetchModelsAsync()
    {
        try
        {
            await SaveSettingsAsync();
            await FetchModelsAsync();
        }
        catch (Exception exception) { StatusText.Text = $"Could not fetch models: {exception.Message}"; }
    }

    private async Task FetchModelsAsync()
    {
        FetchModelsButton.IsEnabled = false;
        StatusText.Text = "Fetching models from provider…";
        try
        {
            var models = await _provider.GetModelsAsync();
            var selectedModel = ModelBox.Text;
            ModelBox.ItemsSource = models;
            ModelBox.Text = selectedModel;
            StatusText.Text = models.Count == 0 ? "No models listed. You can type a model name and test it." : $"Loaded {models.Count} models. Choose a model and test the connection.";
        }
        catch (Exception exception) { StatusText.Text = AiProviderErrors.Friendly(exception); }
        finally { FetchModelsButton.IsEnabled = true; }
    }

    private async void Test_Click(object sender, RoutedEventArgs e)
    {
        TestButton.IsEnabled = false;
        StatusText.Text = "Testing provider connection…";
        try
        {
            await SaveSettingsAsync();
            StatusText.Text = await _provider.TestConnectionAsync();
        }
        catch (Exception exception)
        {
            StatusText.Text = AiProviderErrors.Friendly(exception);
        }
        finally { TestButton.IsEnabled = true; }
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        var answer = System.Windows.MessageBox.Show("Remove the AI provider credential from Windows Credential Manager?", "Remove credential",
            MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;
        try
        {
            _credentials.Delete(ProviderCredentialNames.ForBaseUrl(BaseUrlBox.Text));
            ApiKeyBox.Clear();
            UpdateCredentialStatus();
            StatusText.Text = "Saved credential removed.";
            _connection.Set(new(AiConnectionState.NotConfigured, "No provider key saved."));
        }
        catch (Exception exception) { StatusText.Text = $"Could not remove credential: {exception.Message}"; }
    }

    public async Task<bool> TrySaveAsync()
    {
        try { await SaveSettingsAsync(); StatusText.Text = "AI settings saved."; return true; }
        catch (Exception exception) { StatusText.Text = $"AI settings could not be saved: {exception.Message}"; return false; }
    }

    private async Task SaveSettingsAsync()
    {
        if (!Uri.TryCreate(BaseUrlBox.Text.Trim(), UriKind.Absolute, out var baseUri)
            || (baseUri.Scheme != Uri.UriSchemeHttps && !(baseUri.Scheme == Uri.UriSchemeHttp && baseUri.IsLoopback))
            || !string.IsNullOrEmpty(baseUri.UserInfo) || !string.IsNullOrEmpty(baseUri.Query) || !string.IsNullOrEmpty(baseUri.Fragment))
            throw new InvalidOperationException("Use an HTTPS API base URL. HTTP is allowed only for a local loopback provider.");
        if (string.IsNullOrWhiteSpace(ModelBox.Text) || ModelBox.Text.Trim().Length > 100)
            throw new InvalidOperationException("Enter a model name up to 100 characters.");
        var updated = _settings.Current.Clone();
        if (!int.TryParse(TimeoutBox.Text, out var timeout) || timeout < 10 || timeout > 180)
            throw new InvalidOperationException("Enter a timeout between 10 and 180 seconds.");
        updated.Ai.TimeoutSeconds = timeout;
        updated.Ai.Provider = (ProviderBox.SelectedItem as ProviderPreset)?.Name ?? "Custom OpenAI-compatible";
        updated.Ai.Enabled = AiEnabledBox.IsChecked == true;
        updated.Ai.BaseUrl = BaseUrlBox.Text.Trim();
        updated.Ai.Model = ModelBox.Text.Trim();
        updated.Ai.ShowToolActivity = ToolActivityBox.IsChecked == true;
        updated.Ai.SaveConversationHistory = false;
        var changed = updated.Ai.BaseUrl != _settings.Current.Ai.BaseUrl || updated.Ai.Model != _settings.Current.Ai.Model ||
            !string.IsNullOrWhiteSpace(ApiKeyBox.Password);
        if (!string.IsNullOrWhiteSpace(ApiKeyBox.Password))
        {
            var secret = ApiKeyBox.Password.Trim();
            if (secret.Any(char.IsControl)) throw new InvalidOperationException("The API key contains an invalid character.");
            _credentials.Save(ProviderCredentialNames.ForBaseUrl(updated.Ai.BaseUrl), secret);
        }
        await _settings.SaveAsync(updated);
        if (changed) _connection.Set(new(AiConnectionState.NotTested, "Connection not tested."));
        ApiKeyBox.Clear();
        UpdateCredentialStatus();
    }

    private void UpdateCredentialStatus() => CredentialStatus.Text = _credentials.Exists(ProviderCredentialNames.ForBaseUrl(BaseUrlBox.Text))
        ? "Credential saved in Windows Credential Manager"
        : "No credential saved";
}
