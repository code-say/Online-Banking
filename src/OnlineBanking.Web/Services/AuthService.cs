using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OnlineBanking.Web.Data;
using OnlineBanking.Web.Entities;
using OnlineBanking.Web.Infrastructure;

namespace OnlineBanking.Web.Services;

public sealed class AuthService(AppDbContext db, IPasswordHasher<User> hasher)
{
    public async Task<User> RegisterCustomerAsync(string fullName, string email, string phone, string password, CancellationToken ct = default)
    {
        email = email.Trim().ToLowerInvariant();
        if (await db.Users.AnyAsync(u => u.Email == email, ct))
            throw new BankingException("An account with this email already exists. Try signing in.");

        var user = new User { Email = email, Role = Roles.Customer, FullName = fullName.Trim(), Phone = phone.Trim() };
        user.PasswordHash = hasher.HashPassword(user, password);
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        return user;
    }

    /// <summary>The user for these credentials, or null. Unknown email and wrong password look the same.</summary>
    public async Task<User?> ValidateAsync(string email, string password, CancellationToken ct = default)
    {
        email = email.Trim().ToLowerInvariant();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);
        if (user is null)
        {
            hasher.HashPassword(new User { Email = email, Role = "", FullName = "" }, password); // similar timing either way
            return null;
        }

        var result = hasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (result == PasswordVerificationResult.Failed)
            return null;
        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = hasher.HashPassword(user, password);
            await db.SaveChangesAsync(ct);
        }
        return user;
    }

    public async Task ChangePasswordAsync(int userId, string currentPassword, string newPassword, CancellationToken ct = default)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct) ?? throw new NotFoundException();
        if (hasher.VerifyHashedPassword(user, user.PasswordHash, currentPassword) == PasswordVerificationResult.Failed)
            throw new BankingException("Your current password is incorrect.");
        user.PasswordHash = hasher.HashPassword(user, newPassword);
        await db.SaveChangesAsync(ct);
    }

    public static ClaimsPrincipal CreatePrincipal(User user) =>
        new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.FullName),
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Role, user.Role),
        ], CookieAuthenticationDefaults.AuthenticationScheme));
}
