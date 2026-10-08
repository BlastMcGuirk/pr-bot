using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.Win32;
using PR_Bot.Settings;
using PRBot.Core.Abstractions;
using PRBot.Core.Models;
using PRBot.Core.Sources;

namespace PR_Bot;

public sealed class SettingsWindow : Window
{
    private readonly AppSettings _currentSettings;
    private readonly SourceCredentialCollection _currentCredentials;
    private readonly AppSettingsStore _settingsStore;
    private readonly ISourceCredentialStore _credentialStore;
    private readonly IReadOnlyList<ILocalLlmHost> _hosts;
    private readonly ObservableCollection<SourceRowEditor> _sourceRows = [];
    private readonly DataGrid _sourceGrid = new();
    private readonly ComboBox _hostSelector = new() { MinWidth = 220 };
    private readonly TextBox _endpointBox = new() { MinWidth = 360, Padding = new Thickness(8) };
    private readonly ComboBox _modelSelector = new() { MinWidth = 300, DisplayMemberPath = nameof(ModelInfo.DisplayName) };
    private readonly Button _refreshModelsButton = new() { Content = "Refresh models", Padding = new Thickness(12, 7, 12, 7) };
    private readonly TextBox _instructionsPathBox = new() { Padding = new Thickness(8) };
    private readonly TextBox _instructionsEditor = new() { AcceptsReturn = true, AcceptsTab = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MinHeight = 190, Padding = new Thickness(10), FontFamily = new FontFamily("Consolas") };
    private readonly TextBlock _statusText = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _sourceStatusText = new() { TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.FromRgb(140, 47, 37)), Margin = new Thickness(0, 6, 0, 0) };
    private bool _initializing = true;

    public SettingsWindow(
        AppSettings settings,
        SourceCredentialCollection credentials,
        AppSettingsStore settingsStore,
        ISourceCredentialStore credentialStore,
        IReadOnlyList<ILocalLlmHost> hosts)
    {
        _currentSettings = settings;
        _currentCredentials = credentials;
        _settingsStore = settingsStore;
        _credentialStore = credentialStore;
        _hosts = hosts;

        Title = "PR Bot settings";
        Width = 1100;
        Height = 900;
        MinWidth = 980;
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

    public SourceCredentialCollection? SavedCredentials { get; private set; }

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

        page.Children.Add(SectionHeader("Pull request sources"));
        page.Children.Add(new TextBlock
        {
            Text = "Source URLs are matched to pull request hosts. Credentials are encrypted for your Windows account; leave credential fields blank to keep saved values.",
            Foreground = Brushes.DimGray,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10)
        });
        var addSourceButton = new Button { Content = "Add source", Padding = new Thickness(12, 7, 12, 7), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 8) };
        addSourceButton.Click += (_, _) => AddSourceRow();
        page.Children.Add(addSourceButton);
        page.Children.Add(BuildSourceTable());
        page.Children.Add(_sourceStatusText);
        foreach (var configuration in _currentSettings.PullRequestSources)
        {
            _sourceRows.Add(CreateSourceRowEditor(configuration,
                _currentCredentials.Entries.TryGetValue(configuration.Id, out var credential) ? credential : new SourceCredential()));
        }
        _sourceGrid.ItemsSource = _sourceRows;

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

    private UIElement BuildSourceTable()
    {
        _sourceGrid.AutoGenerateColumns = false;
        _sourceGrid.CanUserAddRows = false;
        _sourceGrid.CanUserDeleteRows = false;
        _sourceGrid.CanUserResizeRows = false;
        _sourceGrid.HeadersVisibility = DataGridHeadersVisibility.Column;
        _sourceGrid.GridLinesVisibility = DataGridGridLinesVisibility.All;
        _sourceGrid.SelectionUnit = DataGridSelectionUnit.FullRow;
        _sourceGrid.RowHeaderWidth = 0;
        _sourceGrid.MinWidth = 980;
        _sourceGrid.MinHeight = 80;
        _sourceGrid.MaxHeight = 330;
        _sourceGrid.Background = Brushes.White;
        _sourceGrid.AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(248, 247, 250));
        _sourceGrid.BorderBrush = new SolidColorBrush(Color.FromRgb(190, 185, 198));
        _sourceGrid.BorderThickness = new Thickness(1);
        _sourceGrid.CellStyle = CreateGridCellStyle();
        _sourceGrid.RowStyle = CreateGridRowStyle();
        _sourceGrid.ColumnHeaderStyle = new Style(typeof(DataGridColumnHeader))
        {
            Setters =
            {
                new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(239, 236, 244))),
                new Setter(Control.PaddingProperty, new Thickness(8, 7, 8, 7)),
                new Setter(Control.FontWeightProperty, FontWeights.SemiBold),
                new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Center),
                new Setter(Control.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(205, 201, 212))),
                new Setter(Control.BorderThicknessProperty, new Thickness(0, 0, 1, 1))
            }
        };
        _sourceGrid.Columns.Add(new DataGridComboBoxColumn
        {
            Header = "Source type",
            ItemsSource = PullRequestSourceCatalog.SourceTypes,
            DisplayMemberPath = nameof(PullRequestSourceType.DisplayName),
            SelectedValuePath = nameof(PullRequestSourceType.Id),
            SelectedValueBinding = new Binding(nameof(SourceRowEditor.SourceTypeId)) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
            ElementStyle = CreateComboBoxStyle(),
            EditingElementStyle = CreateComboBoxStyle(),
            Width = new DataGridLength(160)
        });
        _sourceGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Source URL",
            Binding = new Binding(nameof(SourceRowEditor.SourceUrl)) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
            ElementStyle = CreateCenteredTextBlockStyle(),
            EditingElementStyle = CreateCenteredTextBoxStyle(),
            Width = new DataGridLength(200)
        });
        _sourceGrid.Columns.Add(BuildAuthMethodColumn());
        _sourceGrid.Columns.Add(BuildCredentialsColumn());
        _sourceGrid.Columns.Add(BuildDeleteColumn());
        return new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = 350,
            Content = _sourceGrid
        };
    }

    private static Style CreateGridCellStyle()
    {
        var style = new Style(typeof(DataGridCell));
        style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Center));
        style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(6, 4, 6, 4)));
        var selectedTrigger = new Trigger { Property = DataGridCell.IsSelectedProperty, Value = true };
        selectedTrigger.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(241, 237, 247))));
        selectedTrigger.Setters.Add(new Setter(Control.ForegroundProperty, new SolidColorBrush(Color.FromRgb(38, 36, 42))));
        selectedTrigger.Setters.Add(new Setter(Control.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(199, 190, 213))));
        style.Triggers.Add(selectedTrigger);
        return style;
    }

    private static Style CreateGridRowStyle()
    {
        var style = new Style(typeof(DataGridRow));
        style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.White));
        style.Setters.Add(new Setter(Control.ForegroundProperty, new SolidColorBrush(Color.FromRgb(38, 36, 42))));
        var selectedTrigger = new Trigger { Property = DataGridRow.IsSelectedProperty, Value = true };
        selectedTrigger.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(241, 237, 247))));
        selectedTrigger.Setters.Add(new Setter(Control.ForegroundProperty, new SolidColorBrush(Color.FromRgb(38, 36, 42))));
        style.Triggers.Add(selectedTrigger);
        return style;
    }

    private static Style CreateComboBoxStyle()
    {
        var style = new Style(typeof(ComboBox));
        style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Center));
        style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
        return style;
    }

    private static Style CreateCenteredTextBlockStyle()
    {
        var style = new Style(typeof(TextBlock));
        style.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Center));
        style.Setters.Add(new Setter(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center));
        return style;
    }

    private static Style CreateCenteredTextBoxStyle()
    {
        var style = new Style(typeof(TextBox));
        style.Setters.Add(new Setter(TextBox.TextAlignmentProperty, TextAlignment.Center));
        style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Center));
        style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
        return style;
    }

    private void AddSourceRow()
    {
        var type = PullRequestSourceCatalog.SourceTypes[0];
        _sourceRows.Add(CreateSourceRowEditor(new PullRequestSourceConfiguration
        {
            SourceTypeId = type.Id,
            SourceUrl = type.DefaultSourceUrl,
            AuthenticationMethod = type.AuthenticationMethods[0]
        }, new SourceCredential()));
    }

    private SourceRowEditor CreateSourceRowEditor(PullRequestSourceConfiguration configuration, SourceCredential credential)
    {
        var editor = new SourceRowEditor(configuration, credential);
        editor.PropertyChanged += (_, _) => _sourceStatusText.Text = string.Empty;
        return editor;
    }

    private static DataGridTemplateColumn BuildAuthMethodColumn()
    {
        var comboBox = new FrameworkElementFactory(typeof(ComboBox));
        comboBox.SetValue(ItemsControl.DisplayMemberPathProperty, nameof(AuthenticationMethodOption.DisplayName));
        comboBox.SetValue(Selector.SelectedValuePathProperty, nameof(AuthenticationMethodOption.Method));
        comboBox.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(nameof(SourceRowEditor.AuthenticationOptions)));
        comboBox.SetBinding(Selector.SelectedValueProperty, new Binding(nameof(SourceRowEditor.AuthenticationMethod))
        {
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
        });
        comboBox.SetValue(Control.PaddingProperty, new Thickness(4));
        comboBox.SetValue(Control.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
        comboBox.SetValue(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Center);
        comboBox.SetValue(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center);
        return new DataGridTemplateColumn
        {
            Header = "Auth method",
            CellTemplate = new DataTemplate { VisualTree = comboBox },
            Width = new DataGridLength(175)
        };
    }

    private static DataGridTemplateColumn BuildCredentialsColumn()
    {
        var root = new FrameworkElementFactory(typeof(StackPanel));
        var patPanel = new FrameworkElementFactory(typeof(StackPanel));
        patPanel.SetValue(FrameworkElement.StyleProperty, CreateCredentialPanelStyle(SourceAuthenticationMethod.PersonalAccessToken));
        AddTextBlock(patPanel, "Personal access token (leave blank to keep saved)");
        var patBox = new FrameworkElementFactory(typeof(PasswordBox));
        patBox.SetValue(Control.PaddingProperty, new Thickness(5));
        patBox.SetValue(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Center);
        patBox.SetValue(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center);
        patBox.AddHandler(FrameworkElement.LoadedEvent, new RoutedEventHandler(PatPasswordBoxLoaded));
        patBox.AddHandler(PasswordBox.PasswordChangedEvent, new RoutedEventHandler(PatPasswordChanged));
        patPanel.AppendChild(patBox);
        root.AppendChild(patPanel);

        var usernamePasswordPanel = new FrameworkElementFactory(typeof(StackPanel));
        usernamePasswordPanel.SetValue(FrameworkElement.StyleProperty, CreateCredentialPanelStyle(SourceAuthenticationMethod.UsernamePassword));
        AddTextBlock(usernamePasswordPanel, "Username");
        var usernameBox = new FrameworkElementFactory(typeof(TextBox));
        usernameBox.SetValue(Control.PaddingProperty, new Thickness(5));
        usernameBox.SetValue(TextBox.TextAlignmentProperty, TextAlignment.Center);
        usernameBox.SetValue(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Center);
        usernameBox.SetBinding(TextBox.TextProperty, new Binding(nameof(SourceRowEditor.UsernameInput))
        {
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
        });
        usernamePasswordPanel.AppendChild(usernameBox);
        AddTextBlock(usernamePasswordPanel, "Password (leave blank to keep saved)");
        var passwordBox = new FrameworkElementFactory(typeof(PasswordBox));
        passwordBox.SetValue(Control.PaddingProperty, new Thickness(5));
        passwordBox.SetValue(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Center);
        passwordBox.SetValue(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center);
        passwordBox.AddHandler(FrameworkElement.LoadedEvent, new RoutedEventHandler(PasswordBoxLoaded));
        passwordBox.AddHandler(PasswordBox.PasswordChangedEvent, new RoutedEventHandler(PasswordChanged));
        usernamePasswordPanel.AppendChild(passwordBox);
        root.AppendChild(usernamePasswordPanel);

        var clearCredentials = new FrameworkElementFactory(typeof(CheckBox));
        clearCredentials.SetValue(ContentControl.ContentProperty, "Clear saved credentials");
        clearCredentials.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        clearCredentials.SetBinding(ToggleButton.IsCheckedProperty, new Binding(nameof(SourceRowEditor.ClearCredentials))
        {
            Mode = BindingMode.TwoWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
        });
        root.AppendChild(clearCredentials);

        return new DataGridTemplateColumn
        {
            Header = "Auth credentials",
            CellTemplate = new DataTemplate { VisualTree = root },
            Width = new DataGridLength(390)
        };
    }

    private DataGridTemplateColumn BuildDeleteColumn()
    {
        var deleteButton = new FrameworkElementFactory(typeof(Button));
        deleteButton.SetValue(ContentControl.ContentProperty, "Remove");
        deleteButton.SetValue(Control.PaddingProperty, new Thickness(5, 3, 5, 3));
        deleteButton.SetValue(Control.ForegroundProperty, new SolidColorBrush(Color.FromRgb(140, 47, 37)));
        deleteButton.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        deleteButton.SetValue(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Center);
        deleteButton.AddHandler(Button.ClickEvent, new RoutedEventHandler(DeleteSourceRow));
        return new DataGridTemplateColumn
        {
            Header = string.Empty,
            CellTemplate = new DataTemplate { VisualTree = deleteButton },
            Width = new DataGridLength(75)
        };
    }

    private static Style CreateCredentialPanelStyle(SourceAuthenticationMethod method)
    {
        var style = new Style(typeof(StackPanel));
        style.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Collapsed));
        var trigger = new DataTrigger
        {
            Binding = new Binding(nameof(SourceRowEditor.AuthenticationMethod)),
            Value = method
        };
        trigger.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible));
        style.Triggers.Add(trigger);
        return style;
    }

    private static void AddTextBlock(FrameworkElementFactory panel, string text)
    {
        var label = new FrameworkElementFactory(typeof(TextBlock));
        label.SetValue(TextBlock.TextProperty, text);
        label.SetValue(TextBlock.ForegroundProperty, Brushes.DimGray);
        label.SetValue(TextBlock.FontSizeProperty, 11d);
        label.SetValue(TextBlock.TextAlignmentProperty, TextAlignment.Center);
        label.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        panel.AppendChild(label);
    }

    private static void PatPasswordChanged(object sender, RoutedEventArgs args)
    {
        if (sender is PasswordBox passwordBox && passwordBox.DataContext is SourceRowEditor editor)
        {
            editor.PersonalAccessTokenInput = passwordBox.Password;
        }
    }

    private static void PatPasswordBoxLoaded(object sender, RoutedEventArgs args)
    {
        if (sender is PasswordBox passwordBox
            && passwordBox.DataContext is SourceRowEditor editor
            && passwordBox.Password != editor.PersonalAccessTokenInput)
        {
            passwordBox.Password = editor.PersonalAccessTokenInput;
        }
    }

    private static void PasswordChanged(object sender, RoutedEventArgs args)
    {
        if (sender is PasswordBox passwordBox && passwordBox.DataContext is SourceRowEditor editor)
        {
            editor.PasswordInput = passwordBox.Password;
        }
    }

    private static void PasswordBoxLoaded(object sender, RoutedEventArgs args)
    {
        if (sender is PasswordBox passwordBox
            && passwordBox.DataContext is SourceRowEditor editor
            && passwordBox.Password != editor.PasswordInput)
        {
            passwordBox.Password = editor.PasswordInput;
        }
    }

    private void DeleteSourceRow(object sender, RoutedEventArgs args)
    {
        if (sender is FrameworkElement { DataContext: SourceRowEditor editor })
        {
            _sourceRows.Remove(editor);
            _sourceStatusText.Text = string.Empty;
        }
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
        _sourceStatusText.Text = string.Empty;
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

        if (!TryBuildSourceSettings(out var sourceConfigurations, out var credentials, out var sourceError))
        {
            _sourceStatusText.Text = sourceError;
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
                AgentInstructionsPath = Path.GetFullPath(_instructionsPathBox.Text.Trim()),
                PullRequestSources = sourceConfigurations
            };
            var instructionDirectory = Path.GetDirectoryName(newSettings.AgentInstructionsPath);
            if (!string.IsNullOrWhiteSpace(instructionDirectory))
            {
                Directory.CreateDirectory(instructionDirectory);
            }

            await File.WriteAllTextAsync(newSettings.AgentInstructionsPath, _instructionsEditor.Text);
            await _credentialStore.SaveAsync(credentials);
            await _settingsStore.SaveAsync(newSettings);
            SavedSettings = newSettings;
            SavedCredentials = credentials;
            DialogResult = true;
        }
        catch (Exception ex)
        {
            _statusText.Text = $"Could not save settings: {ex.Message}";
            saveButton.IsEnabled = true;
        }
    }

    private bool TryBuildSourceSettings(
        out List<PullRequestSourceConfiguration> configurations,
        out SourceCredentialCollection credentials,
        out string error)
    {
        configurations = [];
        credentials = new SourceCredentialCollection();
        foreach (var editor in _sourceRows)
        {
            var sourceType = PullRequestSourceCatalog.Find(editor.SourceTypeId);
            var authenticationMethod = editor.AuthenticationMethod;
            if (sourceType is null)
            {
                error = "Choose a supported source type and authentication method for every row.";
                return false;
            }

            if (!sourceType.AuthenticationMethods.Contains(authenticationMethod))
            {
                error = $"{sourceType.DisplayName} does not support {authenticationMethod} authentication.";
                return false;
            }

            var sourceUrl = editor.SourceUrl.Trim();
            try
            {
                _ = PullRequestSourceCatalog.GetHost(sourceUrl);
            }
            catch (ArgumentException)
            {
                error = $"Enter a valid source URL or domain for {sourceType.DisplayName}.";
                return false;
            }

            var configuration = new PullRequestSourceConfiguration
            {
                Id = editor.Configuration.Id,
                SourceTypeId = sourceType.Id,
                SourceUrl = sourceUrl,
                AuthenticationMethod = authenticationMethod
            };
            var credential = editor.ClearCredentials
                ? new SourceCredential()
                : new SourceCredential
                {
                    PersonalAccessToken = editor.Credential.PersonalAccessToken,
                    Username = editor.Credential.Username,
                    Password = editor.Credential.Password
                };

            if (authenticationMethod == SourceAuthenticationMethod.PersonalAccessToken)
            {
                if (!string.IsNullOrWhiteSpace(editor.PersonalAccessTokenInput)
                    && (!editor.ClearCredentials
                        || !editor.PersonalAccessTokenInput.Equals(editor.Credential.PersonalAccessToken, StringComparison.Ordinal)))
                {
                    credential.PersonalAccessToken = editor.PersonalAccessTokenInput.Trim();
                }
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(editor.UsernameInput)
                    && (!editor.ClearCredentials
                        || !editor.UsernameInput.Equals(editor.Credential.Username, StringComparison.Ordinal)))
                {
                    credential.Username = editor.UsernameInput.Trim();
                }

                if (!string.IsNullOrWhiteSpace(editor.PasswordInput)
                    && (!editor.ClearCredentials
                        || !editor.PasswordInput.Equals(editor.Credential.Password, StringComparison.Ordinal)))
                {
                    credential.Password = editor.PasswordInput;
                }

                if (string.IsNullOrWhiteSpace(credential.Username) != string.IsNullOrWhiteSpace(credential.Password))
                {
                    error = $"Enter both username and password for {sourceType.DisplayName}, or clear the saved credentials.";
                    return false;
                }
            }

            configurations.Add(configuration);
            credentials.Entries[configuration.Id] = credential;
        }

        try
        {
            if (!PullRequestSourceCatalog.TryValidateUniqueHosts(configurations, out var duplicateHost))
            {
                error = $"More than one source row uses the host '{duplicateHost}'. Source URLs must be unique.";
                return false;
            }
        }
        catch (ArgumentException)
        {
            error = "Enter a valid source URL or domain in each source row.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private sealed class SourceRowEditor : INotifyPropertyChanged
    {
        private string _personalAccessTokenInput = string.Empty;
        private string _usernameInput = string.Empty;
        private string _passwordInput = string.Empty;
        private bool _clearCredentials;

        public SourceRowEditor(PullRequestSourceConfiguration configuration, SourceCredential credential)
        {
            Configuration = configuration;
            Credential = credential;
            _personalAccessTokenInput = credential.PersonalAccessToken;
            _usernameInput = credential.Username;
            _passwordInput = credential.Password;
            AuthenticationOptions = GetAuthenticationOptions(PullRequestSourceCatalog.Find(configuration.SourceTypeId));
            if (AuthenticationOptions.All(option => option.Method != configuration.AuthenticationMethod)
                && AuthenticationOptions.Count > 0)
            {
                Configuration.AuthenticationMethod = AuthenticationOptions[0].Method;
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public PullRequestSourceConfiguration Configuration { get; }

        public SourceCredential Credential { get; }

        public string SourceTypeId
        {
            get => Configuration.SourceTypeId;
            set
            {
                if (string.Equals(Configuration.SourceTypeId, value, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                var previousType = PullRequestSourceCatalog.Find(Configuration.SourceTypeId);
                var nextType = PullRequestSourceCatalog.Find(value);
                var shouldUpdateUrl = string.IsNullOrWhiteSpace(SourceUrl)
                                      || (previousType is not null && SourceUrl.Equals(previousType.DefaultSourceUrl, StringComparison.OrdinalIgnoreCase));
                Configuration.SourceTypeId = value;
                if (nextType is not null && shouldUpdateUrl)
                {
                    SourceUrl = nextType.DefaultSourceUrl;
                }

                AuthenticationOptions = GetAuthenticationOptions(nextType);
                if (AuthenticationOptions.All(option => option.Method != Configuration.AuthenticationMethod)
                    && AuthenticationOptions.Count > 0)
                {
                    AuthenticationMethod = AuthenticationOptions[0].Method;
                }

                OnPropertyChanged();
                OnPropertyChanged(nameof(AuthenticationOptions));
                OnPropertyChanged(nameof(AuthenticationMethod));
            }
        }

        public string SourceUrl
        {
            get => Configuration.SourceUrl;
            set
            {
                if (Configuration.SourceUrl == value)
                {
                    return;
                }

                Configuration.SourceUrl = value;
                OnPropertyChanged();
            }
        }

        public SourceAuthenticationMethod AuthenticationMethod
        {
            get => Configuration.AuthenticationMethod;
            set
            {
                if (Configuration.AuthenticationMethod == value)
                {
                    return;
                }

                Configuration.AuthenticationMethod = value;
                OnPropertyChanged();
            }
        }

        public IReadOnlyList<AuthenticationMethodOption> AuthenticationOptions { get; private set; }

        public string PersonalAccessTokenInput
        {
            get => _personalAccessTokenInput;
            set => SetField(ref _personalAccessTokenInput, value);
        }

        public string UsernameInput
        {
            get => _usernameInput;
            set => SetField(ref _usernameInput, value);
        }

        public string PasswordInput
        {
            get => _passwordInput;
            set => SetField(ref _passwordInput, value);
        }

        public bool ClearCredentials
        {
            get => _clearCredentials;
            set => SetField(ref _clearCredentials, value);
        }

        private static IReadOnlyList<AuthenticationMethodOption> GetAuthenticationOptions(PullRequestSourceType? sourceType) =>
            sourceType?.AuthenticationMethods.Select(method => new AuthenticationMethodOption(
                method,
                method == SourceAuthenticationMethod.PersonalAccessToken ? "Personal access token (PAT)" : "Username + password")).ToArray()
            ?? [];

        private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
            {
                return false;
            }

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private sealed record AuthenticationMethodOption(SourceAuthenticationMethod Method, string DisplayName);

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
