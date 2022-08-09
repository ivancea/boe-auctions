using BoeAuctions;
using BoeAuctions.Objects;
using dotenv.net;
using dotenv.net.Utilities;

DotEnv.Load();


try
{
    // Setup database
    var connectionString = EnvReader.GetStringValue("POSTGRES_CONNECTION_STRING");
    using var context = new AuctionsContext(connectionString);

    await context.Database.EnsureCreatedAsync();

    // Load data
    using var client = new Client();
    var startTime = DateTime.Now;

    using var semaphore = new SemaphoreSlim(2);

    var seenIds = new HashSet<string>();

    // Add current auction IDs to seenIds, to ignore them
    seenIds.UnionWith(context.Auctions.Select(a => a.Id!));

    var auctions = new List<Auction>();

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

                foreach (var lot in auction.Lots.DistinctBy(lot => (lot.Type, lot.Province)))
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
                Console.Error.WriteLine($"Error Loading auction with ID '${id}': " + e);
            }
            finally
            {
                semaphore.Release();
            }
        })
        .ToListAsync();

    await Task.WhenAll(auctionTasks);

    Console.WriteLine($"{auctions.Count} auctions loaded in {(DateTime.Now - startTime).TotalSeconds} seconds");

    // Save data
    context.Auctions.AddRange(auctions);

    await context.SaveChangesAsync();
}
catch (Exception e)
{
    Console.Error.WriteLine("Error: " + e);
}