using BackendApplication.Data.Repositories;
using BackendApplication.Domain.Entities;
using BackendApplication.Domain.Enums;
using BackendApplication.DTOs.Auth;
using BackendApplication.Exceptions;
using BackendApplication.Mapping;
using BackendApplication.Services.Abstractions;

namespace BackendApplication.Services.Implementations;

/// <summary>
/// Registration, login, refresh-token rotation and logout.
/// </summary>
public sealed class AuthService : IAuthService
{
    private readonly IRepository<AppUser> _users;
    private readonly IRepository<RefreshToken> _refreshTokens;
    private readonly ITokenService _tokens;
    private readonly ICurrentUserService _currentUser;
    private readonly ILogger<AuthService> _logger;
    private readonly TimeProvider _time;

    public AuthService(
        IRepository<AppUser> users,
        IRepository<RefreshToken> refreshTokens,
        ITokenService tokens,
        ICurrentUserService currentUser,
        ILogger<AuthService> logger,
        TimeProvider time)
    {
        _users = users;
        _refreshTokens = refreshTokens;
        _tokens = tokens;
        _currentUser = currentUser;
        _logger = logger;
        _time = time;
    }

    /// <inheritdoc />
    public async Task<AuthResponseDto> RegisterAsync(RegisterDto dto, CancellationToken ct = default)
    {
        var email = dto.Email.Trim().ToLowerInvariant();

        if (await _users.ExistsAsync(u => u.Email == email, ct))
        {
            throw new ConflictException("An account with this email already exists.", "email_taken");
        }

        var user = new AppUser
        {
            Email = email,
            FullName = dto.FullName.Trim(),

            // Work factor 12: deliberately slow, so a stolen database is expensive to
            // attack offline. It is the one place in the app where slow is the feature.
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password, workFactor: 12),

            // Never taken from the request. A client that could pick its own role would
            // simply ask for Admin.
            Role = UserRole.Member,
            IsActive = true
        };

        await _users.AddAsync(user, ct);
        await _users.SaveChangesAsync(ct);

        _logger.LogInformation("Registered new user {UserId} ({Email})", user.Id, user.Email);

        return await IssueTokensAsync(user, ct);
    }

    /// <inheritdoc />
    public async Task<AuthResponseDto> LoginAsync(LoginDto dto, CancellationToken ct = default)
    {
        var email = dto.Email.Trim().ToLowerInvariant();

        var user = await _users.Query(asNoTracking: false)
            .FirstOrDefaultAsync(u => u.Email == email, ct);

        if (user is null)
        {
            // Verify against a throwaway hash anyway. Returning immediately would make an
            // unknown email measurably faster than a wrong password, which is enough to
            // enumerate who has an account.
            BCrypt.Net.BCrypt.Verify(dto.Password, DummyHash);
            throw new UnauthorizedException("Invalid email or password.");
        }

        if (!BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash))
        {
            _logger.LogWarning("Failed login attempt for {Email}", email);

            // Same message for both failures, on purpose: "no such user" and "wrong
            // password" are different facts, and telling them apart is the attacker's job.
            throw new UnauthorizedException("Invalid email or password.");
        }

        if (!user.IsActive)
        {
            throw new ForbiddenException("This account has been deactivated. Please contact support.");
        }

        _logger.LogInformation("User {UserId} signed in", user.Id);

        return await IssueTokensAsync(user, ct);
    }

    /// <inheritdoc />
    public async Task<AuthResponseDto> RefreshAsync(RefreshTokenDto dto, CancellationToken ct = default)
    {
        var stored = await _refreshTokens.Query(asNoTracking: false)
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.Token == dto.RefreshToken, ct)
            ?? throw new UnauthorizedException("Invalid refresh token.");

        if (!stored.IsActive)
        {
            // Presenting an already-used token is a signal worth logging: either a bug in
            // the client, or someone replaying a stolen token.
            _logger.LogWarning(
                "Attempted reuse of an inactive refresh token for user {UserId}", stored.UserId);

            throw new UnauthorizedException("Refresh token has expired or been revoked.");
        }

        var now = _time.GetUtcNow().UtcDateTime;
        var replacement = _tokens.CreateRefreshToken(stored.UserId);

        // Rotation: the old token dies as the new one is born, and the link between them
        // is recorded so the chain can be followed later.
        stored.RevokedAtUtc = now;
        stored.ReplacedByToken = replacement.Token;

        await _refreshTokens.AddAsync(replacement, ct);
        await _refreshTokens.SaveChangesAsync(ct);

        var (accessToken, expiresAt) = _tokens.CreateAccessToken(stored.User);

        return new AuthResponseDto(
            accessToken, replacement.Token, expiresAt, "Bearer", stored.User.ToDto());
    }

    /// <inheritdoc />
    /// <remarks>Idempotent: revoking an unknown or already-dead token is a no-op, not an error.</remarks>
    public async Task RevokeAsync(string refreshToken, CancellationToken ct = default)
    {
        var stored = await _refreshTokens.Query(asNoTracking: false)
            .FirstOrDefaultAsync(t => t.Token == refreshToken, ct);

        if (stored is null || !stored.IsActive)
        {
            return;
        }

        stored.RevokedAtUtc = _time.GetUtcNow().UtcDateTime;
        await _refreshTokens.SaveChangesAsync(ct);

        _logger.LogInformation("Revoked refresh token for user {UserId}", stored.UserId);
    }

    /// <inheritdoc />
    public async Task<UserDto> GetCurrentUserAsync(CancellationToken ct = default)
    {
        var userId = _currentUser.UserId ?? throw new UnauthorizedException();

        var user = await _users.GetByIdAsync(userId, asNoTracking: true, ct)
                   ?? throw new NotFoundException("User", userId);

        return user.ToDto();
    }

    /// <summary>Issues a fresh access + refresh token pair and persists the refresh token.</summary>
    private async Task<AuthResponseDto> IssueTokensAsync(AppUser user, CancellationToken ct)
    {
        var (accessToken, expiresAt) = _tokens.CreateAccessToken(user);
        var refreshToken = _tokens.CreateRefreshToken(user.Id);

        await _refreshTokens.AddAsync(refreshToken, ct);
        await _refreshTokens.SaveChangesAsync(ct);

        return new AuthResponseDto(accessToken, refreshToken.Token, expiresAt, "Bearer", user.ToDto());
    }

    /// <summary>
    /// A real BCrypt hash of a random value, verified against when the email is unknown so
    /// that login timing does not reveal whether an account exists.
    /// </summary>
    private const string DummyHash = "$2a$12$C6UzMDM.H6dfI/f/IKcEe.7Dn7VDPJVUCXlkNi0mkLQ.dMEXSpS7O";
}
