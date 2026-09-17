using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UltimatePos.Domain.Entities;

namespace UltimatePos.Application.Business.Dtos
{
    public record RegisterBusinessRequestDto(
    string BusinessName,
    string? TradingName,
    BusinessType BusinessType,
    string? RegistrationNumber,
    string KraPin,
    string? PhysicalAddress,
    string? County,
    string? PhoneNumber,
    string? Email,
    string? LogoUrl,
    string? BackgroundImageUrl);

    public record UpdateBusinessRequestDto(
        string BusinessName,
        string? TradingName,
        BusinessType BusinessType,
        string? RegistrationNumber,
        string KraPin,
        string? PhysicalAddress,
        string? County,
        string? PhoneNumber,
        string? Email,
        string? LogoUrl,
        string? BackgroundImageUrl);

    public record BusinessDto(
        Guid BusinessId, string BusinessName, string? TradingName, BusinessType BusinessType,
        string? RegistrationNumber, string KraPin, string? PhysicalAddress, string? County,
        string? PhoneNumber, string? Email, string? LogoUrl, string? BackgroundImageUrl, bool IsActive);

    public record RegisterCustomerRequestDto(
        Guid BusinessId,
        string CustomerName,
        string? KraPin,
        string? ContactPerson,
        string? PhoneNumber,
        string? Email,
        string? PhysicalAddress);

    public record UpdateCustomerRequestDto(
        string CustomerName,
        string? KraPin,
        string? ContactPerson,
        string? PhoneNumber,
        string? Email,
        string? PhysicalAddress);

    public record CustomerDto(
        Guid CustomerId, Guid BusinessId, string CustomerName, string? KraPin, string? ContactPerson,
        string? PhoneNumber, string? Email, string? PhysicalAddress, bool IsActive);
}
