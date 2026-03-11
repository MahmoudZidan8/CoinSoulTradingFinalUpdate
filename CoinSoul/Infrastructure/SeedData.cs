using CoinSoul.Entities;
using Microsoft.AspNetCore.Identity;

namespace CoinSoul.Infrastructure;

public static class SeedData
{
    /// <summary>
    /// Create default Admin user if it doesn't exist.
    /// Username: Admin
    /// Password: Admin308
    /// </summary>
    public static async Task SeedAdminAsync(this WebApplication app)
    {
        await using var scope = app.Services.CreateAsyncScope();

        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<AppRole>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();

        const string roleName = "Admin";
        const string userName = "Admin";
        const string password = "Admin@308!";

        // Role
        var role = await roleManager.FindByNameAsync(roleName);
        if (role is null)
        {
            role = new AppRole
            {
                Name = roleName,
                NormalizedName = roleName.ToUpperInvariant()
            };
            await roleManager.CreateAsync(role);
        }

        // User
        var user = await userManager.FindByNameAsync(userName);
        if (user is null)
        {
            user = new AppUser
            {
                UserName = userName,
                NormalizedUserName = userName.ToUpperInvariant(),
                Email = "admin@local",
                NormalizedEmail = "ADMIN@LOCAL",
                EmailConfirmed = true
            };

            var create = await userManager.CreateAsync(user, password);
            if (!create.Succeeded)
            {
                var msg = string.Join(" | ", create.Errors.Select(e => $"{e.Code}: {e.Description}"));
                throw new InvalidOperationException($"Failed to create Admin user: {msg}");
            }
        }

        // Add role
        var inRole = await userManager.IsInRoleAsync(user, roleName);
        if (!inRole)
        {
            await userManager.AddToRoleAsync(user, roleName);
        }
    }
}
