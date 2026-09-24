namespace ShotAI.Core.Home;

/// <summary>The date spans of the Home list (spec 06 2.11), in canonical newest-to-oldest order.</summary>
public enum DateBucket
{
    /// <summary>On or after this week's Sunday midnight, the future included.</summary>
    ThisWeek,

    /// <summary>The seven days before this week.</summary>
    LastWeek,

    /// <summary>From the first of this month, when that is before last week.</summary>
    ThisMonth,

    /// <summary>The calendar month before this one.</summary>
    LastMonth,

    /// <summary>Anything earlier, and a timestamp that is not a date.</summary>
    Older,
}
