using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace CloudShell.Persistence;

public sealed class CloudShellIdentityDbContext(
    DbContextOptions<CloudShellIdentityDbContext> options) :
    IdentityDbContext<IdentityUser, IdentityRole, string>(options);
