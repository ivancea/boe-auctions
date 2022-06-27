using System.Web;
using HtmlAgilityPack;
using BoeAuctions.Objects;

namespace BoeAuctions;

public partial class Client
{
    private async Task LoadLotsAsync(string auctionId, Auction auction)
    {
        var html = await LoadHtml($"https://subastas.boe.es/detalleSubasta.php?ver=3&idSub={auctionId}");

        var tabs = html.DocumentNode.SelectNodes("//div[@id='tabsver']//ul//li//a");

        if (tabs is not null)
        {
            var tabIds = tabs
                .Where(tab => tab.Id.StartsWith("idTabLote"))
                .Select(tab => tab.Id.Substring("idTabLote".Length).Trim())
                .ToList();

            var lot = new AuctionLot() {
                AuctionId = auction.Id,
                Auction = auction,
                Id = tabIds[0]
            };
            auction.Lots.Add(lot);

            LoadLot(html, auction, lot);

            foreach (var tabId in tabIds.Skip(1))
            {
                html = await LoadHtml($"https://subastas.boe.es/detalleSubasta.php?ver=3&idSub={auctionId}&idLote={tabId}");

                lot = new AuctionLot() {
                    AuctionId = auction.Id,
                    Auction = auction,
                    Id = tabId
                };
                auction.Lots.Add(lot);

                LoadLot(html, auction, lot);
            }
        } else {
            LoadLot(html, auction, auction.Lots.First());
            // TODO: Fill existing lot
        }
    }

    private static readonly IDictionary<string, Action<string, AuctionLot>> _lotReaders = new Dictionary<string, Action<string, AuctionLot>>() {
        // First block
        { "Cantidad reclamada", (data, auctionLot) => auctionLot.ClaimedAmount = ParseEuros(data) },
        { "Valor Subasta", (data, auctionLot) => auctionLot.Value = ParseEuros(data) },
        { "Valor de tasación", (data, auctionLot) => auctionLot.Valuation = ParseEuros(data) },
        { "Importe del depósito", (data, auctionLot) => auctionLot.DepositAmount = ParseEuros(data) },
        { "Puja mínima", (data, auctionLot) => auctionLot.MinimumBid = data == "Sin puja mínima" ? null : ParseEuros(data) },
        { "Tramos entre pujas", (data, auctionLot) => auctionLot.BidIncrement = ParseEuros(data) },

        // Property
        { "Descripción", (data, auctionLot) => auctionLot.Description = data },
        { "IDUFIR", (data, auctionLot) => auctionLot.Idufir = data },
        { "Referencia catastral", (data, auctionLot) => auctionLot.RegisterReference = data },
        { "Dirección", (data, auctionLot) => auctionLot.Address = data },
        { "Código Postal", (data, auctionLot) => auctionLot.ZipCode = data },
        { "Localidad", (data, auctionLot) => auctionLot.Locality = data },
        { "Provincia", (data, auctionLot) => auctionLot.Province = data },
        { "Superficie", (data, auctionLot) => auctionLot.Area = ParseEuros(data) },
        { "Cuota", (data, auctionLot) => auctionLot.Quota = ParseEuros(data) },
        { "Parcela", (data, auctionLot) => auctionLot.Parcel = data },
        { "Nombre paraje", (data, auctionLot) => auctionLot.PlaceName = data },
        { "Referencia Registral", (data, auctionLot) => auctionLot.RegistryReference = data },
        { "Vivienda habitual", (data, auctionLot) => auctionLot.HabitualResidence = ParseBool(data) },
        { "Situación posesoria", (data, auctionLot) => auctionLot.PossessoryStatus = data },
        { "Visitable", (data, auctionLot) => auctionLot.Visitable = data },
        { "Cargas", (data, auctionLot) => auctionLot.Burdens = data },
        { "Inscripción registral", (data, auctionLot) => auctionLot.RegistryInscription = data },
        { "Título jurídico", (data, auctionLot) => auctionLot.JuridicTitle = data },
        { "Información adicional", (data, auctionLot) => auctionLot.AdditionalInformation = data },

        // Vehicles
        { "Matrícula", (data, auctionLot) => auctionLot.RegistrationPlate = data },
        { "Marca", (data, auctionLot) => auctionLot.Brand = data },
        { "Modelo", (data, auctionLot) => auctionLot.Model = data },
        { "Número de bastidor", (data, auctionLot) => auctionLot.FrameNumber = data },
        { "Fecha de matriculación", (data, auctionLot) => auctionLot.RegistrationDate = ParseDate(data) },
        { "Fecha de adquisición", (data, auctionLot) => auctionLot.AcquisitionDate = ParseDate(data) },
        { "Depósito", (data, auctionLot) => auctionLot.Deposit = data },
    };

    private static void LoadLot(HtmlDocument html, Auction auction, AuctionLot lot)
    {
        var goodTypeElement = html.DocumentNode.SelectSingleNode("//div[@id='idBloqueDatos3']//h4");

        var goodType = HttpUtility.HtmlDecode(goodTypeElement?.InnerText.Trim())?.Split('-').Last().Trim();
        
        lot.Type = goodType;

        var summaryElement = html.DocumentNode.SelectSingleNode("//div[@id='idBloqueDatos3']//div[@class='caja']");

        if (summaryElement is not null) {
            var summary = HttpUtility.HtmlDecode(summaryElement?.InnerText.Trim());

            lot.Summary = summary;
        }

        var rows = html.DocumentNode.SelectNodes("//div[@id='idBloqueDatos3']//table//tr");

        foreach (var row in rows)
        {
            var header = HttpUtility.HtmlDecode(row.SelectSingleNode("th")?.InnerText.Trim());
            var data = HttpUtility.HtmlDecode(row.SelectSingleNode("td")?.InnerText.Trim());

            if (header == null || data == null)
            {
                Console.WriteLine("No headers found in auction {0}", auction.Id);
                continue;
            }

            if (_lotReaders.TryGetValue(header, out var reader))
            {
                reader(data, lot);
            }
            else
            {
                Console.WriteLine("Unknown header '{0}' in auction {1}", header, auction.Id);
            }
        }
    }
}
