using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace FinancialStatements.BFF.Services;

/// <summary>
/// Options controlling self-issued JWT tokens. Tokens are signed with an RSA
/// private key (RS256) and validated with the matching public key.
/// </summary>
public sealed class JwtOptions
{
    public string Issuer { get; init; } = "financial-statements-bff";
    public string Audience { get; init; } = "financial-statements-bff";
    public int AccessTokenLifetimeMinutes { get; init; } = 60;
    public string PrivateKeyPath { get; init; } = "keys/private.pem";
    public string PublicKeyPath { get; init; } = "keys/public.pem";
}

public interface IJwtTokenService
{
    /// <summary>Issues a signed access token for the given user.</summary>
    (string AccessToken, DateTimeOffset ExpiresAt) GenerateToken(string username);

    /// <summary>The public signing key, used to configure token validation.</summary>
    SecurityKey PublicSigningKey { get; }

    string Issuer { get; }
    string Audience { get; }
}

public sealed class JwtTokenService : IJwtTokenService, IDisposable
{
    public const string NameClaimType = JwtRegisteredClaimNames.Sub;

    private readonly JwtOptions _options;
    private readonly RSA _privateKey;
    private readonly RSA _publicKey;
    private readonly SigningCredentials _signingCredentials;

    public JwtTokenService(JwtOptions options, IHostEnvironment env)
    {
        _options = options;

        _privateKey = RSA.Create();
        _privateKey.ImportFromPem(File.ReadAllText(Resolve(env, options.PrivateKeyPath)));

        _publicKey = RSA.Create();
        _publicKey.ImportFromPem(File.ReadAllText(Resolve(env, options.PublicKeyPath)));

        // KeyId ties the issued token's header to the validation key.
        var privateSecurityKey = new RsaSecurityKey(_privateKey) { KeyId = "fs-bff-rsa" };
        PublicSigningKey = new RsaSecurityKey(_publicKey) { KeyId = "fs-bff-rsa" };

        _signingCredentials = new SigningCredentials(privateSecurityKey, SecurityAlgorithms.RsaSha256);
    }

    public SecurityKey PublicSigningKey { get; }
    public string Issuer => _options.Issuer;
    public string Audience => _options.Audience;

    public (string AccessToken, DateTimeOffset ExpiresAt) GenerateToken(string username)
    {
        var now = DateTimeOffset.UtcNow;
        var expiresAt = now.AddMinutes(_options.AccessTokenLifetimeMinutes);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, username),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expiresAt.UtcDateTime,
            signingCredentials: _signingCredentials);

        var accessToken = new JwtSecurityTokenHandler().WriteToken(token);
        return (accessToken, expiresAt);
    }

    private static string Resolve(IHostEnvironment env, string path) =>
        Path.IsPathRooted(path) ? path : Path.Combine(env.ContentRootPath, path);

    public void Dispose()
    {
        _privateKey.Dispose();
        _publicKey.Dispose();
    }
}
