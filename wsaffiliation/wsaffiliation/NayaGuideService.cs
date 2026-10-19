
using System.Net.Http.Headers;
using System.Text.Json;
using System.Net.Http.Json;
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
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var slugs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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

        // 2. Upsert les nouvelles références et mettre à jour les existantes.
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

        // Charge les produits existants de cette marque.
        using var productsDocument =
            await GetBrandProductsAsync(brandName, cancellationToken);

        // On ne transmet pas les URL commerciales au modèle :
        // elles seront récupérées directement depuis Supabase.
        var productsForResearch = productsDocument.RootElement
            .EnumerateArray()
            .Select(p => new
            {
                id = p.TryGetProperty("id", out var id) ? id.GetInt64() : 0,
                brand = GetString(p, "brand"),
                name = GetString(p, "name"),
                short_name = GetString(p, "short_name"),
                type = GetString(p, "type"),
                concentration = GetString(
                    p.TryGetProperty("naya_attributes", out var attrs)
                        ? attrs : default,
                    "concentration"),
                gender = GetString(
                    p.TryGetProperty("naya_attributes", out attrs)
                        ? attrs : default,
                    "gender"),
                notes = GetString(
                    p.TryGetProperty("product_data", out var pd)
                        ? pd : default,
                    "notes")
            })
            .ToList();

        var catalogJson = JsonSerializer.Serialize(productsForResearch);

        var researchedCatalogJson = await ResearchBrandCatalogAsync(
    brandName,
    catalogJson,
    cancellationToken);

        var instructions = """
    Tu es le rédacteur en chef de NAYA, un guide indépendant de parfumerie.

    Recherche des informations fiables sur la marque demandée, ses collections
    et ses parfums essentiels. Utilise la recherche Web. Privilégie les sources
    officielles et conserve les URL réellement consultées.

    Rédige en français un guide lumineux, élégant, original et utile au client.
    Inclus l'histoire, l'identité olfactive, les principales collections,
    les parfums essentiels, leurs descriptions, leurs notes et les conseils
    de découverte.

    Ne prétends pas que le catalogue est exhaustif sans vérification.
    N'invente aucune note, date, concentration, citation ou source.
    Pour toute donnée inconnue, utilise null ou [].
    Le contenu doit être rédigé, pas seulement constitué de listes.

    Les produits fournis sont le catalogue commercial disponible dans Supabase.
    Ne crée aucune URL commerciale. Ne génère aucun product_id fictif.
    Les associations commerciales seront faites séparément par le backend.

    
    IMPORTANT — RECHERCHE ET COUVERTURE DU CATALOGUE

    1. Recherche d'abord le site officiel de la marque :
       son catalogue, ses collections et ses fiches produits.

    2. Identifie les principales collections et les parfums associés.
       Recherche plusieurs références dans chaque collection pertinente.

    3. Pour chaque parfum, vérifie autant que possible :
       - son nom commercial exact ;
       - sa collection ;
       - sa concentration ;
       - son genre, si la marque le précise ;
       - ses notes olfactives ;
       - les sources réellement consultées.

    4. Génère un objectif de 15 à 20 références distinctes.
       Retourne au moins 10 références lorsque ce nombre peut être
       vérifié à partir des sources disponibles.

    5. Si la recherche initiale ne trouve qu'un ou deux parfums,
       approfondis la recherche sur les collections et les fiches officielles
       avant de rédiger le guide final.

    6. N'invente jamais de références pour atteindre un nombre cible.
       Si les informations sont insuffisantes, retourne uniquement
       les références identifiables et signale les limites de recherche.

    7. Ne compte pas les coffrets, déodorants, gels douche ou autres
       produits dérivés comme des parfums distincts.

    8. Les différentes concentrations d'un même parfum peuvent être
       des références distinctes uniquement si elles existent réellement.
       Ne crée pas de doublons artificiels.

    9. Recherche également les parfums essentiels absents du catalogue
       Supabase. Ils peuvent figurer dans le guide sans lien affilié.

    10. Chaque parfum doit avoir un identifiant stable, un nom exact,
        un slug unique, un objet editorial et un objet verification.

    11. Chaque parfum doit posséder ses propres sources de vérification.
        N'invente aucune URL et ne déclare pas une référence vérifiée
        si les sources consultées ne le permettent pas.

    12. Le tableau JSON "perfumes" doit contenir toutes les références
        identifiées et suffisamment documentées, pas seulement
        le parfum le plus populaire.

    13. Mets à jour "coverage" en fonction de la recherche réellement faite.
        Ne marque jamais le catalogue comme exhaustif sans preuve.
    
        
    Retourne uniquement un objet JSON valide ayant cette structure :

    {
      "brand": {
        "id": "slug-de-marque",
        "name": "Nom de la marque"
      },
      "metadata": {
        "language": "fr",
        "generation_status": "draft",
        "research_status": "completed",
        "editorial_status": "generated",
        "coverage_status": "needs_review"
      },
      "seo": {
        "title": "Titre SEO",
        "meta_description": "Description SEO",
        "slug": "guide-parfums-marque",
        "introduction": "Introduction éditoriale"
      },
      "brand_story": {
        "title": "Histoire de la marque",
        "history": [],
        "identity": "Identité de la maison",
        "olfactory_signature": "Signature olfactive",
        "editorial_conclusion": "Conclusion",
        "sources": []
      },
      "coverage": {
        "strategy": "essential_fragrances",
        "collections_identified": [],
        "collections_researched": [],
        "collections_remaining": [],
        "essential_perfume_count": 0,
        "catalogue_scope": "current_and_iconic",
        "is_complete": false,
        "limitations": []
      },
      "collections": [
        {
          "id": "collection-slug",
          "name": "Nom de collection",
          "description": "Description documentée",
          "perfume_ids": [],
          "sources": [],
          "verification_status": "needs_review"
        }
      ],
      "perfumes": [
        {
          "id": "identifiant-stable-du-parfum",
          "name": "Nom exact du parfum",
          "slug": "slug-du-parfum",
          "collection_name": null,
          "concentration": null,
          "gender": null,
          "editorial": {
            "title": "Titre de la fiche",
            "introduction": "Texte éditorial original",
            "description": "Description détaillée",
            "olfactory_family": [],
            "character": [],
            "accords": [],
            "notes": {
              "top": [],
              "heart": [],
              "base": []
            },
            "olfactory_explanation": "Explication des notes",
            "recommendation": "Conseil de découverte",
            "occasions": [],
            "seasons": [],
            "sources": []
          },
          "verification": {
            "status": "needs_review",
            "verified_fields": [],
            "unverified_fields": [],
            "sources": []
          }
        }
      ],
      "guide_sections": [],
      "recommendations": [],
      "publication": {
        "status": "draft",
        "review_required": true,
        "published_at": null
      }
    }

    Chaque parfum doit posséder un id stable, un nom et un slug.
    Les champs de vérification doivent refléter les recherches réellement faites.
    Ne marque pas le guide comme complet si des collections importantes
    restent à vérifier.
    """;



        var userPrompt = $"""
Crée un guide éditorial complet pour la marque : {brandName}.

CATALOGUE COMMERCIAL SUPABASE :
{catalogJson}

RÉSULTATS DE LA RECHERCHE PRÉALABLE :
{researchedCatalogJson}

INSTRUCTIONS :
- Utilise les références de la recherche préalable comme base du guide.
- Couvre les différentes collections identifiées.
- Conserve les noms commerciaux exacts.
- Ne crée pas de nouvelles références sans source vérifiable.
- Élimine les doublons réels.
- Conserve les concentrations distinctes lorsqu'elles correspondent
  à des références réellement commercialisées.
- Les parfums absents de Supabase peuvent figurer dans le guide,
  sans lien affilié.
- Conserve les URL sources disponibles dans les résultats de recherche.
- N'invente aucune URL ni aucune note olfactive.
- Signale les informations non vérifiées.
- Génère les descriptions éditoriales en français.
- Respecte exactement le schéma JSON demandé dans les instructions.

Retourne uniquement le JSON final.
""";




        var requestBody = new
        {
            model = openAiModel,
            instructions,
            input = userPrompt,
            max_output_tokens = 30000
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

        Console.WriteLine(
            $"[NAYA OPENAI STATUS] {GetString(responseRoot, "status")}");

        if (responseRoot.TryGetProperty("incomplete_details", out var details))
        {
            Console.WriteLine(
                $"[NAYA OPENAI INCOMPLETE] {details.GetRawText()}");
        }

        // Extraire le texte de la réponse Responses API.
        var outputTexts = new List<string>();

        if (apiResponse.RootElement.TryGetProperty(
            "output", out var output))
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


        Console.WriteLine($"[NAYA OUTPUT BLOCKS] {outputTexts.Count}");

        for (var i = 0; i < outputTexts.Count; i++)
        {
            Console.WriteLine(
                $"[NAYA OUTPUT BLOCK {i}] Length={outputTexts[i].Length}, " +
                $"Start={outputTexts[i][..Math.Min(150, outputTexts[i].Length)]}");
        }



        if (outputTexts.Count != 1)
        {
            throw new InvalidOperationException(
                $"OpenAI a retourné {outputTexts.Count} blocs de texte au lieu d'un seul.");
        }

        var json = outputTexts[0];


        Console.WriteLine($"[NAYA RAW OUTPUT LENGTH] {json.Length}");

        Console.WriteLine(
            $"[NAYA RAW OUTPUT END] " +
            json.Substring(Math.Max(0, json.Length - 2500)));

        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidOperationException(
                "OpenAI n'a retourné aucun JSON exploitable.");
        }

        var cleanJson = ExtractJson(json);

        using var checkDocument = JsonDocument.Parse(cleanJson);

        var perfumeCount = checkDocument.RootElement
            .GetProperty("perfumes")
            .GetArrayLength();

        Console.WriteLine(
            $"[NAYA GENERATED PERFUMES] {perfumeCount}");

        try
        {
            var guide = JsonDocument.Parse(cleanJson);

            ValidateGuideJson(guide.RootElement);

            return guide;
        }
        catch (JsonException ex)
        {
            Console.Error.WriteLine(
                $"[NAYA GUIDE JSON ERROR] {ex.Message}");

            Console.Error.WriteLine(
                $"[NAYA GUIDE JSON LENGTH] {cleanJson.Length}");

            Console.Error.WriteLine(
                $"[NAYA GUIDE JSON END] " +
                cleanJson.Substring(Math.Max(0, cleanJson.Length - 1500)));

            throw;
        }
    }


    private static void ValidateGuideJson(JsonElement guide)
    {
        if (guide.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException(
                "Le guide doit être un objet JSON.");

        if (!guide.TryGetProperty("brand", out var brand) ||
            string.IsNullOrWhiteSpace(GetString(brand, "name")))
            throw new InvalidOperationException(
                "Le champ brand.name est obligatoire.");

        if (!guide.TryGetProperty("seo", out var seo) ||
            string.IsNullOrWhiteSpace(GetString(seo, "slug")))
            throw new InvalidOperationException(
                "Le champ seo.slug est obligatoire.");

        if (!guide.TryGetProperty("perfumes", out var perfumes) ||
            perfumes.ValueKind != JsonValueKind.Array ||
            perfumes.GetArrayLength() == 0)
            throw new InvalidOperationException(
                "Le guide doit contenir au moins un parfum.");

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var perfume in perfumes.EnumerateArray())
        {
            var id = GetString(perfume, "id");
            var name = GetString(perfume, "name");
            var slug = GetString(perfume, "slug");

            if (string.IsNullOrWhiteSpace(id) ||
                string.IsNullOrWhiteSpace(name) ||
                string.IsNullOrWhiteSpace(slug))
            {
                throw new InvalidOperationException(
                    "Chaque parfum doit avoir id, name et slug.");
            }

            if (!ids.Add(id))
            {
                throw new InvalidOperationException(
                    $"Identifiant de parfum en double : {id}");
            }
        }
    }



    public async Task<int> MatchSavedGuideProductsAsync(
        long guideId,
        string brand,
        CancellationToken cancellationToken = default)
    {
        var url =
            $"perfumes?select=id,name,concentration" +
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

        var totalMatches = 0;

        foreach (var perfume in document.RootElement.EnumerateArray())
        {
            var perfumeId = perfume.GetProperty("id").GetInt64();
            var name = GetString(perfume, "name");

            if (string.IsNullOrWhiteSpace(name))
                continue;

            var concentration = GetString(perfume, "concentration");

            totalMatches += await MatchAffiliateProductsAsync(
                perfumeId,
                brand,
                name,
                concentration,
                cancellationToken);
        }

        return totalMatches;
    }


    public async Task<object> GenerateAndSaveGuideAsync(
        string brandName,
        CancellationToken cancellationToken = default)
    {
        using var guide = await GenerateGuideJsonAsync(
            brandName, cancellationToken);

        var root = guide.RootElement;



        if (brandName.Contains(
                "Jean Paul Gaultier",
                StringComparison.OrdinalIgnoreCase))
        {
            var perfumes = root.GetProperty("perfumes");

            var collections = perfumes.EnumerateArray()
                .Select(p => GetString(p, "collection_name"))
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (collections.Count < 3)
            {
                throw new InvalidOperationException(
                    $"Guide insuffisant : {collections.Count} collection(s) " +
                    $"identifiée(s). Collections : {string.Join(", ", collections)}. " +
                    "La sauvegarde est annulée.");
            }
        }



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
        var instructions = """
    Tu es un chercheur spécialisé en parfumerie.

    Recherche la marque demandée en utilisant la recherche Web.
    Privilégie le site officiel de la marque et ses fiches produits.

    Identifie :
    - les principales collections de parfums ;
    - les références actuelles et emblématiques de chaque collection ;
    - le nom commercial exact de chaque parfum ;
    - sa concentration, lorsqu'elle est confirmée ;
    - son genre, lorsqu'il est précisé par la marque ;
    - les sources réellement consultées.

    Recherche plusieurs collections, et pas uniquement la plus populaire.
    Le nombre de collections dépend de la marque.
    Ne suppose pas que toutes les marques ont le même catalogue.

    N'inclus pas les coffrets, déodorants ou gels douche comme parfums.
    Ne crée aucune référence fictive.
    Ne confonds pas deux concentrations sans vérifier leur existence.
    Si une information n'est pas confirmée, utilise null.
    Si une source n'est pas disponible, indique cette limite.

    Retourne uniquement un objet JSON valide avec cette structure :

    {
      "brand": "Nom de la marque",
      "collections": [
        {
          "name": "Nom de la collection",
          "source_urls": []
        }
      ],
      "perfumes": [
        {
          "id": "identifiant-stable",
          "name": "Nom commercial exact",
          "collection_name": "Collection ou null",
          "concentration": "Concentration ou null",
          "gender": "Genre ou null",
          "source_urls": [],
          "verification_status": "needs_review"
        }
      ],
      "limitations": []
    }

    Les URL doivent être des URL réellement consultées.
    Ne déclare pas une référence vérifiée sans éléments suffisants.
    """;

        var userPrompt = $"""
    Recherche le catalogue de parfums de la marque :
    {brandName}

    Produits déjà présents dans le catalogue commercial Supabase :
    {existingCatalogJson}

    Utilise ce catalogue comme point de départ, pas comme limite.
    Recherche également les références absentes de Supabase.
    Évite les doublons et couvre les différentes collections pertinentes.
    """;

        var requestBody = new
        {
            model = openAiModel,
            instructions,
            input = userPrompt,
            tools = new[]
            {
            new { type = "web_search" }
        },
            max_output_tokens = 10000
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

        if (!string.Equals(status, "completed",
            StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Recherche catalogue incomplète. Statut : {status}");
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
                "La recherche OpenAI n'a retourné aucun résultat.");
        }

        var cleanJson = ExtractJson(rawText);

        using var researchDocument = JsonDocument.Parse(cleanJson);

        var researchRoot = researchDocument.RootElement;

        if (!researchRoot.TryGetProperty("collections", out var collections) ||
            collections.ValueKind != JsonValueKind.Array ||
            !researchRoot.TryGetProperty("perfumes", out var perfumes) ||
            perfumes.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException(
                "Le résultat de recherche ne contient pas les tableaux " +
                "'collections' et 'perfumes'.");
        }

        if (perfumes.GetArrayLength() == 0)
        {
            throw new InvalidOperationException(
                $"Aucun parfum trouvé pour {brandName}. " +
                "La génération du guide est annulée.");
        }

        Console.WriteLine(
            $"[NAYA RESEARCH] {brandName}: " +
            $"{collections.GetArrayLength()} collections, " +
            $"{perfumes.GetArrayLength()} parfums.");

        return cleanJson;
    }



}
