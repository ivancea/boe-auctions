using System.Web;
using HtmlAgilityPack;
using Microsoft.AspNetCore.WebUtilities;
using BoeAuctions.Model.Objects;
using System.Globalization;

namespace BoeAuctions;

public partial class Client : IDisposable
{
    private enum Tab
    {
        GeneralInformation,
        Authority,
        Goods,
        Related,
        Bids
    }

    private const int ELEMENTS_PER_PAGE = 500;

    private readonly HttpClient _httpClient = new();

    public void Dispose()
    {
        GC.SuppressFinalize(this);

        _httpClient.Dispose();
    }

    /// <summary>
    /// Fetches the links of the items
    /// </summary>
    /// <returns>The item references, to be used in further calls</returns>
    public async IAsyncEnumerable<(AuctionStatus, string)> ListAsync(IEnumerable<AuctionStatus>? statuses = null)
    {
        foreach (var auctionStatus in statuses ?? new[] { AuctionStatus.Active })
        {
            Console.WriteLine("Obtaining auctions with status {0}", auctionStatus);
            var postData = new Dictionary<string, string>() {
                { "page_hits", $"{ELEMENTS_PER_PAGE}" },
                { "campo[0]", "SUBASTA.ESTADO.CODIGO" },
                { "dato[0]", auctionStatus.GetId() },
                { "sort_field[0]", "SUBASTA.FECHA_FIN" },
                { "sort_order[0]", "desc" },
                { "accion", "Buscar" }
            };
            var html = await LoadHtml("https://subastas.boe.es/subastas_ava.php", postData);

            var idUrl = html.DocumentNode.SelectSingleNode("//a[contains(@class, 'current')]")?.GetAttributeValue("href", null);

            if (idUrl == null)
            {
                throw new Exception("No search ID found");
            }

            var idQuery = HttpUtility.HtmlDecode(idUrl).Substring(idUrl.IndexOf('?') + 1);
            var idWithElements = QueryHelpers.ParseQuery(idQuery)["id_busqueda"].First();

            if (idWithElements == null)
            {
                throw new Exception("No search ID found");
            }

            var searchId = idWithElements.Substring(0, idWithElements.IndexOf('-'));

            var elementCount = ELEMENTS_PER_PAGE;

            while (elementCount < 200_000 /* Arbitrary limit */)
            {
                var nodes = html.DocumentNode.SelectNodes("//div[contains(@class, 'listadoResult')]//a[contains(@class, 'resultado-busqueda-link-defecto')]");

                if (nodes == null)
                {
                    break;
                }

                var items = nodes
                    .Select(link => link.GetAttributeValue("href", null))
                    .Where(link => link != null)
                    .Select(HttpUtility.HtmlDecode)
                    .Select(link => link!)
                    .Select(link => link.Substring(link.IndexOf('?') + 1))
                    .Select(query => QueryHelpers.ParseQuery(query)["idSub"].First())
                    .Where(id => id != null)
                    .Select(id => (auctionStatus, id!))
                    .ToList();

                if (items.Count == 0)
                {
                    break;
                }

                foreach (var item in items)
                {
                    yield return item;
                }

                Console.WriteLine("+{0} items of type {1}", items.Count, auctionStatus);

                html = await LoadHtml($"https://subastas.boe.es/subastas_ava.php?accion=Mas&id_busqueda={searchId}-{elementCount}-{ELEMENTS_PER_PAGE}");
                elementCount += ELEMENTS_PER_PAGE;
            }
        }
    }

    public async Task<Auction> GetAuctionAsync(AuctionStatus auctionStatus, string auctionId)
    {
        var auction = new Auction()
        {
            Id = auctionId,
            Status = auctionStatus
        };

        var tabs = await LoadGeneralInformationAsync(auctionId, auction);

        var tasks = new List<Task>();

        if (tabs.Contains(Tab.Authority))
        {
            tasks.Add(LoadAuthorityAsync(auctionId, auction));
        }
        if (tabs.Contains(Tab.Related))
        {
            tasks.Add(LoadRelatedPeopleAsync(auctionId, auction));
        }
        if (tabs.Contains(Tab.Goods))
        {
            tasks.Add(LoadLotsAsync(auctionId, auction));
        }
        if (tabs.Contains(Tab.Bids))
        {
            // tasks.Add(LoadBidsAsync(auctionId, auction));
        }

        await Task.WhenAll(tasks);

        var html = await LoadHtml($"https://subastas.boe.es/detalleSubasta.php?ver=5&idSub={auctionId}");
        var noBidsBlock = html.DocumentNode.SelectSingleNode("//div[@id='idBloqueDatos8']");

        if (noBidsBlock is null)
        {
            Console.WriteLine("Bids found in auction {0}", auctionId);
        }

        return auction;
    }

    private static decimal? ParseEuros(string data)
    {
        if (data.StartsWith("Ver ") || data == "No consta" || data == "Sin tramos")
        {
            return null;
        }

        if (!decimal.TryParse(data, NumberStyles.Currency, new CultureInfo("es-ES"), out var result))
        {
            Console.WriteLine("Could not parse '{0}' as euros", data);
            return null;
        }

        return result;
    }

    private static readonly ISet<string> _trueValues = new HashSet<string>() {
        "Sí",
    };

    private static readonly ISet<string> _falseValues = new HashSet<string>() {
        "No", "No consta"
    };

    private static bool? ParseBool(string data)
    {
        if (_trueValues.Contains(data))
        {
            return true;
        }
        if (_falseValues.Contains(data))
        {
            return false;
        }

        Console.WriteLine("Could not parse '{0}' as bool", data);
        return null;
    }

    private static DateTime? ParseDateTime(string data)
    {
        var isoLabelIndex = data.IndexOf("ISO:");

        if (isoLabelIndex < 0)
        {
            Console.WriteLine("Could not parse '{0}' as date", data);
            return null;
        }

        var isoDate = data.Substring(isoLabelIndex + 4, data.Length - isoLabelIndex - 4 - 1);

        if (!DateTime.TryParse(isoDate, out var result))
        {
            Console.WriteLine("Could not parse '{0}' as date", isoDate);
            return null;
        }

        return result.ToUniversalTime();
    }

    private static DateOnly? ParseDate(string data)
    {
        if (!DateOnly.TryParse(data, out var result))
        {
            Console.WriteLine("Could not parse '{0}' as date", data);
            return null;
        }

        return result;
    }

    private async Task<HtmlDocument> LoadHtml(string url, Dictionary<string, string>? postData = null)
    {
        var request = new HttpRequestMessage()
        {
            RequestUri = new Uri(url),
            Method = postData == null ? HttpMethod.Get : HttpMethod.Post
        };

        if (postData != null)
        {
            request.Content = new FormUrlEncodedContent(postData);
        }

        for (int i = 3; i > 0; i--)
        {
            try
            {
                var response = await _httpClient.SendAsync(request);
                var body = await response.Content.ReadAsStringAsync();
                var document = new HtmlDocument();

                document.LoadHtml(body);

                return document;
            }
            catch (HttpRequestException exc)
            {
                Console.WriteLine("Error loading {0}: {1}", url, exc.Message);
            }
        }

        throw new Exception("Could not load " + url);
    }
}
