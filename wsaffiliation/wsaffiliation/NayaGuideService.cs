
using System.Net.Http.Headers;
using System.Text.Json;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Globalization;
using System.Text;

using System.Text.Json.Nodes;



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

public sealed class PerfumeNoteRecord1
{
    [JsonPropertyName("name_fr")]
    public string NameFr { get; set; } = "";

    [JsonPropertyName("name_en")]
    public string NameEn { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("image_url")]
    public string? ImageUrl { get; set; }

    [JsonPropertyName("is_active")]
    public bool IsActive { get; set; }
}


public class NayaGuideService
{
    private readonly string? supabaseUrl =
        Environment.GetEnvironmentVariable("SUPABASE_URL");

    private readonly string? supabaseKey =
        Environment.GetEnvironmentVariable("SUPABASE_KEY");

    private readonly string openAiKey =
        Environment.GetEnvironmentVariable("OPENAI_API")
        ?? throw new InvalidOperationException(
            "OPENAI_API_KEY est manquante.");

    private readonly string openAiModel = "gpt-4o-mini";


    private readonly HttpClient openAiHttpClient = new()
    {
        BaseAddress = new Uri("https://api.openai.com/v1/"),
        Timeout = TimeSpan.FromMinutes(5)
    };


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

        openAiHttpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", openAiKey);

    }



    public async Task<JsonDocument> GetBrandProductsAsync(
        string brand,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(brand))
        {
            throw new ArgumentException(
                "La marque est obligatoire.",
                nameof(brand));
        }

        var encodedBrand = Uri.EscapeDataString(
            $"ilike.{brand.Trim()}");

        var url =
                "products?select=id,brand,name,short_name," +
                "category,sub_category,type,image,description," +
                "product_data,naya_attributes,slug,slug_normalized," +
                "product_offers(product_url)" +
                $"&brand={encodedBrand}" +
                "&order=name.asc";

        using var response = await httpClient.GetAsync(
            url,
            cancellationToken);

        var body = await response.Content.ReadAsStringAsync(
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Supabase a retourné {(int)response.StatusCode}: {body}");
        }

        return JsonDocument.Parse(body);
    }



    public async Task<string?> GetPerfumeImageAsync(
    string brand,
    string perfumeName,
    CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(brand) ||
            string.IsNullOrWhiteSpace(perfumeName))
        {
            return null;
        }

        // Recherche par nom contenant le nom du parfum.
        var encodedName = Uri.EscapeDataString(
            $"ilike.%{perfumeName.Trim()}%");

        var encodedBrand = Uri.EscapeDataString(
            $"ilike.{brand.Trim()}");

        var url =
            $"products?select=name,image,brand" +
            $"&brand={encodedBrand}" +
            $"&category=eq.Parfum" +
            $"&name={encodedName}" +
            $"&order=name.asc" +
            $"&limit=20";

        using var response = await httpClient.GetAsync(
            url, cancellationToken);

        var body = await response.Content.ReadAsStringAsync(
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            Console.WriteLine(
                $"[NAYA IMAGE SEARCH ERROR] {perfumeName}: " +
                $"{(int)response.StatusCode} - {body}");

            return null;
        }

        using var document = JsonDocument.Parse(body);

        if (document.RootElement.ValueKind != JsonValueKind.Array)
            return null;

        var products = document.RootElement
            .EnumerateArray()
            .Select(p => new
            {
                Name = GetString(p, "name")?.Trim(),
                Image = GetString(p, "image")?.Trim()
            })
            .Where(p =>
                !string.IsNullOrWhiteSpace(p.Name) &&
                !string.IsNullOrWhiteSpace(p.Image))
            .ToList();

        if (products.Count == 0)
        {
            Console.WriteLine(
                $"[NAYA IMAGE NOT FOUND] {perfumeName}");
            Console.WriteLine(
                $"[encodedBrand] {encodedBrand}");
            Console.WriteLine(
                $"[encodedName] {encodedName}");

            return null;
        }
        else
        {
            Console.WriteLine(
                $"[NAYA IMAGE FOUND] {perfumeName}");

        }

        // 1. Correspondance exacte, sans tenir compte de la casse.
        var exact = products.FirstOrDefault(p =>
            string.Equals(
                p.Name,
                perfumeName.Trim(),
                StringComparison.OrdinalIgnoreCase));

        if (exact != null)
        {
            Console.WriteLine(
                $"[NAYA IMAGE EXACT] {perfumeName} -> {exact.Name}");

            return exact.Image;
        }

        // 2. Correspondance partielle prudente.
        // Évite de prendre automatiquement l'image d'un flanker
        // lorsque le nom recherché est plus court.
        var normalizedSearch = NormalizeResearchKey(perfumeName);

        var partial = products.FirstOrDefault(p =>
            NormalizeResearchKey(p.Name!) == normalizedSearch);

        if (partial != null)
        {
            Console.WriteLine(
                $"[NAYA IMAGE NORMALIZED] {perfumeName} -> {partial.Name}");

            return partial.Image;
        }

        Console.WriteLine(
            $"[NAYA IMAGE AMBIGUOUS] {perfumeName}: " +
            $"{products.Count} résultats, aucun nom suffisamment proche.");

        return null;
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
        var keys = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        var slugs = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);
        var productIds = new HashSet<long>();

        foreach (var perfume in perfumes.EnumerateArray())
        {
            var key = GetString(perfume, "id");
            var name = GetString(perfume, "name");
            var slug = GetString(perfume, "slug");

            if (string.IsNullOrWhiteSpace(key) ||
                string.IsNullOrWhiteSpace(name) ||
                string.IsNullOrWhiteSpace(slug))
            {
                throw new InvalidOperationException(
                    "Chaque parfum doit avoir un id, un name et un slug.");
            }

            if (!perfume.TryGetProperty(
                    "product_id", out var productIdElement)
                || productIdElement.ValueKind != JsonValueKind.Number
                || !productIdElement.TryGetInt64(out var productId)
                || productId <= 0)
            {
                throw new InvalidOperationException(
                    $"Le parfum '{name}' doit avoir un product_id valide.");
            }

            if (!keys.Add(key))
            {
                throw new InvalidOperationException(
                    $"Identifiant de parfum en double : {key}");
            }

            if (!slugs.Add(slug))
            {
                throw new InvalidOperationException(
                    $"Slug de parfum en double : {slug}");
            }

            if (!productIds.Add(productId))
            {
                throw new InvalidOperationException(
                    $"product_id en double : {productId}");
            }

            var editorial = perfume.TryGetProperty(
                "editorial", out var editorialElement)
                ? editorialElement.Clone()
                : perfume.Clone();

            rows.Add(new Dictionary<string, object?>
            {
                ["brand_guide_id"] = brandGuideId,
                ["product_id"] = productId,
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

        // Refuser une liste vide pour éviter une suppression accidentelle.
        if (rows.Count == 0)
        {
            throw new InvalidOperationException(
                "Aucun parfum reçu : synchronisation annulée.");
        }

        // 1. Charger les parfums actuellement enregistrés.
        using var getRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"perfumes?brand_guide_id=eq.{brandGuideId}" +
            "&select=id,perfume_key");

        using var getResponse = await httpClient.SendAsync(
            getRequest, cancellationToken);

        var getBody = await getResponse.Content.ReadAsStringAsync(
            cancellationToken);

        if (!getResponse.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Erreur lecture perfumes : " +
                $"{(int)getResponse.StatusCode} - {getBody}");
        }

        using var existingDocument = JsonDocument.Parse(getBody);

        var existing = existingDocument.RootElement
            .EnumerateArray()
            .Select(p => new
            {
                Id = p.GetProperty("id").GetInt64(),
                Key = GetString(p, "perfume_key") ?? ""
            })
            .ToList();

        var incomingKeys = new HashSet<string>(
            rows.Select(r => (string)r["perfume_key"]!),
            StringComparer.OrdinalIgnoreCase);

        // 2. Insérer ou mettre à jour les parfums.
        using var upsertRequest = new HttpRequestMessage(
            HttpMethod.Post,
            "perfumes?on_conflict=brand_guide_id,perfume_key");

        upsertRequest.Headers.TryAddWithoutValidation(
            "Prefer",
            "resolution=merge-duplicates,return=representation");

        upsertRequest.Content = JsonContent.Create(rows);

        using var upsertResponse = await httpClient.SendAsync(
            upsertRequest, cancellationToken);

        var upsertBody = await upsertResponse.Content.ReadAsStringAsync(
            cancellationToken);

        if (!upsertResponse.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Erreur sauvegarde perfumes : " +
                $"{(int)upsertResponse.StatusCode} - {upsertBody}");
        }

        // 3. Supprimer uniquement les références obsolètes de ce guide.
        foreach (var oldPerfume in existing.Where(
            p => !incomingKeys.Contains(p.Key)))
        {
            var deleteUrl =
                $"perfumes?id=eq.{oldPerfume.Id}" +
                $"&brand_guide_id=eq.{brandGuideId}";

            using var deleteRequest = new HttpRequestMessage(
                HttpMethod.Delete, deleteUrl);

            using var deleteResponse = await httpClient.SendAsync(
                deleteRequest, cancellationToken);

            var deleteBody = await deleteResponse.Content.ReadAsStringAsync(
                cancellationToken);

            if (!deleteResponse.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    $"Erreur suppression du parfum {oldPerfume.Key} : " +
                    $"{(int)deleteResponse.StatusCode} - {deleteBody}");
            }
        }

        using var result = JsonDocument.Parse(upsertBody);

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


    public async Task<JsonDocument> GenerateGuideJsonAsync(
     string brandName,
     CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(brandName))
            throw new ArgumentException("La marque est obligatoire.");


        var perfumeNotes = await GetPerfumeNotesAsync(
                cancellationToken);


        // =========================================================
        // 1. CHARGER LES PRODUITS DEPUIS SUPABASE
        // =========================================================
        using var productsDocument =
            await GetBrandProductsAsync(brandName, cancellationToken);

        var dbProducts = productsDocument.RootElement
            .EnumerateArray()
            .ToList();

        if (dbProducts.Count == 0)
        {
            throw new InvalidOperationException(
                $"Aucun parfum trouvé dans public.products pour '{brandName}'.");
        }

        Console.WriteLine(
            $"[NAYA SUPABASE PRODUCTS] {brandName} : {dbProducts.Count}");

        // Vérifier les identifiants et les noms avant de générer le guide.
        foreach (var product in dbProducts)
        {
            if (!product.TryGetProperty("id", out var idElement) ||
                idElement.ValueKind != JsonValueKind.Number ||
                string.IsNullOrWhiteSpace(GetString(product, "name")))
            {
                throw new InvalidOperationException(
                    "Un produit Supabase ne possède pas d'id numérique ou de nom.");
            }
        }

        // =========================================================
        // 2. DEMANDER À OPENAI UNIQUEMENT L'HISTOIRE DE LA MARQUE
        // =========================================================
        var instructions = """
                    Tu es le rédacteur en chef de NAYA, un guide indépendant de parfumerie.

                    Rédige en français un contenu éditorial élégant, précis et original
                    sur l'histoire et l'identité de la marque demandée.

                    Tu dois retourner uniquement un objet JSON avec ces deux propriétés :
                    - seo
                    - brand_story

                    La propriété seo contient :
                    title, meta_description, slug, introduction.

                    La propriété brand_story contient :
                    title, history, identity, olfactory_signature,
                    editorial_conclusion, sources.

                    history doit être un tableau de paragraphes ou d'étapes historiques.
                    sources doit être un tableau de sources réellement connues et vérifiables.

                    N'inclus aucun tableau de parfums.
                    N'inclus aucune collection.
                    Ne génère aucun nom de produit, product_id, image_url ou slug de parfum.
                    Ne crée pas de références commerciales.

                    N'invente pas de dates, de citations ou de faits historiques.
                    Si une information est incertaine, formule-la prudemment.
                    Ne prétends pas avoir effectué une recherche Web si aucun outil de recherche
                    n'est disponible dans cette requête.

                    Retourne uniquement du JSON valide, sans Markdown.
                    """;


        var userPrompt = $$$"""
                Rédige le contenu éditorial général du guide NAYA pour la marque :
                {{{brandName}}}

                Retourne exactement cette structure :

                {{
                  "seo": {{
                    "title": "",
                    "meta_description": "",
                    "slug": "",
                    "introduction": ""
                  }},
                  "brand_story": {{
                    "title": "",
                    "history": [],
                    "identity": "",
                    "olfactory_signature": "",
                    "editorial_conclusion": "",
                    "sources": []
                  }}
                }}

                Ne retourne aucune autre propriété.

                IMPORTANT :
                - Utilise le vrai nom de la marque : {{{brandName}}}.
                - Ne retourne jamais le texte littéral "{brandName}".
                - Remplace toute référence à la variable par le nom réel de la marque
                  dans les titres et les textes lorsque cela est pertinent.
                """;


        var requestBody = new
        {
            model = openAiModel,
            instructions,
            input = userPrompt,
            max_output_tokens = 5000
        };

        using var request = new HttpRequestMessage(
            HttpMethod.Post, "responses");

        request.Content = JsonContent.Create(requestBody);

        using var response = await openAiHttpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        var responseBody = await response.Content.ReadAsStringAsync(
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Erreur OpenAI {(int)response.StatusCode}: {responseBody}");
        }

        using var apiResponse = JsonDocument.Parse(responseBody);
        var responseRoot = apiResponse.RootElement;

        var responseStatus = GetString(responseRoot, "status");

        Console.WriteLine($"[NAYA OPENAI STATUS] {responseStatus}");

        if (responseStatus == "incomplete")
        {
            var details = responseRoot.TryGetProperty(
                "incomplete_details", out var incompleteDetails)
                ? incompleteDetails.GetRawText()
                : "Aucun détail disponible.";

            throw new InvalidOperationException(
                $"La réponse OpenAI est incomplète : {details}");
        }

        var outputTexts = new List<string>();

        if (responseRoot.TryGetProperty("output", out var output))
        {
            foreach (var item in output.EnumerateArray())
            {
                if (!item.TryGetProperty("content", out var content))
                    continue;

                foreach (var block in content.EnumerateArray())
                {
                    if (GetString(block, "type") == "output_text" &&
                        GetString(block, "text") is string text)
                    {
                        outputTexts.Add(text);
                    }
                }
            }
        }

        if (outputTexts.Count != 1 ||
            string.IsNullOrWhiteSpace(outputTexts[0]))
        {
            throw new InvalidOperationException(
                $"OpenAI a retourné {outputTexts.Count} blocs de texte exploitables.");
        }

        var cleanJson = ExtractJson(outputTexts[0]);

        using var editorialDocument = JsonDocument.Parse(cleanJson);
        var editorialRoot = editorialDocument.RootElement;

        if (!editorialRoot.TryGetProperty("seo", out var seoElement) ||
            seoElement.ValueKind != JsonValueKind.Object ||
            !editorialRoot.TryGetProperty("brand_story", out var storyElement) ||
            storyElement.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException(
                "Le JSON OpenAI doit contenir seo et brand_story.");
        }

        // =========================================================
        // 3. PRÉPARER LES COLLECTIONS DEPUIS sub_category
        // =========================================================
        static string MakeSlug(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "reference";

            var slug = value.ToLowerInvariant();

            slug = Regex.Replace(slug, @"[^a-z0-9]+", "-");
            slug = slug.Trim('-');

            return string.IsNullOrWhiteSpace(slug)
                ? "reference"
                : slug;
        }

        var collections = new JsonArray();
        var collectionIds = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);

        var usedCollectionSlugs = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var product in dbProducts)
        {
            var collectionName = GetString(product, "sub_category")?.Trim();

            if (string.IsNullOrWhiteSpace(collectionName))
                continue;

            if (collectionIds.ContainsKey(collectionName))
                continue;

            var baseSlug = MakeSlug(collectionName);
            var slug = baseSlug;
            var suffix = 2;

            while (!usedCollectionSlugs.Add(slug))
                slug = $"{baseSlug}-{suffix++}";

            collectionIds[collectionName] = slug;

            collections.Add(new JsonObject
            {
                ["id"] = slug,
                ["name"] = collectionName,
                ["description"] = "",
                ["perfume_ids"] = new JsonArray(),
                ["sources"] = new JsonArray(),
                ["verification_status"] = "needs_review"
            });
        }

        // =========================================================
        // 4. CONSTRUIRE TOUS LES PARFUMS EXCLUSIVEMENT DEPUIS products
        // =========================================================
        var perfumes = new JsonArray();
        var usedPerfumeSlugs = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var product in dbProducts)
        {
            var productId = product.GetProperty("id").GetInt64();
            var name = GetString(product, "name")!.Trim();

            var brand = GetString(product, "brand") ?? brandName;
            var shortName = GetString(product, "short_name");
            Console.WriteLine(
                    $"[description before] {GetString(product, "description")}");
            var description = await SummarizeDescriptionAsync(
                        GetString(product, "description") ?? "",
                        cancellationToken);
            Console.WriteLine(
                    $"[description] {description}");
            var image = GetString(product, "image");



            string? product_url = null;

            if (product.TryGetProperty("product_offers", out var offers)
                && offers.ValueKind == JsonValueKind.Array)
            {
                foreach (var offer in offers.EnumerateArray())
                {
                    product_url = GetString(offer, "product_url");

                    if (!string.IsNullOrWhiteSpace(product_url))
                        break;
                }
            }

            Console.WriteLine($"[product_url] {product_url}");


            var category = GetString(product, "category");
            var subCategory = GetString(product, "sub_category");
            var type = GetString(product, "type");

            var attributes = product.TryGetProperty(
                "naya_attributes", out var attributesElement)
                ? attributesElement
                : default;

            var productData = product.TryGetProperty(
                "product_data", out var productDataElement)
                ? productDataElement
                : default;

            var concentration =
                GetString(attributes, "concentration") ?? type;

            var gender = GetString(attributes, "gender");

            var sourceSlug =
                GetString(product, "slug_normalized")
                ?? GetString(product, "slug");

            var baseSlug = MakeSlug(
                string.IsNullOrWhiteSpace(sourceSlug) ? name : sourceSlug);

            // Un slug unique, sans modifier l'identifiant source.
            var perfumeSlug = baseSlug;
            var suffix = 2;

            while (!usedPerfumeSlugs.Add(perfumeSlug))
                perfumeSlug = $"{baseSlug}-{suffix++}";

            // Les notes ne sont reprises que si product_data.notes
            // contient déjà un objet JSON avec cette structure.
            // Images des notes récupérées depuis Supabase

            var attrib = product.GetProperty("naya_attributes");

            var top_notes_img = FindNoteImage(
                attrib,
                "top_notes",
                "tete",
                perfumeNotes);

            var heart_notes_img = FindNoteImage(
                attrib,
                "heart_notes",
                "coeur",
                perfumeNotes);

            var base_notes_img = FindNoteImage(
                attrib,
                "base_notes",
                "fond",
                perfumeNotes);

            // Récupérer les notes existantes depuis product_data.notes
            var notes = new JsonObject();

            if (productData.ValueKind == JsonValueKind.Object &&
                productData.TryGetProperty("notes", out var notesElement) &&
                notesElement.ValueKind == JsonValueKind.Object)
            {
                if (JsonNode.Parse(notesElement.GetRawText())
                    is JsonObject existingNotes)
                {
                    notes = existingNotes;
                }
            }

            // Ajouter les images aux trois familles de notes.
            notes["top_image"] = top_notes_img;
            notes["heart_image"] = heart_notes_img;
            notes["base_image"] = base_notes_img;


            Console.WriteLine(
            $"[NOTE-NOTE] {notes}");

            var editorial = new JsonObject
            {
                ["title"] = name,
                ["introduction"] = description ?? "",
                ["description"] = description ?? "",
                ["olfactory_family"] = new JsonArray(),
                ["character"] = new JsonArray(),
                ["accords"] = new JsonArray(),
                ["notes"] = notes,
                ["olfactory_explanation"] = "",
                ["recommendation"] = "",
                ["occasions"] = new JsonArray(),
                ["seasons"] = new JsonArray(),
                ["sources"] = new JsonArray()
            };

            var perfume = new JsonObject
            {
                // SavePerfumesAsync utilise id comme perfume_key.
                ["id"] = $"product-{productId}",

                // Identifiant réel de public.products.
                ["product_id"] = productId,

                ["brand"] = brand,
                ["name"] = name,
                ["short_name"] = shortName,
                ["slug"] = perfumeSlug,
                ["image_url"] = string.IsNullOrWhiteSpace(image)
                    ? null
                    : JsonValue.Create(image),

                ["product_url"] = product_url,
                    
                ["category"] = category,
                ["collection_name"] = subCategory,
                ["concentration"] = concentration,
                ["gender"] = gender,
                ["editorial"] = editorial,
                ["verification"] = new JsonObject
                {
                    ["status"] = "needs_review",
                    ["verified_fields"] = new JsonArray
                {
                    "product_id",
                    "name"
                },
                    ["unverified_fields"] = new JsonArray(),
                    ["sources"] = new JsonArray()
                }
            };

            perfumes.Add(perfume);

            // Remplir perfume_ids dans la collection correspondante.
            if (!string.IsNullOrWhiteSpace(subCategory) &&
                collectionIds.TryGetValue(subCategory, out var collectionId))
            {
                foreach (var collectionNode in collections)
                {
                    if (collectionNode is not JsonObject collection)
                        continue;

                    if (GetString(
                        JsonDocument.Parse(collection.ToJsonString()).RootElement,
                        "id") != collectionId)
                    {
                        continue;
                    }

                    if (collection["perfume_ids"] is JsonArray perfumeIds)
                        perfumeIds.Add($"product-{productId}");

                    break;
                }
            }
        }

        if (perfumes.Count == 0)
        {
            throw new InvalidOperationException(
                "Aucun parfum exploitable : le guide ne sera pas sauvegardé.");
        }

        // =========================================================
        // 5. ASSEMBLER LE JSON FINAL DU GUIDE
        // =========================================================
        var seo = JsonNode.Parse(seoElement.GetRawText()) as JsonObject
            ?? new JsonObject();

        var brandStory = JsonNode.Parse(storyElement.GetRawText()) as JsonObject
            ?? new JsonObject();

        var brandSlug = MakeSlug(brandName);

        // Garantir les champs SEO indispensables.
        seo["title"] ??= $"Guide des parfums {brandName} | NAYA";
        seo["meta_description"] ??= $"Découvrez l'univers olfactif de {brandName}.";
        seo["slug"] ??= $"guide-parfums-{brandSlug}";
        seo["introduction"] ??= "";

        var collectionNames = collections
            .OfType<JsonObject>()
            .Select(c => c["name"]?.GetValue<string>())
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => (JsonNode?)JsonValue.Create(n))
            .ToArray();

        var guide = new JsonObject
        {
            ["brand"] = new JsonObject
            {
                ["id"] = brandSlug,
                ["name"] = brandName
            },

            ["metadata"] = new JsonObject
            {
                ["language"] = "fr",
                ["generation_status"] = "draft",
                ["research_status"] = "not_requested",
                ["editorial_status"] = "generated",
                ["coverage_status"] = "needs_review"
            },

            ["seo"] = seo,
            ["brand_story"] = brandStory,

            ["coverage"] = new JsonObject
            {
                ["strategy"] = "supabase_products",
                ["collections_identified"] = new JsonArray(collectionNames),
                ["collections_researched"] = new JsonArray(),
                ["collections_remaining"] = new JsonArray(),
                ["essential_perfume_count"] = perfumes.Count,
                ["catalogue_scope"] = "supabase_products",
                ["is_complete"] = false,
                ["limitations"] = new JsonArray
            {
                "Les parfums proviennent exclusivement de public.products.",
                "Les informations olfactives absentes de la base ne sont pas inventées."
            }
            },

            ["collections"] = collections,
            ["perfumes"] = perfumes,
            ["guide_sections"] = new JsonArray(),
            ["recommendations"] = new JsonArray(),

            ["publication"] = new JsonObject
            {
                ["status"] = "draft",
                ["review_required"] = true,
                ["published_at"] = null
            }
        };

        var finalJson = guide.ToJsonString(
            new JsonSerializerOptions
            {
                WriteIndented = true
            });

        using var finalDocument = JsonDocument.Parse(finalJson);

        ValidateGuideJson(finalDocument.RootElement);

        Console.WriteLine(
            $"[NAYA FINAL COLLECTIONS] {collections.Count}");

        Console.WriteLine(
            $"[NAYA FINAL PERFUMES] {perfumes.Count}");

        return JsonDocument.Parse(finalJson);
    }

    public async Task<string> SummarizeDescriptionAsync(
    string description,
    CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(description))
            return string.Empty;

        var apiKey = openAiKey;

        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException(
                "La clé API OpenAI est manquante.");

        var requestBody = new
        {
            model = "gpt-4.1-mini",
            instructions =
                "Tu es un rédacteur spécialisé en parfumerie. " +
                "Résume la description fournie en français, " +
                "en conservant les informations les plus importantes " +
                "sur le parfum, son caractère et ses notes olfactives. " +
                "Ignore les prix, promotions, produits associés, " +
                "publicités, cookies et contenus de navigation. " +
                "N'invente aucune information. " +
                "Retourne uniquement le résumé, sans titre, " +
                "en 300 caractères maximum.",
            input = description,
            max_output_tokens = 150
        };

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "https://api.openai.com/v1/responses");

        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue(
                "Bearer", apiKey);

        request.Content = JsonContent.Create(requestBody);

        using var response = await httpClient.SendAsync(
            request, cancellationToken);

        var responseBody = await response.Content.ReadAsStringAsync(
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Erreur OpenAI {(int)response.StatusCode} : " +
                responseBody);
        }

        using var document = JsonDocument.Parse(responseBody);

        var outputText = new System.Text.StringBuilder();

        if (document.RootElement.TryGetProperty(
                "output", out var output))
        {
            foreach (var item in output.EnumerateArray())
            {
                if (!item.TryGetProperty("content", out var content))
                    continue;

                foreach (var contentItem in content.EnumerateArray())
                {
                    if (contentItem.TryGetProperty(
                            "type", out var type) &&
                        type.GetString() == "output_text" &&
                        contentItem.TryGetProperty(
                            "text", out var text))
                    {
                        outputText.Append(text.GetString());
                    }
                }
            }
        }

        var summary = outputText.ToString().Trim();

        // Sécurité : garantir une limite stricte de 300 caractères.
        if (summary.Length > 300)
        {
            summary = summary[..300].TrimEnd();

            var lastSpace = summary.LastIndexOf(' ');
            if (lastSpace > 0)
                summary = summary[..lastSpace];
        }

        return summary;
    }


    private static void ValidateGuideJson(JsonElement guide)
    {
        if (guide.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException(
                "Le guide doit être un objet JSON.");
        }

        // 1. Vérifier la marque.
        if (!guide.TryGetProperty("brand", out var brand) ||
            brand.ValueKind != JsonValueKind.Object ||
            string.IsNullOrWhiteSpace(GetString(brand, "name")))
        {
            throw new InvalidOperationException(
                "Le champ brand.name est obligatoire.");
        }

        // 2. Vérifier le SEO.
        if (!guide.TryGetProperty("seo", out var seo) ||
            seo.ValueKind != JsonValueKind.Object ||
            string.IsNullOrWhiteSpace(GetString(seo, "slug")))
        {
            throw new InvalidOperationException(
                "Le champ seo.slug est obligatoire.");
        }

        // 3. Vérifier la liste des parfums.
        if (!guide.TryGetProperty("perfumes", out var perfumes) ||
            perfumes.ValueKind != JsonValueKind.Array ||
            perfumes.GetArrayLength() == 0)
        {
            throw new InvalidOperationException(
                "Le guide doit contenir au moins un parfum.");
        }

        var ids = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        var slugs = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        var productIds = new HashSet<long>();

        foreach (var perfume in perfumes.EnumerateArray())
        {
            if (perfume.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException(
                    "Chaque parfum doit être un objet JSON.");
            }

            var id = GetString(perfume, "id");
            var name = GetString(perfume, "name");
            var slug = GetString(perfume, "slug");

            // 4. Vérifier les champs obligatoires.
            if (string.IsNullOrWhiteSpace(id) ||
                string.IsNullOrWhiteSpace(name) ||
                string.IsNullOrWhiteSpace(slug))
            {
                throw new InvalidOperationException(
                    "Chaque parfum doit avoir id, name et slug.");
            }

            // 5. Vérifier l'unicité de l'identifiant.
            if (!ids.Add(id))
            {
                throw new InvalidOperationException(
                    $"Identifiant de parfum en double : {id}");
            }

            // 6. Vérifier l'unicité du slug.
            if (!slugs.Add(slug))
            {
                throw new InvalidOperationException(
                    $"Slug de parfum en double : {slug}");
            }

            // 7. Vérifier product_id, qui référence products.id.
            if (!perfume.TryGetProperty(
                    "product_id", out var productIdElement) ||
                productIdElement.ValueKind != JsonValueKind.Number ||
                !productIdElement.TryGetInt64(out var productId) ||
                productId <= 0)
            {
                throw new InvalidOperationException(
                    $"Le parfum '{name}' doit avoir un product_id " +
                    "numérique et positif.");
            }

            // 8. Un produit Supabase ne doit apparaître qu'une fois.
            if (!productIds.Add(productId))
            {
                throw new InvalidOperationException(
                    $"product_id en double : {productId}");
            }
        }
    }




    public async Task<int> MatchSavedGuideProductsAsync(
     long guideId,
     string brand,
     CancellationToken cancellationToken = default)
    {
        var url =
            $"perfumes?select=id,product_id" +
            $"&brand_guide_id=eq.{guideId}";

        using var response = await httpClient.GetAsync(
            url, cancellationToken);

        var body = await response.Content.ReadAsStringAsync(
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Erreur lecture des parfums : " +
                $"{(int)response.StatusCode} - {body}");
        }

        using var document = JsonDocument.Parse(body);

        var rows = new List<Dictionary<string, object?>>();

        foreach (var perfume in document.RootElement.EnumerateArray())
        {
            var perfumeId = perfume.GetProperty("id").GetInt64();

            if (!perfume.TryGetProperty("product_id", out var productIdElement)
                || productIdElement.ValueKind != JsonValueKind.Number)
            {
                throw new InvalidOperationException(
                    $"Le parfum {perfumeId} n'a pas de product_id.");
            }

            rows.Add(new Dictionary<string, object?>
            {
                ["perfume_id"] = perfumeId,
                ["product_id"] = productIdElement.GetInt64(),
                ["match_status"] = "needs_review",
                ["match_reason"] =
                    "Association directe avec le produit source Supabase."
            });
        }

        if (rows.Count == 0)
            return 0;

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "perfume_product_matches?on_conflict=perfume_id,product_id");

        request.Headers.TryAddWithoutValidation(
            "Prefer",
            "resolution=merge-duplicates,return=minimal");

        request.Content = JsonContent.Create(rows);

        using var saveResponse = await httpClient.SendAsync(
            request, cancellationToken);

        var saveBody = await saveResponse.Content.ReadAsStringAsync(
            cancellationToken);

        if (!saveResponse.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Erreur sauvegarde correspondances : " +
                $"{(int)saveResponse.StatusCode} - {saveBody}");
        }

        return rows.Count;
    }


    public async Task<object> GenerateAndSaveGuideAsync(
        string brandName,
        CancellationToken cancellationToken = default)
    {
        using var guide = await GenerateGuideJsonAsync(
            brandName, cancellationToken);

        var root = guide.RootElement;



        var perfumes = root.GetProperty("perfumes");

        var collections = perfumes.EnumerateArray()
            .Select(p => GetString(p, "collection_name"))
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        Console.WriteLine(
            $"[NAYA GUIDE VALIDATION] {brandName}: " +
            $"{collections.Count} collections identifiées.");



        var guideId = await SaveBrandGuideAsync(
            root, cancellationToken);

        var perfumeCount = await SavePerfumesAsync(
            guideId, root, cancellationToken);

        var matchedCount = await MatchSavedGuideProductsAsync(
            guideId, brandName, cancellationToken);

        return new
        {
            success = true,
            guideId,
            brand = GetString(root.GetProperty("brand"), "name"),
            perfumeCount,
            affiliateMatches = matchedCount,
            researchStatus = "completed",
            message = "Guide généré et enregistré. Les correspondances affiliées restent à vérifier."
        };
    }


    private static string ExtractJson(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException(
                "La réponse OpenAI est vide.");

        text = text.Trim();

        // Retirer les marqueurs Markdown ```json ... ```
        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewLine = text.IndexOf('\n');

            if (firstNewLine >= 0)
                text = text[(firstNewLine + 1)..];

            var lastFence = text.LastIndexOf(
                "```", StringComparison.Ordinal);

            if (lastFence >= 0)
                text = text[..lastFence];

            text = text.Trim();
        }

        // Tolérer du texte avant ou après l'objet JSON.
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');

        if (start < 0 || end < start)
            throw new InvalidOperationException(
                "Aucun objet JSON trouvé dans la réponse OpenAI.");

        return text[start..(end + 1)];
    }


    public async Task<object?> GetBrandGuideAsync(
        string slug,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(slug))
            throw new ArgumentException("Le slug est obligatoire.");

        // 1. Lire le guide.
        var guideUrl = SupabaseRestUrl(
            "brand_guides?select=id,brand,slug,language,title," +
            "meta_description,content_json,research_status," +
            "publication_status,updated_at" +
            $"&slug=eq.{Uri.EscapeDataString(slug)}&limit=1");

        using var guideDoc = await GetSupabaseJsonAsync(
            guideUrl, cancellationToken);

        var guideArray = guideDoc.RootElement;

        if (guideArray.GetArrayLength() == 0)
            return null;

        var guide = guideArray[0].Clone();
        var guideId = guide.GetProperty("id").GetInt64();

        // 2. Lire les parfums du guide.
        var perfumesUrl = SupabaseRestUrl(
            "perfumes?select=id,perfume_key,name,slug," +
            "collection_name,concentration,gender,editorial_json," +
            "verification_status" +
            $"&brand_guide_id=eq.{guideId}&order=name.asc");

        using var perfumesDoc = await GetSupabaseJsonAsync(
            perfumesUrl, cancellationToken);

        var perfumeRows = perfumesDoc.RootElement
            .EnumerateArray()
            .Select(p => p.Clone())
            .ToList();

        var perfumeIds = perfumeRows
            .Select(p => p.GetProperty("id").GetInt64())
            .ToList();

        // Un guide sans parfum reste lisible.
        if (perfumeIds.Count == 0)
        {
            return new
            {
                guide,
                perfumes = Array.Empty<object>()
            };
        }

        // 3. Lire toutes les associations en une requête.
        var perfumeIdsFilter = string.Join(",", perfumeIds);

        var matchesUrl = SupabaseRestUrl(
            "perfume_product_matches?select=perfume_id,product_id," +
            "match_status,match_reason" +
            $"&perfume_id=in.({perfumeIdsFilter})");

        using var matchesDoc = await GetSupabaseJsonAsync(
            matchesUrl, cancellationToken);

        var matches = matchesDoc.RootElement
            .EnumerateArray()
            .Select(m => m.Clone())
            .ToList();

        var productIds = matches
            .Select(m => m.GetProperty("product_id").GetInt64())
            .Distinct()
            .ToList();

        // 4. Lire les produits réels dans products.
        var productsById = new Dictionary<long, JsonElement>();

        if (productIds.Count > 0)
        {
            var productIdsFilter = string.Join(",", productIds);

            var productsUrl = SupabaseRestUrl(
                "products?select=id,brand,name,short_name,image," +
                "description,product_data,naya_attributes" +
                $"&id=in.({productIdsFilter})");

            using var productsDoc = await GetSupabaseJsonAsync(
                productsUrl, cancellationToken);

            foreach (var product in productsDoc.RootElement.EnumerateArray())
            {
                var id = product.GetProperty("id").GetInt64();
                productsById[id] = product.Clone();
            }
        }

        // 5. Construire chaque fiche avec ses produits affiliés.
        var resultPerfumes = new List<object>();

        foreach (var perfume in perfumeRows)
        {
            var perfumeId = perfume.GetProperty("id").GetInt64();

            var affiliateProducts = new List<object>();

            foreach (var match in matches.Where(m =>
                m.GetProperty("perfume_id").GetInt64() == perfumeId))
            {
                var productId = match.GetProperty("product_id").GetInt64();

                if (!productsById.TryGetValue(productId, out var product))
                    continue;

                var productData = GetObjectProperty(
                    product, "product_data");

                var attributes = GetObjectProperty(
                    product, "naya_attributes");

                var url = ReadString(productData, "url");

                // N'exposer que les URL HTTP(S) valides.
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                    (uri.Scheme != Uri.UriSchemeHttps &&
                     uri.Scheme != Uri.UriSchemeHttp))
                {
                    url = null;
                }

                affiliateProducts.Add(new
                {
                    id = productId,
                    brand = ReadString(product, "brand"),
                    name = ReadString(product, "name"),
                    shortName = ReadString(product, "short_name"),
                    image = ReadString(product, "image"),
                    description = ReadString(product, "description"),
                    url,
                    source = ReadString(productData, "source"),
                    concentration = ReadString(attributes, "concentration"),
                    matchStatus = ReadString(match, "match_status"),
                    matchReason = ReadString(match, "match_reason")
                });
            }

            resultPerfumes.Add(new
            {
                id = perfumeId,
                perfumeKey = ReadString(perfume, "perfume_key"),
                name = ReadString(perfume, "name"),
                slug = ReadString(perfume, "slug"),
                collectionName = ReadString(perfume, "collection_name"),
                concentration = ReadString(perfume, "concentration"),
                gender = ReadString(perfume, "gender"),
                verificationStatus = ReadString(
                    perfume, "verification_status"),
                editorial = GetObjectProperty(
                    perfume, "editorial_json").Clone(),
                affiliateProducts
            });
        }

        return new
        {
            guide,
            perfumes = resultPerfumes
        };
    }

    private string SupabaseRestUrl(string path)
    {
        if (string.IsNullOrWhiteSpace(supabaseUrl))
            throw new InvalidOperationException(
                "SUPABASE_URL est manquante.");

        return $"{supabaseUrl.TrimEnd('/')}/rest/v1/{path}";
    }

    private async Task<JsonDocument> GetSupabaseJsonAsync(
        string url,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(
            url, cancellationToken);

        var body = await response.Content.ReadAsStringAsync(
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Erreur Supabase {(int)response.StatusCode}: {body}");
        }

        return JsonDocument.Parse(body);
    }

    private static JsonElement GetObjectProperty(
        JsonElement element,
        string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(propertyName, out var value) &&
            value.ValueKind == JsonValueKind.Object)
        {
            return value;
        }

        return default;
    }

    private static string? ReadString(
        JsonElement element,
        string propertyName)
    {
        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(propertyName, out var value))
            return null;

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };
    }



    private async Task<string> ResearchBrandCatalogAsync(
        string brandName,
        string existingCatalogJson,
        CancellationToken cancellationToken = default)
    {
        // PHASE 1 : découvrir les collections de la marque.
        var collectionInstructions = """
Tu es un chercheur spécialisé en parfumerie.

Recherche sur le Web les collections de parfums de la marque demandée.
Privilégie les sources officielles de la marque.

Identifie les différentes gammes et collections pertinentes,
y compris les collections pour hommes, femmes et mixtes,
les gammes historiques encore commercialisées et les flankers.

Ne limite pas la recherche aux collections les plus populaires.
N'invente aucune collection.
Chaque URL doit correspondre à une source réellement consultée.

Retourne uniquement un JSON valide :
{
  "brand": "Nom de la marque",
  "collections": [
    {
      "name": "Nom exact de la collection",
      "source_urls": []
    }
  ],
  "limitations": []
}
""";

        var collectionPrompt = $"""
Recherche les collections de parfums de la marque :
{brandName}

Catalogue commercial existant :
{existingCatalogJson}

Utilise ce catalogue comme indice, pas comme limite.
Recherche les autres collections existantes.
Ne confonds pas une collection avec un parfum individuel.
""";

        var collectionsJson = await ExecuteResearchRequestAsync(
            collectionInstructions,
            collectionPrompt,
            8000,
            cancellationToken);

        using var collectionsDocument = JsonDocument.Parse(collectionsJson);

        if (!collectionsDocument.RootElement.TryGetProperty(
                "collections", out var collectionsElement) ||
            collectionsElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException(
                "La recherche n'a pas retourné de tableau collections.");
        }

        var collections = new List<(string Name, string SourceJson)>();
        var knownCollections = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);



        foreach (var collection in collectionsElement.EnumerateArray())
        {

            var collectionName = GetString(collection, "name")?.Trim();

            if (string.Equals(
                collectionName,
                "Collectors & Gift Sets",
                StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine(
                    $"[NAYA RESEARCH SKIP COLLECTION] {collectionName}");
                continue;
            }

            var name = GetString(collection, "name")?.Trim();

            if (string.IsNullOrWhiteSpace(name))
                continue;

            if (!knownCollections.Add(NormalizeResearchKey(name)))
                continue;

            collections.Add((
                name,
                collection.GetRawText()));
        }

        if (collections.Count == 0)
        {
            throw new InvalidOperationException(
                $"Aucune collection trouvée pour {brandName}.");
        }

        Console.WriteLine(
            $"[NAYA RESEARCH COLLECTIONS] {collections.Count}");

        foreach (var collection in collections)
            Console.WriteLine(collection.Name);

        // PHASE 2 : rechercher les parfums de chaque collection.
        var perfumeInstructions = """
Tu es un chercheur spécialisé en parfumerie.

Recherche les parfums de la marque et de la collection indiquées.
Utilise la recherche Web. Privilégie les fiches officielles
et les pages produits des sources fiables.

Recherche plusieurs références de la collection :
- parfums actuellement commercialisés ;
- différentes concentrations réellement existantes ;
- flankers et éditions pertinentes ;
- références emblématiques encore documentées.

Ne retourne pas de coffrets, déodorants ou gels douche seuls.
Ne transforme pas une variante en référence distincte sans preuve.
Ne crée aucun nom, aucune concentration et aucune URL.
Utilise null si une information n'est pas confirmée.

Pour chaque parfum, indique le nom commercial exact.
Les URL doivent être des URL réellement consultées.
Le statut doit rester needs_review tant que les informations
n'ont pas été vérifiées indépendamment.

Pour chaque parfum, recherche également l'URL de son image officielle.
- Privilégie l'image de la fiche produit officielle de la marque.
- Retourne image_url uniquement si l'URL a réellement été trouvée et vérifiée.
- N'invente jamais d'URL d'image.
- Si aucune image fiable n'est trouvée, utilise null.

Retourne uniquement un JSON valide :
{
  "perfumes": [
    {
      "id": "identifiant-stable",
      "name": "Nom commercial exact",
      "collection_name": "Nom de la collection",
      "concentration": null,
      "gender": null,
      "image_url": null,
      "source_urls": [],
      "verification_status": "needs_review"
    }
  ],
  "limitations": []
}
""";

        var allPerfumes = new List<JsonElement>();
        var knownPerfumes = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        var allLimitations = new List<string>();

        foreach (var collection in collections)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Console.WriteLine(
                $"[NAYA RESEARCH COLLECTION START] {collection.Name}");

            var perfumePrompt = $"""
Marque : {brandName}
Collection à rechercher : {collection.Name}

Informations sur cette collection :
{collection.SourceJson}

Catalogue commercial existant :
{existingCatalogJson}

Recherche spécifiquement les parfums de cette collection.
Ne te limite pas aux produits déjà présents dans le catalogue.
Vérifie que chaque référence appartient bien à cette collection.
Recherche les variantes commerciales pertinentes.
Ne retourne pas de parfums d'autres collections.

Retourne tous les résultats pertinents que tu peux documenter,
sans inventer de références pour atteindre un nombre cible.
""";

            try
            {
                var perfumesJson = await ExecuteResearchRequestAsync(
                    perfumeInstructions,
                    perfumePrompt,
                    10000,
                    cancellationToken);

                using var perfumeDocument = JsonDocument.Parse(perfumesJson);

                var root = perfumeDocument.RootElement;

                if (!root.TryGetProperty("perfumes", out var perfumeArray) ||
                    perfumeArray.ValueKind != JsonValueKind.Array)
                {
                    Console.WriteLine(
                        $"[NAYA RESEARCH INVALID ARRAY] {collection.Name}");
                    continue;
                }

                var countBefore = allPerfumes.Count;

                foreach (var perfume in perfumeArray.EnumerateArray())
                {
                    var name = GetString(perfume, "name")?.Trim();

                    if (string.IsNullOrWhiteSpace(name))
                        continue;

                    // Exclure les coffrets et produits dérivés.

                    // Exclure les coffrets et produits dérivés.
                    var nonPerfumePattern =
                        @"\b(body\s+lotion|lait\s+corps|body\s+cream|" +
                        @"shower\s+gel|gel\s+douche|deodorants?|déodorants?|" +
                        @"coffrets?|gift\s+sets?|discovery\s+sets?|" +
                        @"kits?\s+de\s+découverte|discovery\s+kits?|" +
                        @"miniatures?|travel\s+sets?)\b";

                    if (Regex.IsMatch(
                        name,
                        nonPerfumePattern,
                        RegexOptions.IgnoreCase)
                        ||
                        Regex.IsMatch(
                            name,
                            @"\b(et|with|plus)\b.*\b" +
                            @"(spray\s+de\s+voyage|travel\s+spray|" +
                            @"miniature|mini)\b",
                            RegexOptions.IgnoreCase))
                    {
                        Console.WriteLine(
                            $"[NAYA RESEARCH SKIP NON-PERFUME] {name}");
                        continue;
                    }

                    // Déduplication après filtrage.
                    var key = NormalizeResearchKey(name);

                    if (!knownPerfumes.Add(key))
                        continue;

                    allPerfumes.Add(perfume.Clone());

                }

                var added = allPerfumes.Count - countBefore;

                Console.WriteLine(
                    $"[NAYA RESEARCH COLLECTION DONE] " +
                    $"{collection.Name}: {added} nouveaux parfums");

                if (root.TryGetProperty("limitations", out var limitations) &&
                    limitations.ValueKind == JsonValueKind.Array)
                {
                    foreach (var limitation in limitations.EnumerateArray())
                    {
                        if (limitation.ValueKind == JsonValueKind.String)
                        {
                            var value = limitation.GetString();

                            if (!string.IsNullOrWhiteSpace(value))
                                allLimitations.Add(
                                    $"{collection.Name}: {value}");
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Une collection en échec ne doit pas effacer
                // les résultats des collections déjà recherchées.
                Console.WriteLine(
                    $"[NAYA RESEARCH COLLECTION ERROR] " +
                    $"{collection.Name}: {ex.Message}");

                allLimitations.Add(
                    $"Recherche incomplète pour {collection.Name}.");
            }
        }

        if (allPerfumes.Count == 0)
        {
            throw new InvalidOperationException(
                $"Aucun parfum trouvé pour {brandName}. " +
                "Le guide ne peut pas être généré.");
        }

        // PHASE 3 : construire le catalogue consolidé.
        var result = new
        {
            brand = brandName,

            collections = collections.Select(c =>
                JsonSerializer.Deserialize<JsonElement>(c.SourceJson)),

            perfumes = allPerfumes.Select(p =>
            {
                // Garantit que la sortie contient les propriétés attendues.
                var name = GetString(p, "name") ?? "";
                var collectionName = GetString(p, "collection_name");

                return new
                {
                    id = GetString(p, "id") ??
                         NormalizeResearchKey(name),

                    name,

                    collection_name = collectionName,

                    concentration = GetString(p, "concentration"),

                    gender = GetString(p, "gender"),

                    image_url = GetString(p, "image_url"),

                    source_urls = p.TryGetProperty(
                        "source_urls", out var urls)
                        ? urls.Clone()
                        : JsonSerializer.SerializeToElement(
                            Array.Empty<string>()),

                    verification_status =
                        GetString(p, "verification_status") ??
                        "needs_review"
                };
            }).ToList(),

            limitations = allLimitations.Distinct().ToList()
        };

        var resultJson = JsonSerializer.Serialize(
            result,
            new JsonSerializerOptions
            {
                WriteIndented = true
            });

        Console.WriteLine(
            $"[NAYA RESEARCH FINAL] {brandName}: " +
            $"{collections.Count} collections, " +
            $"{allPerfumes.Count} parfums");

        Console.WriteLine("[NAYA RESEARCH PERFUMES]");

        foreach (var perfume in allPerfumes)
        {
            Console.WriteLine(
                $"{GetString(perfume, "name")} | " +
                $"{GetString(perfume, "collection_name")} | " +
                $"{GetString(perfume, "concentration")}");
        }

        return resultJson;
    }

    private static string NormalizeResearchKey(string value)
    {
        return System.Text.RegularExpressions.Regex.Replace(
            value.Trim().ToLowerInvariant(),
            @"[^a-z0-9]+",
            "");
    }


    private async Task<string> ExecuteResearchRequestAsync(
        string instructions,
        string userPrompt,
        int maxOutputTokens,
        CancellationToken cancellationToken)
    {
        var requestBody = new
        {
            model = openAiModel,
            instructions,
            input = userPrompt,
            tools = new[]
            {
            new { type = "web_search" }
        },
            max_output_tokens = maxOutputTokens
        };

        using var request = new HttpRequestMessage(
            HttpMethod.Post, "responses");

        request.Content = JsonContent.Create(requestBody);

        using var response = await openAiHttpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        var responseBody = await response.Content.ReadAsStringAsync(
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Erreur recherche OpenAI {(int)response.StatusCode}: " +
                responseBody);
        }

        using var apiResponse = JsonDocument.Parse(responseBody);
        var root = apiResponse.RootElement;

        var status = GetString(root, "status");

        if (!string.Equals(
                status, "completed",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Recherche OpenAI incomplète. Statut : {status}");
        }

        var outputTexts = new List<string>();

        if (root.TryGetProperty("output", out var output))
        {
            foreach (var item in output.EnumerateArray())
            {
                if (!item.TryGetProperty("content", out var content))
                    continue;

                foreach (var block in content.EnumerateArray())
                {
                    if (GetString(block, "type") == "output_text" &&
                        block.TryGetProperty("text", out var textElement))
                    {
                        outputTexts.Add(textElement.GetString() ?? "");
                    }
                }
            }
        }

        var rawText = string.Join("\n", outputTexts);

        if (string.IsNullOrWhiteSpace(rawText))
        {
            throw new InvalidOperationException(
                "OpenAI n'a retourné aucun texte.");
        }

        var cleanJson = ExtractJson(rawText);

        // Vérifie que le résultat est du JSON valide.
        using var validationDocument = JsonDocument.Parse(cleanJson);

        return cleanJson;
    }



    private async Task<List<PerfumeNoteRecord1>> GetPerfumeNotesAsync(
        CancellationToken cancellationToken = default)
    {
        var url =
            $"{supabaseUrl.TrimEnd('/')}/rest/v1/perfume_notes" +
            "?select=name_fr,name_en,type,image_url,is_active" +
            "&is_active=eq.true" +
            "&order=name_fr.asc";

        using var request = new HttpRequestMessage(
            HttpMethod.Get, url);

        using var response = await httpClient.SendAsync(
            request, cancellationToken);

        var body = await response.Content.ReadAsStringAsync(
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Erreur Supabase {(int)response.StatusCode} : {body}");
        }

        return JsonSerializer.Deserialize<List<PerfumeNoteRecord1>>(
            body,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            })
            ?? new List<PerfumeNoteRecord1>();
    }



    private static string? FindNoteImage(
    JsonElement attributes,
    string propertyName,
    string noteType,
    List<PerfumeNoteRecord1> notes)
    {
        var productNotes = GetStringArray(attributes, propertyName);

        var activeNotes = notes
            .Where(n => n.IsActive && !string.IsNullOrWhiteSpace(n.ImageUrl))
            .ToList();

        foreach (var productNote in productNotes)
        {
            var normalized = Normalize(productNote);

            // 1. Recherche exacte dans le type attendu.
            var exactMatch = activeNotes
                .Where(n => string.Equals(
                    n.Type, noteType, StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault(n =>
                    Normalize(n.NameFr) == normalized ||
                    Normalize(n.NameEn) == normalized);

            if (exactMatch != null)
                return exactMatch.ImageUrl;

            // 2. Recherche exacte dans tous les types.
            exactMatch = activeNotes.FirstOrDefault(n =>
                Normalize(n.NameFr) == normalized ||
                Normalize(n.NameEn) == normalized);

            if (exactMatch != null)
                return exactMatch.ImageUrl;

            // 3. Extraire les mots significatifs du texte produit.
            var searchWords = NormalizeNoteSearch(productNote)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(NormalizeNoteWord)
                .Where(w => w.Length >= 3)
                .Distinct()
                .ToList();

            if (searchWords.Count == 0)
                continue;

            // Recherche dans un type, puis dans les autres.
            foreach (var typeNotes in new[]
            {
            activeNotes.Where(n => string.Equals(
                n.Type, noteType, StringComparison.OrdinalIgnoreCase)),
            activeNotes.Where(n => !string.Equals(
                n.Type, noteType, StringComparison.OrdinalIgnoreCase))
        })
            {
                // Préparer les noms français et anglais.
                var candidates = typeNotes.Select(n => new
                {
                    Note = n,
                    FrenchWords = GetNormalizedNoteWords(n.NameFr),
                    EnglishWords = GetNormalizedNoteWords(n.NameEn)
                }).ToList();

                // Favoriser la note qui correspond au plus grand nombre de mots.
                var bestMatch = candidates
                    .Select(c => new
                    {
                        c.Note,
                        Score = searchWords.Count(word =>
                            c.FrenchWords.Any(nameWord =>
                                IsNoteWordMatch(nameWord, word)) ||
                            c.EnglishWords.Any(nameWord =>
                                IsNoteWordMatch(nameWord, word)))
                    })
                    .Where(x => x.Score > 0)
                    .OrderByDescending(x => x.Score)
                    .FirstOrDefault();

                if (bestMatch != null)
                    return bestMatch.Note.ImageUrl;
            }
        }

        return null;
    }

    private static readonly HashSet<string> IgnoredNoteWords =
            new(StringComparer.OrdinalIgnoreCase)
            {
        "accord", "huile", "essence", "essentielle",
        "absolu", "duo", "pulpeux", "pulpeuse",
        "notes", "note", "riche", "riches",
        "de", "du", "des", "la", "le", "les",
        "un", "une", "et", "avec", "pour", "the",
        "and", "of", "with", "rich", "richly"
            };

    private static string NormalizeNoteSearch(string value)
    {
        var words = GetNormalizedNoteWords(value)
            .Where(w => !IgnoredNoteWords.Contains(w));

        return string.Join(" ", words);
    }

    private static List<string> GetNormalizedNoteWords(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return new List<string>();

        var normalized = Normalize(value)
            .Replace("'", " ")
            .Replace("’", " ")
            .Replace("-", " ");

        return normalized
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(NormalizeNoteWord)
            .Where(w => w.Length > 0)
            .ToList();
    }

    private static string NormalizeNoteWord(string value)
    {
        var word = Normalize(value).Trim();

        // Normalisation grammaticale générique.
        // Exemples : florales -> floral, boisées -> boise,
        // vanillées -> vanille, vanille -> vanill.
        if (word.Length > 6 && word.EndsWith("es"))
            word = word[..^2];
        else if (word.Length > 5 && word.EndsWith("s"))
            word = word[..^1];

        if (word.Length > 5 && word.EndsWith("e"))
            word = word[..^1];

        return word;
    }

    private static bool IsNoteWordMatch(string nameWord, string searchWord)
    {
        if (nameWord == searchWord)
            return true;

        // Évite les correspondances trop courtes ou accidentelles.
        return nameWord.Length >= 4 &&
               searchWord.Length >= 4 &&
               (nameWord.StartsWith(searchWord, StringComparison.Ordinal) ||
                searchWord.StartsWith(nameWord, StringComparison.Ordinal));
    }

    private static string Normalize(string value)
    {
        var normalized = value
            .Normalize(NormalizationForm.FormD);

        var builder = new StringBuilder();

        foreach (var c in normalized)
        {
            if (System.Globalization.CharUnicodeInfo
                .GetUnicodeCategory(c) !=
                UnicodeCategory.NonSpacingMark)
            {
                builder.Append(char.ToLowerInvariant(c));
            }
        }

        return builder.ToString()
            .Normalize(NormalizationForm.FormC)
            .Trim();
    }

    private static List<string> GetStringArray(
            JsonElement element,
            string propertyName)
    {
        var result = new List<string>();

        if (element.ValueKind != JsonValueKind.Object ||
            !element.TryGetProperty(propertyName, out var value))
            return result;

        if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    var text = item.GetString();

                    if (!string.IsNullOrWhiteSpace(text))
                        result.Add(text);
                }
            }
        }
        else if (value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString();

            if (!string.IsNullOrWhiteSpace(text))
                result.Add(text);
        }

        return result;
    }


}
