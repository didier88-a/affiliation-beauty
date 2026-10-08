using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;

namespace wsaffiliation.Controllers;

[ApiController]
[Route("api/perfume-match")]
public class PerfumeMatchController : ControllerBase
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;

    public PerfumeMatchController(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration)
    {
        _httpClient = httpClientFactory.CreateClient();
        _configuration = configuration;
    }

    [HttpPost]
    public async Task<IActionResult> Match(
        [FromBody] PerfumeMatchRequest request)
    {
        try
        {
            var supabaseUrl = Environment.GetEnvironmentVariable("SUPABASE_URL");
            var supabaseKey = Environment.GetEnvironmentVariable("SUPABASE_KEY");

            if (string.IsNullOrWhiteSpace(supabaseUrl) ||
                string.IsNullOrWhiteSpace(supabaseKey))
            {
                return StatusCode(500, new
                {
                    error = "SUPABASE_URL or SUPABASE_KEY is missing."
                });
            }

            var url =
                $"{supabaseUrl.TrimEnd('/')}/rest/v1/products" +
                "?select=id,brand,name,short_name,image,slug,category,sub_category,type,naya_attributes";

            using var httpRequest = new HttpRequestMessage(
                HttpMethod.Get,
                url);

            httpRequest.Headers.Add("apikey", supabaseKey);
            httpRequest.Headers.Add("Authorization", $"Bearer {supabaseKey}");

            var response = await _httpClient.SendAsync(httpRequest);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();

                return StatusCode(
                    (int)response.StatusCode,
                    new
                    {
                        error = "Supabase error",
                        details = error
                    });
            }

            var json = await response.Content.ReadAsStringAsync();

            var products =
                JsonSerializer.Deserialize<List<Product>>(
                    json,
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    })
                ?? new List<Product>();

            // ---------------------------------------------------------
            // 1. GARDER UNIQUEMENT LES PARFUMS
            // ---------------------------------------------------------

            products = products
                .Where(IsPerfume)
                .ToList();

            var totalProducts = products.Count;

            // ---------------------------------------------------------
            // 2. NORMALISER LA REQUÊTE
            // ---------------------------------------------------------

            request.Moods = CleanList(request.Moods);
            request.Styles = CleanList(request.Styles);
            request.Notes = CleanList(request.Notes);
            request.Families = CleanList(request.Families);

            var occasions = new List<string>();

            if (!string.IsNullOrWhiteSpace(request.Occasion))
                occasions.Add(request.Occasion);

            occasions.AddRange(request.Occasions ?? []);

            occasions = CleanList(occasions);

            // ---------------------------------------------------------
            // 3. CALCUL DU SCORE MAXIMUM
            // ---------------------------------------------------------

            var maximumScore =
                CalculateMaximumScore(request, occasions);

            // ---------------------------------------------------------
            // 4. MATCHING
            // ---------------------------------------------------------

            var results = new List<PerfumeMatchResult>();

            foreach (var product in products)
            {
                var result = CalculateScore(
                    product,
                    request,
                    occasions,
                    maximumScore);

                // On élimine uniquement les parfums qui
                // n'ont absolument aucun match.
                if (result.Score > 0)
                {
                    results.Add(result);
                }
            }

            // ---------------------------------------------------------
            // 5. DÉDUPLICATION
            // ---------------------------------------------------------

            results = results
                .GroupBy(x =>
                    Normalize(
                        !string.IsNullOrWhiteSpace(x.ShortName)
                            ? x.ShortName!
                            : x.Name ?? ""))
                .Select(g =>
                    g.OrderByDescending(x => x.Score)
                     .First())
                .ToList();

            // ---------------------------------------------------------
            // 6. TRI
            // ---------------------------------------------------------

            results = results
                .OrderByDescending(x => x.Score)
                .ThenByDescending(x => x.MatchPercent)
                .Take(24)
                .ToList();

            return Ok(new
            {
                count = results.Count,
                totalProducts,
                maximumScore,
                results
            });
        }
        catch (Exception ex)
        {
            return StatusCode(
                500,
                new
                {
                    error = ex.Message,
                    stack = ex.StackTrace
                });
        }
    }

    // ================================================================
    // SCORE
    // ================================================================

    private static PerfumeMatchResult CalculateScore(
        Product product,
        PerfumeMatchRequest request,
        List<string> occasions,
        int maximumScore)
    {
        var result = new PerfumeMatchResult
        {
            Id = product.Id,
            Brand = product.Brand,
            Name = product.Name,
            ShortName = product.ShortName,
            Image = product.Image,
            Slug = product.Slug
        };

        var score = 0;

        var attr = product.NayaAttributes;

        // ------------------------------------------------------------
        // GENDER
        // ------------------------------------------------------------

        if (!string.IsNullOrWhiteSpace(request.Whom))
        {
            var gender = GetString(attr, "gender");

            if (GenderMatches(request.Whom, gender))
            {
                score += 10;
                result.MatchedGender = true;
            }
        }

        // ------------------------------------------------------------
        // NOTES
        // ------------------------------------------------------------

        var topNotes =
            GetStringList(attr, "top_notes");

        var heartNotes =
            GetStringList(attr, "heart_notes");

        var baseNotes =
            GetStringList(attr, "base_notes");

        foreach (var selectedNote in request.Notes)
        {
            if (ContainsAny(topNotes, selectedNote))
            {
                score += 8;

                result.MatchedTopNotes.Add(
                    selectedNote);
            }

            if (ContainsAny(heartNotes, selectedNote))
            {
                score += 10;

                result.MatchedHeartNotes.Add(
                    selectedNote);
            }

            if (ContainsAny(baseNotes, selectedNote))
            {
                score += 8;

                result.MatchedBaseNotes.Add(
                    selectedNote);
            }
        }

        // ------------------------------------------------------------
        // STYLES
        // ------------------------------------------------------------

        var productStyles =
            GetStringList(attr, "style");

        var productFamilies =
            GetStringList(attr, "fragrance_family");

        foreach (var selectedStyle in request.Styles)
        {
            if (StyleMatches(
                selectedStyle,
                productStyles,
                productFamilies))
            {
                score += 6;

                result.MatchedStyles.Add(
                    selectedStyle);
            }
        }

        // ------------------------------------------------------------
        // MOODS
        // ------------------------------------------------------------

        foreach (var selectedMood in request.Moods)
        {
            if (MoodMatches(
                selectedMood,
                productStyles,
                productFamilies))
            {
                score += 6;

                result.MatchedMoods.Add(
                    selectedMood);
            }
        }

        // ------------------------------------------------------------
        // OCCASION
        // ------------------------------------------------------------

        var productOccasions =
            GetStringList(attr, "occasion");

        foreach (var selectedOccasion in occasions)
        {
            if (ContainsAny(
                productOccasions,
                selectedOccasion))
            {
                score += 4;

                result.MatchedOccasions.Add(
                    selectedOccasion);
            }
        }

        // ------------------------------------------------------------
        // FAMILIES
        // ------------------------------------------------------------

        foreach (var selectedFamily in request.Families)
        {
            if (ContainsAny(
                productFamilies,
                selectedFamily))
            {
                score += 4;

                result.MatchedFamilies.Add(
                    selectedFamily);
            }
        }

        result.Score = score;

        result.MatchPercent =
            maximumScore > 0
                ? Math.Min(
                    100,
                    (int)Math.Round(
                        score * 100.0 / maximumScore))
                : 0;

        return result;
    }

    // ================================================================
    // MAXIMUM SCORE
    // ================================================================

    private static int CalculateMaximumScore(
        PerfumeMatchRequest request,
        List<string> occasions)
    {
        var score = 0;

        // Gender
        if (!string.IsNullOrWhiteSpace(request.Whom))
            score += 10;

        // Notes
        foreach (var note in request.Notes)
        {
            // Une note sélectionnée peut être top, cœur ou fond.
            // Pour le maximum on prend le poids maximum possible.
            score += 10;
        }

        // Styles
        score += request.Styles.Count * 6;

        // Moods
        score += request.Moods.Count * 6;

        // Occasions
        score += occasions.Count * 4;

        // Families
        score += request.Families.Count * 4;

        return score;
    }

    // ================================================================
    // PERFUME FILTER
    // ================================================================

    private static bool IsPerfume(Product product)
    {
        var values = new[]
        {
            product.Category,
            product.SubCategory,
            product.Type
        };

        return values.Any(x =>
            !string.IsNullOrWhiteSpace(x) &&
            (
                Normalize(x).Contains("parfum") ||
                Normalize(x).Contains("parfumerie")
            ));
    }

    // ================================================================
    // GENDER
    // ================================================================

    private static bool GenderMatches(
        string selected,
        string? productGender)
    {
        if (string.IsNullOrWhiteSpace(productGender))
            return true;

        var selectedValue =
            Normalize(selected);

        var productValue =
            Normalize(productGender);

        if (productValue.Contains("unisexe"))
            return true;

        if (selectedValue == "her" ||
            selectedValue == "femme" ||
            selectedValue == "woman")
        {
            return productValue.Contains("femme") ||
                   productValue.Contains("female");
        }

        if (selectedValue == "him" ||
            selectedValue == "homme" ||
            selectedValue == "man")
        {
            return productValue.Contains("homme") ||
                   productValue.Contains("male");
        }

        return true;
    }

    // ================================================================
    // STYLE MAPPING
    // ================================================================

    private static bool StyleMatches(
        string selectedStyle,
        List<string> styles,
        List<string> families)
    {
        var possibleValues =
            GetStyleMapping(selectedStyle);

        return possibleValues.Any(value =>
            ContainsAny(styles, value) ||
            ContainsAny(families, value));
    }

    private static List<string> GetStyleMapping(
        string style)
    {
        return Normalize(style) switch
        {
            "aquatic" =>
                ["aquatique", "aquatic"],

            "aromatic" =>
                ["aromatique", "aromatic"],

            "chypre" =>
                ["chypre", "chypree"],

            "citrus" =>
                ["agrume", "agrumes", "citrus"],

            "creamy" =>
                ["cremeux", "cremeuse", "creamy"],

            "earthy" =>
                ["terreux", "terreuse", "earthy"],

            "floral" =>
                ["floral", "florale"],

            "fresh" =>
                ["frais", "fraiche", "fresh"],

            "fruity" =>
                ["fruite", "fruitee", "fruity"],

            "gourmand" =>
                ["gourmand", "gourmande"],

            "green" =>
                ["vert", "verte", "green"],

            "iris-makeup" =>
                ["iris", "maquillage"],

            "leather" =>
                ["cuir", "cuire", "cuiree", "leather"],

            "musky" =>
                ["musc", "musque", "musquee", "musky"],

            "oriental-amber" =>
                [
                    "oriental",
                    "orientale",
                    "ambre",
                    "ambree",
                    "ambré"
                ],

            "oud" =>
                ["oud"],

            "powdery" =>
                ["poudre", "poudre", "poudree"],

            "smoky" =>
                ["fume", "fumee", "smoky"],

            "spicy" =>
                [
                    "epice",
                    "epicee",
                    "epices",
                    "spicy"
                ],

            "aldehydic" =>
                [
                    "aldehydique",
                    "aldehydé",
                    "aldehyde"
                ],

            "woody" =>
                [
                    "boise",
                    "boisee",
                    "woody"
                ],

            _ =>
                [style]
        };
    }

    // ================================================================
    // MOOD MAPPING
    // ================================================================

    private static bool MoodMatches(
        string mood,
        List<string> styles,
        List<string> families)
    {
        var values =
            GetMoodMapping(mood);

        return values.Any(value =>
            ContainsAny(styles, value) ||
            ContainsAny(families, value));
    }

    private static List<string> GetMoodMapping(
        string mood)
    {
        return Normalize(mood) switch
        {
            "bold" =>
                [
                    "intense",
                    "puissant",
                    "puissante"
                ],

            "calm" =>
                [
                    "doux",
                    "douce",
                    "frais",
                    "fraiche"
                ],

            "chic" =>
                [
                    "elegant",
                    "elegante",
                    "sophistique",
                    "sophistiquee"
                ],

            "clean" =>
                [
                    "frais",
                    "fraiche",
                    "aromatique"
                ],

            "comforting" =>
                [
                    "doux",
                    "douce",
                    "ambre",
                    "ambree"
                ],

            "confident" =>
                [
                    "elegant",
                    "intense",
                    "puissant",
                    "luxueux"
                ],

            "cozy" =>
                [
                    "doux",
                    "douce",
                    "ambre",
                    "ambree",
                    "oriental",
                    "orientale"
                ],

            "dramatic" =>
                [
                    "intense",
                    "oriental",
                    "orientale",
                    "boise",
                    "boisee",
                    "oud"
                ],

            "elegant" =>
                [
                    "elegant",
                    "elegante",
                    "sophistique",
                    "sophistiquee"
                ],

            "energizing" =>
                [
                    "frais",
                    "fraiche",
                    "agrumes",
                    "aromatique"
                ],

            "fresh-invigorating" =>
                [
                    "frais",
                    "fraiche",
                    "agrumes",
                    "aromatique"
                ],

            "luxury" =>
                [
                    "luxueux",
                    "luxueuse",
                    "elegant",
                    "elegante",
                    "sophistique",
                    "sophistiquee"
                ],

            "modern" =>
                [
                    "moderne",
                    "sophistique",
                    "sophistiquee"
                ],

            "addictive" =>
                [
                    "seduisant",
                    "seduisante",
                    "sensuel",
                    "sensuelle",
                    "gourmand",
                    "gourmande"
                ],

            "mysterious" =>
                [
                    "oriental",
                    "orientale",
                    "ambre",
                    "ambree",
                    "boise",
                    "boisee",
                    "oud"
                ],

            "playful" =>
                [
                    "fruite",
                    "fruitee",
                    "gourmand",
                    "gourmande"
                ],

            "powerful" =>
                [
                    "intense",
                    "puissant",
                    "puissante"
                ],

            "relaxing" =>
                [
                    "doux",
                    "douce",
                    "frais",
                    "fraiche"
                ],

            "romantic" =>
                [
                    "floral",
                    "florale",
                    "sensuel",
                    "sensuelle",
                    "seduisant",
                    "seduisante"
                ],

            "seductive" =>
                [
                    "seduisant",
                    "seduisante",
                    "sensuel",
                    "sensuelle"
                ],

            "sensual" =>
                [
                    "sensuel",
                    "sensuelle",
                    "seduisant",
                    "seduisante"
                ],

            "sexy" =>
                [
                    "sensuel",
                    "sensuelle",
                    "seduisant",
                    "seduisante"
                ],

            "sophisticated" =>
                [
                    "sophistique",
                    "sophistiquee",
                    "elegant",
                    "elegante"
                ],

            "timeless" =>
                [
                    "elegant",
                    "elegante",
                    "sophistique",
                    "sophistiquee"
                ],

            "warm" =>
                [
                    "ambre",
                    "ambree",
                    "oriental",
                    "orientale",
                    "chaud",
                    "chaude",
                    "epice",
                    "epicee"
                ],

            "youthful" =>
                [
                    "fruite",
                    "fruitee",
                    "frais",
                    "fraiche"
                ],

            _ =>
                [mood]
        };
    }

    // ================================================================
    // HELPERS
    // ================================================================

    private static List<string> GetStringList(
        JsonElement json,
        string property)
    {
        if (json.ValueKind != JsonValueKind.Object)
            return [];

        if (!json.TryGetProperty(
            property,
            out var value))
        {
            return [];
        }

        if (value.ValueKind != JsonValueKind.Array)
            return [];

        var result = new List<string>();

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

    private static string? GetString(
        JsonElement json,
        string property)
    {
        if (json.ValueKind != JsonValueKind.Object)
            return null;

        if (!json.TryGetProperty(
            property,
            out var value))
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static bool ContainsAny(
        IEnumerable<string> values,
        string search)
    {
        if (string.IsNullOrWhiteSpace(search))
            return false;

        var normalizedSearch =
            Normalize(search);

        foreach (var value in values)
        {
            var normalizedValue =
                Normalize(value);

            if (string.IsNullOrWhiteSpace(normalizedValue))
                continue;

            if (normalizedValue == normalizedSearch ||
                normalizedValue.Contains(normalizedSearch) ||
                normalizedSearch.Contains(normalizedValue))
            {
                return true;
            }
        }

        return false;
    }

    private static List<string> CleanList(
        IEnumerable<string>? values)
    {
        if (values == null)
            return [];

        return values
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string Normalize(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        var normalized =
            value.Normalize(
                NormalizationForm.FormD);

        var sb = new StringBuilder();

        foreach (var c in normalized)
        {
            var category =
                CharUnicodeInfo.GetUnicodeCategory(c);

            if (category !=
                UnicodeCategory.NonSpacingMark)
            {
                sb.Append(c);
            }
        }

        return sb
            .ToString()
            .Normalize(NormalizationForm.FormC)
            .ToLowerInvariant()
            .Trim()
            .Replace(" ", "-");
    }
}


// ====================================================================
// REQUEST
// ====================================================================

public class PerfumeMatchRequest
{
    public string? Whom { get; set; }

    public string? Occasion { get; set; }

    public List<string> Occasions { get; set; } = [];

    public List<string> Moods { get; set; } = [];

    public List<string> Styles { get; set; } = [];

    public List<string> Notes { get; set; } = [];

    public List<string> Families { get; set; } = [];
}


// ====================================================================
// PRODUCT
// ====================================================================

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


// ====================================================================
// RESULT
// ====================================================================

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

    public List<string> MatchedTopNotes { get; set; } = [];

    public List<string> MatchedHeartNotes { get; set; } = [];

    public List<string> MatchedBaseNotes { get; set; } = [];

    public List<string> MatchedStyles { get; set; } = [];

    public List<string> MatchedMoods { get; set; } = [];

    public List<string> MatchedOccasions { get; set; } = [];

    public List<string> MatchedFamilies { get; set; } = [];
}