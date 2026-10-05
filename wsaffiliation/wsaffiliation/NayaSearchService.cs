using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Hosting;

namespace wsaffiliation
{
    public class NayaSearchService : BackgroundService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;

        private readonly object _lock = new();

        private List<SearchProduct> _products = new();

        private DateTime _lastLoadedUtc = DateTime.MinValue;

        private readonly TimeSpan _cacheDuration =
            TimeSpan.FromHours(6);


        // ============================================================
        // CONSTRUCTOR
        // ============================================================

        public NayaSearchService(
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration)
        {
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
        }


        // ============================================================
        // BACKGROUND SERVICE
        // ============================================================

        protected override async Task ExecuteAsync(
            CancellationToken stoppingToken)
        {
            Console.WriteLine(
                "NAYA SEARCH: Service starting...");

            // Premier chargement
            try
            {
                await LoadAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"NAYA SEARCH: Initial load error: {ex.Message}");
            }


            // Refresh automatique
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(
                        _cacheDuration,
                        stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }


                if (stoppingToken.IsCancellationRequested)
                    break;


                try
                {
                    Console.WriteLine(
                        "NAYA SEARCH: Refreshing cache...");

                    await LoadAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    Console.WriteLine(
                        $"NAYA SEARCH: Refresh error: {ex.Message}");
                }
            }


            Console.WriteLine(
                "NAYA SEARCH: Service stopped.");
        }


        // ============================================================
        // LOAD / CACHE
        // ============================================================

        public async Task LoadAsync(
            CancellationToken cancellationToken = default)
        {
            Console.WriteLine(
                "NAYA SEARCH: Loading products from Supabase...");


            var products =
                await LoadFromSupabaseAsync(
                    cancellationToken);


            // IMPORTANT :
            // Ne jamais remplacer un cache valide par une liste vide.
            if (products.Count == 0)
            {
                Console.WriteLine(
                    "NAYA SEARCH: Supabase returned 0 products. " +
                    "Existing cache kept.");

                return;
            }


            lock (_lock)
            {
                _products = products;

                _lastLoadedUtc =
                    DateTime.UtcNow;
            }


            Console.WriteLine(
                $"NAYA SEARCH: {_products.Count} products loaded into cache.");
        }


        // ============================================================
        // CACHE INFORMATION
        // ============================================================

        public int Count
        {
            get
            {
                lock (_lock)
                {
                    return _products.Count;
                }
            }
        }


        public DateTime LastLoadedUtc
        {
            get
            {
                lock (_lock)
                {
                    return _lastLoadedUtc;
                }
            }
        }


        public bool IsLoaded
        {
            get
            {
                lock (_lock)
                {
                    return _products.Count > 0;
                }
            }
        }


        // ============================================================
        // AUTOCOMPLETE SEARCH
        // ============================================================

        public List<SearchProduct> Search(
            string? query,
            int maxResults = 8)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return new List<SearchProduct>();
            }


            query = Normalize(query);


            if (query.Length < 2)
            {
                return new List<SearchProduct>();
            }


            List<SearchProduct> products;


            lock (_lock)
            {
                // Copie de la référence.
                // La liste n'est jamais modifiée directement
                // pendant une recherche.
                products = _products;
            }


            var results =
                products
                    .Select(product =>
                    {
                        var score =
                            CalculateScore(
                                product,
                                query);

                        return new
                        {
                            Product = product,
                            Score = score
                        };
                    })
                    .Where(x => x.Score > 0)
                    .OrderByDescending(x => x.Score)
                    .ThenBy(x => x.Product.Name)
                    .Take(maxResults)
                    .Select(x => x.Product)
                    .ToList();


            return results;
        }


        // ============================================================
        // SEARCH SCORE
        // ============================================================

        private static int CalculateScore(
            SearchProduct product,
            string query)
        {
            var score = 0;


            var name =
                product.SearchName;

            var brand =
                product.SearchBrand;

            var shortName =
                product.SearchShortName;


            // ========================================================
            // EXACT NAME
            // ========================================================

            if (name == query)
            {
                score += 1000;
            }


            // ========================================================
            // NAME STARTS WITH
            // ========================================================

            if (name.StartsWith(
                    query,
                    StringComparison.Ordinal))
            {
                score += 500;
            }


            // ========================================================
            // BRAND STARTS WITH
            // ========================================================

            if (brand.StartsWith(
                    query,
                    StringComparison.Ordinal))
            {
                score += 450;
            }


            // ========================================================
            // SHORT NAME STARTS WITH
            // ========================================================

            if (
                !string.IsNullOrEmpty(shortName)
                &&
                shortName.StartsWith(
                    query,
                    StringComparison.Ordinal)
            )
            {
                score += 400;
            }


            // ========================================================
            // NAME CONTAINS
            // ========================================================

            if (name.Contains(
                    query,
                    StringComparison.Ordinal))
            {
                score += 200;
            }


            // ========================================================
            // BRAND CONTAINS
            // ========================================================

            if (brand.Contains(
                    query,
                    StringComparison.Ordinal))
            {
                score += 180;
            }


            // ========================================================
            // SHORT NAME CONTAINS
            // ========================================================

            if (
                !string.IsNullOrEmpty(shortName)
                &&
                shortName.Contains(
                    query,
                    StringComparison.Ordinal)
            )
            {
                score += 150;
            }


            return score;
        }


        // ============================================================
        // SUPABASE
        // ============================================================

        private async Task<List<SearchProduct>>
            LoadFromSupabaseAsync(
                CancellationToken cancellationToken)
        {
            var supabaseUrl =
                _configuration["SUPABASE_URL"];

            var supabaseKey =
                _configuration["SUPABASE_SERVICE_KEY"];


            if (string.IsNullOrWhiteSpace(
                    supabaseUrl))
            {
                throw new Exception(
                    "SUPABASE_URL is missing.");
            }


            if (string.IsNullOrWhiteSpace(
                    supabaseKey))
            {
                throw new Exception(
                    "SUPABASE_SERVICE_KEY is missing.");
            }


            var result =
                new List<SearchProduct>();


            const int pageSize = 1000;

            var offset = 0;


            var httpClient =
                _httpClientFactory.CreateClient();


            // ========================================================
            // PAGINATION
            // ========================================================

            while (true)
            {
                var url =
                    $"{supabaseUrl.TrimEnd('/')}" +
                    "/rest/v1/products" +
                    "?select=id,brand,name,short_name,image,slug" +
                    "&slug=not.is.null" +
                    "&order=id.asc" +
                    $"&limit={pageSize}" +
                    $"&offset={offset}";


                using var request =
                    new HttpRequestMessage(
                        HttpMethod.Get,
                        url);


                request.Headers.Add(
                    "apikey",
                    supabaseKey);


                request.Headers.Authorization =
                    new AuthenticationHeaderValue(
                        "Bearer",
                        supabaseKey);


                using var response =
                    await httpClient.SendAsync(
                        request,
                        cancellationToken);


                var json =
                    await response.Content
                        .ReadAsStringAsync(
                            cancellationToken);


                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception(
                        $"Supabase error " +
                        $"{(int)response.StatusCode}: " +
                        json);
                }


                var page =
                    JsonSerializer.Deserialize<
                        List<SupabaseProduct>
                    >(
                        json,
                        new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        }
                    )
                    ?? new List<SupabaseProduct>();


                if (page.Count == 0)
                {
                    break;
                }


                foreach (var item in page)
                {
                    if (item.Id <= 0)
                        continue;


                    if (string.IsNullOrWhiteSpace(
                            item.Name))
                        continue;


                    if (string.IsNullOrWhiteSpace(
                            item.Slug))
                        continue;


                    var name =
                        item.Name.Trim();

                    var brand =
                        item.Brand?.Trim() ?? "";

                    var shortName =
                        item.ShortName?.Trim() ?? "";

                    var slug =
                        item.Slug.Trim();

                    var image =
                        item.Image?.Trim() ?? "";


                    // =================================================
                    // NORMALISATION FAITE UNE SEULE FOIS
                    // =================================================

                    result.Add(
                        new SearchProduct
                        {
                            Id = item.Id,

                            Name = name,

                            Brand = brand,

                            ShortName = shortName,

                            Slug = slug,

                            Image = image,

                            SearchName =
                                Normalize(name),

                            SearchBrand =
                                Normalize(brand),

                            SearchShortName =
                                Normalize(shortName)
                        }
                    );
                }


                if (page.Count < pageSize)
                {
                    break;
                }


                offset += pageSize;
            }


            return result;
        }


        // ============================================================
        // NORMALIZE
        // ============================================================

        private static string Normalize(
            string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "";
            }


            var normalized =
                value
                    .Trim()
                    .ToLowerInvariant()
                    .Normalize(
                        NormalizationForm.FormD);


            var builder =
                new StringBuilder();


            foreach (var c in normalized)
            {
                var category =
                    CharUnicodeInfo
                        .GetUnicodeCategory(c);


                if (
                    category !=
                    UnicodeCategory.NonSpacingMark
                )
                {
                    builder.Append(c);
                }
            }


            return builder
                .ToString()
                .Normalize(
                    NormalizationForm.FormC);
        }


        // ============================================================
        // MANUAL INITIALIZATION
        // ============================================================

        public async Task InitializeAsync(
            CancellationToken cancellationToken = default)
        {
            if (IsLoaded)
                return;


            await LoadAsync(
                cancellationToken);
        }


        // ============================================================
        // SEARCH PRODUCT
        // ============================================================

        public class SearchProduct
        {
            public long Id { get; set; }

            public string Name { get; set; } = "";

            public string Brand { get; set; } = "";

            public string ShortName { get; set; } = "";

            public string Slug { get; set; } = "";

            public string Image { get; set; } = "";


            // ========================================================
            // INTERNAL SEARCH FIELDS
            // ========================================================

            [JsonIgnore]
            public string SearchName { get; set; } = "";

            [JsonIgnore]
            public string SearchBrand { get; set; } = "";

            [JsonIgnore]
            public string SearchShortName { get; set; } = "";
        }


        // ============================================================
        // SUPABASE MODEL
        // ============================================================

        private class SupabaseProduct
        {
            [JsonPropertyName("id")]
            public long Id { get; set; }


            [JsonPropertyName("brand")]
            public string? Brand { get; set; }


            [JsonPropertyName("name")]
            public string? Name { get; set; }


            [JsonPropertyName("short_name")]
            public string? ShortName { get; set; }


            [JsonPropertyName("image")]
            public string? Image { get; set; }


            [JsonPropertyName("slug")]
            public string? Slug { get; set; }
        }
    }
}