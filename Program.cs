﻿using System.Web;
using BoeAuctions;
using BoeAuctions.Objects;
using dotenv.net;
using dotenv.net.Utilities;
using MoreLinq.Extensions;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;

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

    await SaveInDatabase(context, auctions);
    await SendToTelegram(auctions);
}
catch (Exception e)
{
    Console.Error.WriteLine("Error: " + e);
}

static async Task SaveInDatabase(AuctionsContext context, IEnumerable<Auction> auctions)
{
    try {
        context.Auctions.AddRange(auctions);

        await context.SaveChangesAsync();
    } catch (Exception e) {
        throw new Exception("Database error", e);
    }
}

static async Task SendToTelegram(IEnumerable<Auction> auctions) {
    if (!EnvReader.HasValue("TELEGRAM_BOT_TOKEN")) {
        Console.WriteLine("### Telegram disabled (No token) ###");
        return;
    }
    if (!EnvReader.HasValue("TELEGRAM_CHAT_ID")) {
        Console.WriteLine("### Telegram disabled (No chat ID) ###");
        return;
    }

    const string AUCTION_URL = "https://subastas.boe.es/detalleSubasta.php?idSub=";

    try {
        var telegramToken = EnvReader.GetStringValue("TELEGRAM_BOT_TOKEN");
        var chatId = EnvReader.GetStringValue("TELEGRAM_CHAT_ID");
        
        var botClient = new TelegramBotClient(telegramToken);

        var messageBatches = auctions
            .OrderBy(a => a.EndDate)
            .Select(auction => {
                var auctionPart =
                        $"<b>Subasta {HttpUtility.HtmlEncode(AUCTION_URL + auction.Id)}</b>" +
                        $"\nFechas: {auction.StartDate:dd/MM/yyyy} - {auction.EndDate:dd/MM/yyyy}";
                
                var lotParts = new List<String>();

                foreach (var lot in auction.Lots) {
                    lotParts.Add(
                        $"\n\n<b>{lot.Type} en {HttpUtility.HtmlEncode(lot.Province) ?? "<i>Sin provincia</i>"}</b>" +
                        $"\n - Valor de la subasta: {lot.Value:N0}€" +
                        $"\n - Descripción: {(lot.Description == null ? "<i>Sin descripción</i>" : HttpUtility.HtmlEncode(TruncateDescription(lot.Description)))}"
                    );
                }
                
                return (auction, auctionPart, lotParts);
            })
            .SelectMany((data) => {
                // Chunk size, with a little extra for the "(1 of N)" part
                var chunkSize = 4050;
                
                var messages = new List<string>();

                messages.Add(data.auctionPart);

                for (int i=0; i<data.lotParts.Count; i++) {
                    var currentPart = data.lotParts[i];

                    if (messages.Last().Length + currentPart.Length > chunkSize) {
                        // To avoid infinite loops
                        if (data.auctionPart.Length + currentPart.Length > chunkSize) {
                            throw new Exception($"Lot description too long: {data.auctionPart}{currentPart}");
                        }

                        i--;
                        messages.Add(data.auctionPart);
                    } else {
                        messages[messages.Count - 1] += currentPart;
                    }
                }

                if (messages.Count > 1) {
                    for (int i=0; i<messages.Count; i++) {
                        messages[i] = $"<b><i>({i + 1} de {messages.Count})</i></b> {messages[i]}";
                    }
                }

                return messages.Select(messagePart => (data.auction, messagePart));
            })
            .Batch(20)
            .ToList();

        for (var i = 0; i < messageBatches.Count; i++) {
            foreach(var (auction, message) in messageBatches[i]) {
                try {
                    await botClient.SendTextMessageAsync(
                        chatId: chatId,
                        text: message,
                        parseMode: ParseMode.Html,
                        disableWebPagePreview: true
                    );

                    // Avoid reaching bot limits per second
                    await Task.Delay(250);
                } catch (Exception e) {
                    throw new Exception($"Telegram error in auction {auction.Id}, Message: {message}", e);
                }
            }

            if (i < messageBatches.Count - 1) {
                // Avoid reaching bot limits per minute
                await Task.Delay(60000);
            }
        }
    } catch (Exception e) {
        throw new Exception("Telegram error", e);
    }
}

static string TruncateDescription(string description) {
    const int LIMIT = 100;

    if (description.Length > LIMIT) {
        return string.Concat(description.AsSpan(0, LIMIT - 3), "...");
    }

    return description;
}