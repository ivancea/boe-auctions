using System.Web;
using HtmlAgilityPack;
using BoeAuctions.Objects;

namespace BoeAuctions;

public partial class Client
{
    private static readonly IDictionary<string, Action<string, AuctionAuthority>> _auctionAuthorityReaders = new Dictionary<string, Action<string, AuctionAuthority>>() {
        { "Código", (data, authority) => authority.Code = data },
        { "Descripción", (data, authority) => authority.Description = data },
        { "Dirección", (data, authority) => authority.Address = data },
        { "Teléfono", (data, authority) => authority.Phone = data },
        { "Fax", (data, authority) => authority.Fax = data },
        { "Correo electrónico", (data, authority) => authority.Email = data },
    };

    private async Task LoadAuthorityAsync(string auctionId, Auction auction)
    {
        var html = await LoadHtml($"https://subastas.boe.es/detalleSubasta.php?ver=2&idSub={auctionId}");
        var rows = html.DocumentNode.SelectNodes("//div[@id='idBloqueDatos2']//table//tr");

        auction.Authority = new AuctionAuthority();

        foreach (var row in rows)
        {
            var header = HttpUtility.HtmlDecode(row.SelectSingleNode("th")?.InnerText.Trim());
            var data = HttpUtility.HtmlDecode(row.SelectSingleNode("td")?.InnerText.Trim());

            if (header == null || data == null)
            {
                Console.WriteLine("No headers found in auction {0}", auction.Id);
                continue;
            }

            if (_auctionAuthorityReaders.TryGetValue(header, out var reader))
            {
                reader(data, auction.Authority);
            }
            else
            {
                Console.WriteLine("Unknown header '{0}' in auction {1}", header, auction.Id);
            }
        }
    }
}
