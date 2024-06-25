using BoeAuctions;
using BoeAuctions.Model;
using BoeAuctions.Model.Objects;
using BoeAuctions.Telegram;
using dotenv.net;
using dotenv.net.Utilities;
using Microsoft.EntityFrameworkCore;
using MoreLinq.Extensions;

DotEnv.Load();

try
{
    // Setup database
    var connectionString = EnvReader.GetStringValue("POSTGRES_CONNECTION_STRING");
    using var context = new AuctionsContext(connectionString);

    await context.Database.EnsureCreatedAsync();

    Console.WriteLine("Loading existing auctions from database...");
    var existingAuctionIds = await context.Auctions.Select(a => a.Id!).ToListAsync();
    var nonNotifiedStoredAuctions = await context.Auctions
        .Where(a => !a.WasNotified)
        .Where(a => a.EndDate > DateTime.UtcNow)
        .Include(a => a.Lots)
        .ToListAsync();

    var startTime = DateTime.Now;

    var newAuctions = await LoadAuctionsFromApi(existingAuctionIds);

    Console.WriteLine($"{newAuctions.Count} auctions loaded in {(DateTime.Now - startTime).TotalSeconds} seconds. Saving them...");
    await SaveInDatabase(context, newAuctions);

    var nonNotifiedAuctions = newAuctions
        .Concat(nonNotifiedStoredAuctions)
        .OrderBy(a => a.EndDate)
        .ToList();

    Console.WriteLine($"Sending {nonNotifiedAuctions.Count} non-notified auctions to Telegram...");

    await SendToTelegram(context, nonNotifiedAuctions);
}
catch (Exception e)
{
    Console.Error.WriteLine("Error: " + e);
}

static async Task<List<Auction>> LoadAuctionsFromApi(IEnumerable<string> existingIds)
{
    using var client = new Client();

    using var semaphore = new SemaphoreSlim(2);

    var seenIds = new HashSet<string>();

    // Add current auction IDs to seenIds, to ignore them
    seenIds.UnionWith(existingIds);

    var auctions = new List<Auction>();

    var totalAuctionCount = 0;
    var failedAuctionCount = 0;

    // Load all auctions not already in the DB
    var auctionTasks = await client.ListAsync()
        .Select(id =>
        {
            totalAuctionCount++;
            return id;
        })
        .Where(id => seenIds.Add(id.Item2))
        .SelectAwait(async id =>
        {
            await semaphore.WaitAsync();
            return id;
        })
        .Select(async id =>
        {
            try
            {
                var auction = await client.GetAuctionAsync(id.Item1, id.Item2);

                Console.WriteLine("Done: " + auction.Id);

                foreach (var lot in auction.Lots)
                {
                    Console.WriteLine($" - {lot.Type} at {lot.Province}");
                }

                auctions.Add(auction);

                if (auctions.Count % 50 == 0)
                {
                    Console.WriteLine($"{auctions.Count} auctions loaded...");
                }
            }
            catch (Exception e)
            {
                failedAuctionCount++;
                Console.Error.WriteLine($"Error Loading auction with ID '${id}': " + e);
            }
            finally
            {
                semaphore.Release();
            }
        })
        .ToListAsync();

    await Task.WhenAll(auctionTasks);

    if (totalAuctionCount == 0)
    {
        throw new Exception("No auctions found! Suspicious!");
    }

    if (failedAuctionCount == totalAuctionCount)
    {
        throw new Exception("All auctions failed to load! Suspicious!");
    }

    return auctions;
}

static async Task SaveInDatabase(AuctionsContext context, IEnumerable<Auction> auctions)
{
    try
    {
        context.Auctions.AddRange(auctions);

        await context.SaveChangesAsync();
    }
    catch (Exception e)
    {
        throw new Exception("Database error", e);
    }
}

static async Task SendToTelegram(AuctionsContext context, IEnumerable<Auction> auctions)
{
    var telegramClient = new TelegramClient();

    foreach (var auctionBatch in auctions.Batch(10))
    {
        // Update auctions as notified, to avoid not sending or resending on errors
        var auctionIds = auctionBatch.Select(a => a.Id!).ToList();
        Console.WriteLine($"Marking {string.Join(", ", auctionIds)} auctions as notified...");
        await context.Auctions
            .Where(a => auctionIds.Contains(a.Id!))
            .ExecuteUpdateAsync(a =>
                a.SetProperty(a => a.WasNotified, true)
            );

        // Notify
        await telegramClient.SendAuctionsToTelegram(auctionBatch);
    };
}