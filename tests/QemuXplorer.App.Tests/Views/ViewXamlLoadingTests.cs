using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using FluentAssertions;
using QemuXplorer.App.Views;
using Xunit;

namespace QemuXplorer.App.Tests.Views;

/// <summary>
/// Guards the failure mode where a view declares no constructor, so the generated
/// InitializeComponent() is never called, its compiled XAML is never applied, and the
/// control renders as an empty box. MainWindow shipped that way and the whole shell was
/// blank. Nothing about it fails the build, so only a test catches it.
/// </summary>
public class ViewXamlLoadingTests
{
    /// <summary>
    /// Discovered by reflection rather than listed by hand, so a newly added view is
    /// covered the moment it exists.
    /// </summary>
    public static TheoryData<Type> ViewTypes
    {
        get
        {
            var data = new TheoryData<Type>();
            foreach (var type in ConstructibleViews())
                data.Add(type);
            return data;
        }
    }

    private static IEnumerable<Type> ConstructibleViews() =>
        typeof(MainWindow).Assembly
            .GetTypes()
            .Where(t => t is { IsAbstract: false, IsPublic: true }
                        && typeof(ContentControl).IsAssignableFrom(t)
                        && t.GetConstructor(Type.EmptyTypes) is not null)
            .OrderBy(t => t.Name);

    [AvaloniaFact]
    public void ViewTypes_AreActuallyDiscovered()
    {
        // A reflection filter that silently matches nothing would make every theory below
        // vacuously pass.
        ConstructibleViews().Should().HaveCountGreaterThan(4);
    }

    [AvaloniaTheory]
    [MemberData(nameof(ViewTypes))]
    public void View_LoadsItsCompiledXaml(Type viewType)
    {
        var view = (ContentControl)Activator.CreateInstance(viewType)!;

        view.Content.Should().NotBeNull(
            "{0} should call InitializeComponent() in its constructor; without it the " +
            "compiled XAML is never applied and the control renders empty", viewType.Name);
    }

    [AvaloniaFact]
    public void MainWindow_HasNavigationButtonsAndAContentHost()
    {
        var window = new MainWindow();

        var descendants = window.GetLogicalDescendants().ToList();

        descendants.OfType<Button>().Should().HaveCountGreaterThanOrEqualTo(4,
            "the shell needs a navigation button per page");
        descendants.OfType<ContentControl>().Should().NotBeEmpty(
            "the shell needs a ContentControl to host the current page");
    }
}
