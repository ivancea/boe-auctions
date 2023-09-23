namespace BoeAuctions.Model.Objects;

public class AuctionLot
{

    public string? AuctionId { get; set; }

    public Auction? Auction { get; set; }

    public string? Id { get; set; }

    public decimal? ClaimedAmount { get; set; }

    public decimal? Value { get; set; }

    public decimal? Valuation { get; set; }

    public decimal? DepositAmount { get; set; }

    public decimal? MinimumBid { get; set; }

    public decimal? BidIncrement { get; set; }


    // Good data

    public string? Type { get; set; }

    public string? Summary { get; set; }

    public string? Description { get; set; }

    public string? Idufir { get; set; }

    public string? RegisterReference { get; set; }

    public string? Address { get; set; }

    public string? ZipCode { get; set; }

    public string? Locality { get; set; }

    public string? Province { get; set; }

    public decimal? Area { get; set; }

    public decimal? Quota { get; set; }

    public string? Parcel { get; set; }

    public string? PlaceName { get; set; }

    public string? RegistryReference { get; set; }

    public bool? HabitualResidence { get; set; }

    public string? PossessoryStatus { get; set; }

    public string? Visitable { get; set; }

    public string? Burdens { get; set; }

    public string? RegistryInscription { get; set; }

    public string? JuridicTitle { get; set; }

    public string? RegistrationPlate { get; set; }

    public string? Brand { get; set; }

    public string? Model { get; set; }

    public string? FrameNumber { get; set; }

    public DateOnly? RegistrationDate { get; set; }

    public DateOnly? AcquisitionDate { get; set; }

    public string? Deposit { get; set; }

    public string? AdditionalInformation { get; set; }
}