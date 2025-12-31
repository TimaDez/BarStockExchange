using Microsoft.EntityFrameworkCore;
using OrdersApi.Models;
using OrdersApi.Outbox;

namespace OrdersApi.Data;

public sealed class OrdersDbContext : DbContext
{
  public OrdersDbContext(DbContextOptions<OrdersDbContext> options) : base(options)
  {
  }

  public DbSet<Order> Orders => Set<Order>();
  public DbSet<OrderItem> OrderItems => Set<OrderItem>();
  public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

  protected override void OnModelCreating(ModelBuilder modelBuilder)
  {
    modelBuilder.Entity<Order>()
      .HasMany(x => x.Items)
      .WithOne(x => x.Order)
      .HasForeignKey(x => x.OrderId);

    modelBuilder.Entity<Order>()
      .HasIndex(x => new { x.PubId, x.CreatedAt });

    // DisplayNumber: מייצר מספר אוטומטי (Identity). זה גלובלי לשירות כרגע.
    modelBuilder.Entity<Order>()
      .Property(x => x.DisplayNumber)
      .ValueGeneratedOnAdd();

    modelBuilder.Entity<Order>()
      .HasIndex(x => new { x.PubId, x.ClientRequestId })
      .IsUnique()
      .HasFilter("\"ClientRequestId\" IS NOT NULL"); // NEW

    base.OnModelCreating(modelBuilder);
  }
}
