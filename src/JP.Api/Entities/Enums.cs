namespace JP.Api.Entities;

/// <summary>Condition of the item, as on Vinted.</summary>
public enum ItemCondition
{
    NewWithTags,   // New with tags
    NewWithoutTags,// New without tags
    VeryGood,      // Very good condition
    Good,          // Good condition
    Satisfactory   // Satisfactory
}

/// <summary>Lifecycle of a listing.</summary>
public enum ItemStatus
{
    Available, // For sale
    Reserved,  // Reserved (payment in progress)
    Sold       // Sold
}

/// <summary>Lifecycle of a price offer. Pending is the only state that can still change.</summary>
public enum OfferStatus
{
    Pending,  // Waiting for the seller
    Accepted, // Seller agreed to the price
    Declined, // Seller refused (or another offer was accepted)
    Cancelled // Was accepted, then the reservation was cancelled
}

/// <summary>Lifecycle of a Stripe payment.</summary>
public enum PaymentStatus
{
    Pending,   // Checkout Session created, buyer has not paid yet
    Succeeded, // Stripe confirmed the payment, the item is sold
    Expired,   // The buyer did not pay before the session expired
    Refunded   // Paid too late (reservation already released): money sent back automatically
}
