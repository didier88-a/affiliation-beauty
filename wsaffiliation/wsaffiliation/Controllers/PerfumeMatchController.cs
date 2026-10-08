using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using System.Text.Json.Serialization;

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

        // ============================================================
        // POST /api/perfume-match
        // ============================================================

        [HttpPost]
        public async Task<IActionResult> Match(
            [FromBody] PerfumeMatchRequest request)
        {
            try
            {
                if (request == null)
                    return BadRequest("Request is empty.");

                var products = await GetProductsAsync();

                // ----------------------------------------------------
                // ONLY PERFUMES
                // ----------------------------------------------------

                products = products
                    .Where(IsPerfume)
                    .ToList();

                // ----------------------------------------------------
                // REMOVE DUPLICATES
                // ----------------------------------------------------

                products = RemoveDuplicates(products);

                var results = new List<PerfumeMatchResult>();

                foreach (var product in products)
                {
                    var score = CalculateScore(product, request);

                    // Un parfum doit avoir au moins une correspondance
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

                        MatchedTopNotes = score.MatchedTopNotes,
                        MatchedHeartNotes = score.MatchedHeartNotes,
                        MatchedBaseNotes = score.MatchedBaseNotes,

                        MatchedStyles = score.MatchedStyles,
                        MatchedMoods = score.MatchedMoods,
                        MatchedOccasions = score.MatchedOccasions,
                        MatchedFamilies = score.MatchedFamilies
                    });
                }

                // ----------------------------------------------------
                // SORT
                // ----------------------------------------------------

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
                "?select=id,brand,name,short_name,image,slug,category,sub_category,type,naya_attributes";

            using var request =
                new HttpRequestMessage(
                    HttpMethod.Get,
                    url);

            request.Headers.Add(
                "apikey",
                supabaseKey);

            request.Headers.Add(
                "Authorization",
                $"Bearer {supabaseKey}");

            var response =
                await _httpClient.SendAsync(request);

            var content =
                await response.Content.ReadAsStringAsync();

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
        // FILTER PERFUMES
        // ============================================================

        private bool IsPerfume(Product product)
        {
            var category =
                Normalize(product.Category);

            var subCategory =
                Normalize(product.SubCategory);

            var type =
                Normalize(product.Type);

            if (category != "parfum" &&
                category != "parfums" &&
                category != "parfumerie")
            {
                return false;
            }

            // Exclusions
            if (subCategory.Contains("desodorisant") ||
                subCategory.Contains("huile essentielle") ||
                subCategory.Contains("coffret"))
            {
                return false;
            }

            if (type == "deodorant" ||
                type == "gel" ||
                type == "gel douche" ||
                type == "shampooing" ||
                type == "lotion" ||
                type == "serum" ||
                type == "masque" ||
                type == "body cream" ||
                type == "kit pinceaux")
            {
                return false;
            }

            return true;
        }

        // ============================================================
        // REMOVE DUPLICATES
        // ============================================================

        private List<Product> RemoveDuplicates(
            List<Product> products)
        {
            var result =
                new List<Product>();

            var seen =
                new HashSet<string>();

            foreach (var product in products)
            {
                var brand =
                    Normalize(product.Brand);

                var shortName =
                    Normalize(product.ShortName);

                var name =
                    Normalize(product.Name);

                string key;

                if (!string.IsNullOrWhiteSpace(brand) &&
                    !string.IsNullOrWhiteSpace(shortName))
                {
                    key = $"{brand}|{shortName}";
                }
                else if (!string.IsNullOrWhiteSpace(product.Slug))
                {
                    key = Normalize(product.Slug);
                }
                else
                {
                    key = $"{brand}|{name}";
                }

                if (string.IsNullOrWhiteSpace(key))
                    continue;

                if (seen.Add(key))
                    result.Add(product);
            }

            return result;
        }

        // ============================================================
        // CALCULATE SCORE
        // ============================================================

        private MatchScore CalculateScore(
            Product product,
            PerfumeMatchRequest request)
        {
            var score =
                new MatchScore();

            var attributes =
                product.NayaAttributes;

            if (attributes.ValueKind !=
                JsonValueKind.Object)
            {
                return score;
            }

            // ========================================================
            // GENDER
            // ========================================================

            var productGender =
                GetString(
                    attributes,
                    "gender");

            if (!string.IsNullOrWhiteSpace(
                    request.Whom))
            {
                if (GenderMatches(
                    request.Whom,
                    productGender))
                {
                    score.TotalScore += 10;
                    score.MatchedGender = true;
                }
            }

            // ========================================================
            // TOP NOTES
            // ========================================================

            var topNotes =
                GetStringArray(
                    attributes,
                    "top_notes");

            // ========================================================
            // HEART NOTES
            // ========================================================

            var heartNotes =
                GetStringArray(
                    attributes,
                    "heart_notes");

            // ========================================================
            // BASE NOTES
            // ========================================================

            var baseNotes =
                GetStringArray(
                    attributes,
                    "base_notes");

            // ========================================================
            // SELECTED NOTES
            // ========================================================

            foreach (var selectedNote
                     in request.Notes)
            {
                if (string.IsNullOrWhiteSpace(
                        selectedNote))
                {
                    continue;
                }

                // Top = +8
                if (NoteMatches(
                    selectedNote,
                    topNotes))
                {
                    score.TotalScore += 8;

                    score.MatchedTopNotes.Add(
                        selectedNote);

                    continue;
                }

                // Heart = +10
                if (NoteMatches(
                    selectedNote,
                    heartNotes))
                {
                    score.TotalScore += 10;

                    score.MatchedHeartNotes.Add(
                        selectedNote);

                    continue;
                }

                // Base = +8
                if (NoteMatches(
                    selectedNote,
                    baseNotes))
                {
                    score.TotalScore += 8;

                    score.MatchedBaseNotes.Add(
                        selectedNote);
                }
            }

            // ========================================================
            // STYLES
            // ========================================================

            var productStyles =
                GetStringArray(
                    attributes,
                    "style");

            // Les familles olfactives participent aussi
            var fragranceFamilies =
                GetStringArray(
                    attributes,
                    "fragrance_family");

            var stylesAndFamilies =
                productStyles
                    .Concat(fragranceFamilies)
                    .Distinct()
                    .ToList();

            foreach (var selectedStyle
                     in request.Styles)
            {
                if (string.IsNullOrWhiteSpace(
                        selectedStyle))
                {
                    continue;
                }

                if (StringMatches(
                    selectedStyle,
                    stylesAndFamilies))
                {
                    score.TotalScore += 6;

                    score.MatchedStyles.Add(
                        selectedStyle);
                }
            }

            // ========================================================
            // FRAGRANCE FAMILY
            // ========================================================

            foreach (var selectedFamily
                     in request.Families)
            {
                if (string.IsNullOrWhiteSpace(
                        selectedFamily))
                {
                    continue;
                }

                if (StringMatches(
                    selectedFamily,
                    fragranceFamilies))
                {
                    score.TotalScore += 4;

                    score.MatchedFamilies.Add(
                        selectedFamily);
                }
            }

            // ========================================================
            // OCCASION
            // ========================================================

            var productOccasions =
                GetStringArray(
                    attributes,
                    "occasion");

            foreach (var selectedOccasion
                     in request.Occasions)
            {
                if (string.IsNullOrWhiteSpace(
                        selectedOccasion))
                {
                    continue;
                }

                if (StringMatches(
                    selectedOccasion,
                    productOccasions))
                {
                    score.TotalScore += 4;

                    score.MatchedOccasions.Add(
                        selectedOccasion);
                }
            }

            // Support ancien format :
            // request.occasion = "Soirée"

            if (!string.IsNullOrWhiteSpace(
                    request.Occasion))
            {
                if (!request.Occasions.Contains(
                        request.Occasion))
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
            }

            // ========================================================
            // MOODS
            // ========================================================

            foreach (var selectedMood
                     in request.Moods)
            {
                if (string.IsNullOrWhiteSpace(
                        selectedMood))
                {
                    continue;
                }

                if (MoodMatches(
                    selectedMood,
                    attributes))
                {
                    score.TotalScore += 6;

                    score.MatchedMoods.Add(
                        selectedMood);
                }
            }

            // ========================================================
            // MATCH PERCENT
            // ========================================================

            var maxScore =
                CalculateMaximumScore(
                    request);

            if (maxScore > 0)
            {
                score.MatchPercent =
                    Math.Min(
                        100,
                        (int)Math.Round(
                            score.TotalScore *
                            100.0 /
                            maxScore));
            }

            return score;
        }

        // ============================================================
        // MAX SCORE
        // ============================================================

        private int CalculateMaximumScore(
            PerfumeMatchRequest request)
        {
            var max = 0;

            if (!string.IsNullOrWhiteSpace(
                    request.Whom))
            {
                max += 10;
            }

            foreach (var note in request.Notes)
            {
                if (!string.IsNullOrWhiteSpace(note))
                    max += 10;
            }

            foreach (var style in request.Styles)
            {
                if (!string.IsNullOrWhiteSpace(style))
                    max += 6;
            }

            foreach (var mood in request.Moods)
            {
                if (!string.IsNullOrWhiteSpace(mood))
                    max += 6;
            }

            foreach (var occasion in request.Occasions)
            {
                if (!string.IsNullOrWhiteSpace(occasion))
                    max += 4;
            }

            if (!string.IsNullOrWhiteSpace(
                    request.Occasion) &&
                !request.Occasions.Contains(
                    request.Occasion))
            {
                max += 4;
            }

            foreach (var family in request.Families)
            {
                if (!string.IsNullOrWhiteSpace(family))
                    max += 4;
            }

            return max;
        }

        // ============================================================
        // GENDER
        // ============================================================

        private bool GenderMatches(
            string selectedGender,
            string productGender)
        {
            var selected =
                Normalize(selectedGender);

            var product =
                Normalize(productGender);

            // Pas de genre défini :
            // on ne pénalise pas le parfum
            if (string.IsNullOrWhiteSpace(product))
                return true;

            // Unisexe
            if (product.Contains("unisexe") ||
                product.Contains("unisex"))
            {
                return true;
            }

            // Femme
            if (selected == "her" ||
                selected == "femme" ||
                selected == "female" ||
                selected == "woman")
            {
                return
                    product == "femme" ||
                    product == "female" ||
                    product == "woman";
            }

            // Homme
            if (selected == "him" ||
                selected == "homme" ||
                selected == "male" ||
                selected == "man")
            {
                return
                    product == "homme" ||
                    product == "male" ||
                    product == "man";
            }

            // Unisexe sélectionné
            if (selected == "unisex" ||
                selected == "unisexe")
            {
                return true;
            }

            return false;
        }

        // ============================================================
        // MOOD MATCH
        // ============================================================

        private bool MoodMatches(
            string selectedMood,
            JsonElement attributes)
        {
            var mood =
                Normalize(selectedMood);

            // Si un jour naya_attributes possède
            // directement "mood", on l'utilise.
            var directMoods =
                GetStringArray(
                    attributes,
                    "mood");

            if (StringMatches(
                mood,
                directMoods))
            {
                return true;
            }

            // --------------------------------------------------------
            // MAPPING NAYA MOOD -> ATTRIBUTES
            // --------------------------------------------------------

            var productStyles =
                GetStringArray(
                    attributes,
                    "style");

            var families =
                GetStringArray(
                    attributes,
                    "fragrance_family");

            var all =
                productStyles
                    .Concat(families)
                    .ToList();

            switch (mood)
            {
                case "elegant":
                    return StringMatches(
                        "élégant",
                        all);

                case "sophisticated":
                case "sophistique":
                    return StringMatches(
                        "sophistiqué",
                        all);

                case "sensual":
                case "sensuel":
                    return StringMatches(
                        "sensuel",
                        all);

                case "seductive":
                case "seduisant":
                    return StringMatches(
                        "séduisant",
                        all);

                case "luxury":
                case "luxueux":
                    return StringMatches(
                        "luxueux",
                        all);

                case "fresh":
                case "fresh-invigorating":
                case "frais":
                    return StringMatches(
                        "frais",
                        all);

                case "intense":
                case "powerful":
                    return StringMatches(
                        "intense",
                        all);

                case "romantic":
                    return
                        StringMatches(
                            "floral",
                            all)
                        ||
                        StringMatches(
                            "séduisant",
                            all)
                        ||
                        StringMatches(
                            "sensuel",
                            all);

                case "sexy":
                    return
                        StringMatches(
                            "séduisant",
                            all)
                        ||
                        StringMatches(
                            "sensuel",
                            all);

                case "chic":
                    return
                        StringMatches(
                            "élégant",
                            all)
                        ||
                        StringMatches(
                            "sophistiqué",
                            all);

                case "modern":
                    return StringMatches(
                        "moderne",
                        all);

                case "warm":
                    return
                        StringMatches(
                            "oriental",
                            families)
                        ||
                        StringMatches(
                            "ambré",
                            families);

                case "playful":
                    return
                        StringMatches(
                            "fruité",
                            families);

                default:
                    return false;
            }
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

            if (string.IsNullOrWhiteSpace(
                    selected))
            {
                return false;
            }

            foreach (var productNote
                     in productNotes)
            {
                var normalized =
                    Normalize(productNote);

                if (string.IsNullOrWhiteSpace(
                        normalized))
                {
                    continue;
                }

                if (normalized == selected)
                    return true;

                if (normalized.Contains(
                        selected))
                {
                    return true;
                }

                if (selected.Contains(
                        normalized))
                {
                    return true;
                }
            }

            return false;
        }

        // ============================================================
        // STRING MATCH
        // ============================================================

        private bool StringMatches(
            string selected,
            List<string> values)
        {
            var normalizedSelected =
                Normalize(selected);

            if (string.IsNullOrWhiteSpace(
                    normalizedSelected))
            {
                return false;
            }

            foreach (var value in values)
            {
                var normalizedValue =
                    Normalize(value);

                if (string.IsNullOrWhiteSpace(
                        normalizedValue))
                {
                    continue;
                }

                if (normalizedValue ==
                    normalizedSelected)
                {
                    return true;
                }

                if (normalizedValue.Contains(
                        normalizedSelected))
                {
                    return true;
                }

                if (normalizedSelected.Contains(
                        normalizedValue))
                {
                    return true;
                }
            }

            return false;
        }

        // ============================================================
        // GET STRING
        // ============================================================

        private string GetString(
            JsonElement json,
            string property)
        {
            if (!json.TryGetProperty(
                    property,
                    out var value))
            {
                return "";
            }

            if (value.ValueKind ==
                JsonValueKind.String)
            {
                return value.GetString() ?? "";
            }

            return "";
        }

        // ============================================================
        // GET STRING ARRAY
        // ============================================================

        private List<string> GetStringArray(
            JsonElement json,
            string property)
        {
            var result =
                new List<string>();

            if (!json.TryGetProperty(
                    property,
                    out var value))
            {
                return result;
            }

            if (value.ValueKind !=
                JsonValueKind.Array)
            {
                return result;
            }

            foreach (var item
                     in value.EnumerateArray())
            {
                if (item.ValueKind ==
                    JsonValueKind.String)
                {
                    var text =
                        item.GetString();

                    if (!string.IsNullOrWhiteSpace(
                            text))
                    {
                        result.Add(text);
                    }
                }
            }

            return result;
        }

        // ============================================================
        // NORMALIZE
        // ============================================================

        private string Normalize(
            string? value)
        {
            if (string.IsNullOrWhiteSpace(
                    value))
            {
                return "";
            }

            var normalized =
                value.Trim()
                     .ToLowerInvariant();

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

        public List<string> Occasions { get; set; }
            = new();

        public List<string> Moods { get; set; }
            = new();

        public List<string> Styles { get; set; }
            = new();

        public List<string> Notes { get; set; }
            = new();

        public List<string> Families { get; set; }
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

        [JsonPropertyName("short_name")]
        public string? ShortName { get; set; }

        public string? Image { get; set; }

        public string? Slug { get; set; }

        public string? Category { get; set; }

        [JsonPropertyName("sub_category")]
        public string? SubCategory { get; set; }

        public string? Type { get; set; }

        [JsonPropertyName("naya_attributes")]
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

        public List<string> MatchedTopNotes { get; set; }
            = new();

        public List<string> MatchedHeartNotes { get; set; }
            = new();

        public List<string> MatchedBaseNotes { get; set; }
            = new();

        public List<string> MatchedStyles { get; set; }
            = new();

        public List<string> MatchedMoods { get; set; }
            = new();

        public List<string> MatchedOccasions { get; set; }
            = new();

        public List<string> MatchedFamilies { get; set; }
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

        public List<string> MatchedTopNotes { get; set; }
            = new();

        public List<string> MatchedHeartNotes { get; set; }
            = new();

        public List<string> MatchedBaseNotes { get; set; }
            = new();

        public List<string> MatchedStyles { get; set; }
            = new();

        public List<string> MatchedMoods { get; set; }
            = new();

        public List<string> MatchedOccasions { get; set; }
            = new();

        public List<string> MatchedFamilies { get; set; }
            = new();
    }
}