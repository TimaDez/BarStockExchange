using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace OrdersApi.Data;

public sealed class OrdersDbContextFactory : IDesignTimeDbContextFactory<OrdersDbContext>
{
  public OrdersDbContext CreateDbContext(string[] args)
  {
    var config = new ConfigurationBuilder()
      .SetBasePath(Directory.GetCurrentDirectory())
      .AddJsonFile("appsettings.json", optional: true)
      .AddEnvironmentVariables()
      .Build();

    // נסה לקחת מה-config, ואם אין – fallback חכם ללוקאל (לא חייב DB קיים בשביל "migrations add")
    var connectionString =
      config.GetConnectionString("Db")
      ?? "Host=localhost;Port=5435;Database=ordersdb;Username=app;Password=app";

    var optionsBuilder = new DbContextOptionsBuilder<OrdersDbContext>();
    optionsBuilder.UseNpgsql(connectionString);

    return new OrdersDbContext(optionsBuilder.Options);
  }
}
