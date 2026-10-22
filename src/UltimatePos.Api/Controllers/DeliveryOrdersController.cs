using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UltimatePos.Application.Common.Dtos;
using UltimatePos.Application.Deliveries;
using UltimatePos.Application.Reports;
using UltimatePos.Domain.Entities;

namespace UltimatePos.Api.Controllers;

/// <summary>
/// Orders for delivery: create (Pending, stock reserved; or straight out with a rider when rung up at the till with delivery by rider)
/// → dispatch with a rider, alone or several on one trip → deliver (becomes a sale, paid by cash, an M-Pesa code, a till payment or on
/// credit), or return (the rider brought it back), or cancel. Delivering on credit also needs DeliveryOrders.AcceptCredit.
/// </summary>
[ApiController]
[Route("api/v1/delivery-orders")]
public class DeliveryOrdersController : ControllerBase
{
    private readonly DeliveryOrderService _orders;
    public DeliveryOrdersController(DeliveryOrderService orders) => _orders = orders;

    /// <summary>Orders at the branches the caller can see, newest first. from/to are the local days they were taken.</summary>
    [Authorize(Policy = "PERMISSION:DeliveryOrders.View")]
    [HttpGet]
    public async Task<IActionResult> GetOrders([FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        [FromQuery] DeliveryOrderStatus? status = null, [FromQuery] Guid? customerId = null, [FromQuery] string? riderName = null,
        [FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null, [FromQuery] int utcOffsetMinutes = ReportPeriod.DefaultUtcOffsetMinutes,
        [FromQuery] Guid? deliveryTripId = null)
    {
        var paged = await _orders.GetOrdersAsync(page, pageSize,
            new DeliveryOrderQuery(status, customerId, riderName, from, to, utcOffsetMinutes, deliveryTripId));
        var meta = new PagingMeta(paged.Page, paged.PageSize, paged.TotalCount, paged.TotalPages);
        return Ok(ApiResponse<IEnumerable<DeliveryOrderDto>>.Ok(paged.Items, meta));
    }

    /// <summary>Deliveries per rider in the period and how they were paid (cash to hand in), plus what is open now.</summary>
    [Authorize(Policy = "PERMISSION:DeliveryOrders.View")]
    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary([FromQuery] DateOnly from, [FromQuery] DateOnly to,
        [FromQuery] int utcOffsetMinutes = ReportPeriod.DefaultUtcOffsetMinutes)
    {
        var result = await _orders.GetSummaryAsync(from, to, utcOffsetMinutes);
        return Ok(ApiResponse<DeliverySummaryDto>.Ok(result));
    }

    /// <summary>Trips dispatched in the period (from/to: local days), newest first, with their orders.</summary>
    [Authorize(Policy = "PERMISSION:DeliveryOrders.View")]
    [HttpGet("trips")]
    public async Task<IActionResult> GetTrips([FromQuery] DateOnly from, [FromQuery] DateOnly to, [FromQuery] string? riderName = null,
        [FromQuery] int utcOffsetMinutes = ReportPeriod.DefaultUtcOffsetMinutes)
    {
        var result = await _orders.GetTripsAsync(new DeliveryTripQuery(from, to, riderName, utcOffsetMinutes));
        return Ok(ApiResponse<IEnumerable<DeliveryTripDto>>.Ok(result));
    }

    [Authorize(Policy = "PERMISSION:DeliveryOrders.View")]
    [HttpGet("trips/{id:guid}")]
    public async Task<IActionResult> GetTrip(Guid id)
    {
        var result = await _orders.GetTripAsync(id);
        return Ok(ApiResponse<DeliveryTripDto>.Ok(result));
    }

    /// <summary>
    /// Trips per rider against the fuel paid for them (expenses tagged with the rider, in expenseCategoryId when given). With
    /// expectedFuelPerTrip each rider's fuel is checked against what their trips account for.
    /// </summary>
    [Authorize(Policy = "PERMISSION:DeliveryOrders.View")]
    [Authorize(Policy = "PERMISSION:Expenses.View")]
    [HttpGet("fuel-report")]
    public async Task<IActionResult> GetFuelReport([FromQuery] DateOnly from, [FromQuery] DateOnly to, [FromQuery] Guid? expenseCategoryId = null,
        [FromQuery] decimal? expectedFuelPerTrip = null, [FromQuery] int utcOffsetMinutes = ReportPeriod.DefaultUtcOffsetMinutes)
    {
        var result = await _orders.GetFuelReportAsync(from, to, expenseCategoryId, expectedFuelPerTrip, utcOffsetMinutes);
        return Ok(ApiResponse<DeliveryFuelReportDto>.Ok(result));
    }

    [Authorize(Policy = "PERMISSION:DeliveryOrders.View")]
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetOrder(Guid id)
    {
        var result = await _orders.GetAsync(id);
        return Ok(ApiResponse<DeliveryOrderDto>.Ok(result));
    }

    /// <summary>
    /// Takes an order at the branch signed in to. Its quantities are held back from other sales until it is delivered, returned or
    /// cancelled. With riderName (checkout with delivery by rider) it goes straight out for delivery, which also needs
    /// DeliveryOrders.Dispatch. It is not a sale until the rider is back with the payment.
    /// </summary>
    [Authorize(Policy = "PERMISSION:DeliveryOrders.Create")]
    [HttpPost]
    public async Task<IActionResult> CreateOrder(CreateDeliveryOrderRequestDto request)
    {
        var result = await _orders.CreateAsync(request);
        return CreatedAtAction(nameof(GetOrder), new { id = result.DeliveryOrderId }, ApiResponse<DeliveryOrderDto>.Ok(result));
    }

    [Authorize(Policy = "PERMISSION:DeliveryOrders.Dispatch")]
    [HttpPost("{id:guid}/dispatch")]
    public async Task<IActionResult> Dispatch(Guid id, DispatchDeliveryOrderRequestDto request)
    {
        var result = await _orders.DispatchAsync(id, request);
        return Ok(ApiResponse<DeliveryOrderDto>.Ok(result));
    }

    /// <summary>Several pending orders out with one rider, as one trip (or added to a trip still out).</summary>
    [Authorize(Policy = "PERMISSION:DeliveryOrders.Dispatch")]
    [HttpPost("trips")]
    public async Task<IActionResult> DispatchTrip(DispatchTripRequestDto request)
    {
        var result = await _orders.DispatchTripAsync(request);
        return Ok(ApiResponse<DeliveryTripDto>.Ok(result));
    }

    /// <summary>The rider is back with the payment: records the sale (completes it). Stock leaves here, once.</summary>
    [Authorize(Policy = "PERMISSION:DeliveryOrders.Deliver")]
    [HttpPost("{id:guid}/deliver")]
    public async Task<IActionResult> Deliver(Guid id, DeliverDeliveryOrderRequestDto request)
    {
        var result = await _orders.DeliverAsync(id, request);
        return Ok(ApiResponse<DeliveryOrderDto>.Ok(result));
    }

    /// <summary>The rider brought it back undelivered: releases the reserved stock. A reason is required.</summary>
    [Authorize(Policy = "PERMISSION:DeliveryOrders.Return")]
    [HttpPost("{id:guid}/return")]
    public async Task<IActionResult> Return(Guid id, ReturnDeliveryOrderRequestDto request)
    {
        var result = await _orders.ReturnAsync(id, request);
        return Ok(ApiResponse<DeliveryOrderDto>.Ok(result));
    }

    /// <summary>Called off: releases the reserved stock. A reason is required.</summary>
    [Authorize(Policy = "PERMISSION:DeliveryOrders.Cancel")]
    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancelDeliveryOrderRequestDto request)
    {
        var result = await _orders.CancelAsync(id, request);
        return Ok(ApiResponse<DeliveryOrderDto>.Ok(result));
    }
}
