using System.Xml.Linq;

namespace Lumora.Infrastructure.IntegrationTests;

public sealed class DependencyBoundaryTests
{
    [Fact]
    public void Infrastructure_references_application_and_domain_but_not_api_or_contracts()
    {
        var project = LoadProject("src/Lumora.Infrastructure/Lumora.Infrastructure.csproj");
        var references = project.Descendants("ProjectReference")
            .Select(reference => reference.Attribute("Include")?.Value ?? string.Empty)
            .ToArray();

        Assert.Contains(references, reference => reference.EndsWith("Lumora.Application.csproj", StringComparison.Ordinal));
        Assert.Contains(references, reference => reference.EndsWith("Lumora.Domain.csproj", StringComparison.Ordinal));
        Assert.DoesNotContain(references, reference =>
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
