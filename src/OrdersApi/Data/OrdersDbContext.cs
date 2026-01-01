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

    // ✅ FIX: force EF to persist Total (not store-generated)
    modelBuilder.Entity<Order>() // NEW
      .Property(x => x.Total) // NEW
      .HasPrecision(18, 2) // NEW (recommended)
      .ValueGeneratedNever(); // NEW (this is the key)

    modelBuilder.Entity<Order>()
      .HasIndex(x => new { x.PubId, x.ClientRequestId })
      .IsUnique()
      .HasFilter("\"ClientRequestId\" IS NOT NULL");

    modelBuilder.Entity<OrderItem>(b => // NEW/UPDATE
    {
      b.Property(x => x.Sku).IsRequired(); // NEW
      b.HasIndex(x => new { x.OrderId, x.Sku }); // NEW (נוח לחיפושים)
      b.Property(x => x.UnitPrice)
        .HasPrecision(18, 2); // NEW (recommended)
    });

    modelBuilder.Entity<OutboxMessage>(b =>
    {
      b.ToTable("OutboxMessages");
      b.HasKey(x => x.Id);
      b.Property(x => x.Type).IsRequired();
      b.Property(x => x.PayloadJson).IsRequired();
      b.HasIndex(x => x.PublishedAtUtc);
      b.HasIndex(x => x.OccurredAtUtc);
    });

    base.OnModelCreating(modelBuilder);
  }
}
