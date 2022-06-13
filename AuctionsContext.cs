using BoeAuctions.Objects;
using Microsoft.EntityFrameworkCore;

namespace BoeAuctions;

public class AuctionsContext : DbContext
{
    private readonly string _connectionString;

    public AuctionsContext(string connectionString)
    {
        _connectionString = connectionString;
    }

    public DbSet<Auction> Auctions { get; set; } = null!;
    public DbSet<AuctionLot> Lots { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AuctionLot>()
            .HasKey(l => new { l.AuctionId, l.Id });
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        => optionsBuilder
            .UseNpgsql(_connectionString)
            .EnableSensitiveDataLogging();
}