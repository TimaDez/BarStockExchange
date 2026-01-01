using InventoryApi.Models;
using Microsoft.EntityFrameworkCore;

namespace InventoryApi.Data;

public sealed class InventoryDbContext : DbContext
{
  public InventoryDbContext(DbContextOptions<InventoryDbContext> options) : base(options)
  {
  }

  public DbSet<InventoryItem> Items => Set<InventoryItem>();
  public DbSet<InventoryReservation> Reservations => Set<InventoryReservation>();

  protected override void OnModelCreating(ModelBuilder modelBuilder)
  {
    modelBuilder.Entity<InventoryItem>()
      .HasIndex(x => new { x.PubId, x.Sku })
      .IsUnique();

    modelBuilder.Entity<InventoryReservation>()
      .HasIndex(x => new { x.PubId, x.RequestId })
      .IsUnique()
      .HasFilter("\"RequestId\" IS NOT NULL");

    base.OnModelCreating(modelBuilder);
  }
}
