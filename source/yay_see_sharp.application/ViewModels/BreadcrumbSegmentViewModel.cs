namespace yay_see_sharp.application.ViewModels;

public sealed class BreadcrumbSegmentViewModel
{
    public BreadcrumbSegmentViewModel(string name, string path)
    {
        Name = name;
        Path = path;
    }

    public string Name { get; }

    public string Path { get; }
}
