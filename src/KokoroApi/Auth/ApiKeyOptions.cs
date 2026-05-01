using Microsoft.AspNetCore.Authentication;

namespace KokoroApi.Auth;

public sealed class ApiKeyEntry
{
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public sealed class ApiKeyAuthOptions : AuthenticationSchemeOptions
{
    public const string SchemeName = "ApiKey";
    public const string HeaderName = "X-API-Key";
    public const string QueryName = "api_key";

    public List<ApiKeyEntry> ApiKeys { get; set; } = new();
    public List<string> AllowedOrigins { get; set; } = new();
}
