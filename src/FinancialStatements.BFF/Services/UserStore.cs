using Microsoft.AspNetCore.Identity;

namespace FinancialStatements.BFF.Services;

public sealed class AuthUser
{
    public string Username { get; init; } = string.Empty;

    /// <summary>
    /// PBKDF2 password hash in ASP.NET Core Identity's v3 format
    /// (produced by <see cref="PasswordHasher{TUser}"/>). Never store plaintext.
    /// </summary>
    public string PasswordHash { get; init; } = string.Empty;
}

public sealed class AuthOptions
{
    public List<AuthUser> Users { get; init; } = new();
}

public interface IUserStore
{
    /// <summary>Returns the canonical username if credentials are valid, otherwise null.</summary>
    string? Validate(string username, string password);
}

/// <summary>
/// In-memory credential store backed by configuration. Passwords are stored as
/// PBKDF2 hashes (Identity v3 format) and verified with <see cref="PasswordHasher{TUser}"/>;
/// swap for a real user directory in production.
/// </summary>
public sealed class ConfigurationUserStore : IUserStore
{
    private readonly IReadOnlyDictionary<string, AuthUser> _usersByName;
    private readonly IPasswordHasher<AuthUser> _passwordHasher;

    public ConfigurationUserStore(AuthOptions options, IPasswordHasher<AuthUser> passwordHasher)
    {
        _passwordHasher = passwordHasher;
        _usersByName = options.Users.ToDictionary(
            u => u.Username,
            u => u,
            StringComparer.OrdinalIgnoreCase);
    }

    public string? Validate(string username, string password)
    {
        if (string.IsNullOrEmpty(username) || !_usersByName.TryGetValue(username, out var user))
        {
            // Verify against a throwaway hash so a missing user costs roughly the
            // same time as a wrong password, mitigating user-enumeration via timing.
            _passwordHasher.VerifyHashedPassword(DummyUser, DummyHash, password);
            return null;
        }

        var result = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password);
        return result is PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded
            ? user.Username
            : null;
    }

    // A fixed valid hash used only to burn comparable CPU time for unknown users.
    private static readonly AuthUser DummyUser = new();
    private static readonly string DummyHash =
        new PasswordHasher<AuthUser>().HashPassword(DummyUser, "timing-equalizer");
}
