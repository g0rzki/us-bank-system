using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using UsBankSystem.Api.Models.Auth;
using UsBankSystem.Core.Domain.Common;
using UsBankSystem.Core.Entities;
using UsBankSystem.Infrastructure.Persistence;

namespace UsBankSystem.Api.Services;

public class AuthService(AppDbContext db, IConfiguration config)
{
    public async Task<(bool Success, string? Error, object? Result)> RegisterAsync(RegisterRequest request)
    {
        var emailTaken = await db.Users.AnyAsync(u => u.Email == request.Email.ToLowerInvariant());
        if (emailTaken)
            return (false, "Email is already taken", null);

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = request.Email.ToLowerInvariant(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            FirstName = request.FirstName,
            LastName = request.LastName,
            Status = UserStatus.Active,
            CreatedAt = DateTime.UtcNow
        };

        db.Users.Add(user);
        await db.SaveChangesAsync();

        return (true, null, new { id = user.Id, email = user.Email });
    }

    public async Task<(bool Success, string? Error, LoginResponse? Token)> LoginAsync(LoginRequest request)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == request.Email.ToLowerInvariant());
        if (user is null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            return (false, "Invalid email or password", null);

        if (user.Status != UserStatus.Active)
            return (false, "Account is not active", null);

        return (true, null, GenerateJwt(user));
    }

    private LoginResponse GenerateJwt(User user)
    {
        var secret = config["Jwt:Secret"]!;
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expiresAt = DateTime.UtcNow.AddHours(1);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            claims: claims,
            expires: expiresAt,
            signingCredentials: credentials
        );

        return new LoginResponse
        {
            Token = new JwtSecurityTokenHandler().WriteToken(token),
            ExpiresAt = expiresAt
        };
    }
}
