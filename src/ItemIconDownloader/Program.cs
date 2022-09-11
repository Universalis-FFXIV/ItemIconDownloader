using System.Net.Http.Headers;
using System.Text.Json;
using CommandLine;
using HtmlAgilityPack;
using Lumina;
using Lumina.Data;
using Lumina.Excel.GeneratedSheets;

namespace ItemIconDownloader;

public class Program
{
    [Verb("all", HelpText = "Export every item icon from the Lodestone DB.")]
    public class ExportAllOptions
    {
        [Option('s', "sqpack", Required = true, HelpText = "The path to your sqpack folder.")]
        public string? SqPack { get; set; }

        [Option('o', "output", Required = true, HelpText = "The path to the output directory.")]
        public string? Output { get; set; }
    }

    [Verb("marketable", HelpText = "Export marketable item icons from the Lodestone DB.")]
    public class ExportMarketableOptions
    {
        [Option('s', "sqpack", Required = true, HelpText = "The path to your sqpack folder.")]
        public string? SqPack { get; set; }

        [Option('o', "output", Required = true, HelpText = "The path to the output directory.")]
        public string? Output { get; set; }
    }

    public static async Task Main(string[] args)
    {
        await Parser.Default.ParseArguments<ExportAllOptions, ExportMarketableOptions>(args)
            .MapResult(
                (ExportAllOptions opts) => ExportAllIcons(opts),
                (ExportMarketableOptions opts) => Task.FromResult(0),
                _ => Task.FromResult(1));
    }

    private static async Task<int> ExportAllIcons(ExportAllOptions opts)
    {
        if (opts.SqPack == null) throw new ArgumentNullException(nameof(opts));
        if (opts.Output == null) throw new ArgumentNullException(nameof(opts));

        if (!Directory.Exists(opts.Output))
        {
            Directory.CreateDirectory(opts.Output);
        }

        var lumina = new GameData(opts.SqPack,
            new LuminaOptions { DefaultExcelLanguage = Language.Japanese, PanicOnSheetChecksumMismatch = false });
        if (lumina == null) throw new InvalidOperationException("Failed to initialize Lumina.");

        var items = new Dictionary<string, int>();

        {
            var itemSheet = lumina.GetExcelSheet<Item>();
            if (itemSheet == null) throw new InvalidOperationException("Failed to fetch item sheet.");

            foreach (var item in itemSheet.Where(e => !string.IsNullOrWhiteSpace(e.Name)))
            {
                if (!items.TryAdd(item.Name, Convert.ToInt32(item.RowId)))
                {
                    Console.WriteLine($"Got duplicate item name \"{item.Name}\", skipping...");
                }
            }
        }

        await ExportIcons(items, opts.Output);

        return 0;
    }

    private static async Task ExportIcons(IReadOnlyDictionary<string, int> items, string outputPath)
    {
        var itemUrls = new Dictionary<int, string>();

        var pages = await GetPageCount();
        var requests = Enumerable.Range(1, pages)
            .Select(async pageNumber =>
            {
                // Fetch the next search page, retrying until the operation succeeds.
                var searchPage = await Retry.Do(() => Get(GetSearchUrl(pageNumber)), TimeSpan.FromSeconds(20), 100);

                // Get the items table in the search results.
                var tableNode = searchPage.DocumentNode.SelectSingleNode(
                    "/html/body/div[3]/div[2]/div[1]/div[1]/div[2]/div[2]/div[5]/div/table/tbody");
                if (tableNode == null)
                {
                    throw new InvalidOperationException(
                        $"Failed to find table node.\nURL: {GetSearchUrl(pageNumber)}\nDocument:\n{searchPage.Text}");
                }

                var tableEntries = tableNode.SelectNodes("tr");

                Console.WriteLine($"=> Page {pageNumber}");

                // Process table rows.
                foreach (var tableEntry in tableEntries)
                {
                    var itemRow = tableEntry.ChildNodes[1];
                    var itemDivs = itemRow.ChildNodes.Where(x => x.Name == "div");
                    var item1 = itemDivs.ElementAt(1);
                    var item2 = item1.ChildNodes[3];
                    var itemUrl = item2.GetAttributeValue("href", string.Empty);
                    var itemName = item2.InnerHtml;

                    Console.WriteLine($"    => {itemName}: {itemUrl}");
                    if (items.TryGetValue(itemName, out var key))
                    {
                        Console.WriteLine($"         => IN SET");

                        lock (itemUrls)
                        {
                            itemUrls.Add(key, itemUrl);
                        }
                    }
                    else
                    {
                        Console.WriteLine($"         => NOT IN SET");
                    }
                }
            });

        await Task.WhenAll(requests);
        await File.WriteAllTextAsync(Path.Combine(outputPath, "dbMapping.json"), JsonSerializer.Serialize(itemUrls));
    }

    private static async Task<int> GetPageCount()
    {
        // Get the first page of results
        var url = GetSearchUrl(1);
        var doc = await Get(url);
        var node = doc.DocumentNode.SelectSingleNode(
            "/html/body/div[3]/div[2]/div[1]/div/div[2]/div[2]/div[6]/div/div/ul/li[9]/a");

        // Extract the page number from the response
        var sendTo = node.GetAttributeValue("href", "1");
        var pageNum = sendTo[(sendTo.IndexOf('=') + 1)..][..3];
        return int.Parse(pageNum);
    }

    private static async Task<HtmlDocument> Get(Uri uri)
    {
        using var http = new HttpClient();
        using var req = new HttpRequestMessage(HttpMethod.Get, uri);

        {
            // Add user agent headers
            // Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/79.0.3945.117 Safari/537.36
            req.Headers.UserAgent.Add(new ProductInfoHeaderValue("Mozilla", "5.0"));
            req.Headers.UserAgent.Add(new ProductInfoHeaderValue("(Windows NT 10.0; Win64; x64)"));

            req.Headers.UserAgent.Add(new ProductInfoHeaderValue("AppleWebKit", "537.36"));
            req.Headers.UserAgent.Add(new ProductInfoHeaderValue("(KHTML, like Gecko)"));

            req.Headers.UserAgent.Add(new ProductInfoHeaderValue("Chrome", "79.0.3945.117"));

            req.Headers.UserAgent.Add(new ProductInfoHeaderValue("Safari", "537.36"));
        }

        // Send the request, throwing if an error response is returned.
        using var res = await http.SendAsync(req);
        res.EnsureSuccessStatusCode();

        await using var s = await res.Content.ReadAsStreamAsync();
        var html = new HtmlDocument();
        html.Load(s);
        return html;
    }

    private static Uri GetSearchUrl(int page)
    {
        return new Uri($"https://jp.finalfantasyxiv.com/lodestone/playguide/db/item/?page={page}");
    }
}