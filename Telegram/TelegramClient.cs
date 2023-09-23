using System.Web;
using BoeAuctions.Model.Objects;
using dotenv.net.Utilities;
using MoreLinq.Extensions;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Exceptions;

namespace BoeAuctions.Telegram;

class TelegramClient
{
    // 20 messages per minute (A bit less, just in case)
    private static readonly TimeSpan TIME_BETWEEN_MESSAGES = TimeSpan.FromSeconds(60d / 18d);

    private const string AUCTION_URL = "https://subastas.boe.es/detalleSubasta.php?idSub=";

    private TelegramBotClient? botClient;

    private string? chatId;

    private DateTime lastMessageTime = DateTime.MinValue;

    public TelegramClient()
    {
        if (!EnvReader.HasValue("TELEGRAM_BOT_TOKEN"))
        {
            Console.WriteLine("### Telegram disabled (No token) ###");
            return;
        }
        if (!EnvReader.HasValue("TELEGRAM_CHAT_ID"))
        {
            Console.WriteLine("### Telegram disabled (No chat ID) ###");
            return;
        }

        var telegramToken = EnvReader.GetStringValue("TELEGRAM_BOT_TOKEN");
        chatId = EnvReader.GetStringValue("TELEGRAM_CHAT_ID");

        botClient = new TelegramBotClient(telegramToken);
    }

    public async Task SendAuctionsToTelegram(IEnumerable<Auction> auctions)
    {
        if (botClient == null || chatId == null)
        {
            return;
        }

        try
        {
            var messageChunks = auctions
                .OrderBy(a => a.EndDate)
                .Select(MakeMessages)
                .SelectMany(MakeChunks)
                .ToList();

            foreach (var (auction, message) in messageChunks)
            {
                // Wait to avoid reaching Telegram limits
                var delay = lastMessageTime + TIME_BETWEEN_MESSAGES - DateTime.Now;

                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay);
                }

                try
                {
                    await botClient.SendTextMessageAsync(
                        chatId: chatId,
                        text: message,
                        parseMode: ParseMode.Html,
                        disableWebPagePreview: true
                    );
                }
                catch (ApiRequestException e)
                {
                    throw new Exception($"Telegram error in auction {auction.Id}, Message: {message}", e);
                }
                finally
                {
                    lastMessageTime = DateTime.Now;
                }
            }
        }
        catch (Exception e)
        {
            throw new Exception("Telegram error", e);
        }
    }

    private struct AuctionMessages
    {
        public Auction Auction { get; set; }

        public string AuctionPart { get; set; }

        public List<string> LotParts { get; set; }
    }

    private AuctionMessages MakeMessages(Auction auction)
    {
        var auctionPart =
            $"<b>Subasta {HttpUtility.HtmlEncode(AUCTION_URL + auction.Id)}</b>" +
            $"\nFechas: {auction.StartDate:dd/MM/yyyy} - {auction.EndDate:dd/MM/yyyy}";

        var lotParts = new List<string>();

        foreach (var lot in auction.Lots)
        {
            lotParts.Add(
                $"\n\n<b>{lot.Type} en {HttpUtility.HtmlEncode(lot.Province) ?? "<i>Sin provincia</i>"}</b>" +
                $"\n - Valor de la subasta: {lot.Value:N0}€" +
                $"\n - Descripción: {(lot.Description == null ? "<i>Sin descripción</i>" : HttpUtility.HtmlEncode(TruncateDescription(lot.Description)))}"
            );
        }

        return new AuctionMessages()
        {
            Auction = auction,
            AuctionPart = auctionPart,
            LotParts = lotParts
        };
    }

    private IEnumerable<(Auction, string chunk)> MakeChunks(AuctionMessages data)
    {
        // Chunk size, with a little extra for the "(1 of N)" part
        var chunkSize = 4050;

        var messages = new List<string>();

        messages.Add(data.AuctionPart);

        for (int i = 0; i < data.LotParts.Count; i++)
        {
            var currentPart = data.LotParts[i];

            if (messages.Last().Length + currentPart.Length > chunkSize)
            {
                // To avoid infinite loops
                if (data.AuctionPart.Length + currentPart.Length > chunkSize)
                {
                    throw new Exception($"Lot description too long: {data.AuctionPart}{currentPart}");
                }

                i--;
                messages.Add(data.AuctionPart);
            }
            else
            {
                messages[messages.Count - 1] += currentPart;
            }
        }

        if (messages.Count > 1)
        {
            for (int i = 0; i < messages.Count; i++)
            {
                messages[i] = $"<b><i>({i + 1} de {messages.Count})</i></b> {messages[i]}";
            }
        }

        return messages.Select(messagePart => (data.Auction, messagePart));
    }

    private static string TruncateDescription(string description)
    {
        const int LIMIT = 100;

        if (description.Length > LIMIT)
        {
            return string.Concat(description.AsSpan(0, LIMIT - 3), "...");
        }

        return description;
    }
}