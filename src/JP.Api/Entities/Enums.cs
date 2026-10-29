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
