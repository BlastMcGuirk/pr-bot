using System.Net.Http;
using System.IO;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using PR_Bot.Settings;
using PRBot.Core.Abstractions;
using PRBot.Core.Llm;
using PRBot.Core.Models;
using PRBot.Core.Review;
using PRBot.Core.Sources;

namespace PR_Bot;

public partial class MainWindow : Window
{
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromMinutes(10) };
    private readonly AppSettingsStore _settingsStore = new();
    private readonly DpapiSourceCredentialStore _credentialStore = new();
    private readonly ILocalLlmHost[] _llmHosts;
    private readonly DispatcherTimer _messageTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly string[] _waitMessages =
    [
        "Thinking real hard...", "Staring at some clouds...", "Consulting the council...",
        "Asking the rubber duck...", "Counting curly braces...", "Reading the diff twice, just to be sure...",
        "Looking for the sneaky edge cases...", "Checking whether null has entered the chat...",
        "Untangling a particularly suspicious loop...", "Giving the tests a pep talk...",
        "Polishing the severity scale...", "Comparing notes with the imaginary reviewer...",
        "Looking under the error-handling rug...", "Making sure the fix is still only a suggestion...",
        "Searching the diff for plot twists...", "Re-reading that one line with dramatic music...",
        "Asking: what could possibly go wrong?...", "Consulting a very small committee...",
        "Looking for off-by-one gremlins...", "Checking the diff's alibi...",
        "Separating signal from semicolons...", "Reviewing responsibly. No comments will be posted."
    ];
    private readonly TextBox _urlBox = new() { FontSize = 14, Padding = new Thickness(12, 10, 12, 10), MinWidth = 420 };
    private readonly TextBox _filePathBox = new() { FontSize = 14, Padding = new Thickness(12, 10, 12, 10), IsReadOnly = true };
    private readonly TextBox _diffTextBox = new() { FontSize = 13, Padding = new Thickness(12, 10, 12, 10), AcceptsReturn = true, AcceptsTab = true, TextWrapping = TextWrapping.NoWrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, MinHeight = 260, MaxHeight = 320, FontFamily = new FontFamily("Consolas") };
    private readonly RadioButton _urlModeButton = new() { Content = "URL", GroupName = "ReviewInputMode", IsChecked = true, Margin = new Thickness(0, 0, 20, 0) };
    private readonly RadioButton _fileModeButton = new() { Content = "File", GroupName = "ReviewInputMode", Margin = new Thickness(0, 0, 20, 0) };
    private readonly RadioButton _textModeButton = new() { Content = "Text", GroupName = "ReviewInputMode" };
    private readonly StackPanel _urlInputPanel = new();
    private readonly StackPanel _fileInputPanel = new();
    private readonly StackPanel _textInputPanel = new();
    private readonly Button _reviewButton = new() { Content = "Review", FontSize = 15, FontWeight = FontWeights.SemiBold, Padding = new Thickness(22, 12, 22, 12), Background = new SolidColorBrush(Color.FromRgb(103, 80, 164)), Foreground = Brushes.White, BorderThickness = new Thickness(0), Cursor = Cursors.Hand };
    private readonly TextBlock _modelConfigurationText = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray, Margin = new Thickness(0, 0, 0, 18) };
    private readonly Border _progressCard = new() { Visibility = Visibility.Collapsed, Background = Brushes.White, CornerRadius = new CornerRadius(14), Padding = new Thickness(22), Margin = new Thickness(0, 0, 0, 18) };
    private readonly TextBlock _waitMessage = new() { Text = "Thinking real hard...", FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 0) };
    private readonly Border _errorCard = new() { Visibility = Visibility.Collapsed, Background = new SolidColorBrush(Color.FromRgb(255, 242, 240)), CornerRadius = new CornerRadius(10), Padding = new Thickness(14), Margin = new Thickness(0, 0, 0, 18) };
    private readonly TextBlock _errorText = new() { Foreground = new SolidColorBrush(Color.FromRgb(140, 47, 37)), TextWrapping = TextWrapping.Wrap };
    private readonly StackPanel _resultsPanel = new() { Visibility = Visibility.Collapsed };
    private readonly StackPanel _actionItemsPanel = new();
    private readonly TextBlock _resultTitle = new() { FontSize = 13, Foreground = Brushes.DimGray, Margin = new Thickness(0, 4, 0, 10) };
    private readonly TextBlock _resultCount = new() { Foreground = Brushes.DimGray, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _noFindings = new() { Text = "No action items found in this diff.", Visibility = Visibility.Collapsed, Padding = new Thickness(20), Background = Brushes.White, TextWrapping = TextWrapping.Wrap };
    private AppSettings? _settings;
    private SourceCredentialCollection _credentials = new();
    private int _messageIndex;

    public MainWindow()
    {
        Title = "PR Bot";
        Width = 1120;
        Height = 820;
        MinWidth = 900;
        MinHeight = 680;
        Background = new SolidColorBrush(Color.FromRgb(250, 248, 252));
        Foreground = new SolidColorBrush(Color.FromRgb(28, 27, 31));

        _llmHosts =
        [
            new OllamaLlmHost(_httpClient),
            new LmStudioLlmHost(_httpClient)
        ];
        Content = BuildLayout();
        _reviewButton.Click += async (_, _) => await ReviewAsync();
        _urlBox.KeyDown += async (_, args) =>
        {
            if (args.Key == Key.Enter)
            {
                await ReviewAsync();
            }
        };
        _urlModeButton.Checked += (_, _) => UpdateInputModeVisibility();
        _fileModeButton.Checked += (_, _) => UpdateInputModeVisibility();
        _textModeButton.Checked += (_, _) => UpdateInputModeVisibility();
        _messageTimer.Tick += (_, _) =>
        {
            _messageIndex = (_messageIndex + 1) % _waitMessages.Length;
            _waitMessage.Text = _waitMessages[_messageIndex];
        };
        Loaded += async (_, _) => await LoadSettingsAsync();
        Closed += (_, _) => _httpClient.Dispose();
    }

    private UIElement BuildLayout()
    {
        var root = new DockPanel();
        var menu = new Menu { Height = 38, Background = Brushes.White };
        var settingsItem = new MenuItem { Header = "_Settings", Padding = new Thickness(14, 8, 14, 8) };
        settingsItem.Click += async (_, _) => await OpenSettingsAsync();
        menu.Items.Add(settingsItem);
        DockPanel.SetDock(menu, Dock.Top);
        root.Children.Add(menu);

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var content = new StackPanel { Margin = new Thickness(56, 34, 56, 48), MaxWidth = 920, HorizontalAlignment = HorizontalAlignment.Center };
        content.Children.Add(new TextBlock { Text = "PR Bot", FontSize = 32, FontWeight = FontWeights.Bold });
        content.Children.Add(new TextBlock { Text = "A second set of eyes for your pull request.", FontSize = 16, Foreground = Brushes.DimGray, Margin = new Thickness(0, 5, 0, 24) });
        content.Children.Add(_modelConfigurationText);

        var inputCard = CreateCard();
        var inputContent = new StackPanel();
        var inputModes = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) };
        inputModes.Children.Add(_urlModeButton);
        inputModes.Children.Add(_fileModeButton);
        inputModes.Children.Add(_textModeButton);
        inputContent.Children.Add(inputModes);

        _urlInputPanel.Children.Add(new TextBlock { Text = "Pull request URL", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
        _urlBox.ToolTip = "Paste an Azure DevOps or Bitbucket pull request URL";
        _urlInputPanel.Children.Add(_urlBox);

        _fileInputPanel.Children.Add(new TextBlock { Text = "Diff file", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
        var fileRow = new DockPanel { LastChildFill = true };
        var browseDiffButton = new Button { Content = "Browse...", Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(8, 0, 0, 0) };
        browseDiffButton.Click += (_, _) => BrowseDiffFile();
        DockPanel.SetDock(browseDiffButton, Dock.Right);
        fileRow.Children.Add(browseDiffButton);
        fileRow.Children.Add(_filePathBox);
        _fileInputPanel.Children.Add(fileRow);

        _textInputPanel.Children.Add(new TextBlock { Text = "Paste diff text", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
        _textInputPanel.Children.Add(_diffTextBox);

        inputContent.Children.Add(_urlInputPanel);
        inputContent.Children.Add(_fileInputPanel);
        inputContent.Children.Add(_textInputPanel);
        var row = new DockPanel { LastChildFill = true };
        _reviewButton.Margin = new Thickness(14, 0, 0, 0);
        DockPanel.SetDock(_reviewButton, Dock.Right);
        row.Children.Add(_reviewButton);
        row.Children.Add(new Border { Child = new TextBlock { Text = "Your diff stays on this machine and is sent only to your selected local model.", FontSize = 12, Foreground = Brushes.DimGray, VerticalAlignment = VerticalAlignment.Center } });
        inputContent.Children.Add(row);
        UpdateInputModeVisibility();
        inputCard.Child = inputContent;
        content.Children.Add(inputCard);

        var progressContent = new StackPanel();
        progressContent.Children.Add(new ProgressBar { IsIndeterminate = true, Height = 5, Foreground = new SolidColorBrush(Color.FromRgb(103, 80, 164)) });
        progressContent.Children.Add(_waitMessage);
        progressContent.Children.Add(new TextBlock { Text = "Reviewing the diff with your local model. This can take a little while.", FontSize = 12, Foreground = Brushes.DimGray, Margin = new Thickness(0, 5, 0, 0) });
        _progressCard.Child = progressContent;
        content.Children.Add(_progressCard);

        _errorCard.Child = _errorText;
        content.Children.Add(_errorCard);
        var resultsHeader = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
        DockPanel.SetDock(_resultCount, Dock.Right);
        resultsHeader.Children.Add(_resultCount);
        var heading = new StackPanel();
        heading.Children.Add(new TextBlock { Text = "Review findings", FontSize = 22, FontWeight = FontWeights.Bold });
        heading.Children.Add(_resultTitle);
        resultsHeader.Children.Add(heading);
        _resultsPanel.Children.Add(resultsHeader);
        _resultsPanel.Children.Add(_noFindings);
        _resultsPanel.Children.Add(_actionItemsPanel);
        content.Children.Add(_resultsPanel);

        scroll.Content = content;
        root.Children.Add(scroll);
        return root;
    }

    private static Border CreateCard() => new()
    {
        Background = Brushes.White,
        BorderBrush = new SolidColorBrush(Color.FromRgb(231, 227, 236)),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(14),
        Padding = new Thickness(22),
        Margin = new Thickness(0, 0, 0, 18)
    };

    private void UpdateInputModeVisibility()
    {
        _urlInputPanel.Visibility = _urlModeButton.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        _fileInputPanel.Visibility = _fileModeButton.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        _textInputPanel.Visibility = _textModeButton.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BrowseDiffFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select a diff file",
            Filter = "Diff files (*.diff;*.patch)|*.diff;*.patch|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) == true)
        {
            _filePathBox.Text = dialog.FileName;
        }
    }

    private async Task LoadSettingsAsync()
    {
        try
        {
            _settings = await _settingsStore.LoadAsync();
            _credentials = await _credentialStore.LoadAsync();
        }
        catch (Exception ex)
        {
            ShowError($"Could not load local settings: {ex.Message}");
        }

        UpdateModelConfigurationStatus();
    }

    private void UpdateModelConfigurationStatus()
    {
        var host = _settings is null
            ? null
            : _llmHosts.SingleOrDefault(candidate => candidate.Id.Equals(_settings.SelectedLlmHostId, StringComparison.OrdinalIgnoreCase));
        if (host is null
            || string.IsNullOrWhiteSpace(_settings!.SelectedModelId)
            || !_settings.LlmEndpoints.TryGetValue(host.Id, out var endpointText)
            || !Uri.TryCreate(endpointText, UriKind.Absolute, out _))
        {
            _modelConfigurationText.Text = "No local model is configured. Go to Settings to choose a host and model.";
            return;
        }

        _modelConfigurationText.Text = $"Using {host.DisplayName} · {_settings.SelectedModelId}";
    }

    private async Task OpenSettingsAsync()
    {
        if (_settings is null)
        {
            await LoadSettingsAsync();
        }

        if (_settings is null)
        {
            return;
        }

        var settingsWindow = new SettingsWindow(_settings, _credentials, _settingsStore, _credentialStore, _llmHosts) { Owner = this };
        if (settingsWindow.ShowDialog() == true)
        {
            _settings = settingsWindow.SavedSettings;
            _credentials = settingsWindow.SavedCredentials ?? _credentials;
            UpdateModelConfigurationStatus();
        }
    }

    private async Task ReviewAsync()
    {
        ClearReviewState();
        var isUrlMode = _urlModeButton.IsChecked == true;
        var isFileMode = _fileModeButton.IsChecked == true;
        Uri? pullRequestUrl = null;
        string? suppliedDiff = null;
        if (isUrlMode)
        {
            if (!Uri.TryCreate(_urlBox.Text.Trim(), UriKind.Absolute, out pullRequestUrl)
                || (pullRequestUrl.Scheme != Uri.UriSchemeHttps && pullRequestUrl.Scheme != Uri.UriSchemeHttp))
            {
                ShowError("Enter a valid Azure DevOps or Bitbucket pull request URL.");
                return;
            }
        }
        else if (isFileMode)
        {
            if (string.IsNullOrWhiteSpace(_filePathBox.Text))
            {
                ShowError("Select a diff file to review.");
                return;
            }

            try
            {
                suppliedDiff = await System.IO.File.ReadAllTextAsync(_filePathBox.Text);
            }
            catch (Exception ex)
            {
                ShowError($"Could not read the diff file: {ex.Message}");
                return;
            }
        }
        else
        {
            suppliedDiff = _diffTextBox.Text;
        }

        if (!isUrlMode && string.IsNullOrWhiteSpace(suppliedDiff))
        {
            ShowError("The diff is empty. Select a file or paste diff text before reviewing.");
            return;
        }

        if (_settings is null)
        {
            ShowError("Settings could not be loaded. Open the app again after fixing the local settings file.");
            return;
        }

        PullRequestSourceConfiguration? sourceConfiguration = null;
        IPullRequestSource? source = null;
        string username = string.Empty;
        string credential = string.Empty;
        if (isUrlMode)
        {
            var matchingConfigurations = _settings.PullRequestSources
                .Where(configuration =>
                {
                    try
                    {
                        return PullRequestSourceCatalog.GetHost(configuration.SourceUrl)
                            .Equals(pullRequestUrl!.IdnHost, StringComparison.OrdinalIgnoreCase);
                    }
                    catch (ArgumentException)
                    {
                        return false;
                    }
                })
                .ToArray();
            if (matchingConfigurations.Length == 0)
            {
                ShowError($"No source is configured for '{pullRequestUrl!.IdnHost}'. Add a matching source URL in Settings.");
                return;
            }

            if (matchingConfigurations.Length > 1)
            {
                ShowError($"More than one source is configured for '{pullRequestUrl!.IdnHost}'. Remove the duplicate source row in Settings.");
                return;
            }

            sourceConfiguration = matchingConfigurations[0];
            var sourceType = PullRequestSourceCatalog.Find(sourceConfiguration.SourceTypeId);
            if (sourceType is null)
            {
                ShowError($"Source type '{sourceConfiguration.SourceTypeId}' is no longer supported. Update its row in Settings.");
                return;
            }

            if (!sourceType.AuthenticationMethods.Contains(sourceConfiguration.AuthenticationMethod))
            {
                ShowError($"{sourceType.DisplayName} does not support the configured authentication method.");
                return;
            }

            if (!_credentials.Entries.TryGetValue(sourceConfiguration.Id, out var sourceCredentials))
            {
                sourceCredentials = new SourceCredential();
            }

            if (sourceConfiguration.AuthenticationMethod == SourceAuthenticationMethod.PersonalAccessToken)
            {
                credential = sourceCredentials.PersonalAccessToken;
            }
            else
            {
                username = sourceCredentials.Username;
                credential = sourceCredentials.Password;
                if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(credential))
                {
                    ShowError($"Add both a username and password for {sourceType.DisplayName} in Settings.");
                    return;
                }
            }

            if (string.IsNullOrWhiteSpace(credential))
            {
                ShowError($"Add the configured {sourceConfiguration.AuthenticationMethod} credentials for {sourceType.DisplayName} in Settings.");
                return;
            }

            source = PullRequestSourceCatalog.CreateSource(_httpClient, sourceConfiguration);
            if (!source.CanHandle(pullRequestUrl!))
            {
                ShowError($"The URL path is not a supported {sourceType.DisplayName} pull request.");
                return;
            }
        }

        var host = _llmHosts.SingleOrDefault(candidate => candidate.Id.Equals(_settings.SelectedLlmHostId, StringComparison.OrdinalIgnoreCase));
        if (host is null)
        {
            ShowError("Select a supported local model host in Settings.");
            return;
        }

        if (!_settings.LlmEndpoints.TryGetValue(host.Id, out var endpointText)
            || !Uri.TryCreate(endpointText, UriKind.Absolute, out var endpoint)
            || string.IsNullOrWhiteSpace(_settings.SelectedModelId))
        {
            ShowError("Select a model and valid local endpoint in Settings.");
            return;
        }

        _reviewButton.IsEnabled = false;
        _progressCard.Visibility = Visibility.Visible;
        _messageIndex = 0;
        _waitMessage.Text = _waitMessages[0];
        _messageTimer.Start();
        try
        {
            var instructions = await AppSettingsStore.ReadAgentInstructionsAsync(_settings.AgentInstructionsPath);
            var sources = source is null ? Array.Empty<IPullRequestSource>() : [source];
            var reviewService = new PullRequestReviewService(sources, _llmHosts, new DiffOnlyPrReviewAgentFactory());
            var result = suppliedDiff is not null
                ? await reviewService.ReviewDiffAsync(suppliedDiff, instructions, host.Id, endpoint, _settings.SelectedModelId)
                : await reviewService.ReviewAsync(new ReviewRequest(
                    pullRequestUrl!,
                    sourceConfiguration!.AuthenticationMethod,
                    username,
                    credential,
                    instructions,
                    host.Id,
                    endpoint,
                    _settings.SelectedModelId));
            _resultTitle.Text = result.PullRequestTitle;
            _resultCount.Text = result.ActionItems.Count == 1 ? "1 action item" : $"{result.ActionItems.Count} action items";
            _noFindings.Visibility = result.ActionItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            foreach (var actionItem in result.ActionItems)
            {
                _actionItemsPanel.Children.Add(CreateActionItemCard(actionItem));
            }

            _resultsPanel.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            ShowError($"Review failed: {ex.Message}");
        }
        finally
        {
            _messageTimer.Stop();
            _progressCard.Visibility = Visibility.Collapsed;
            _reviewButton.IsEnabled = true;
        }
    }

    private static Border CreateActionItemCard(ReviewActionItem item)
    {
        var card = CreateCard();
        var expander = new Expander();
        var header = new StackPanel();
        var badge = new Border { Background = SeverityBrush(item.Severity), CornerRadius = new CornerRadius(7), Padding = new Thickness(8, 4, 8, 4), HorizontalAlignment = HorizontalAlignment.Left };
        badge.Child = new TextBlock { Text = item.Severity.ToString().ToUpperInvariant(), FontSize = 10, FontWeight = FontWeights.Bold, Foreground = Brushes.White };
        header.Children.Add(badge);
        header.Children.Add(new TextBlock { Text = item.Title, FontSize = 15, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 9, 0, 0) });
        header.Children.Add(new TextBlock { Text = item.Description, FontSize = 13, Foreground = Brushes.DimGray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 0) });
        var location = item.FilePath ?? string.Empty;
        if (item.LineNumber is not null)
        {
            location += $" · line {item.LineNumber}";
        }

        if (!string.IsNullOrWhiteSpace(location))
        {
            header.Children.Add(new TextBlock { Text = location, FontSize = 11, Foreground = Brushes.DimGray, Margin = new Thickness(0, 5, 0, 0) });
        }

        expander.Header = header;
        var details = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
        details.Children.Add(new TextBlock { Text = "Why it matters", FontWeight = FontWeights.SemiBold });
        details.Children.Add(new TextBlock { Text = item.WhyItMatters, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 12) });
        details.Children.Add(new TextBlock { Text = "Possible fix", FontWeight = FontWeights.SemiBold });
        details.Children.Add(new TextBlock { Text = item.SuggestedFix, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
        expander.Content = details;
        card.Child = expander;
        return card;
    }

    private static Brush SeverityBrush(ActionItemSeverity severity) => severity switch
    {
        ActionItemSeverity.Critical => new SolidColorBrush(Color.FromRgb(153, 27, 27)),
        ActionItemSeverity.High => new SolidColorBrush(Color.FromRgb(194, 65, 12)),
        ActionItemSeverity.Medium => new SolidColorBrush(Color.FromRgb(161, 98, 7)),
        ActionItemSeverity.Low => new SolidColorBrush(Color.FromRgb(71, 85, 105)),
        _ => new SolidColorBrush(Color.FromRgb(100, 116, 139))
    };

    private void ClearReviewState()
    {
        _messageTimer.Stop();
        _actionItemsPanel.Children.Clear();
        _resultTitle.Text = string.Empty;
        _resultCount.Text = string.Empty;
        _resultsPanel.Visibility = Visibility.Collapsed;
        _errorCard.Visibility = Visibility.Collapsed;
        _progressCard.Visibility = Visibility.Collapsed;
    }

    private void ShowError(string message)
    {
        _errorText.Text = message;
        _errorCard.Visibility = Visibility.Visible;
    }
}
