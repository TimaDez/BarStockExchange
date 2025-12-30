using AuthApi.Models;
using Microsoft.EntityFrameworkCore;

namespace AuthApi.Data;

public sealed class AuthDbContext : DbContext
{
    public AuthDbContext(DbContextOptions<AuthDbContext> options) : base(options) { }

    public DbSet<AuthUser> Users => Set<AuthUser>();
    public DbSet<Pub> Pubs => Set<Pub>();
    public DbSet<UserPub> UserPubs => Set<UserPub>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AuthUser>()
            .HasIndex(x => x.Email)
            .IsUnique();

        modelBuilder.Entity<UserPub>()
            .HasKey(x => new { x.UserId, x.PubId });

        modelBuilder.Entity<UserPub>()
            .HasOne(x => x.User)
            .WithMany(x => x.UserPubs)
            .HasForeignKey(x => x.UserId);

        modelBuilder.Entity<UserPub>()
            .HasOne(x => x.Pub)
            .WithMany(x => x.UserPubs)
            .HasForeignKey(x => x.PubId);

        base.OnModelCreating(modelBuilder);
    }
}
