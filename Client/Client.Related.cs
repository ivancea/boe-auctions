using System.Web;
using HtmlAgilityPack;
using BoeAuctions.Model.Objects;

namespace BoeAuctions;

public partial class Client
{
    private static readonly IDictionary<string, Action<string, AuctionRelatedPerson>> _relatedPersonReaders = new Dictionary<string, Action<string, AuctionRelatedPerson>>() {
        { "Nombre", (data, related) => related.Name = data },
        { "NIF", (data, related) => related.Nif = data },
        { "Dirección", (data, related) => related.Address = data },
        { "Localidad", (data, related) => related.Locality = data },
        { "Provincia", (data, related) => related.Province = data },
        { "País", (data, related) => related.Country = data },
    };

    private async Task LoadRelatedPeopleAsync(string auctionId, Auction auction)
    {
        var html = await LoadHtml($"https://subastas.boe.es/detalleSubasta.php?ver=4&idSub={auctionId}");

        LoadCreditor(html, auction);
        LoadAdministrator(html, auction);

        if (auction.Creditor is null && auction.Administrator is null)
        {
            Console.WriteLine("No creditor nor administrator found in auction {0}", auction.Id);
            return;
        }
    }

    private static void LoadCreditor(HtmlDocument html, Auction auction)
    {
        var rows = html.DocumentNode.SelectNodes("//div[@id='idBloqueDatos4']//table//tr");

        if (rows is null)
        {
            return;
        }

        auction.Creditor = new AuctionRelatedPerson();

        foreach (var row in rows)
        {
            var header = HttpUtility.HtmlDecode(row.SelectSingleNode("th")?.InnerText.Trim());
            var data = HttpUtility.HtmlDecode(row.SelectSingleNode("td")?.InnerText.Trim());

            if (header == null || data == null)
            {
                Console.WriteLine("No headers found in auction {0}", auction.Id);
                continue;
            }

            if (_relatedPersonReaders.TryGetValue(header, out var reader))
            {
                reader(data, auction.Creditor);
            }
            else
            {
                Console.WriteLine("Unknown header '{0}' in auction {1}", header, auction.Id);
            }
        }
    }

    private static void LoadAdministrator(HtmlDocument html, Auction auction)
    {
        var rows = html.DocumentNode.SelectNodes("//div[@id='idBloqueDatos7']//table//tr");

        if (rows is null)
        {
            return;
        }

        auction.Administrator = new AuctionRelatedPerson();

        foreach (var row in rows)
        {
            var header = HttpUtility.HtmlDecode(row.SelectSingleNode("th")?.InnerText.Trim());
            var data = HttpUtility.HtmlDecode(row.SelectSingleNode("td")?.InnerText.Trim());

            if (header == null || data == null)
            {
                Console.WriteLine("No headers found in auction {0}", auction.Id);
                continue;
            }

            if (_relatedPersonReaders.TryGetValue(header, out var reader))
            {
                reader(data, auction.Administrator);
            }
            else
            {
                Console.WriteLine("Unknown header '{0}' in auction {1}", header, auction.Id);
            }
        }
    }
}
