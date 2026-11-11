using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Lumora.Api.IntegrationTests;

public sealed class HealthCheckTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public HealthCheckTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetHealth_returns_api_status_name_and_utc_server_time()
    {
        using var response = await _client.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var responseStream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(responseStream);
        var root = document.RootElement;

        Assert.Equal("ok", root.GetProperty("status").GetString());
        Assert.Equal("P.P. AI - LUMORA", root.GetProperty("applicationName").GetString());
        Assert.True(
            DateTimeOffset.TryParse(
                root.GetProperty("serverTime").GetString(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var serverTime));
        Assert.Equal(TimeSpan.Zero, serverTime.Offset);
    }

    [Fact]
    public async Task GetOpenApi_returns_document_in_development_environment()
    {
        using var response = await _client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("GetHealth", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }
}
