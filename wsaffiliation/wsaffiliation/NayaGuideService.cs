
using System.Net.Http.Headers;
using System.Text.Json;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Globalization;
using System.Text;


public sealed class BrandGuideInput
{
    public string Brand { get; set; } = "";
    public string Slug { get; set; } = "";
    public string Language { get; set; } = "fr";
    public string? Title { get; set; }
    public string? MetaDescription { get; set; }
    public JsonElement ContentJson { get; set; }
    public string ResearchStatus { get; set; } = "pending";
    public string PublicationStatus { get; set; } = "draft";
}

public sealed class PerfumeInput
{
    public string PerfumeKey { get; set; } = "";
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
    public string? CollectionName { get; set; }
    public string? Concentration { get; set; }
    public string? Gender { get; set; }
    public JsonElement EditorialJson { get; set; }
}

public sealed class ProductMatchInput
{
    public long ProductId { get; set; }
    public string Status { get; set; } = "needs_review";
    public string Reason { get; set; } = "";
}



public class NayaGuideService
{
    private readonly string? supabaseUrl =
        Environment.GetEnvironmentVariable("SUPABASE_URL");

    private readonly string? supabaseKey =
        Environment.GetEnvironmentVariable("SUPABASE_KEY");

    private readonly HttpClient httpClient;

    public NayaGuideService()
    {
        if (string.IsNullOrWhiteSpace(supabaseUrl) ||
            string.IsNullOrWhiteSpace(supabaseKey))
        {
            throw new InvalidOperationException(
                "SUPABASE_URL ou SUPABASE_KEY est manquant.");
        }

        httpClient = new HttpClient
        {
            BaseAddress = new Uri(
                supabaseUrl.TrimEnd('/') + "/rest/v1/"),
            Timeout = TimeSpan.FromSeconds(30)
        };

        httpClient.DefaultRequestHeaders.Add(
            "apikey", supabaseKey);

        httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(
                "Bearer", supabaseKey);
    }

    public async Task<JsonDocument> GetBrandProductsAsync(
        string brand,
        CancellationToken cancellationToken = default)
    {
        var encodedBrand = Uri.EscapeDataString(
            $"ilike.{brand}");

        var url =
            $"products?select=id,brand,name,short_name," +
            $"category,sub_category,type,image,description," +
            $"product_data,naya_attributes" +
            $"&brand={encodedBrand}" +
            $"&category=eq.Parfum" +
            $"&order=name.asc";

        using var response = await httpClient.GetAsync(
            url, cancellationToken);

        var body = await response.Content.ReadAsStringAsync(
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Supabase a retourné {(int)response.StatusCode}: {body}");
        }

        return JsonDocument.Parse(body);
    }


    public async Task<long> SaveBrandGuideAsync(
        JsonElement guide,
        CancellationToken cancellationToken = default)
    {
        var brand = guide.GetProperty("brand");
        var seo = guide.GetProperty("seo");

        var input = new BrandGuideInput
        {
            Brand = brand.GetProperty("name").GetString()
                    ?? throw new InvalidOperationException(
                        "Le nom de la marque est absent."),
            Slug = seo.GetProperty("slug").GetString()
                    ?? throw new InvalidOperationException(
                        "Le slug SEO est absent."),
            Language = guide.TryGetProperty("metadata", out var metadata)
                       && metadata.TryGetProperty("language", out var lang)
                ? lang.GetString() ?? "fr"
                : "fr",
            Title = seo.TryGetProperty("title", out var title)
                ? title.GetString()
                : null,
            MetaDescription = seo.TryGetProperty(
                "meta_description", out var description)
                ? description.GetString()
                : null,
            ContentJson = guide.Clone(),
            ResearchStatus = "completed",
            PublicationStatus = "draft"
        };

        var payload = new
        {
            brand = input.Brand,
            slug = input.Slug,
            language = input.Language,
            title = input.Title,
            meta_description = input.MetaDescription,
            content_json = input.ContentJson,
            research_status = input.ResearchStatus,
            publication_status = input.PublicationStatus,
            updated_at = DateTimeOffset.UtcNow
        };

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "brand_guides?on_conflict=brand");

        request.Headers.TryAddWithoutValidation(
            "Prefer", "resolution=merge-duplicates,return=representation");

        request.Content = JsonContent.Create(new[] { payload });

        using var response = await httpClient.SendAsync(
            request, cancellationToken);

        var body = await response.Content.ReadAsStringAsync(
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Erreur sauvegarde brand_guides: " +
                $"{(int)response.StatusCode} - {body}");
        }

        using var result = JsonDocument.Parse(body);

        if (result.RootElement.GetArrayLength() == 0)
            throw new InvalidOperationException(
                "Supabase n'a retourné aucun guide.");

        return result.RootElement[0]
            .GetProperty("id").GetInt64();
    }



    public async Task<int> SavePerfumesAsync(
        long brandGuideId,
        JsonElement guide,
        CancellationToken cancellationToken = default)
    {
        if (!guide.TryGetProperty("perfumes", out var perfumes)
            || perfumes.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException(
                "Le JSON doit contenir un tableau perfumes.");
        }

        var rows = new List<Dictionary<string, object?>>();

        foreach (var perfume in perfumes.EnumerateArray())
        {
            var key = GetString(perfume, "id");
            var name = GetString(perfume, "name");

            if (string.IsNullOrWhiteSpace(key) ||
                string.IsNullOrWhiteSpace(name))
            {
                throw new InvalidOperationException(
                    "Chaque parfum doit avoir un id et un name.");
            }

            var slug = GetString(perfume, "slug") ?? key;

            var editorial = perfume.TryGetProperty(
                "editorial", out var editorialElement)
                ? editorialElement.Clone()
                : perfume.Clone();

            rows.Add(new Dictionary<string, object?>
            {
                ["brand_guide_id"] = brandGuideId,
                ["perfume_key"] = key,
                ["name"] = name,
                ["slug"] = slug,
                ["collection_name"] =
                    GetString(perfume, "collection_name"),
                ["concentration"] =
                    GetString(perfume, "concentration"),
                ["gender"] = GetString(perfume, "gender"),
                ["editorial_json"] = editorial,
                ["verification_status"] =
                    GetVerificationStatus(perfume)
            });
        }

        if (rows.Count == 0)
            return 0;

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "perfumes?on_conflict=brand_guide_id,perfume_key");

        request.Headers.TryAddWithoutValidation(
            "Prefer", "resolution=merge-duplicates,return=representation");

        request.Content = JsonContent.Create(rows);

        using var response = await httpClient.SendAsync(
            request, cancellationToken);

        var body = await response.Content.ReadAsStringAsync(
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Erreur sauvegarde perfumes: " +
                $"{(int)response.StatusCode} - {body}");
        }

        using var result = JsonDocument.Parse(body);
        return result.RootElement.GetArrayLength();
    }

    private static string? GetString(
        JsonElement element,
        string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }

    private static string GetVerificationStatus(JsonElement perfume)
    {
        if (perfume.TryGetProperty(
            "verification", out var verification)
            && GetString(verification, "status") is string status)
        {
            return status;
        }

        if (perfume.TryGetProperty(
            "verification_status", out var directStatus)
            && directStatus.ValueKind == JsonValueKind.String)
        {
            return directStatus.GetString() ?? "needs_review";
        }

        return "needs_review";
    }



    public async Task<int> MatchAffiliateProductsAsync(
        long perfumeId,
        string brand,
        string perfumeName,
        string? concentration = null,
        CancellationToken cancellationToken = default)
    {
        var brandFilter = Uri.EscapeDataString($"ilike.{brand}");

        var url =
            $"products?select=id,brand,name,short_name,type," +
            $"product_data,naya_attributes" +
            $"&brand={brandFilter}" +
            $"&category=eq.Parfum";

        using var response = await httpClient.GetAsync(
            url, cancellationToken);

        var body = await response.Content.ReadAsStringAsync(
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Erreur lecture products: " +
                $"{(int)response.StatusCode} - {body}");
        }

        using var document = JsonDocument.Parse(body);

        var candidates = new List<(long Id, string Name)>();
        var target = NormalizePerfumeName(perfumeName);

        foreach (var product in document.RootElement.EnumerateArray())
        {
            var name = GetString(product, "name") ?? "";
            var shortName = GetString(product, "short_name") ?? "";

            var productData = product.TryGetProperty(
                "product_data", out var pd)
                ? pd
                : default;

            var attributes = product.TryGetProperty(
                "naya_attributes", out var na)
                ? na
                : default;

            var productConcentration =
                GetString(attributes, "concentration")
                ?? GetString(product, "type")
                ?? GetString(productData, "nature");

            // Si la concentration est connue des deux côtés,
            // elle doit être compatible.
            if (!string.IsNullOrWhiteSpace(concentration)
                && !string.IsNullOrWhiteSpace(productConcentration)
                && !ConcentrationsCompatible(
                    concentration, productConcentration))
            {
                continue;
            }

            var normalizedName = NormalizePerfumeName(name);
            var normalizedShortName = NormalizePerfumeName(shortName);

            if (normalizedName == target ||
                normalizedShortName == target)
            {
                candidates.Add((
                    product.GetProperty("id").GetInt64(),
                    name));
            }
        }

        if (candidates.Count == 0)
            return 0;

        // Une correspondance unique peut être proposée comme
        // correspondance candidate ; une validation reste nécessaire.
        var status = candidates.Count == 1
            ? "needs_review"
            : "needs_review";

        var rows = candidates.Select(candidate =>
            new Dictionary<string, object?>
            {
                ["perfume_id"] = perfumeId,
                ["product_id"] = candidate.Id,
                ["match_status"] = status,
                ["match_reason"] =
                    $"Correspondance de nom avec '{candidate.Name}'. " +
                    "Vérifier la version et la concentration."
            }).ToList();

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "perfume_product_matches?on_conflict=perfume_id,product_id");

        request.Headers.TryAddWithoutValidation(
            "Prefer", "resolution=merge-duplicates,return=minimal");

        request.Content = JsonContent.Create(rows);

        using var saveResponse = await httpClient.SendAsync(
            request, cancellationToken);

        var saveBody = await saveResponse.Content.ReadAsStringAsync(
            cancellationToken);

        if (!saveResponse.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Erreur sauvegarde correspondances: " +
                $"{(int)saveResponse.StatusCode} - {saveBody}");
        }

        return candidates.Count;
    }

    private static string NormalizePerfumeName(string value)
    {
        var normalized = value
            .Normalize(NormalizationForm.FormD);

        var builder = new StringBuilder();

        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c)
                != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(c);
            }
        }

        var result = builder.ToString()
            .ToLowerInvariant();

        result = Regex.Replace(
            result,
            @"\b(eau de parfum|eau de toilette|eau de cologne|" +
            @"parfum|edp|edt|edc|extrait de parfum|elixir)\b",
            " ");

        result = Regex.Replace(result, @"[^a-z0-9]+", " ");
        return Regex.Replace(result, @"\s+", " ").Trim();
    }

    private static bool ConcentrationsCompatible(
        string requested,
        string available)
    {
        string NormalizeConcentration(string value)
        {
            var text = value.ToLowerInvariant();

            if (text.Contains("eau de toilette") ||
                Regex.IsMatch(text, @"\bedt\b"))
                return "edt";

            if (text.Contains("eau de parfum") ||
                Regex.IsMatch(text, @"\bedp\b"))
                return "edp";

            if (text.Contains("eau de cologne") ||
                Regex.IsMatch(text, @"\bedc\b"))
                return "edc";

            if (text.Contains("extrait"))
                return "extrait";

            if (Regex.IsMatch(text, @"\bparfum\b"))
                return "parfum";

            return text.Trim();
        }

        return NormalizeConcentration(requested) ==
               NormalizeConcentration(available);
    }




}
