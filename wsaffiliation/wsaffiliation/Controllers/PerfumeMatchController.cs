using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace wsaffiliation.Controllers
{
    [ApiController]
    [Route("api/perfume-match")]
    public class PerfumeMatchController : ControllerBase
    {
        private readonly IConfiguration _configuration;
        private readonly HttpClient _httpClient;

        private const int MaxResults = 24;

        public PerfumeMatchController(
            IConfiguration configuration,
            IHttpClientFactory httpClientFactory)
        {
            _configuration = configuration;
            _httpClient = httpClientFactory.CreateClient();
        }

        [HttpPost]
        public async Task<IActionResult> Match(
            [FromBody] PerfumeMatchRequest request)
        {
            try
            {
                if (request == null)
                    return BadRequest("Request is empty.");

                var products = await GetProductsAsync();

                var results = new List<PerfumeMatchResult>();

                foreach (var product in products)
                {
                    var score = CalculateScore(product, request);

                    if (score.TotalScore <= 0)
                        continue;

                    results.Add(new PerfumeMatchResult
                    {
                        Id = product.Id,
                        Brand = product.Brand,
                        Name = product.Name,
                        ShortName = product.ShortName,
                        Image = product.Image,
                        Slug = product.Slug,
                        Score = score.TotalScore,
                        MatchPercent = score.MatchPercent,

                        MatchedGender = score.MatchedGender,
                        MatchedNotes = score.MatchedNotes,
                        MatchedStyles = score.MatchedStyles,
                        MatchedOccasions = score.MatchedOccasions
                    });
                }

                var orderedResults = results
                    .OrderByDescending(x => x.Score)
                    .ThenByDescending(x => x.MatchPercent)
                    .Take(MaxResults)
                    .ToList();

                return Ok(new
                {
                    count = orderedResults.Count,
                    totalProducts = products.Count,
                    results = orderedResults
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    error = "Error while matching perfumes.",
                    message = ex.Message
                });
            }
        }

        // ============================================================
        // GET PRODUCTS FROM SUPABASE
        // ============================================================

        private async Task<List<Product>> GetProductsAsync()
        {
            var supabaseUrl = Environment.GetEnvironmentVariable("SUPABASE_URL");
            var supabaseKey = Environment.GetEnvironmentVariable("SUPABASE_KEY");

            if (string.IsNullOrWhiteSpace(supabaseUrl))
                throw new Exception("Supabase:Url is missing.");

            if (string.IsNullOrWhiteSpace(supabaseKey))
                throw new Exception("Supabase:Key is missing.");

            var url =
                $"{supabaseUrl.TrimEnd('/')}/rest/v1/products" +
                "?select=id,brand,name,short_name,image,slug,naya_attributes";

            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                url);

            request.Headers.Add(
                "apikey",
                supabaseKey);

            request.Headers.Add(
                "Authorization",
                $"Bearer {supabaseKey}");

            var response = await _httpClient.SendAsync(request);

            var content = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception(
                    $"Supabase error: {response.StatusCode} - {content}");
            }

            return JsonSerializer.Deserialize<List<Product>>(
                content,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                })
                ?? new List<Product>();
        }

        // ============================================================
        // SCORE
        // ============================================================

        private MatchScore CalculateScore(
            Product product,
            PerfumeMatchRequest request)
        {
            var score = new MatchScore();

            var attributes = product.NayaAttributes;

            Console.WriteLine(
                $"PRODUCT: {product.Id} - {product.Name}");

            Console.WriteLine(
                $"ATTRIBUTES: {attributes}");

            if (attributes.ValueKind != JsonValueKind.Object)
                return score;

            // --------------------------------------------------------
            // GENDER
            // --------------------------------------------------------

            var productGender =
                GetString(attributes, "gender");

            if (!string.IsNullOrWhiteSpace(request.Whom))
            {
                if (GenderMatches(
                    request.Whom,
                    productGender))
                {
                    score.TotalScore += 10;
                    score.MatchedGender = true;
                }
            }

            // --------------------------------------------------------
            // NOTES
            // --------------------------------------------------------

            var productTopNotes =
                GetStringArray(attributes, "top_notes");

            var productHeartNotes =
                GetStringArray(attributes, "heart_notes");

            var productBaseNotes =
                GetStringArray(attributes, "base_notes");

            var productNotes =
                productTopNotes
                    .Concat(productHeartNotes)
                    .Concat(productBaseNotes)
                    .ToList();

            foreach (var selectedNote in request.Notes)
            {
                if (string.IsNullOrWhiteSpace(selectedNote))
                    continue;

                if (NoteMatches(
                    selectedNote,
                    productNotes))
                {
                    score.TotalScore += 8;

                    score.MatchedNotes.Add(
                        selectedNote);
                }
            }

            // --------------------------------------------------------
            // STYLE
            // --------------------------------------------------------

            var productStyles =
                GetStringArray(attributes, "style");

            foreach (var selectedStyle in request.Styles)
            {
                if (string.IsNullOrWhiteSpace(selectedStyle))
                    continue;

                if (StringMatches(
                    selectedStyle,
                    productStyles))
                {
                    score.TotalScore += 6;

                    score.MatchedStyles.Add(
                        selectedStyle);
                }
            }

            // --------------------------------------------------------
            // OCCASION
            // --------------------------------------------------------

            var productOccasions =
                GetStringArray(attributes, "occasion");

            if (!string.IsNullOrWhiteSpace(request.Occasion))
            {
                if (StringMatches(
                    request.Occasion,
                    productOccasions))
                {
                    score.TotalScore += 4;

                    score.MatchedOccasions.Add(
                        request.Occasion);
                }
            }

            // --------------------------------------------------------
            // MATCH %
            // --------------------------------------------------------

            var maxScore = 0;

            if (!string.IsNullOrWhiteSpace(request.Whom))
                maxScore += 10;

            maxScore += request.Notes.Count * 8;
            maxScore += request.Styles.Count * 6;

            if (!string.IsNullOrWhiteSpace(request.Occasion))
                maxScore += 4;

            if (maxScore > 0)
            {
                score.MatchPercent =
                    Math.Min(
                        100,
                        (int)Math.Round(
                            score.TotalScore * 100.0 / maxScore));
            }

            Console.WriteLine(
                $"SCORE: {score.TotalScore}");

            return score;
        }

        // ============================================================
        // GENDER MATCH
        // ============================================================

        private bool GenderMatches(
     string selectedGender,
     string productGender)
        {
            var selected = Normalize(selectedGender);
            var product = Normalize(productGender);

            if (string.IsNullOrWhiteSpace(selected))
                return false;

            // Produit sans genre = on le garde
            // pour les parfums unisexes / universels
            if (string.IsNullOrWhiteSpace(product))
                return true;

            // Produit unisexe
            if (product.Contains("unisexe") ||
                product.Contains("unisex"))
                return true;

            // Sélection femme
            if (selected == "her" ||
                selected == "femme" ||
                selected == "female" ||
                selected == "woman")
            {
                return product == "femme" ||
                       product == "female" ||
                       product == "woman";
            }

            // Sélection homme
            if (selected == "him" ||
                selected == "homme" ||
                selected == "male" ||
                selected == "man")
            {
                return product == "homme" ||
                       product == "male" ||
                       product == "man";
            }

            // Sélection unisexe
            if (selected == "unisex" ||
                selected == "unisexe")
            {
                return true;
            }

            return false;
        }

        // ============================================================
        // NOTE MATCH
        // ============================================================

        private bool NoteMatches(
            string selectedNote,
            List<string> productNotes)
        {
            var selected =
                Normalize(selectedNote);

            if (string.IsNullOrWhiteSpace(selected))
                return false;

            foreach (var productNote in productNotes)
            {
                var normalizedProduct =
                    Normalize(productNote);

                if (string.IsNullOrWhiteSpace(normalizedProduct))
                    continue;

                // Exact
                if (normalizedProduct == selected)
                    return true;

                // Exemple :
                // "rose" => "rose damascena"
                if (normalizedProduct.Contains(selected))
                    return true;

                if (selected.Contains(normalizedProduct))
                    return true;
            }

            return false;
        }

        // ============================================================
        // GENERIC STRING MATCH
        // ============================================================

        private bool StringMatches(
            string selected,
            List<string> values)
        {
            var normalizedSelected =
                Normalize(selected);

            foreach (var value in values)
            {
                var normalizedValue =
                    Normalize(value);

                if (normalizedValue == normalizedSelected)
                    return true;

                if (normalizedValue.Contains(
                        normalizedSelected))
                    return true;

                if (normalizedSelected.Contains(
                        normalizedValue))
                    return true;
            }

            return false;
        }

        // ============================================================
        // JSON HELPERS
        // ============================================================

        private string GetString(
            JsonElement json,
            string property)
        {
            if (!json.TryGetProperty(
                    property,
                    out var value))
                return "";

            if (value.ValueKind == JsonValueKind.String)
                return value.GetString() ?? "";

            return "";
        }

        private List<string> GetStringArray(
            JsonElement json,
            string property)
        {
            var result = new List<string>();

            if (!json.TryGetProperty(
                    property,
                    out var value))
                return result;

            if (value.ValueKind != JsonValueKind.Array)
                return result;

            foreach (var item in value.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    var text = item.GetString();

                    if (!string.IsNullOrWhiteSpace(text))
                        result.Add(text);
                }
            }

            return result;
        }

        // ============================================================
        // NORMALIZATION
        // ============================================================

        private string Normalize(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";

            var normalized =
                value.Trim().ToLowerInvariant();

            normalized = normalized
                .Replace("é", "e")
                .Replace("è", "e")
                .Replace("ê", "e")
                .Replace("ë", "e")
                .Replace("à", "a")
                .Replace("â", "a")
                .Replace("ä", "a")
                .Replace("î", "i")
                .Replace("ï", "i")
                .Replace("ô", "o")
                .Replace("ö", "o")
                .Replace("ù", "u")
                .Replace("û", "u")
                .Replace("ü", "u")
                .Replace("ç", "c");

            return normalized;
        }
    }

    // ================================================================
    // REQUEST
    // ================================================================

    public class PerfumeMatchRequest
    {
        public string? Whom { get; set; }

        public string? Occasion { get; set; }

        public List<string> Moods { get; set; }
            = new();

        public List<string> Styles { get; set; }
            = new();

        public List<string> Notes { get; set; }
            = new();
    }

    // ================================================================
    // PRODUCT
    // ================================================================

    public class Product
    {
        public long Id { get; set; }

        public string? Brand { get; set; }

        public string? Name { get; set; }

        public string? ShortName { get; set; }

        public string? Image { get; set; }

        public string? Slug { get; set; }

        public JsonElement NayaAttributes { get; set; }
    }

    // ================================================================
    // SCORE
    // ================================================================

    public class MatchScore
    {
        public int TotalScore { get; set; }

        public int MatchPercent { get; set; }

        public bool MatchedGender { get; set; }

        public List<string> MatchedNotes { get; set; }
            = new();

        public List<string> MatchedStyles { get; set; }
            = new();

        public List<string> MatchedOccasions { get; set; }
            = new();
    }

    // ================================================================
    // RESULT
    // ================================================================

    public class PerfumeMatchResult
    {
        public long Id { get; set; }

        public string? Brand { get; set; }

        public string? Name { get; set; }

        public string? ShortName { get; set; }

        public string? Image { get; set; }

        public string? Slug { get; set; }

        public int Score { get; set; }

        public int MatchPercent { get; set; }

        public bool MatchedGender { get; set; }

        public List<string> MatchedNotes { get; set; }
            = new();

        public List<string> MatchedStyles { get; set; }
            = new();

        public List<string> MatchedOccasions { get; set; }
            = new();
    }
}