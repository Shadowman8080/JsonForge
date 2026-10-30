using System.Collections.ObjectModel;
using JsonForge.Core.Ai;
using JsonForge.Core.Json;
using JsonForge.Services;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Windows.Storage;

namespace JsonForge.ViewModels;

public enum EditorPanel
{
    Preview,
    Tree,
    Problems,
    Ai,
    Settings,
}

public partial class MainViewModel : ObservableObject
{
    private readonly IJsonAiService _ai;
    private readonly AiRuntimeState _aiState;
    private readonly IUserSettingsService _settings;
    private readonly IFilePickerService _files;
    private readonly DispatcherQueue _dispatcher;
    private DispatcherQueueTimer? _validateTimer;
    private DispatcherQueueTimer? _autoFormatTimer;
    private CancellationTokenSource? _aiCts;

    private StorageFile? _currentFile;
    private string? _currentFileToken;
    private bool _suppressDirty;
    private bool _suppressAutoFormat;
    private bool _suppressSettingsPersist;

    private readonly AiConfigService? _aiConfigSvc;
    private readonly LocalLlmManager? _localLlm;

    public MainViewModel(
        IJsonAiService ai,
        AiRuntimeState aiState,
        OllamaBootstrapper ollama,
        IUserSettingsService settings,
        IFilePickerService files,
        AiConfigService? aiConfigSvc = null,
        LocalLlmManager? localLlm = null)
    {
        _ai = ai;
        _aiState = aiState;
        _settings = settings;
        _files = files;
        _aiConfigSvc = aiConfigSvc;
        _localLlm = localLlm;
        _dispatcher = DispatcherQueue.GetForCurrentThread();

        ApplyAiState(aiState.Current);
        ApplyUserSettings(settings.Current);
        CurrentFileName = "Untitled";

        // Populate the initial document without marking it as unsaved work.
        _suppressDirty = true;
        try
        {
            Source = SampleJson;
            IsDirty = false;
        }
        finally
        {
            _suppressDirty = false;
        }

        StatusText = "Ready";
        DetectedFormatText = string.Empty;
        LastActionText = string.Empty;
        AiInstruction = string.Empty;
        AiOutput = string.Empty;
        AiSummary = string.Empty;
        AiError = string.Empty;

        aiState.Changed += config =>
        {
            _dispatcher.TryEnqueue(() =>
            {
                ApplyAiState(config);
                OnPropertyChanged(nameof(AiPanelVisibility));
                OnPropertyChanged(nameof(AiGuidanceVisibility));
            });
        };

        // Best-effort: start local AI (Ollama) automatically if available.
        ollama.StartInBackground();

        settings.Changed += s =>
        {
            _dispatcher.TryEnqueue(() =>
            {
                ApplyUserSettings(s);
                Validate();
                RefreshRecentFiles();
            });
        };

        RefreshRecentFiles();
        RefreshTree();
    }

    private void ApplyUserSettings(UserSettings s)
    {
        _suppressSettingsPersist = true;
        try
        {
            Indent = s.Indent;
            SortKeysDefault = s.SortKeys;
            RelaxedEscaping = s.RelaxedEscaping;
            TrailingNewline = s.TrailingNewline;
            ValidationStrictness = s.Strictness;
            AutoFormatOnPaste = s.AutoFormatOnPaste;
            AutoFormatOnIdle = s.AutoFormatOnIdle;
            AutoFormatIdleMs = s.AutoFormatIdleMs;

            AiEnabledSetting = s.AiEnabled;
            AiAutoEnableSetting = s.AiAutoEnable;
            AiProviderNameSetting = string.IsNullOrWhiteSpace(s.AiProviderName) ? "AI" : s.AiProviderName;
            AiModelSetting = s.AiModel ?? string.Empty;
            AiEndpointSetting = s.AiEndpoint ?? string.Empty;
            AiApiKeySetting = s.AiApiKey ?? string.Empty;
            AiMaxTokensSetting = s.AiMaxTokens > 0 ? s.AiMaxTokens : 4096;
        }
        finally
        {
            _suppressSettingsPersist = false;
        }
    }

    private void ApplyAiState(AiRuntimeConfig config)
    {
        AiProviderName = string.IsNullOrWhiteSpace(config.ProviderName)
            ? (config.Enabled ? "AI" : "AI not configured")
            : config.ProviderName;
        AiConfigured = config.Enabled;
        AiGuidanceText = config.Enabled
            ? string.Empty
            : (config.DiagnosticsMessage ??
               "AI is not configured. JsonForge auto-detects local OpenAI-compatible endpoints (recommended: Ollama at http://localhost:11434/v1). " +
               "Install/start Ollama and pull a model (e.g. `ollama pull qwen2.5-coder:7b`).");
    }

    public ObservableCollection<JsonDiagnostic> Problems { get; } = [];
    public ObservableCollection<RecentFileEntry> RecentFiles { get; } = [];
    public ObservableCollection<JsonTreeNode> TreeNodes { get; } = [];

    public IReadOnlyList<JsonLexeme>? PreviewLexemes { get; private set; }

    /// <summary>Raised on the UI thread when the token preview must be re-rendered.</summary>
    public event Action? PreviewRefreshed;

    /// <summary>Raised when the user clicks a problem; the view highlights that text span.</summary>
    public event Action<int, int>? NavigateRequested;

    public static string SampleJson { get; } = """
        {
          "name": "JsonForge",
          "version": "0.1.0",
          "description": "A modern, AI-assisted JSON editor built with Uno Platform and .NET 10",
          "features": [
            "pretty print",
            "minify",
            "auto-repair",
            "sort keys"
          ],
          "preferences": {
            "theme": "dark",
            "indent": 2,
            "relaxedEscaping": true,
            "aiEnabled": false
          },
          "metrics": {
            "edits": 0,
            "lines": 120,
            "rating": 4.7
          }
        }
        """;

    [ObservableProperty]
    public partial string Source { get; set; }

    [ObservableProperty]
    public partial bool IsValid { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; }

    [ObservableProperty]
    public partial string LastActionText { get; set; }

    [ObservableProperty]
    public partial string DetectedFormatText { get; set; }

    [ObservableProperty]
    public partial string AiProviderName { get; set; }

    [ObservableProperty]
    public partial bool AiConfigured { get; set; }

    [ObservableProperty]
    public partial string AiGuidanceText { get; set; }

    [ObservableProperty]
    public partial bool AiBusy { get; set; }

    [ObservableProperty]
    public partial string AiInstruction { get; set; }

    [ObservableProperty]
    public partial string AiOutput { get; set; }

    [ObservableProperty]
    public partial string AiSummary { get; set; }

    [ObservableProperty]
    public partial string AiError { get; set; }

    [ObservableProperty]
    public partial EditorPanel SelectedPanel { get; set; }

    public bool IsPreviewSelected => SelectedPanel == EditorPanel.Preview;

    public bool IsTreeSelected => SelectedPanel == EditorPanel.Tree;

    public bool IsProblemsSelected => SelectedPanel == EditorPanel.Problems;

    public bool IsAiSelected => SelectedPanel == EditorPanel.Ai;

    public bool IsSettingsSelected => SelectedPanel == EditorPanel.Settings;

    public Visibility ProblemsListVisibility => Problems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ProblemsEmptyVisibility => Problems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    public Visibility AiPanelVisibility => AiConfigured ? Visibility.Visible : Visibility.Collapsed;

    public Visibility AiGuidanceVisibility => AiConfigured ? Visibility.Collapsed : Visibility.Visible;

    partial void OnSourceChanged(string value)
    {
        AiOutput = string.Empty;
        AiSummary = string.Empty;
        LastActionText = string.Empty;
        StatusText = "Editing...";
        if (!_suppressDirty)
        {
            IsDirty = true;
        }
        RefreshTree();
        ScheduleValidation();
        ScheduleAutoFormat();
    }

    partial void OnSelectedPanelChanged(EditorPanel value)
    {
        OnPropertyChanged(nameof(IsPreviewSelected));
        OnPropertyChanged(nameof(IsTreeSelected));
        OnPropertyChanged(nameof(IsProblemsSelected));
        OnPropertyChanged(nameof(IsAiSelected));
        OnPropertyChanged(nameof(IsSettingsSelected));
    }

    partial void OnAiBusyChanged(bool value)
    {
        AiFixCommand.NotifyCanExecuteChanged();
        AiFormatCommand.NotifyCanExecuteChanged();
        AiTransformCommand.NotifyCanExecuteChanged();
        AiGenerateCommand.NotifyCanExecuteChanged();
        AiExplainCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void ShowPreview() => SelectedPanel = EditorPanel.Preview;

    [RelayCommand]
    private void ShowTree() => SelectedPanel = EditorPanel.Tree;

    [RelayCommand]
    private void ShowProblems() => SelectedPanel = EditorPanel.Problems;

    [RelayCommand]
    private void ShowAi() => SelectedPanel = EditorPanel.Ai;

    [RelayCommand]
    private void ShowSettings() => SelectedPanel = EditorPanel.Settings;

    [ObservableProperty]
    public partial JsonIndentStyle Indent { get; set; }

    [ObservableProperty]
    public partial bool SortKeysDefault { get; set; }

    [ObservableProperty]
    public partial bool RelaxedEscaping { get; set; }

    [ObservableProperty]
    public partial bool TrailingNewline { get; set; }

    [ObservableProperty]
    public partial JsonValidationStrictness ValidationStrictness { get; set; }

    [ObservableProperty]
    public partial bool AutoFormatOnPaste { get; set; }

    [ObservableProperty]
    public partial bool AutoFormatOnIdle { get; set; }

    [ObservableProperty]
    public partial int AutoFormatIdleMs { get; set; }

    [ObservableProperty]
    public partial bool AiEnabledSetting { get; set; }

    [ObservableProperty]
    public partial bool AiAutoEnableSetting { get; set; }

    [ObservableProperty]
    public partial string AiProviderNameSetting { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string AiModelSetting { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string AiEndpointSetting { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string AiApiKeySetting { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int AiMaxTokensSetting { get; set; } = 4096;

    [ObservableProperty]
    public partial bool IsDirty { get; set; }

    [ObservableProperty]
    public partial string CurrentFileName { get; set; }

    public int IndentIndex
    {
        get => Indent switch
        {
            JsonIndentStyle.Minified => 0,
            JsonIndentStyle.TwoSpaces => 1,
            JsonIndentStyle.FourSpaces => 2,
            JsonIndentStyle.Tab => 3,
            _ => 1,
        };
        set
        {
            Indent = value switch
            {
                0 => JsonIndentStyle.Minified,
                2 => JsonIndentStyle.FourSpaces,
                3 => JsonIndentStyle.Tab,
                _ => JsonIndentStyle.TwoSpaces,
            };
            OnPropertyChanged();
        }
    }

    public int ValidationStrictnessIndex
    {
        get => ValidationStrictness switch
        {
            JsonValidationStrictness.StrictJson => 0,
            JsonValidationStrictness.Jsonc => 1,
            JsonValidationStrictness.Jsonish => 2,
            _ => 0,
        };
        set
        {
            ValidationStrictness = value switch
            {
                1 => JsonValidationStrictness.Jsonc,
                2 => JsonValidationStrictness.Jsonish,
                _ => JsonValidationStrictness.StrictJson,
            };
            OnPropertyChanged();
        }
    }

    partial void OnIndentChanged(JsonIndentStyle value)
    {
        PersistSettings();
        OnPropertyChanged(nameof(IndentIndex));
    }

    partial void OnValidationStrictnessChanged(JsonValidationStrictness value)
    {
        PersistSettings();
        OnPropertyChanged(nameof(ValidationStrictnessIndex));
    }

    partial void OnSortKeysDefaultChanged(bool value) => PersistSettings();
    partial void OnRelaxedEscapingChanged(bool value) => PersistSettings();
    partial void OnTrailingNewlineChanged(bool value) => PersistSettings();
    partial void OnAutoFormatOnPasteChanged(bool value) => PersistSettings();
    partial void OnAutoFormatOnIdleChanged(bool value) => PersistSettings();
    partial void OnAutoFormatIdleMsChanged(int value) => PersistSettings();
    partial void OnAiEnabledSettingChanged(bool value) { PersistSettings(); _aiConfigSvc?.ApplyFromSettings(_aiState); }
    partial void OnAiAutoEnableSettingChanged(bool value) => PersistSettings();
    partial void OnAiProviderNameSettingChanged(string value) { PersistSettings(); _aiConfigSvc?.ApplyFromSettings(_aiState); }
    partial void OnAiModelSettingChanged(string value) { PersistSettings(); _aiConfigSvc?.ApplyFromSettings(_aiState); }
    partial void OnAiEndpointSettingChanged(string value) { PersistSettings(); _aiConfigSvc?.ApplyFromSettings(_aiState); }
    partial void OnAiApiKeySettingChanged(string value) { PersistSettings(); _aiConfigSvc?.ApplyFromSettings(_aiState); }
    partial void OnAiMaxTokensSettingChanged(int value) { PersistSettings(); _aiConfigSvc?.ApplyFromSettings(_aiState); }

    private void PersistSettings()
    {
        if (_suppressSettingsPersist)
        {
            return;
        }

        var current = _settings.Current;
        _settings.Update(current with
        {
            Indent = Indent,
            SortKeys = SortKeysDefault,
            RelaxedEscaping = RelaxedEscaping,
            TrailingNewline = TrailingNewline,
            Strictness = ValidationStrictness,
            AutoFormatOnPaste = AutoFormatOnPaste,
            AutoFormatOnIdle = AutoFormatOnIdle,
            AutoFormatIdleMs = Math.Clamp(AutoFormatIdleMs, 200, 5000),
            AiEnabled = AiEnabledSetting,
            AiAutoEnable = AiAutoEnableSetting,
            AiProviderName = string.IsNullOrWhiteSpace(AiProviderNameSetting) ? "AI" : AiProviderNameSetting.Trim(),
            AiModel = string.IsNullOrWhiteSpace(AiModelSetting) ? null : AiModelSetting.Trim(),
            AiEndpoint = string.IsNullOrWhiteSpace(AiEndpointSetting) ? null : AiEndpointSetting.Trim(),
            AiApiKey = string.IsNullOrWhiteSpace(AiApiKeySetting) ? null : AiApiKeySetting.Trim(),
            AiMaxTokens = Math.Clamp(AiMaxTokensSetting, 256, 32768),
        });
    }

    private void RefreshRecentFiles()
    {
        _dispatcher.TryEnqueue(() =>
        {
            RecentFiles.Clear();
            foreach (var r in _settings.Current.RecentFiles.OrderByDescending(x => x.LastOpenedUtc))
            {
                RecentFiles.Add(r);
            }
            OnPropertyChanged(nameof(RecentFiles));
        });
    }

    private void RefreshTree()
    {
        _dispatcher.TryEnqueue(() =>
        {
            TreeNodes.Clear();
            var root = JsonTreeNode.Build(Source);
            TreeNodes.Add(root);
        });
    }

    [RelayCommand]
    private async Task OpenRecentAsync(RecentFileEntry? entry)
    {
        if (entry is null || string.IsNullOrWhiteSpace(entry.Token))
        {
            return;
        }

        var file = await _files.TryGetFromTokenAsync(entry.Token);
        if (file is null)
        {
            StatusText = $"Could not open recent file: {entry.DisplayName}";
            return;
        }

        var text = await _files.ReadTextAsync(file);

        _suppressDirty = true;
        try
        {
            _currentFile = file;
            _currentFileToken = entry.Token;
            CurrentFileName = file.Name;
            Source = text;
            IsDirty = false;
            AddRecentFile(entry.Token, file.Name);
            LastActionText = $"Opened {file.Name}.";
        }
        finally
        {
            _suppressDirty = false;
        }

        Validate();
    }

    [RelayCommand]
    private async Task StartLocalAiAsync()
    {
        if (_localLlm is null)
        {
            StatusText = "Local LLM manager not available.";
            return;
        }
        var model = string.IsNullOrWhiteSpace(AiModelSetting) ? "qwen2.5-coder:3b" : AiModelSetting.Trim();
        StatusText = $"Starting local AI ({model})...";
        await _localLlm.StartAsync(model);
        _aiConfigSvc?.ApplyFromSettings(_aiState);
        ApplyUserSettings(_settings.Current);
        StatusText = "Local AI started.";
        ShowAi();
    }

    [RelayCommand]
    private void ClearRecentFiles()
    {
        var current = _settings.Current;
        _settings.Update(current with { RecentFiles = Array.Empty<RecentFileEntry>() });
        RefreshRecentFiles();
        StatusText = "Cleared recent files.";
    }

    [RelayCommand]
    private void LoadSample()
    {
        Source = SampleJson;
        Validate();
    }

    [RelayCommand]
    private void Format() => ApplyFormatting(BuildDefaultFormatOptions());

    [RelayCommand]
    private void Minify() => ApplyFormatting(BuildDefaultFormatOptions() with { Indent = JsonIndentStyle.Minified });

    [RelayCommand]
    private void SortKeys() => ApplyFormatting(BuildDefaultFormatOptions() with { SortKeys = true });

    [RelayCommand]
    private void NewDocument()
    {
        if (IsDirty)
        {
            // Best-effort: for now just proceed; UI can prompt separately
        }

        _currentFile = null;
        _currentFileToken = null;
        CurrentFileName = "Untitled";

        _suppressDirty = true;
        try
        {
            Source = string.Empty;
            IsDirty = false;
        }
        finally
        {
            _suppressDirty = false;
        }

        Validate();
    }

    [RelayCommand]
    private async Task OpenFileAsync()
    {
        if (IsDirty)
        {
            // Best-effort: proceed; UI can prompt separately
        }

        var picked = await _files.PickOpenAsync();
        if (picked is null)
        {
            return;
        }

        var (file, token) = picked.Value;
        var text = await _files.ReadTextAsync(file);

        _suppressDirty = true;
        try
        {
            _currentFile = file;
            _currentFileToken = token;
            CurrentFileName = file.Name;
            Source = text;
            IsDirty = false;
            AddRecentFile(token, file.Name);
            LastActionText = $"Opened {file.Name}.";
        }
        finally
        {
            _suppressDirty = false;
        }

        Validate();
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (_currentFile is null)
        {
            await SaveAsAsync();
            return;
        }

        await _files.WriteTextAsync(_currentFile, Source ?? string.Empty);
        IsDirty = false;
        LastActionText = $"Saved {CurrentFileName}.";
        Validate();
    }

    [RelayCommand]
    private async Task SaveAsAsync()
    {
        var suggested = string.IsNullOrWhiteSpace(CurrentFileName) || CurrentFileName == "Untitled"
            ? "document.json"
            : CurrentFileName;

        var picked = await _files.PickSaveAsync(suggested);
        if (picked is null)
        {
            return;
        }

        var (file, token) = picked.Value;
        _currentFile = file;
        _currentFileToken = token;
        CurrentFileName = file.Name;
        AddRecentFile(token, file.Name);
        await SaveAsync();
    }

    private void AddRecentFile(string token, string name)
    {
        var current = _settings.Current;
        var list = current.RecentFiles
            .Where(r => r.Token != token)
            .Prepend(new RecentFileEntry(token, name, DateTimeOffset.UtcNow))
            .Take(Math.Clamp(current.RecentFilesMax, 1, 25))
            .ToArray();

        _settings.Update(current with { RecentFiles = list });
    }

    [RelayCommand]
    private void AutoRepair()
    {
        var repair = JsonRepairer.Repair(Source, new JsonRepairOptions
        {
            PrettyPrint = true,
            FormatOptions = BuildDefaultFormatOptions() with { Indent = Indent == JsonIndentStyle.Minified ? JsonIndentStyle.TwoSpaces : Indent },
        });
        Source = repair.Repaired;
        StatusText = repair.IsValid
            ? $"Repaired â€” {repair.Actions.Count} fix(es) applied."
            : "Repair produced JSON but it is still invalid. Check the Problems list.";
        Validate();
    }

    private void ApplyFormatting(JsonFormatOptions options)
    {
        if (JsonSmartFormatter.TryFormat(Source, options, out var formatted, out var status, out _))
        {
            // Assign Source first: OnSourceChanged clears LastActionText.
            Source = formatted;
            LastActionText = status;
        }
        else
        {
            LastActionText = status;
        }

        Validate();
    }

    private JsonFormatOptions BuildDefaultFormatOptions() => new()
    {
        Indent = Indent,
        SortKeys = SortKeysDefault,
        RelaxedEscaping = RelaxedEscaping,
        TrailingNewline = TrailingNewline,
    };

    [RelayCommand]
    private void SelectProblem(JsonDiagnostic? diagnostic)
    {
        if (diagnostic is null)
        {
            return;
        }

        NavigateRequested?.Invoke(diagnostic.Offset, Math.Max(1, diagnostic.Length));
    }

    [RelayCommand(CanExecute = nameof(CanRunAi))]
    private Task AiFixAsync() => RunAiAsync(AiJsonTaskKind.Repair, null);

    [RelayCommand(CanExecute = nameof(CanRunAi))]
    private Task AiFormatAsync() => RunAiAsync(AiJsonTaskKind.Format, null);

    [RelayCommand(CanExecute = nameof(CanRunAi))]
    private Task AiTransformAsync() => RunAiAsync(AiJsonTaskKind.Transform, AiInstruction);

    [RelayCommand(CanExecute = nameof(CanRunAi))]
    private Task AiGenerateAsync() => RunAiAsync(AiJsonTaskKind.Generate, AiInstruction);

    [RelayCommand(CanExecute = nameof(CanRunAi))]
    private Task AiExplainAsync() => RunAiAsync(AiJsonTaskKind.Explain, null);

    private bool CanRunAi => _ai.IsConfigured && !AiBusy;

    private async Task RunAiAsync(AiJsonTaskKind kind, string? instruction)
    {
        if (!_ai.IsConfigured)
        {
            AiError = AiGuidanceText;
            ShowAi();
            return;
        }

        _aiCts?.Cancel();
        _aiCts = new CancellationTokenSource();
        var ct = _aiCts.Token;

        AiBusy = true;
        AiError = string.Empty;
        AiSummary = string.Empty;

        try
        {
            var request = new AiJsonRequest(
                kind,
                Source,
                instruction,
                Problems.ToList(),
                "2-space indentation");

            var result = await _ai.CompleteAsync(request, ct);

            AiSummary = result.Explanation ?? string.Empty;

            if (kind == AiJsonTaskKind.Explain)
            {
                AiOutput = result.Explanation ?? string.Empty;
                ShowAi();
            }
            else if (result.Succeeded && result.Json is not null)
            {
                // Assign Source first: OnSourceChanged clears the output properties.
                Source = result.Json;
                AiOutput = result.Json;
                AiSummary = result.Explanation ?? string.Empty;
                StatusText = $"AI ({kind}) completed.";
                ShowAi();
            }
            else
            {
                AiError = result.Error ?? "The AI request failed.";
                AiOutput = result.RawResponse;
                ShowAi();
            }
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer request; nothing to show.
        }
        catch (Exception ex)
        {
            AiError = "AI request failed: " + ex.Message;
            AiOutput = string.Empty;
            ShowAi();
        }
        finally
        {
            AiBusy = false;
        }
    }

    /// <summary>Public entry point so the view can trigger an initial/revalidation pass.</summary>
    public void Validate()
    {
        var validationOptions = JsonValidationOptions.FromStrictness(ValidationStrictness);
        var result = JsonValidator.Validate(Source, validationOptions);
        IsValid = result.IsValid;

        Problems.Clear();
        foreach (var diagnostic in result.Diagnostics)
        {
            Problems.Add(diagnostic);
        }

        OnPropertyChanged(nameof(ProblemsListVisibility));
        OnPropertyChanged(nameof(ProblemsEmptyVisibility));

        var errors = result.Diagnostics.Count(d => d.Severity == JsonDiagnosticSeverity.Error);
        var warnings = result.Diagnostics.Count(d => d.Severity == JsonDiagnosticSeverity.Warning);

        var validationText = result.IsValid
            ? "Valid JSON"
            : $"{errors} error(s), {warnings} warning(s)";

        StatusText = string.IsNullOrWhiteSpace(LastActionText)
            ? validationText
            : $"{validationText} â€” {LastActionText}";

        var analysis = JsonTextAnalyzer.Analyze(Source);
        DetectedFormatText = analysis.Kind switch
        {
            JsonTextKind.Empty => string.Empty,
            JsonTextKind.Ndjson => $"NDJSON ({analysis.NdjsonDocumentCount})",
            JsonTextKind.Jsonc => "JSONC",
            JsonTextKind.Jsonish => "JSON-ish",
            JsonTextKind.StrictJson => "JSON",
            _ => "Invalid",
        };

        PreviewLexemes = JsonLexer.Tokenize(Source);
        PreviewRefreshed?.Invoke();
        RefreshTree();
    }

    public void CancelAi() => _aiCts?.Cancel();

    private void ScheduleValidation()
    {
        _validateTimer ??= CreateValidationTimer();
        _validateTimer.Stop();
        _validateTimer.Start();
    }

    private void ScheduleAutoFormat()
    {
        if (!AutoFormatOnIdle || _suppressAutoFormat)
        {
            return;
        }

        _autoFormatTimer ??= CreateAutoFormatTimer();
        _autoFormatTimer.Interval = TimeSpan.FromMilliseconds(Math.Clamp(AutoFormatIdleMs, 200, 5000));
        _autoFormatTimer.Stop();
        _autoFormatTimer.Start();
    }

    private DispatcherQueueTimer CreateAutoFormatTimer()
    {
        var timer = _dispatcher.CreateTimer();
        timer.IsRepeating = false;
        timer.Tick += (_, _) => AutoFormatOnce();
        return timer;
    }

    private void AutoFormatOnce()
    {
        if (_suppressAutoFormat || !AutoFormatOnIdle)
        {
            return;
        }

        var options = BuildDefaultFormatOptions();
        var validationOptions = JsonValidationOptions.FromStrictness(ValidationStrictness);
        var result = JsonValidator.Validate(Source, validationOptions);
        if (!result.IsValid)
        {
            return;
        }

        if (!JsonSmartFormatter.TryFormat(Source, options, out var formatted, out var status, out _))
        {
            return;
        }

        if (formatted == Source)
        {
            return;
        }

        _suppressAutoFormat = true;
        try
        {
            _suppressDirty = true;
            Source = formatted;
            LastActionText = status;
            IsDirty = true;
        }
        finally
        {
            _suppressDirty = false;
            _suppressAutoFormat = false;
        }

        Validate();
    }

    public void AutoFormatOnPasteNow()
    {
        if (!AutoFormatOnPaste)
        {
            return;
        }

        var options = BuildDefaultFormatOptions();
        if (!JsonSmartFormatter.TryFormat(Source, options, out var formatted, out var status, out _))
        {
            return;
        }

        if (formatted == Source)
        {
            return;
        }

        _suppressAutoFormat = true;
        try
        {
            _suppressDirty = true;
            Source = formatted;
            LastActionText = status;
            IsDirty = true;
        }
        finally
        {
            _suppressDirty = false;
            _suppressAutoFormat = false;
        }

        Validate();
    }

    private DispatcherQueueTimer CreateValidationTimer()
    {
        var timer = _dispatcher.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(250);
        timer.IsRepeating = false;
        timer.Tick += (_, _) => Validate();
        return timer;
    }
}
