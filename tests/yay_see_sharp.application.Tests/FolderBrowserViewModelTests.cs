using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using yay_see_sharp.domain.Abstractions;
using yay_see_sharp.infrastructure.Localization;
using yay_see_sharp.application.ViewModels;

namespace yay_see_sharp.application.Tests;

public class FolderBrowserViewModelTests
{
    private const string Root = "/yss-root";
    private static readonly string Downloads = Path.Combine(Root, "downloads");
    private static readonly string Yay = Path.Combine(Root, "yay");
    private static readonly string Nested = Path.Combine(Yay, "nested");

    /// <summary>In-memory stand-in for the real filesystem, so these tests never touch disk.</summary>
    private sealed class FakeFolderBrowserService : IFolderBrowserService
    {
        private readonly Dictionary<string, List<string>> _tree = new()
        {
            [Root] = [Downloads, Yay],
            [Downloads] = [],
            [Yay] = [Nested],
            [Nested] = [],
        };

        public IReadOnlyList<string> GetSubdirectories(string path) =>
            _tree.TryGetValue(path, out var children) ? children : [];

        public bool DirectoryExists(string path) => _tree.ContainsKey(path);

        public string GetParentPath(string path) => Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(path)) ?? path;
    }

    private static FolderBrowserViewModel CreateViewModel(string startPath = Root) =>
        new(startPath, new LocalizationService("en"), new FakeFolderBrowserService());

    [Test]
    public async Task Constructor_lists_immediate_subdirectories_of_the_start_path()
    {
        var viewModel = CreateViewModel();

        await Assert.That(viewModel.Children.Count).IsEqualTo(2);
        await Assert.That(viewModel.Children.Any(child => child.Name == "downloads")).IsTrue();
        await Assert.That(viewModel.Children.Any(child => child.Name == "yay")).IsTrue();
    }

    [Test]
    public async Task Opening_a_subfolder_navigates_into_it_and_lists_its_children()
    {
        var viewModel = CreateViewModel();
        var yayEntry = viewModel.Children.Single(child => child.Name == "yay");

        await viewModel.OpenEntryCommand.Execute(yayEntry);

        await Assert.That(viewModel.CurrentPath).IsEqualTo(Yay);
        await Assert.That(viewModel.Children.Count).IsEqualTo(1);
        await Assert.That(viewModel.Children[0].Name).IsEqualTo("nested");
    }

    [Test]
    public async Task Selecting_an_entry_updates_selected_path_and_marks_only_that_entry_selected()
    {
        var viewModel = CreateViewModel();
        var yayEntry = viewModel.Children.Single(child => child.Name == "yay");
        var downloadsEntry = viewModel.Children.Single(child => child.Name == "downloads");

        await viewModel.SelectEntryCommand.Execute(yayEntry);

        await Assert.That(viewModel.SelectedPath).IsEqualTo(Yay);
        await Assert.That(yayEntry.IsSelected).IsTrue();
        await Assert.That(downloadsEntry.IsSelected).IsFalse();
    }

    [Test]
    public async Task Navigate_command_jumps_directly_to_a_breadcrumb_path()
    {
        var viewModel = CreateViewModel();
        var yayEntry = viewModel.Children.Single(child => child.Name == "yay");
        await viewModel.OpenEntryCommand.Execute(yayEntry);

        await viewModel.NavigateCommand.Execute(Root);

        await Assert.That(viewModel.CurrentPath).IsEqualTo(Root);
        await Assert.That(viewModel.Children.Count).IsEqualTo(2);
    }

    [Test]
    public async Task Select_folder_command_resolves_the_pending_result_with_the_selected_path()
    {
        var viewModel = CreateViewModel();
        var yayEntry = viewModel.Children.Single(child => child.Name == "yay");
        await viewModel.SelectEntryCommand.Execute(yayEntry);

        var resultTask = viewModel.WaitForResultAsync();
        await viewModel.SelectFolderCommand.Execute();

        await Assert.That(await resultTask).IsEqualTo(Yay);
    }

    [Test]
    public async Task Cancel_command_resolves_the_pending_result_with_null()
    {
        var viewModel = CreateViewModel();

        var resultTask = viewModel.WaitForResultAsync();
        await viewModel.CancelCommand.Execute();

        await Assert.That(await resultTask).IsNull();
    }

    [Test]
    public async Task A_start_path_the_service_does_not_recognize_falls_back_to_the_home_directory()
    {
        var homeDirectory = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);

        var viewModel = CreateViewModel("/this/path/does/not/exist/in/the/fake/tree");

        await Assert.That(viewModel.CurrentPath).IsEqualTo(homeDirectory);
    }
}
