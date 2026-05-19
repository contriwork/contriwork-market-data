namespace Contriwork.MarketData;

/// <summary>
/// Time interval for OHLCV candles. CONTRACT v1 §5.5 — the member names and
/// their ordering are invariant within the v1 contract revision.
/// </summary>
public enum Interval
{
    /// <summary>One minute.</summary>
    M1,

    /// <summary>Five minutes.</summary>
    M5,

    /// <summary>Fifteen minutes.</summary>
    M15,

    /// <summary>Thirty minutes.</summary>
    M30,

    /// <summary>One hour.</summary>
    H1,

    /// <summary>Four hours.</summary>
    H4,

    /// <summary>One day.</summary>
    D1,

    /// <summary>One week.</summary>
    W1,

    /// <summary>One month.</summary>
    MN1,
}
