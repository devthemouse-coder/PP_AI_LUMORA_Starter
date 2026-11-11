using System.Xml.Linq;

namespace Lumora.Application.UnitTests;

public sealed class DependencyBoundaryTests
{
    [Fact]
    public void Application_references_domain_but_not_infrastructure_api_or_contracts()
    {
        var project = LoadProject("src/Lumora.Application/Lumora.Application.csproj");
        var references = project.Descendants("ProjectReference")
            .Select(reference => reference.Attribute("Include")?.Value ?? string.Empty)
            .ToArray();

        Assert.Contains(references, reference => reference.EndsWith("Lumora.Domain.csproj", StringComparison.Ordinal));
        Assert.DoesNotContain(references, reference =>
            reference.Contains("Lumora.Infrastructure", StringComparison.Ordinal) ||
            reference.Contains("Lumora.Api", StringComparison.Ordinal) ||
            reference.Contains("Lumora.Contracts", StringComparison.Ordinal));
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
