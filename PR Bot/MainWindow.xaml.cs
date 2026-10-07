using System.Net.Http;
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
    private readonly DpapiPersonalAccessTokenStore _tokenStore = new();
    private readonly IPullRequestSource[] _sources;
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
    private PersonalAccessTokens _tokens = new(string.Empty, string.Empty);
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

        _sources =
        [
            new AzureDevOpsPullRequestSource(_httpClient),
            new BitbucketPullRequestSource(_httpClient)
        ];
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
        inputContent.Children.Add(new TextBlock { Text = "Pull request URL", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
        var row = new DockPanel { LastChildFill = true };
        _reviewButton.Margin = new Thickness(14, 0, 0, 0);
        DockPanel.SetDock(_reviewButton, Dock.Right);
        row.Children.Add(_reviewButton);
        _urlBox.ToolTip = "Paste an Azure DevOps or Bitbucket pull request URL";
        row.Children.Add(_urlBox);
        inputContent.Children.Add(row);
        inputContent.Children.Add(new TextBlock { Text = "Your diff stays on this machine and is sent only to your selected local model.", FontSize = 12, Foreground = Brushes.DimGray, Margin = new Thickness(0, 9, 0, 0) });
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

    private async Task LoadSettingsAsync()
    {
        try
        {
            _settings = await _settingsStore.LoadAsync();
            _tokens = await _tokenStore.LoadAsync();
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

        var settingsWindow = new SettingsWindow(_settings, _tokens, _settingsStore, _tokenStore, _llmHosts) { Owner = this };
        if (settingsWindow.ShowDialog() == true)
        {
            _settings = settingsWindow.SavedSettings;
            _tokens = settingsWindow.SavedTokens ?? _tokens;
            UpdateModelConfigurationStatus();
        }
    }

    private async Task ReviewAsync()
    {
        ClearReviewState();
        if (!Uri.TryCreate(_urlBox.Text.Trim(), UriKind.Absolute, out var pullRequestUrl)
            || (pullRequestUrl.Scheme != Uri.UriSchemeHttps && pullRequestUrl.Scheme != Uri.UriSchemeHttp))
        {
            ShowError("Enter a valid Azure DevOps or Bitbucket pull request URL.");
            return;
        }

        var source = _sources.SingleOrDefault(candidate => candidate.CanHandle(pullRequestUrl));
        if (source is null)
        {
            ShowError("This URL is not a supported Azure DevOps or Bitbucket pull request.");
            return;
        }

        if (_settings is null)
        {
            ShowError("Settings could not be loaded. Open the app again after fixing the local settings file.");
            return;
        }

        var token = source.Id == "azure-devops" ? _tokens.AzureDevOps : _tokens.Bitbucket;
        if (string.IsNullOrWhiteSpace(token))
        {
            var sourceName = source.Id == "azure-devops" ? "Azure DevOps" : "Bitbucket";
            ShowError($"Add a {sourceName} personal access token in Settings.");
            return;
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
            var reviewService = new PullRequestReviewService(_sources, _llmHosts, new DiffOnlyPrReviewAgentFactory());
            var result = await reviewService.ReviewAsync(new ReviewRequest(
                pullRequestUrl,
                token,
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
