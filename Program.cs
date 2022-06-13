using BoeAuctions;
using BoeAuctions.Objects;

var auctions = new List<Auction>();

// Load data
try
{
    using var client = new Client();
    var startTime = DateTime.Now;

    using var semaphore = new SemaphoreSlim(2);

    var auctionTasks = await client.ListAsync().Take(20)
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
    using var context = new AuctionsContext("Host=127.0.0.1;Username=postgres;Password=postgres;Database=postgres");

    // await context.Database.EnsureDeletedAsync();
    await context.Database.EnsureCreatedAsync();

    context.Auctions.AddRange(auctions);

    await context.SaveChangesAsync();
}
/*catch (DbEntityValidationException e)
{
    Console.Error.WriteLine("Validation errors: ");

    foreach (var eve in e.EntityValidationErrors)
    {
        Console.Error.WriteLine($"Entity of type \"{eve.Entry.Entity.GetType().Name}\" in state \"{eve.Entry.State}\" has the following validation errors:");

        foreach (var ve in eve.ValidationErrors)
        {
            Console.Error.WriteLine($"- Property: \"{ve.PropertyName}\", Error: \"{ve.ErrorMessage}\"");
        }
    }
}*/
catch (Exception e)
{
    Console.Error.WriteLine("Error saving data: " + e);
}