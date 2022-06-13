namespace BoeAuctions.Objects;

public enum AuctionStatus
{
    Upcoming,
    Active
}

public static class AuctionStatusMethods
{
    public static string GetId(this AuctionStatus status) => status switch
    {
        AuctionStatus.Upcoming => "PU",
        AuctionStatus.Active => "EJ",
        _ => throw new ArgumentException("Unknown AuctionStatus value: " + status)
    };
}