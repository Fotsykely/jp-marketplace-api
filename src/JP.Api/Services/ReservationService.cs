using Microsoft.EntityFrameworkCore;
using JP.Api.Data;
using JP.Api.Entities;

namespace JP.Api.Services;

public interface IReservationService
{
    /// <summary>
    /// Puts back on sale every item reserved before <paramref name="cutoff"/>.
    /// Returns how many items were released.
    /// </summary>
    Task<int> ExpireStaleAsync(DateTime cutoff, CancellationToken ct);
}

public class ReservationService(AppDbContext db) : IReservationService
{
    public async Task<int> ExpireStaleAsync(DateTime cutoff, CancellationToken ct)
    {
        // The moment of the reservation is the moment the seller accepted the offer
        var staleItemIds = await db.Offers.AsNoTracking()
            .Where(o => o.Status == OfferStatus.Accepted
                        && o.RespondedAt < cutoff
                        && o.Item.Status == ItemStatus.Reserved)
            .Select(o => o.ItemId)
            .ToListAsync(ct);

        var released = 0;
        foreach (var itemId in staleItemIds)
        {
            var now = DateTime.UtcNow;
            // One transaction per item: the item and its accepted offer change together
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            // "WHERE Status = Reserved" is the guard: if the seller marked the item as sold
            // in the meantime, no row matches and we leave everything untouched.
            var updated = await db.Items
                .Where(i => i.Id == itemId && i.Status == ItemStatus.Reserved)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(i => i.Status, ItemStatus.Available)
                    .SetProperty(i => i.UpdatedAt, now), ct);
            if (updated == 0) continue; // disposing tx without commit = rollback

            await db.Offers
                .Where(o => o.ItemId == itemId && o.Status == OfferStatus.Accepted)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(o => o.Status, OfferStatus.Cancelled)
                    .SetProperty(o => o.RespondedAt, now), ct);

            await tx.CommitAsync(ct);
            released++;
        }
        return released;
    }
}
