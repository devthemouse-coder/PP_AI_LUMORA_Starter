using System.Reflection;

using System.Xml.Linq;

namespace Lumora.Domain.UnitTests;

public sealed class DependencyBoundaryTests
{
    [Fact]
    public void Domain_does_not_reference_application_infrastructure_or_frameworks()
    {
        var project = LoadProject("src/Lumora.Domain/Lumora.Domain.csproj");

        Assert.Empty(project.Descendants("ProjectReference"));
        Assert.Empty(project.Descendants("PackageReference"));
    }

    private static XDocument LoadProject(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Lumora.sln")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new InvalidOperationException("The repository root could not be located.");
        }

        return XDocument.Load(Path.Combine(directory.FullName, relativePath));
    }
}
