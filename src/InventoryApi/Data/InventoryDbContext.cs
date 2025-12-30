using InventoryApi.Models;
using Microsoft.EntityFrameworkCore;

namespace InventoryApi.Data;

public sealed class InventoryDbContext : DbContext
{
  public InventoryDbContext(DbContextOptions<InventoryDbContext> options) : base(options)
  {
  }

  public DbSet<InventoryItem> Items => Set<InventoryItem>();

  protected override void OnModelCreating(ModelBuilder modelBuilder)
  {
    modelBuilder.Entity<InventoryItem>()
      .HasIndex(x => new { x.PubId, x.Sku })
      .IsUnique();

    base.OnModelCreating(modelBuilder);
  }
}
