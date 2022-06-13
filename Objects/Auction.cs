
using System.ComponentModel.DataAnnotations.Schema;

namespace BoeAuctions.Objects;

public class Auction
{
    public string? Id { get; set; }

    public AuctionStatus? Status { get; set; }

    public string? Type { get; set; }

    public DateTime? StartDate { get; set; }

    public DateTime? EndDate { get; set; }

    public string? AwardProcedure { get; set; }

    public string? BoeAnnouncementId { get; set; }

    [Column(TypeName = "jsonb")]
    public AuctionAuthority? Authority { get; set; }

    [Column(TypeName = "jsonb")]
    public AuctionRelatedPerson? Creditor { get; set; }

    [Column(TypeName = "jsonb")]
    public AuctionRelatedPerson? Administrator { get; set; }


    public ICollection<AuctionLot> Lots { get; set; } = new List<AuctionLot>();
}