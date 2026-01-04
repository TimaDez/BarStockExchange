using OrdersApi.Contracts;

namespace OrdersApi.Application.Orders.CreateOrder;

public sealed record CreateOrderResult
{
  #region Private members

  public bool IsSuccess { get; init; }
  public OrderResponse? Response { get; init; }

  public CreateOrderErrorCode? ErrorCode { get; init; }
  public string? ErrorMessage { get; init; }
  public string? ErrorDetails { get; init; }

  #endregion

  #region Methods

  public static CreateOrderResult Ok(OrderResponse response) =>
    new()
    {
      IsSuccess = true,
      Response = response
    };

  public static CreateOrderResult Fail(CreateOrderErrorCode code, string message, string? details = null) =>
    new()
    {
      IsSuccess = false,
      ErrorCode = code,
      ErrorMessage = message,
      ErrorDetails = details
    };

  #endregion
}
