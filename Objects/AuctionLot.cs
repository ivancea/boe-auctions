using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BoeAuctions.Objects;

public class AuctionLot
{

    public string? AuctionId { get; set; }

    public Auction? Auction { get; set; }

    public int? Id { get; set; }

    public decimal? Value { get; set; }

    public decimal? Valuation { get; set; }

    public decimal? Deposit { get; set; }

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

    public bool? HabitualResidence { get; set; }

    public string? PossessoryStatus { get; set; }

    public string? Visitable { get; set; }

    public bool? Burdens { get; set; }

    public string? RegistryInscription { get; set; }

    public string? JuridicTitle { get; set; }

    public string? RegistrationPlate { get; set; }

    public string? AdditionalInformation { get; set; }
}