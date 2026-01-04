using System.Net;
using Microsoft.EntityFrameworkCore;
using OrdersApi.Contracts;
using OrdersApi.Data;
using OrdersApi.Events;
using OrdersApi.Models;
using OrdersApi.Outbox;

namespace OrdersApi.Application.Orders.CreateOrder;

public sealed class CreateOrderHandler
{
  #region Private members

  private readonly OrdersDbContext _db;
  private readonly IHttpClientFactory _httpClientFactory;

  #endregion

  #region Methods

  public CreateOrderHandler(OrdersDbContext db, IHttpClientFactory httpClientFactory)
  {
    _db = db;
    _httpClientFactory = httpClientFactory;
  }

  public async Task<CreateOrderResult> HandleAsync(
    Guid pubId,
    CreateOrderRequest req,
    string? authHeader,
    string correlationId,
    CancellationToken ct)
  {
    if (pubId == Guid.Empty)
      return CreateOrderResult.Fail(CreateOrderErrorCode.Unauthorized, "Missing pub_id.");

    if (req.Items is null || req.Items.Count == 0)
      return CreateOrderResult.Fail(CreateOrderErrorCode.ValidationFailed, "Order must contain at least one item.");

    foreach (var item in req.Items)
    {
      if (string.IsNullOrWhiteSpace(item.Sku) || item.Quantity <= 0)
        return CreateOrderResult.Fail(CreateOrderErrorCode.ValidationFailed, "Invalid item in order.");
    }

    var reserveLines = req.Items
      .GroupBy(i => i.Sku.Trim().ToUpperInvariant())
      .Select(g => new InventoryReserveLine(g.Key, g.Sum(x => x.Quantity)))
      .ToList();

    var reserve = await ReserveInventoryAsync(reserveLines, authHeader, correlationId, ct);
    if (!reserve.IsSuccess)
      return reserve.Error!;

    var reservedBySku = reserve.Value!.Lines
      .ToDictionary(x => x.Sku.Trim().ToUpperInvariant(), x => x);

    foreach (var line in reserveLines)
    {
      if (!reservedBySku.ContainsKey(line.Sku))
        return CreateOrderResult.Fail(CreateOrderErrorCode.InventoryMissingSku, "Inventory reserve missing sku.",
          line.Sku);
    }

    var order = new Order
    {
      Id = Guid.NewGuid(),
      PubId = pubId,
      Status = OrderStatus.Pending,
      Items = reserveLines.Select(l =>
      {
        var r = reservedBySku[l.Sku];
        return new OrderItem
        {
          Id = Guid.NewGuid(),
          Sku = r.Sku,
          Name = r.Name.Trim(),
          Quantity = l.Quantity,
          UnitPrice = r.UnitPrice
        };
      }).ToList()
    };

    order.Total = order.Items.Sum(i => i.UnitPrice * i.Quantity);

    try
    {
      _db.Orders.Add(order);
      await _db.SaveChangesAsync(ct);
    }
    catch (DbUpdateException ex)
    {
      return CreateOrderResult.Fail(CreateOrderErrorCode.PersistenceFailed, "Failed to save order.", ex.Message);
    }

    var evt = new OrderCreatedEvent(order.Id, order.PubId, DateTimeOffset.UtcNow, order.Total);
    var payloadJson = System.Text.Json.JsonSerializer.Serialize(evt);

    _db.OutboxMessages.Add(new OutboxMessage
    {
      Id = Guid.NewGuid(),
      OccurredAtUtc = DateTimeOffset.UtcNow,
      Type = nameof(OrderCreatedEvent),
      PayloadJson = payloadJson,
      CorrelationId = correlationId
    });

    try
    {
      await _db.SaveChangesAsync(ct);
    }
    catch (DbUpdateException ex)
    {
      return CreateOrderResult.Fail(CreateOrderErrorCode.PersistenceFailed, "Failed to save outbox message.",
        ex.Message);
    }

    var response = new OrderResponse(
      order.Id,
      order.DisplayNumber,
      order.PubId,
      order.Total,
      order.Status,
      order.CreatedAt,
      order.Items.Select(i => new OrderItemResponse(i.Sku, i.Name, i.Quantity, i.UnitPrice)).ToList()
    );

    return CreateOrderResult.Ok(response);
  }

  private async Task<ReserveResult> ReserveInventoryAsync(
    List<InventoryReserveLine> reserveLines,
    string? authHeader,
    string correlationId,
    CancellationToken ct)
  {
    try
    {
      var inventoryClient = _httpClientFactory.CreateClient("Inventory");
      var reserveReq = new InventoryReserveRequest(reserveLines);

      var invHttpReq = new HttpRequestMessage(HttpMethod.Post, "/api/inventory/reserve")
      {
        Content = JsonContent.Create(reserveReq)
      };

      if (!string.IsNullOrWhiteSpace(authHeader))
        invHttpReq.Headers.TryAddWithoutValidation("Authorization", authHeader);

      invHttpReq.Headers.TryAddWithoutValidation("X-Correlation-ID", correlationId);

      var invResp = await inventoryClient.SendAsync(invHttpReq, ct);

      if (invResp.StatusCode == HttpStatusCode.Conflict)
      {
        var details = await invResp.Content.ReadAsStringAsync(ct);
        return ReserveResult.Fail(CreateOrderResult.Fail(CreateOrderErrorCode.InventoryConflict, "Not enough stock.",
          details));
      }

      if (!invResp.IsSuccessStatusCode)
      {
        var details = await invResp.Content.ReadAsStringAsync(ct);
        return ReserveResult.Fail(CreateOrderResult.Fail(CreateOrderErrorCode.InventoryBadGateway,
          "Inventory reserve failed.", details));
      }

      var dto = await invResp.Content.ReadFromJsonAsync<InventoryReserveResponse>(cancellationToken: ct);
      if (dto?.Lines is null || dto.Lines.Count == 0)
        return ReserveResult.Fail(CreateOrderResult.Fail(CreateOrderErrorCode.InventoryBadGateway,
          "Inventory reserve returned empty response."));

      return ReserveResult.Ok(dto);
    }
    catch (HttpRequestException ex)
    {
      return ReserveResult.Fail(CreateOrderResult.Fail(CreateOrderErrorCode.InventoryUnreachable,
        "Inventory service unreachable.", ex.Message));
    }
    catch (TaskCanceledException ex)
    {
      return ReserveResult.Fail(CreateOrderResult.Fail(CreateOrderErrorCode.InventoryTimeout,
        "Inventory reserve timed out.", ex.Message));
    }
  }

  #endregion

  #region Private types

  private sealed record ReserveResult
  {
    public bool IsSuccess { get; init; }
    public InventoryReserveResponse? Value { get; init; }
    public CreateOrderResult? Error { get; init; }

    public static ReserveResult Ok(InventoryReserveResponse value) =>
      new() { IsSuccess = true, Value = value };

    public static ReserveResult Fail(CreateOrderResult error) =>
      new() { IsSuccess = false, Error = error };
  }

  #endregion
}
