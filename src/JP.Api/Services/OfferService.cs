using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using JP.Api.Data;
using JP.Api.Dtos;
using JP.Api.Entities;

namespace JP.Api.Services;

/// <summary>Why an offer operation was refused. The controller turns each one into an HTTP status.</summary>
public enum OfferError
{
    NotFound,
    NotSeller,        // only the seller of the item may do this
    OwnItem,          // a seller cannot bid on their own listing
    ItemUnavailable,
    AmountTooHigh,    // offer must stay below the asking price
    AlreadyPending,   // this buyer already has a pending offer on the item
    AlreadyAnswered   // the offer is no longer pending
}

/// <summary>Either a value or an error, so rules can be refused without exceptions.</summary>
public readonly record struct OfferOutcome<T>(T? Value, OfferError? Error)
{
    public static implicit operator OfferOutcome<T>(T value) => new(value, null);
    public static implicit operator OfferOutcome<T>(OfferError error) => new(default, error);
}

public interface IOfferService
{
    Task<OfferOutcome<OfferDto>> CreateAsync(Guid itemId, Guid buyerId, decimal amount, CancellationToken ct);
    Task<OfferOutcome<PagedResult<OfferDto>>> ListForItemAsync(Guid itemId, Guid sellerId, PageQuery query, CancellationToken ct);
    Task<PagedResult<OfferDto>> ListMadeByAsync(Guid buyerId, PageQuery query, CancellationToken ct);
    Task<OfferOutcome<OfferDto>> AcceptAsync(Guid offerId, Guid sellerId, CancellationToken ct);
    Task<OfferOutcome<OfferDto>> DeclineAsync(Guid offerId, Guid sellerId, CancellationToken ct);
}

public class OfferService(AppDbContext db) : IOfferService
{
    // Entity -> DTO projection, so EF only reads the needed columns
    private static readonly Expression<Func<Offer, OfferDto>> ToDto = o => new OfferDto(
        o.Id, o.ItemId, o.Item.Title, o.Item.Price, o.BuyerId, o.Buyer.UserName!,
        o.Amount, o.Status, o.CreatedAt, o.RespondedAt);

    public async Task<OfferOutcome<OfferDto>> CreateAsync(
        Guid itemId, Guid buyerId, decimal amount, CancellationToken ct)
    {
        var item = await db.Items.AsNoTracking()
            .Where(i => i.Id == itemId)
            .Select(i => new { i.SellerId, i.Price, i.Status })
            .FirstOrDefaultAsync(ct);
        if (item is null) return OfferError.NotFound;
        if (item.SellerId == buyerId) return OfferError.OwnItem;
        if (item.Status != ItemStatus.Available) return OfferError.ItemUnavailable;
        if (amount >= item.Price) return OfferError.AmountTooHigh;
        if (await db.Offers.AnyAsync(o => o.ItemId == itemId && o.BuyerId == buyerId
                                          && o.Status == OfferStatus.Pending, ct))
            return OfferError.AlreadyPending;

        var offer = new Offer { ItemId = itemId, BuyerId = buyerId, Amount = amount };
        db.Offers.Add(offer);
        await db.SaveChangesAsync(ct);

        return await GetDtoAsync(offer.Id, ct);
    }

    public async Task<OfferOutcome<PagedResult<OfferDto>>> ListForItemAsync(
        Guid itemId, Guid sellerId, PageQuery query, CancellationToken ct)
    {
        var itemSellerId = await db.Items.AsNoTracking()
            .Where(i => i.Id == itemId)
            .Select(i => (Guid?)i.SellerId)
            .FirstOrDefaultAsync(ct);
        if (itemSellerId is null) return OfferError.NotFound;
        if (itemSellerId != sellerId) return OfferError.NotSeller;

        return await PaginateAsync(db.Offers.AsNoTracking().Where(o => o.ItemId == itemId), query, ct);
    }

    public Task<PagedResult<OfferDto>> ListMadeByAsync(Guid buyerId, PageQuery query, CancellationToken ct) =>
        PaginateAsync(db.Offers.AsNoTracking().Where(o => o.BuyerId == buyerId), query, ct);

    // Accepting reserves the item and declines the other pending offers on it.
    public async Task<OfferOutcome<OfferDto>> AcceptAsync(Guid offerId, Guid sellerId, CancellationToken ct)
    {
        var offer = await db.Offers.Include(o => o.Item).FirstOrDefaultAsync(o => o.Id == offerId, ct);
        if (offer is null) return OfferError.NotFound;
        if (offer.Item.SellerId != sellerId) return OfferError.NotSeller;
        if (offer.Status != OfferStatus.Pending) return OfferError.AlreadyAnswered;
        // The state machine decides: only an Available item can become Reserved
        if (!offer.Item.CanTransitionTo(ItemStatus.Reserved)) return OfferError.ItemUnavailable;

        var now = DateTime.UtcNow;
        // One transaction: the reservation, the accepted offer and the declined ones
        // change together or not at all
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        offer.Item.TryTransitionTo(ItemStatus.Reserved);
        offer.Status = OfferStatus.Accepted;
        offer.RespondedAt = now;
        await db.SaveChangesAsync(ct);
        await db.Offers
            .Where(o => o.ItemId == offer.ItemId && o.Id != offer.Id && o.Status == OfferStatus.Pending)
            .ExecuteUpdateAsync(s => s
                .SetProperty(o => o.Status, OfferStatus.Declined)
                .SetProperty(o => o.RespondedAt, now), ct);
        await tx.CommitAsync(ct);

        return await GetDtoAsync(offerId, ct);
    }

    public async Task<OfferOutcome<OfferDto>> DeclineAsync(Guid offerId, Guid sellerId, CancellationToken ct)
    {
        var offer = await db.Offers.Include(o => o.Item).FirstOrDefaultAsync(o => o.Id == offerId, ct);
        if (offer is null) return OfferError.NotFound;
        if (offer.Item.SellerId != sellerId) return OfferError.NotSeller;
        if (offer.Status != OfferStatus.Pending) return OfferError.AlreadyAnswered;

        offer.Status = OfferStatus.Declined;
        offer.RespondedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        return await GetDtoAsync(offerId, ct);
    }

    private Task<OfferDto> GetDtoAsync(Guid id, CancellationToken ct) =>
        db.Offers.AsNoTracking().Where(o => o.Id == id).Select(ToDto).FirstAsync(ct);

    private static async Task<PagedResult<OfferDto>> PaginateAsync(
        IQueryable<Offer> offers, PageQuery query, CancellationToken ct)
    {
        var total = await offers.CountAsync(ct);
        var page = await offers
            .OrderByDescending(o => o.CreatedAt)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(ToDto)
            .ToListAsync(ct);
        return new PagedResult<OfferDto>(page, query.Page, query.PageSize, total);
    }
}
