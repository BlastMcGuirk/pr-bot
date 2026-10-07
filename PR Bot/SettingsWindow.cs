using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using PR_Bot.Settings;
using PRBot.Core.Abstractions;
using PRBot.Core.Models;

namespace PR_Bot;

public sealed class SettingsWindow : Window
{
    private readonly AppSettings _currentSettings;
    private readonly PersonalAccessTokens _currentTokens;
    private readonly AppSettingsStore _settingsStore;
    private readonly IPersonalAccessTokenStore _tokenStore;
    private readonly IReadOnlyList<ILocalLlmHost> _hosts;
    private readonly ComboBox _hostSelector = new() { MinWidth = 220 };
    private readonly TextBox _endpointBox = new() { MinWidth = 360, Padding = new Thickness(8) };
    private readonly ComboBox _modelSelector = new() { MinWidth = 300, DisplayMemberPath = nameof(ModelInfo.DisplayName) };
    private readonly Button _refreshModelsButton = new() { Content = "Refresh models", Padding = new Thickness(12, 7, 12, 7) };
    private readonly PasswordBox _azureTokenBox = new() { Padding = new Thickness(8) };
    private readonly PasswordBox _bitbucketTokenBox = new() { Padding = new Thickness(8) };
    private readonly CheckBox _clearAzureToken = new() { Content = "Clear saved Azure DevOps token" };
    private readonly CheckBox _clearBitbucketToken = new() { Content = "Clear saved Bitbucket token" };
    private readonly TextBox _instructionsPathBox = new() { Padding = new Thickness(8) };
    private readonly TextBox _instructionsEditor = new() { AcceptsReturn = true, AcceptsTab = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MinHeight = 190, Padding = new Thickness(10), FontFamily = new FontFamily("Consolas") };
    private readonly TextBlock _statusText = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray, VerticalAlignment = VerticalAlignment.Center };
    private bool _initializing = true;

    public SettingsWindow(
        AppSettings settings,
        PersonalAccessTokens tokens,
        AppSettingsStore settingsStore,
        IPersonalAccessTokenStore tokenStore,
        IReadOnlyList<ILocalLlmHost> hosts)
    {
        _currentSettings = settings;
        _currentTokens = tokens;
        _settingsStore = settingsStore;
        _tokenStore = tokenStore;
        _hosts = hosts;

        Title = "PR Bot settings";
        Width = 760;
        Height = 820;
        MinWidth = 660;
        MinHeight = 680;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(250, 248, 252));
        Content = BuildLayout();
        Loaded += async (_, _) =>
        {
            await LoadInstructionsAsync();
            await RefreshModelsAsync();
        };
    }

    public AppSettings? SavedSettings { get; private set; }

    public PersonalAccessTokens? SavedTokens { get; private set; }

    private UIElement BuildLayout()
    {
        _hostSelector.ItemsSource = _hosts;
        _hostSelector.DisplayMemberPath = nameof(ILocalLlmHost.DisplayName);
        _hostSelector.SelectedItem = _hosts.FirstOrDefault(host => host.Id.Equals(_currentSettings.SelectedLlmHostId, StringComparison.OrdinalIgnoreCase)) ?? _hosts.FirstOrDefault();
        if (_hostSelector.SelectedItem is ILocalLlmHost selectedHost)
        {
            _endpointBox.Text = GetEndpoint(selectedHost);
        }

        _instructionsPathBox.Text = _currentSettings.AgentInstructionsPath;
        _hostSelector.SelectionChanged += async (_, _) => await HostChangedAsync();
        _refreshModelsButton.Click += async (_, _) => await RefreshModelsAsync();

        var page = new StackPanel { Margin = new Thickness(28) };
        page.Children.Add(new TextBlock { Text = "Settings", FontSize = 26, FontWeight = FontWeights.Bold });
        page.Children.Add(new TextBlock { Text = "Choose your local model host, configure access, and tailor review guidance.", Foreground = Brushes.DimGray, Margin = new Thickness(0, 4, 0, 18) });

        page.Children.Add(SectionHeader("Local model"));
        page.Children.Add(Field("Host", _hostSelector));
        page.Children.Add(Field("Endpoint", _endpointBox));
        var modelRow = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(_refreshModelsButton, Dock.Right);
        modelRow.Children.Add(_refreshModelsButton);
        modelRow.Children.Add(_modelSelector);
        page.Children.Add(Field("Model", modelRow));
        page.Children.Add(_statusText);

        page.Children.Add(SectionHeader("Personal access tokens"));
        page.Children.Add(new TextBlock { Text = "Tokens are encrypted for your Windows account. Leave a field blank to keep its saved token.", Foreground = Brushes.DimGray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
        page.Children.Add(Field("Azure DevOps PAT", _azureTokenBox));
        page.Children.Add(_clearAzureToken);
        page.Children.Add(Field("Bitbucket PAT", _bitbucketTokenBox));
        page.Children.Add(_clearBitbucketToken);

        page.Children.Add(SectionHeader("AGENTS.md review instructions"));
        var pathRow = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 8) };
        var browseButton = new Button { Content = "Browse...", Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(8, 0, 0, 0) };
        browseButton.Click += (_, _) => BrowseInstructionsFile();
        DockPanel.SetDock(browseButton, Dock.Right);
        pathRow.Children.Add(browseButton);
        pathRow.Children.Add(_instructionsPathBox);
        page.Children.Add(pathRow);
        page.Children.Add(_instructionsEditor);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        var cancelButton = new Button { Content = "Cancel", Padding = new Thickness(16, 9, 16, 9), Margin = new Thickness(0, 0, 8, 0) };
        cancelButton.Click += (_, _) => DialogResult = false;
        var saveButton = new Button { Content = "Save settings", Padding = new Thickness(18, 9, 18, 9), Background = new SolidColorBrush(Color.FromRgb(103, 80, 164)), Foreground = Brushes.White, BorderThickness = new Thickness(0), FontWeight = FontWeights.SemiBold };
        saveButton.Click += async (_, _) => await SaveAsync(saveButton);
        buttons.Children.Add(cancelButton);
        buttons.Children.Add(saveButton);
        page.Children.Add(buttons);
        _initializing = false;
        return new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = page };
    }

    private async Task LoadInstructionsAsync()
    {
        try
        {
            var path = _instructionsPathBox.Text;
            if (File.Exists(path))
            {
                _instructionsEditor.Text = await File.ReadAllTextAsync(path);
            }
        }
        catch (Exception ex)
        {
            _statusText.Text = $"Could not load AGENTS.md: {ex.Message}";
        }
    }

    private async Task HostChangedAsync()
    {
        if (_initializing || _hostSelector.SelectedItem is not ILocalLlmHost host)
        {
            return;
        }

        _endpointBox.Text = GetEndpoint(host);
        _modelSelector.ItemsSource = null;
        _statusText.Text = string.Empty;
        await RefreshModelsAsync();
    }

    private string GetEndpoint(ILocalLlmHost host) =>
        _currentSettings.LlmEndpoints.TryGetValue(host.Id, out var endpoint) ? endpoint : host.DefaultEndpoint.ToString();

    private async Task RefreshModelsAsync()
    {
        if (_hostSelector.SelectedItem is not ILocalLlmHost host
            || !Uri.TryCreate(_endpointBox.Text.Trim(), UriKind.Absolute, out var endpoint))
        {
            _statusText.Text = "Enter a valid endpoint before refreshing models.";
            return;
        }

        _refreshModelsButton.IsEnabled = false;
        _statusText.Text = "Connecting to the local model host...";
        try
        {
            var models = await host.ListModelsAsync(endpoint);
            _modelSelector.ItemsSource = models;
            _modelSelector.SelectedItem = models.FirstOrDefault(model => model.Id == _currentSettings.SelectedModelId) ?? models.FirstOrDefault();
            _statusText.Text = models.Count == 0 ? "No models were returned by this host." : $"Found {models.Count} model(s).";
        }
        catch (Exception ex)
        {
            _modelSelector.ItemsSource = null;
            _statusText.Text = $"Could not list models: {ex.Message}";
        }
        finally
        {
            _refreshModelsButton.IsEnabled = true;
        }
    }

    private void BrowseInstructionsFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select AGENTS.md review instructions",
            Filter = "Markdown files (*.md)|*.md|All files (*.*)|*.*",
            CheckFileExists = false,
            FileName = Path.GetFileName(_instructionsPathBox.Text)
        };
        if (dialog.ShowDialog(this) == true)
        {
            _instructionsPathBox.Text = dialog.FileName;
            _ = LoadInstructionsAsync();
        }
    }

    private async Task SaveAsync(Button saveButton)
    {
        if (_hostSelector.SelectedItem is not ILocalLlmHost host
            || !Uri.TryCreate(_endpointBox.Text.Trim(), UriKind.Absolute, out _))
        {
            _statusText.Text = "Choose a host and enter a valid absolute endpoint URL.";
            return;
        }

        if (string.IsNullOrWhiteSpace(_instructionsPathBox.Text))
        {
            _statusText.Text = "Choose a path for the AGENTS.md instructions file.";
            return;
        }

        saveButton.IsEnabled = false;
        try
        {
            var endpoints = new Dictionary<string, string>(_currentSettings.LlmEndpoints, StringComparer.OrdinalIgnoreCase)
            {
                [host.Id] = _endpointBox.Text.Trim()
            };
            var selectedModel = _modelSelector.SelectedItem as ModelInfo;
            var modelId = selectedModel?.Id
                          ?? (host.Id.Equals(_currentSettings.SelectedLlmHostId, StringComparison.OrdinalIgnoreCase)
                              ? _currentSettings.SelectedModelId
                              : string.Empty);
            var newSettings = new AppSettings
            {
                SelectedLlmHostId = host.Id,
                SelectedModelId = modelId,
                LlmEndpoints = endpoints,
                AgentInstructionsPath = Path.GetFullPath(_instructionsPathBox.Text.Trim())
            };
            var instructionDirectory = Path.GetDirectoryName(newSettings.AgentInstructionsPath);
            if (!string.IsNullOrWhiteSpace(instructionDirectory))
            {
                Directory.CreateDirectory(instructionDirectory);
            }

            await File.WriteAllTextAsync(newSettings.AgentInstructionsPath, _instructionsEditor.Text);
            await _settingsStore.SaveAsync(newSettings);
            var tokens = new PersonalAccessTokens(
                _clearAzureToken.IsChecked == true ? string.Empty : string.IsNullOrWhiteSpace(_azureTokenBox.Password) ? _currentTokens.AzureDevOps : _azureTokenBox.Password.Trim(),
                _clearBitbucketToken.IsChecked == true ? string.Empty : string.IsNullOrWhiteSpace(_bitbucketTokenBox.Password) ? _currentTokens.Bitbucket : _bitbucketTokenBox.Password.Trim());
            await _tokenStore.SaveAsync(tokens);
            SavedSettings = newSettings;
            SavedTokens = tokens;
            DialogResult = true;
        }
        catch (Exception ex)
        {
            _statusText.Text = $"Could not save settings: {ex.Message}";
            saveButton.IsEnabled = true;
        }
    }

    private static TextBlock SectionHeader(string title) => new()
    {
        Text = title,
        FontSize = 17,
        FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(0, 18, 0, 8)
    };

    private static UIElement Field(string label, UIElement control)
    {
        var section = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        section.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        section.Children.Add(control);
        return section;
    }
}
