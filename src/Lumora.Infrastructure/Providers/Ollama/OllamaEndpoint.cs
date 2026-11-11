using System.Net;

namespace Lumora.Infrastructure.Providers.Ollama;

internal sealed record OllamaEndpoint(Uri BaseAddress)
{
    public const string DefaultAddress = "http://127.0.0.1:11434/";
    private const int DefaultPort = 11434;

    public static OllamaEndpoint Parse(string? address)
    {
        var configuredAddress = string.IsNullOrWhiteSpace(address) ? DefaultAddress : address.Trim();
        if (!Uri.TryCreate(configuredAddress, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment) ||
            uri.AbsolutePath != "/" ||
            uri.Port != DefaultPort ||
            uri.HostNameType is not (UriHostNameType.IPv4 or UriHostNameType.IPv6) ||
            !IPAddress.TryParse(uri.Host, out var addressValue) ||
            !(IPAddress.IsLoopback(addressValue) &&
              (addressValue.Equals(IPAddress.Loopback) || addressValue.Equals(IPAddress.IPv6Loopback))))
        {
            throw new InvalidOperationException(
                "Ollama 주소는 HTTP IPv4/IPv6 loopback의 포트 11434만 허용합니다.");
        }

        return new OllamaEndpoint(new UriBuilder(uri)
        {
            Path = "/",
            Query = string.Empty,
            Fragment = string.Empty
        }.Uri);
    }
}
