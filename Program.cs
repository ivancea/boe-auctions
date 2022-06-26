using BoeAuctions;
using BoeAuctions.Objects;
using dotenv.net;
using dotenv.net.Utilities;

DotEnv.Load();

var auctions = new List<Auction>();

// Load data
try
{
    using var client = new Client();
    var startTime = DateTime.Now;

    using var semaphore = new SemaphoreSlim(2);

    var seenIds = new HashSet<string>();

    var auctionTasks = await client.ListAsync()
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

                auctions.Add(auction);

                if (auctions.Count % 50 == 0)
                {
                    Console.WriteLine($"{auctions.Count} auctions loaded...");
                }
            }
            finally
            {
                semaphore.Release();
            }
        })
        .ToListAsync();

    await Task.WhenAll(auctionTasks);

    Console.WriteLine($"{auctions.Count} auctions loaded in {(DateTime.Now - startTime).TotalSeconds} seconds");
}
catch (Exception e)
{
    Console.Error.WriteLine("Error fetching data: " + e);
}

// Save in database
try
{
    var connectionString = EnvReader.GetStringValue("POSTGRES_CONNECTION_STRING");
    using var context = new AuctionsContext(connectionString);

    await context.Database.EnsureCreatedAsync();

    context.Auctions.AddRange(auctions);

    await context.SaveChangesAsync();
}
catch (Exception e)
{
    Console.Error.WriteLine("Error saving data: " + e);
}