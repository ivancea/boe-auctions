using System.Web;
using HtmlAgilityPack;
using Microsoft.AspNetCore.WebUtilities;
using BoeAuctions.Objects;
using System.Globalization;

namespace BoeAuctions;

public class Client : IDisposable
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
    public async IAsyncEnumerable<(AuctionStatus, string)> ListAsync()
    {
        foreach (var auctionStatus in Enum.GetValues<AuctionStatus>())
        {
            Console.WriteLine("Obtaining auctions with status {0}", auctionStatus);
            var query = new Dictionary<string, string>() {
                { "page_hits", $"{ELEMENTS_PER_PAGE}" },
                { "campo[0]", "SUBASTA.ESTADO" },
                { "dato[0]", auctionStatus.GetId() },
                { "sort_field[0]", "SUBASTA.FECHA_FIN_YMD" },
                { "sort_order[0]", "desc" },
                { "sort_field[1]", "SUBASTA.FECHA_FIN_YMD" },
                { "sort_order[1]", "asc" },
                { "sort_field[2]", "SUBASTA.HORA_FIN" },
                { "sort_order[2]", "asc" },
                { "accion", "Buscar" }
            };
            var uri = new Uri(QueryHelpers.AddQueryString("https://subastas.boe.es/reg/subastas_ava.php", query));
            var html = await LoadHtml(uri.ToString());

            var idUrl = html.DocumentNode.SelectSingleNode("//a[contains(@class, 'current')]")?.GetAttributeValue("href", null);

            if (idUrl == null)
            {
                throw new Exception("No search ID found");
            }

            var idQuery = HttpUtility.HtmlDecode(idUrl).Substring(idUrl.IndexOf('?') + 1);
            var idWithElements = QueryHelpers.ParseQuery(idQuery)["id_busqueda"].First();
            var id = idWithElements.Substring(0, idWithElements.IndexOf('-'));

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
                    .Select(id => (auctionStatus, id))
                    .ToList();

                if (items.Count == 0)
                {
                    break;
                }

                foreach (var item in items)
                {
                    await Task.Yield();
                    yield return item;
                }

                Console.WriteLine("+{0} items of type {1}", items.Count, auctionStatus);

                html = await LoadHtml($"https://subastas.boe.es/reg/subastas_ava.php?accion=Mas&id_busqueda={id}-{elementCount}-{ELEMENTS_PER_PAGE}");
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
        { "Fecha de inicio", (data, auction, auctionLot) => auction.StartDate = ParseDate(data) },
        { "Fecha de conclusión", (data, auction, auctionLot) => auction.EndDate = ParseDate(data) },
        { "Cantidad reclamada", (data, auction, auctionLot) => auction.ClaimedAmount = ParseEuros(data) },
        { "Forma adjudicación", (data, auction, auctionLot) => auction.AwardProcedure = data },
        { "Anuncio BOE", (data, auction, auctionLot) => auction.BoeAnnouncementId = data },
        { "Valor subasta", (data, auction, auctionLot) => auctionLot.Value = ParseEuros(data) },
        { "Tasación", (data, auction, auctionLot) => auctionLot.Valuation = ParseEuros(data) },
        { "Puja mínima", (data, auction, auctionLot) => auctionLot.MinimumBid = data == "Sin puja mínima" ? null : ParseEuros(data) },
        { "Tramos entre pujas", (data, auction, auctionLot) => auctionLot.BidIncrement = ParseEuros(data) },
        { "Importe del depósito", (data, auction, auctionLot) => auctionLot.Deposit = ParseEuros(data) },
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

    private async Task LoadLotsAsync(string auctionId, Auction auction)
    {
        var html = await LoadHtml($"https://subastas.boe.es/detalleSubasta.php?ver=3&idSub={auctionId}");

        var tabs = html.DocumentNode.SelectNodes("//div[@id='tabsver']//ul//li//a");

        if (tabs is not null)
        {
            var tabIds = tabs
                .Where(tab => tab.Id.StartsWith("idTabLote"))
                .Select(tab => tab.Id.Substring("idTabLote".Length))
                .Select(int.Parse)
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

    private static void LoadLot(HtmlDocument html, Auction auction, AuctionLot lot)
    {
        var goodTypeElement = html.DocumentNode.SelectSingleNode("//div[@id='idBloqueDatos3']//h4");

        var goodType = HttpUtility.HtmlDecode(goodTypeElement?.InnerText.Trim())?.Split('-').Last().Trim();
        
        var summaryElement = html.DocumentNode.SelectSingleNode("//div[@id='idBloqueDatos3']//div[contains(@class, 'caja')]");

        var summary = HttpUtility.HtmlDecode(summaryElement?.InnerText.Trim());

        Console.WriteLine("Good type in {0}/{1}: {2}", auction.Id, lot.Id, goodType);
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

    private static DateTime? ParseDate(string data)
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

    private async Task<HtmlDocument> LoadHtml(string url)
    {
        for (int i=3; i>0; i--) {
            try {
                var response = await _httpClient.GetAsync(url);
                var body = await response.Content.ReadAsStringAsync();
                var document = new HtmlDocument();

                document.LoadHtml(body);

                return document;
            } catch (HttpRequestException exc) {
                Console.WriteLine("Error loading {0}: {1}", url, exc.Message);
            }
        }

        throw new Exception("Could not load " + url);
    }
}
