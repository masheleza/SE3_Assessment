using System.Net.Http.Headers;
using System.Text;
using FinancialStatements.BFF.Services;
using FinancialStatements.Models.DTOs.Response;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace FinancialStatements.BFF.Controllers;

[ApiController]
[Route("api/auth")]
[AllowAnonymous]
public sealed class AuthController : ControllerBase
{
    private readonly IUserStore _userStore;
    private readonly IJwtTokenService _tokenService;
    private readonly IPasswordHasher<AuthUser> _passwordHasher;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        IUserStore userStore,
        IJwtTokenService tokenService,
        IPasswordHasher<AuthUser> passwordHasher,
        IWebHostEnvironment environment,
        ILogger<AuthController> logger)
    {
        _userStore = userStore;
        _tokenService = tokenService;
        _passwordHasher = passwordHasher;
        _environment = environment;
        _logger = logger;
    }

    /// <summary>
    /// Validates HTTP Basic credentials (Authorization: Basic base64(username:password))
    /// and issues a signed (RS256) JWT access token.
    /// </summary>
    [HttpPost("login")]
    [ProducesResponseType<LoginResponseDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public IActionResult Login()
    {
        if (!TryReadBasicCredentials(out var providedUsername, out var password))
            return Unauthorized("Provide credentials via the 'Authorization: Basic' header.");

        var username = _userStore.Validate(providedUsername, password);
        if (username is null)
        {
            _logger.LogWarning("Failed login attempt for User={Username}", providedUsername);
            return Unauthorized("The username or password is incorrect.");
        }

        var (accessToken, expiresAt) = _tokenService.GenerateToken(username);

        _logger.LogInformation("Issued access token for User={Username}", username);

        return Ok(new LoginResponseDto
        {
            AccessToken = accessToken,
            TokenType = "Bearer",
            ExpiresAt = expiresAt
        });
    }

    /// <summary>
    /// Dev-only helper that returns a PBKDF2 hash for the supplied password, ready to
    /// paste into <c>Auth:Users[].PasswordHash</c>. Responds with 404 outside the
    /// Development environment so it is never exposed in staging/production.
    /// </summary>
    [HttpPost("dev/hash")]
    [ProducesResponseType<HashPasswordResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult HashPassword([FromBody] HashPasswordRequest request)
    {
        if (!_environment.IsDevelopment())
            return NotFound();

        if (string.IsNullOrEmpty(request?.Password))
            return BadRequest("Provide a non-empty 'password'.");

        var passwordHash = _passwordHasher.HashPassword(new AuthUser(), request.Password);
        return Ok(new HashPasswordResponse(passwordHash));
    }

    /// <summary>
    /// Parses the base64 "username:password" payload from a Basic Authorization header.
    /// </summary>
    private bool TryReadBasicCredentials(out string username, out string password)
    {
        username = string.Empty;
        password = string.Empty;

        var header = Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(header) ||
            !AuthenticationHeaderValue.TryParse(header, out var parsed) ||
            !"Basic".Equals(parsed.Scheme, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrEmpty(parsed.Parameter))
        {
            return false;
        }

        string decoded;
        try
        {
            decoded = Encoding.UTF8.GetString(Convert.FromBase64String(parsed.Parameter));
        }
        catch (FormatException)
        {
            return false;
        }

        var separator = decoded.IndexOf(':');
        if (separator < 0)
            return false;

        username = decoded[..separator];
        password = decoded[(separator + 1)..];
        return true;
    }

    private IActionResult Unauthorized(string detail) =>
        Problem(
            statusCode: StatusCodes.Status401Unauthorized,
            title: "Invalid credentials",
            detail: detail);
}

/// <summary>Request body for the dev-only password hashing helper.</summary>
public sealed record HashPasswordRequest(string Password);

/// <summary>Response body containing the generated password hash.</summary>
public sealed record HashPasswordResponse(string PasswordHash);
