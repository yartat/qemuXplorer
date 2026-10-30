using CommunityToolkit.Mvvm.ComponentModel;
using QemuXplorer.Models;

namespace QemuXplorer.App.ViewModels;

/// <summary>One editable <c>qemu-img create -o</c> option in the wizard.</summary>
public partial class ImageOptionViewModel : ObservableObject
{
    public const string DefaultChoice = "(default)";

    private readonly string _unsetLabel;

    /// <param name="unsetLabel">What the "not set" entry of a choice reads, e.g. "(leave unchanged)" when amending.</param>
    public ImageOptionViewModel(ImageOption definition, string unsetLabel = DefaultChoice)
    {
        Definition = definition;
        _unsetLabel = unsetLabel;
        _value = definition.Kind == ImageOptionKind.Bool ? definition.Default : string.Empty;
        ChoiceItems = definition.Choices is null ? [] : [unsetLabel, .. definition.Choices];
    }

    public ImageOption Definition { get; }

    public string Label => Definition.Label;
    public string Description => Definition.Description;

    public bool IsBool => Definition.Kind == ImageOptionKind.Bool;
    public bool IsChoice => Definition.Kind == ImageOptionKind.Choice;
    public bool IsText => Definition.Kind is ImageOptionKind.Text or ImageOptionKind.Number;

    public IReadOnlyList<string> ChoiceItems { get; }

    [ObservableProperty]
    private string _value;

    public bool BoolValue
    {
        get => Value == "on";
        set => Value = value ? "on" : "off";
    }

    public string SelectedChoice
    {
        get => string.IsNullOrEmpty(Value) ? _unsetLabel : Value;
        set => Value = value == _unsetLabel ? string.Empty : value;
    }

    /// <summary>True when the option should appear on the command line.</summary>
    public bool IsSet => !string.IsNullOrWhiteSpace(Value) && Value != Definition.Default;

    partial void OnValueChanged(string value)
    {
        OnPropertyChanged(nameof(BoolValue));
        OnPropertyChanged(nameof(SelectedChoice));
        OnPropertyChanged(nameof(IsSet));
    }
}
