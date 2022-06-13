using System.Web;
using HtmlAgilityPack;
using BoeAuctions.Objects;

namespace BoeAuctions;

public partial class Client
{
    private static readonly IDictionary<string, Tab> _tabs = new Dictionary<string, Tab>()
    {
        { "Información general", Tab.GeneralInformation },
        { "Autoridad gestora", Tab.Authority },
        { "Bienes", Tab.Goods },
        { "Lotes", Tab.Goods },
        { "Relacionados", Tab.Related },
        { "Pujas", Tab.Bids },
    };

    private static readonly IDictionary<string, Action<string, Auction, AuctionLot>> _generalInformationReaders = new Dictionary<string, Action<string, Auction, AuctionLot>>() {
        { "Identificador", (data, auction, auctionLot) => {} },
        { "Tipo de subasta", (data, auction, auctionLot) => auction.Type = data },
        { "Fecha de inicio", (data, auction, auctionLot) => auction.StartDate = ParseDateTime(data) },
        { "Fecha de conclusión", (data, auction, auctionLot) => auction.EndDate = ParseDateTime(data) },
        { "Forma adjudicación", (data, auction, auctionLot) => auction.AwardProcedure = data },
        { "Anuncio BOE", (data, auction, auctionLot) => auction.BoeAnnouncementId = data },
        { "Cantidad reclamada", (data, auction, auctionLot) => auctionLot.ClaimedAmount = ParseEuros(data) },
        { "Valor subasta", (data, auction, auctionLot) => auctionLot.Value = ParseEuros(data) },
        { "Tasación", (data, auction, auctionLot) => auctionLot.Valuation = ParseEuros(data) },
        { "Puja mínima", (data, auction, auctionLot) => auctionLot.MinimumBid = data == "Sin puja mínima" ? null : ParseEuros(data) },
        { "Tramos entre pujas", (data, auction, auctionLot) => auctionLot.BidIncrement = ParseEuros(data) },
        { "Importe del depósito", (data, auction, auctionLot) => auctionLot.DepositAmount = ParseEuros(data) },
    };

    private async Task<ISet<Tab>> LoadGeneralInformationAsync(string auctionId, Auction auction)
    {
        var html = await LoadHtml($"https://subastas.boe.es/detalleSubasta.php?ver=1&idSub={auctionId}");

        var tabLinks = html.DocumentNode.SelectNodes("//div[@id='tabs']/ul/li/a");

        var tabs = new HashSet<Tab>();

        foreach (var tabLink in tabLinks)
        {
            var tabText = HttpUtility.HtmlDecode(tabLink.InnerText);

            if (_tabs.TryGetValue(tabText, out var tab))
            {
                tabs.Add(tab);
            }
            else
            {
                Console.WriteLine($"Unknown tab: {tabText}");
            }
        }

        var rows = html.DocumentNode.SelectNodes("//div[@id='idBloqueDatos1']//table//tr");

        var auctionLot = new AuctionLot()
        {
            AuctionId = auction.Id,
            Auction = auction,
            Id = 1
        };

        var multipleLots = false;

        foreach (var row in rows)
        {
            var header = HttpUtility.HtmlDecode(row.SelectSingleNode("th")?.InnerText.Trim());
            var data = HttpUtility.HtmlDecode(row.SelectSingleNode("td")?.InnerText.Trim());

            if (header == null || data == null)
            {
                Console.WriteLine("No headers found in auction {0}", auction.Id);
                continue;
            }

            if (header == "Lotes")
            {
                multipleLots = data != "Sin lotes";
            }
            else if (_generalInformationReaders.TryGetValue(header, out var reader))
            {
                reader(data, auction, auctionLot);
            }
            else
            {
                Console.WriteLine("Unknown header '{0}' in auction {1}", header, auction.Id);
            }
        }

        if (!multipleLots)
        {
            auction.Lots.Add(auctionLot);
        }

        return tabs;
    }
}
