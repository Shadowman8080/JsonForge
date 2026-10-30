using JsonForge.Core.Json;
using JsonForge.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;

namespace JsonForge;

public sealed partial class MainPage : Page
{
    private MainViewModel? _vm;
    private int _findIndex = -1;

    private static readonly FontFamily PreviewFont = new("Cascadia Mono, Consolas, monospace");

    public MainPage()
    {
        this.InitializeComponent();

        if (Application.Current is App app && app.Host is not null)
        {
            _vm = app.Host.Services.GetService<MainViewModel>();
            if (_vm is not null)
            {
                DataContext = _vm;
                _vm.PreviewRefreshed += RenderPreview;
                _vm.NavigateRequested += NavigateTo;
                _vm.Validate();

                // Best-effort: auto-format after paste when enabled.
                try
                {
                    Editor.Paste += (_, _) => _vm.AutoFormatOnPasteNow();
                }
                catch
                {
                    // Some targets may not support the Paste event; ignore.
                }
            }
        }
    }

    /// <summary>True when the editor has unsaved work.</summary>
    public bool HasUnsavedChanges => _vm?.IsDirty == true;

    /// <summary>
    /// Asks the user what to do about unsaved changes. Used when closing the window.
    /// </summary>
    public async Task<UnsavedChangesChoice> PromptUnsavedChangesAsync()
    {
        var name = string.IsNullOrWhiteSpace(_vm?.CurrentFileName) ? "This document" : $"\"{_vm!.CurrentFileName}\"";
        var dialog = new ContentDialog
        {
            Title = "Unsaved changes",
            Content = $"{name} has unsaved changes. Save before closing?",
            PrimaryButtonText = "Save",
            SecondaryButtonText = "Don't save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };

        var result = await dialog.ShowAsync();
        return result switch
        {
            ContentDialogResult.Primary => UnsavedChangesChoice.Save,
            ContentDialogResult.Secondary => UnsavedChangesChoice.DontSave,
            _ => UnsavedChangesChoice.Cancel,
        };
    }

    /// <summary>Runs the save command and reports whether the document is now clean.</summary>
    public async Task<bool> SaveNowAsync()
    {
        if (_vm?.SaveCommand is not { } save)
        {
            return false;
        }

        await save.ExecuteAsync(null);
        return !_vm.IsDirty;
    }

    private void OnRecentFileClicked(object sender, RoutedEventArgs e)
    {
        if (_vm is not null && sender is FrameworkElement { DataContext: RecentFileEntry entry })
        {
            // Commands can't be reached from inside a DataTemplate via ElementName,
            // so dispatch explicitly from the item's DataContext.
            if (_vm.OpenRecentCommand.CanExecute(entry))
            {
                _vm.OpenRecentCommand.Execute(entry);
            }
        }
    }

    private void OnProblemClicked(object sender, ItemClickEventArgs e)
    {
        if (_vm is not null && e.ClickedItem is JsonDiagnostic diagnostic)
        {
            _vm.SelectProblemCommand.Execute(diagnostic);
        }
    }

    private void OnFindNextClick(object sender, RoutedEventArgs e)
    {
        var text = Editor.Text ?? string.Empty;
        var query = FindBox.Text ?? string.Empty;
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(query))
        {
            return;
        }

        var start = Editor.SelectionStart + Editor.SelectionLength;
        if (start < 0) start = 0;
        var idx = text.IndexOf(query, start, StringComparison.OrdinalIgnoreCase);
        if (idx < 0 && start > 0)
        {
            idx = text.IndexOf(query, 0, StringComparison.OrdinalIgnoreCase);
        }
        if (idx >= 0)
        {
            Editor.Focus(FocusState.Programmatic);
            Editor.Select(idx, query.Length);
            _findIndex = idx;
        }
    }

    private void OnFindPrevClick(object sender, RoutedEventArgs e)
    {
        var text = Editor.Text ?? string.Empty;
        var query = FindBox.Text ?? string.Empty;
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(query))
        {
            return;
        }

        var start = Editor.SelectionStart - 1;
        if (start < 0) start = text.Length - 1;
        var idx = text.LastIndexOf(query, start, StringComparison.OrdinalIgnoreCase);
        if (idx < 0 && start >= 0)
        {
            idx = text.LastIndexOf(query, text.Length - 1, StringComparison.OrdinalIgnoreCase);
        }
        if (idx >= 0)
        {
            Editor.Focus(FocusState.Programmatic);
            Editor.Select(idx, query.Length);
            _findIndex = idx;
        }
    }

    private void OnReplaceAllClick(object sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        var text = Editor.Text ?? string.Empty;
        var query = FindBox.Text ?? string.Empty;
        var replacement = ReplaceBox.Text ?? string.Empty;
        if (string.IsNullOrEmpty(query))
        {
            return;
        }

        var newText = text.Replace(query, replacement, StringComparison.OrdinalIgnoreCase);
        if (newText != text)
        {
            _vm.Source = newText;
        }
    }

    private void NavigateTo(int offset, int length)
    {
        var text = Editor.Text ?? string.Empty;
        if (text.Length == 0)
        {
            return;
        }

        offset = Math.Clamp(offset, 0, text.Length);
        length = Math.Clamp(length, 1, Math.Max(1, text.Length - offset));

        Editor.Focus(FocusState.Programmatic);
        Editor.Select(offset, length);
    }

    /// <summary>
    /// Rebuilds the syntax preview. Uno renders <see cref="TextBlock"/> (and its
    /// <see cref="TextBlock.Inlines"/>) on Skia, while RichTextBlock is not implemented,
    /// so each source line becomes one TextBlock whose Runs are colored by token kind.
    /// </summary>
    private void RenderPreview()
    {
        PreviewHost.Children.Clear();

        if (_vm?.PreviewLexemes is null)
        {
            return;
        }

        var current = NewLineTextBlock();
        var hasContent = false;

        foreach (var lexeme in _vm.PreviewLexemes)
        {
            var text = lexeme.Text;
            var start = 0;

            while (start < text.Length)
            {
                var newline = text.IndexOf('\n', start);
                if (newline < 0)
                {
                    AddRun(current, text[start..], lexeme.Kind);
                    hasContent = true;
                    start = text.Length;
                }
                else
                {
                    if (newline > start)
                    {
                        AddRun(current, text[start..newline], lexeme.Kind);
                        hasContent = true;
                    }

                    if (hasContent)
                    {
                        PreviewHost.Children.Add(current);
                    }

                    current = NewLineTextBlock();
                    hasContent = false;
                    start = newline + 1;
                }
            }
        }

        if (hasContent)
        {
            PreviewHost.Children.Add(current);
        }
    }

    private static TextBlock NewLineTextBlock() => new()
    {
        FontFamily = PreviewFont,
        FontSize = 14,
        TextWrapping = TextWrapping.NoWrap,
        IsTextSelectionEnabled = true,
    };

    private static void AddRun(TextBlock line, string text, JsonLexemeKind kind)
    {
        var run = new Run { Text = text };

        var brush = BrushFor(kind);
        if (brush is not null)
        {
            run.Foreground = brush;
        }

        line.Inlines.Add(run);
    }

    private static Brush? BrushFor(JsonLexemeKind kind) => kind switch
    {
        JsonLexemeKind.ObjectStart or JsonLexemeKind.ObjectEnd or
            JsonLexemeKind.ArrayStart or JsonLexemeKind.ArrayEnd or
            JsonLexemeKind.Colon or JsonLexemeKind.Comma => Brush("JsonPunctBrush"),

        JsonLexemeKind.String or JsonLexemeKind.SingleQuotedString => Brush("JsonStringBrush"),
        JsonLexemeKind.UnterminatedString => Brush("JsonErrorBrush"),
        JsonLexemeKind.Number => Brush("JsonNumberBrush"),
        JsonLexemeKind.Bool => Brush("JsonKeywordBrush"),
        JsonLexemeKind.Null => Brush("JsonLiteralBrush"),
        JsonLexemeKind.Comment => Brush("JsonCommentBrush"),
        JsonLexemeKind.Bareword => Brush("JsonErrorBrush"),
        _ => null,
    };

    private static Brush? Brush(string key)
        => Application.Current?.Resources.TryGetValue(key, out var value) is true && value is Brush brush
            ? brush
            : null;
}

/// <summary>User's answer to the unsaved-changes prompt.</summary>
public enum UnsavedChangesChoice
{
    Save,
    DontSave,
    Cancel,
}

