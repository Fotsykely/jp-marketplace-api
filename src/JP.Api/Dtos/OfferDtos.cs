using System.ComponentModel.DataAnnotations;
using JP.Api.Entities;

namespace JP.Api.Dtos;

public record CreateOfferRequest([Range(0.01, 100_000_000)] decimal Amount);

public record OfferDto(
    Guid Id, Guid ItemId, string ItemTitle, decimal ItemPrice,
    Guid BuyerId, string BuyerUserName,
    decimal Amount, OfferStatus Status, DateTime CreatedAt, DateTime? RespondedAt);
