using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;

namespace wsaffiliation.Controllers
{
    [ApiController]
    [Route("api/perfumes")]
    public sealed class PerfumeSearchController : ControllerBase
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;

        public PerfumeSearchController(
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration)
        {
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
        }

        // POST /api/perfumes/search
        [HttpPost("search")]
        public async Task<IActionResult> Search(
            [FromBody] PerfumeSearchRequest request,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(request.Text))
            {
                return BadRequest(new
                {
                    error = "Le texte de recherche est obligatoire."
                });
            }

            if (request.Text.Length > 2000)
            {
                return BadRequest(new
                {
                    error = "La recherche ne doit pas dépasser 2000 caractères."
                });
            }

            try
            {
                var supabaseUrl =
                    _configuration["SUPABASE_URL"]
                    ?? Environment.GetEnvironmentVariable("SUPABASE_URL");

                var supabaseKey =
                    _configuration["SUPABASE_KEY"]
                    ?? Environment.GetEnvironmentVariable("SUPABASE_KEY");

                var openAiKey =
                    _configuration["OPENAI_API"]
                    ?? Environment.GetEnvironmentVariable("OPENAI_API");

                if (string.IsNullOrWhiteSpace(supabaseUrl) ||
                    string.IsNullOrWhiteSpace(supabaseKey))
                {
                    return StatusCode(500, new
                    {
                        error = "SUPABASE_URL ou SUPABASE_KEY est manquant."
                    });
                }

                if (string.IsNullOrWhiteSpace(openAiKey))
                {
                    return StatusCode(500, new
                    {
                        error = "OPENAI_API est introuvable."
                    });
                }

                // 1. Comprendre la demande du client avec OpenAI.
                var criteria = await ExtractCriteriaAsync(
                    request.Text,
                    openAiKey,
                    cancellationToken);




                Console.WriteLine(
                    "CRITERES OPENAI: " +
                    JsonSerializer.Serialize(criteria, JsonOptions)
                );

                var products = await GetProductsAsync(
                    supabaseUrl,
                    supabaseKey,
                    cancellationToken);

                foreach (var p in products.Take(5))
                {
                    Console.WriteLine(
                        $"PRODUIT {p.Id} {p.Name} | " +
                        $"ATTRIBUTES={p.NayaAttributes.ValueKind} | " +
                        $"JSON={p.NayaAttributes}"
                    );
                }

                // 3. Charger les offres disponibles.
                var offers = await GetOffersAsync(
                    supabaseUrl,
                    supabaseKey,
                    products.Select(p => p.Id).ToList(),
                    cancellationToken);


                Console.WriteLine(
                    $"OFFRES : {offers.Count}"
                );

                foreach (var o in offers.Take(5))
                {
                    Console.WriteLine(
                        $"OFFER product_id={o.ProductId} | " +
                        $"price={o.Price} | available={o.IsAvailable}"
                    );
                }

                

                Console.WriteLine($"PRODUITS CHARGES: {products.Count}");
                Console.WriteLine($"OFFRES CHARGEES: {offers.Count}");

                Console.WriteLine(
                    "EXEMPLES PRODUITS: " +
                    JsonSerializer.Serialize(
                        products.Take(5).Select(p => new
                        {
                            p.Id,
                            p.Brand,
                            p.Name,
                            p.Category,
                            p.Type,
                            Attributes = p.NayaAttributes
                        }),
                        JsonOptions
                    )
                );

                // 4. Classer les parfums selon les critères.

                var ranked = products
                    .Select(product =>
                    {
                        var productOffers = offers
                            .Where(o => o.ProductId == product.Id)
                            .ToList();

                        var score = CalculateScore(
                            product,
                            criteria,
                            productOffers);

                        return new
                        {
                            Product = product,
                            Offers = productOffers,
                            Score = score.Score,
                            Reasons = score.Reasons
                        };
                    })
                    .Select(x => new
                    {
                        x.Product,
                        x.Score,
                        x.Reasons,
                        Offers = x.Offers
                            .Where(o => o.IsAvailable)
                            .Where(o => o.Price.HasValue)
                            .Where(o => criteria.MaxPrice <= 0 ||
                                        o.Price.Value <= criteria.MaxPrice)
                            .ToList()
                    })
                    .Where(x => x.Offers.Any())
                    .OrderByDescending(x => x.Score)
                    .ThenBy(x => x.Product.Name)
                    .Take(10)
                    .Select(x => new
                    {
                        id = x.Product.Id,
                        brand = x.Product.Brand,
                        name = x.Product.Name,
                        image = x.Product.Image,
                        slug = x.Product.Slug,
                        category = x.Product.Category,
                        sub_category = x.Product.SubCategory,
                        naya_attributes = x.Product.NayaAttributes,
                        match_score = x.Score,
                        match_reasons = x.Reasons,
                        offers = x.Offers
                            .OrderBy(o => o.Price ?? decimal.MaxValue)
                            .Select(o => new
                            {
                                marketplace = o.Marketplace,
                                price = o.Price,
                                currency = o.Currency,
                                url = o.ProductUrl,
                                available = o.IsAvailable,
                                rating = o.Rating,
                                reviews = o.Reviews
                            })
                    })
                    .ToList();



                return Ok(new
                {
                    query = request.Text,
                    interpreted_criteria = criteria,
                    total = ranked.Count,
                    results = ranked
                });
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (HttpRequestException ex)
            {
                return StatusCode(502, new
                {
                    error = "Erreur lors de l'appel à Supabase ou OpenAI.",
                    details = ex.Message
                });
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(
                    $"ERREUR PERFUME SEARCH: {ex}"
                );

                return StatusCode(500, new
                {
                    error = "Erreur interne lors de la recherche de parfums.",
                    details = ex.Message
                });
            }
        }

        // ---------------------------------------------------------
        // OpenAI : transformer le texte en critères JSON
        // ---------------------------------------------------------

        private async Task<SearchCriteria> ExtractCriteriaAsync(
            string text,
            string apiKey,
            CancellationToken cancellationToken)
        {
            var client = _httpClientFactory.CreateClient();

            using var message = new HttpRequestMessage(
                HttpMethod.Post,
                "https://api.openai.com/v1/chat/completions");

            message.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", apiKey);

            var body = new
            {
                model = "gpt-4o-mini",
                temperature = 0,
                messages = new object[]
                {
                new
                {
                    role = "system",
                    content = """
                    Tu es le moteur de recherche intelligent de parfums de Naya.

                    Analyse la demande de l'utilisateur et extrais TOUS les critères explicitement demandés ou clairement implicites.

                    Règles obligatoires :
                    - "sucré", "odeur sucrée" : ajoute "Gourmand" et "Ambré" dans fragrance_families, et "Vanille" dans preferred_notes.
                    - "très bonne tenue", "qui dure longtemps", "longue durée" : min_longevity = 4.
                    - "tenue exceptionnelle", "tenue maximale" : min_longevity = 5.
                    - "bonne tenue" : min_longevity = 3.
                    - "pour homme" : gender = "Homme".
                    - "pour femme" : gender = "Femme".
                    - "unisexe" : gender = "Unisexe".
                    - "moins de X euros", "budget maximum X euros", "maximum X €" : max_price = X.
                    - "fort", "puissant" : min_sillage = 4 si le contexte parle du sillage ou de la projection.
                    - Si aucun budget n'est précisé, max_price = 0.
                    - Si aucune tenue minimale n'est demandée, min_longevity = 0.
                    - Si aucune préférence de sillage n'est demandée, min_sillage = 0.
                    - Ne supprime jamais un critère parce qu'un autre critère est présent.
                    - max_results doit toujours être 20.
                    - Les tableaux doivent être vides uniquement si aucune valeur ne correspond à la demande.

                    Les familles olfactives et les notes doivent être exprimées en français.
                    Retourne uniquement le JSON conforme au schéma fourni.
                    """
                },
                new
                {
                    role = "user",
                    content = text
                }
                },
                response_format = new
                {
                    type = "json_schema",
                    json_schema = new
                    {
                        name = "perfume_search_criteria",
                        strict = true,
                        schema = new
                        {
                            type = "object",
                            properties = new
                            {
                                fragrance_families = new
                                {
                                    type = "array",
                                    items = new { type = "string" }
                                },
                                preferred_notes = new
                                {
                                    type = "array",
                                    items = new { type = "string" }
                                },
                                styles = new
                                {
                                    type = "array",
                                    items = new { type = "string" }
                                },
                                occasions = new
                                {
                                    type = "array",
                                    items = new { type = "string" }
                                },
                                seasons = new
                                {
                                    type = "array",
                                    items = new { type = "string" }
                                },
                                gender = new { type = "string" },
                                min_longevity = new { type = "integer" },
                                min_sillage = new { type = "integer" },
                                max_price = new { type = "number" },
                                max_results = new { type = "integer" }
                            },
                            required = new[]
                            {
                            "fragrance_families",
                            "preferred_notes",
                            "styles",
                            "occasions",
                            "seasons",
                            "gender",
                            "min_longevity",
                            "min_sillage",
                            "max_price",
                            "max_results"
                        },
                            additionalProperties = false
                        }
                    }
                }
            };

            message.Content = new StringContent(
                JsonSerializer.Serialize(body),
                Encoding.UTF8,
                "application/json");

            using var response = await client.SendAsync(
                message,
                cancellationToken);

            var responseText = await response.Content.ReadAsStringAsync(
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    $"OpenAI HTTP {(int)response.StatusCode}: {responseText}");
            }

            using var document = JsonDocument.Parse(responseText);

            var content = document.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            if (string.IsNullOrWhiteSpace(content))
            {
                throw new InvalidOperationException(
                    "OpenAI a retourné des critères vides.");
            }

            var criteria = JsonSerializer.Deserialize<SearchCriteria>(
                content,
                JsonOptions);

            if (criteria == null)
            {
                throw new InvalidOperationException(
                    "Impossible de lire les critères OpenAI.");
            }

            CompleteCriteriaFromText(text, criteria);

            Console.WriteLine(
                "CRITERES FINAUX: " +
                JsonSerializer.Serialize(criteria, JsonOptions)
            );

            return criteria;


            if (criteria == null)
            {
                throw new InvalidOperationException(
                    "Impossible de lire les critères OpenAI.");
            }

            // Limiter les valeurs à des plages acceptables.
            criteria.MinLongevity =
                Math.Clamp(criteria.MinLongevity, 0, 5);

            criteria.MinSillage =
                Math.Clamp(criteria.MinSillage, 0, 5);

            criteria.MaxPrice =
                Math.Max(0, criteria.MaxPrice);

            return criteria;
        }



        private static void CompleteCriteriaFromText(
            string text,
            SearchCriteria criteria)
        {
            var normalized = Normalize(text);

            // Famille olfactive et notes
            if (normalized.Contains("sucr"))
            {
                if (!criteria.FragranceFamilies.Contains("Gourmand"))
                    criteria.FragranceFamilies.Add("Gourmand");

                if (!criteria.FragranceFamilies.Contains("Ambré"))
                    criteria.FragranceFamilies.Add("Ambré");

                if (!criteria.PreferredNotes.Contains("Vanille"))
                    criteria.PreferredNotes.Add("Vanille");
            }

            if (normalized.Contains("vanille"))
            {
                if (!criteria.PreferredNotes.Contains("Vanille"))
                    criteria.PreferredNotes.Add("Vanille");
            }

            // Tenue du parfum
            if (normalized.Contains("tenue") ||
                normalized.Contains("dure longtemps") ||
                normalized.Contains("longue duree") ||
                normalized.Contains("longue tenue"))
            {
                if (normalized.Contains("tres bonne") ||
                    normalized.Contains("tres longue") ||
                    normalized.Contains("exceptionnelle") ||
                    normalized.Contains("maximale"))
                {
                    criteria.MinLongevity = Math.Max(
                        criteria.MinLongevity, 4);
                }
                else
                {
                    criteria.MinLongevity = Math.Max(
                        criteria.MinLongevity, 3);
                }
            }

            // Genre
            if (normalized.Contains("pour homme") ||
                normalized.Contains("masculin"))
            {
                criteria.Gender = "Homme";
            }
            else if (normalized.Contains("pour femme") ||
                     normalized.Contains("feminin"))
            {
                criteria.Gender = "Femme";
            }
            else if (normalized.Contains("unisexe"))
            {
                criteria.Gender = "Unisexe";
            }

            // Budget : moins de 120 euros, maximum 120 €, etc.
            var match = System.Text.RegularExpressions.Regex.Match(
                normalized,
                @"(?:moins de|maximum|max|budget de|budget maximum|jusqu'a|inferieur a)\s*(\d+(?:[.,]\d+)?)\s*(?:euros?|€)?"
            );

            if (match.Success &&
                decimal.TryParse(
                    match.Groups[1].Value.Replace(',', '.'),
                    System.Globalization.NumberStyles.Number,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var price))
            {
                criteria.MaxPrice = price;
            }

            criteria.MaxResults = 20;
        }



        // ---------------------------------------------------------
        // Supabase : charger les produits
        // ---------------------------------------------------------

        private async Task<List<PerfumeProduct>> GetProductsAsync(
            string supabaseUrl,
            string supabaseKey,
            CancellationToken cancellationToken)
        {
            var client = CreateSupabaseClient(supabaseKey);

            var url =
                $"{supabaseUrl.TrimEnd('/')}/rest/v1/products" +
                "?select=id,brand,name,category,sub_category,type,image,slug,naya_attributes,product_data" +
                "&category=eq.Parfum" +
                "&type=eq.PERFUME" +
                "&order=id.asc" +
                "&limit=1000";

            using var response = await client.GetAsync(
                url,
                cancellationToken);

            var json = await response.Content.ReadAsStringAsync(
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    $"Supabase products HTTP {(int)response.StatusCode}: {json}");
            }

            var products =
    JsonSerializer.Deserialize<List<PerfumeProduct>>(
        json,
        JsonOptions
    ) ?? new List<PerfumeProduct>();

            Console.WriteLine($"PRODUITS CHARGES : {products.Count}");

            foreach (var p in products.Take(5))
            {
                Console.WriteLine(
                    $"ID={p.Id}, NOM={p.Name}, " +
                    $"ATTRIBUTES={p.NayaAttributes.ValueKind}, " +
                    $"DATA={p.ProductData.ValueKind}"
                );
            }

            return products;
        }

        // ---------------------------------------------------------
        // Supabase : charger les offres des produits
        // ---------------------------------------------------------

        private async Task<List<ProductOffer>> GetOffersAsync(
            string supabaseUrl,
            string supabaseKey,
            List<long> productIds,
            CancellationToken cancellationToken)
        {
            if (productIds.Count == 0)
                return new List<ProductOffer>();

            var client = CreateSupabaseClient(supabaseKey);
            var allOffers = new List<ProductOffer>();

            // Petits groupes pour éviter une URL trop longue.
            foreach (var batch in productIds.Chunk(100))
            {
                var ids = string.Join(",", batch);

                var url =
                    $"{supabaseUrl.TrimEnd('/')}/rest/v1/product_offers" +
                    "?select=id,product_id,marketplace,price,currency,product_url,is_available,rating,reviews" +
                    $"&product_id=in.({ids})" +
                    "&is_available=eq.true" +
                    "&order=price.asc" +
                    "&limit=1000";

                using var response = await client.GetAsync(
                    url,
                    cancellationToken);

                var json = await response.Content.ReadAsStringAsync(
                    cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    throw new HttpRequestException(
                        $"Supabase product_offers HTTP {(int)response.StatusCode}: {json}");
                }

                var batchOffers =
                    JsonSerializer.Deserialize<List<ProductOffer>>(
                        json,
                        JsonOptions);

                Console.WriteLine(
    $"OFFRES DU LOT : {batchOffers?.Count ?? 0}"
);

                if (batchOffers != null)
                    allOffers.AddRange(batchOffers);
            }

            return allOffers;
        }

        private HttpClient CreateSupabaseClient(string supabaseKey)
        {
            var client = _httpClientFactory.CreateClient();

            client.DefaultRequestHeaders.Add("apikey", supabaseKey);
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", supabaseKey);

            return client;
        }

        // ---------------------------------------------------------
        // Calcul du score
        // ---------------------------------------------------------

        private static (int Score, List<string> Reasons) CalculateScore(
            PerfumeProduct product,
            SearchCriteria criteria,
            List<ProductOffer> offers)
        {
            var attributes = product.NayaAttributes;
            var score = 0;
            var reasons = new List<string>();

            // Si le client indique un genre, on privilégie ce genre.
            if (!string.IsNullOrWhiteSpace(criteria.Gender))
            {
                var gender = GetString(attributes, "gender");

                if (!string.IsNullOrWhiteSpace(gender) &&
                    !ContainsText(gender, criteria.Gender))
                {
                    return (0, new List<string>());
                }

                if (ContainsText(gender, criteria.Gender))
                {
                    score += 15;
                    reasons.Add($"Genre : {gender}");
                }
            }

            // Familles olfactives.
            var families = GetStringArray(
                attributes,
                "fragrance_family");

            foreach (var wanted in criteria.FragranceFamilies)
            {
                var found = families.FirstOrDefault(
                    value => ContainsText(value, wanted));

                if (found != null)
                {
                    score += 30;
                    reasons.Add($"Famille olfactive : {found}");
                    break;

                }
            }

            // Notes : tête, cœur et fond.
            var notes = GetStringArray(attributes, "top_notes")
                .Concat(GetStringArray(attributes, "heart_notes"))
                .Concat(GetStringArray(attributes, "base_notes"))
                .ToList();

            foreach (var wanted in criteria.PreferredNotes)
            {
                var found = notes.FirstOrDefault(
                    value => ContainsText(value, wanted)
                          || ContainsText(wanted, value));

                if (found != null)
                {
                    score += 25;
                    reasons.Add($"Note recherchée : {found}");
                }
            }

            // Tenue.
            var longevity = GetNumber(attributes, "longevity");

            if (criteria.MinLongevity > 0)
            {
                if (longevity >= criteria.MinLongevity)
                {
                    score += 20;
                    reasons.Add($"Bonne tenue : {longevity}/5");
                }
                else if (longevity > 0)
                {
                    score -= 10;
                }
            }

            // Sillage.
            var sillage = GetNumber(attributes, "sillage");

            if (criteria.MinSillage > 0)
            {
                if (sillage >= criteria.MinSillage)
                {
                    score += 10;
                    reasons.Add($"Sillage : {sillage}/5");
                }
                else if (sillage > 0)
                {
                    score -= 5;
                }
            }

            // Style, occasion et saison.
            score += MatchArrayCriteria(
                attributes, "style", criteria.Styles,
                8, "Style", reasons);

            score += MatchArrayCriteria(
                attributes, "occasion", criteria.Occasions,
                8, "Occasion", reasons);

            score += MatchArrayCriteria(
                attributes, "season", criteria.Seasons,
                6, "Saison", reasons);

            // Si un budget est demandé, il faut avoir un prix connu
            // et au moins une offre disponible sous le budget.
            if (criteria.MaxPrice > 0)
            {
                var affordable = offers.Any(
                    offer => offer.IsAvailable
                             && offer.Price.HasValue
                             && offer.Price.Value <= criteria.MaxPrice);

                if (affordable)
                {
                    score += 10;
                    reasons.Add("Prix dans le budget");
                }
                else
                {
                    // Un budget explicite est une contrainte stricte.
                    return (0, new List<string>());
                }
            }

            return (Math.Max(0, score), reasons.Distinct().ToList());
        }

        private static int MatchArrayCriteria(
            JsonElement attributes,
            string propertyName,
            List<string> wantedValues,
            int points,
            string label,
            List<string> reasons)
        {
            if (wantedValues.Count == 0)
                return 0;

            var actualValues = GetStringArray(
                attributes,
                propertyName);

            var matched = wantedValues
                .Select(wanted => actualValues.FirstOrDefault(
                    actual => ContainsText(actual, wanted)))
                .Where(found => found != null)
                .Distinct()
                .ToList();

            foreach (var value in matched)
                reasons.Add($"{label} : {value}");

            return matched.Count * points;
        }

        // ---------------------------------------------------------
        // Helpers JSON
        // ---------------------------------------------------------

        private static string GetString(
            JsonElement element,
            string propertyName)
        {
            if (element.ValueKind != JsonValueKind.Object ||
                !element.TryGetProperty(propertyName, out var value))
                return "";

            return value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? ""
                : "";
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

        private static int GetNumber(
            JsonElement element,
            string propertyName)
        {
            if (element.ValueKind != JsonValueKind.Object ||
                !element.TryGetProperty(propertyName, out var value))
                return 0;

            if (value.ValueKind == JsonValueKind.Number &&
                value.TryGetInt32(out var number))
                return number;

            return 0;
        }

        private static bool ContainsText(
            string? value,
            string? search)
        {
            if (string.IsNullOrWhiteSpace(value) ||
                string.IsNullOrWhiteSpace(search))
                return false;

            return Normalize(value).Contains(
                Normalize(search),
                StringComparison.Ordinal);
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

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };
    }

    // -------------------------------------------------------------
    // DTOs
    // -------------------------------------------------------------

    public sealed class PerfumeSearchRequest
    {
        public string Text { get; set; } = "";
    }

    public sealed class SearchCriteria
    {
        public List<string> FragranceFamilies { get; set; } = new();
        public List<string> PreferredNotes { get; set; } = new();
        public List<string> Styles { get; set; } = new();
        public List<string> Occasions { get; set; } = new();
        public List<string> Seasons { get; set; } = new();

        public string Gender { get; set; } = "";
        public int MinLongevity { get; set; }
        public int MinSillage { get; set; }
        public decimal MaxPrice { get; set; }
        public int MaxResults { get; set; } = 20;
    }


    public sealed class PerfumeProduct
    {
        public long Id { get; set; }
        public string? Brand { get; set; }
        public string Name { get; set; } = "";
        public string? Category { get; set; }

        public string? SubCategory { get; set; }
        public string? Type { get; set; }
        public string? Image { get; set; }
        public string? Slug { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("naya_attributes")]
        public JsonElement NayaAttributes { get; set; } =
            JsonDocument.Parse("{}").RootElement.Clone();

        [System.Text.Json.Serialization.JsonPropertyName("product_data")]
        public JsonElement ProductData { get; set; } =
            JsonDocument.Parse("{}").RootElement.Clone();
    }



    public sealed class ProductOffer
    {
        public long Id { get; set; }

        [JsonPropertyName("product_id")]
        public long ProductId { get; set; }

        public string Marketplace { get; set; } = "";

        public decimal? Price { get; set; }

        public string Currency { get; set; } = "EUR";

        [JsonPropertyName("product_url")]
        public string? ProductUrl { get; set; }

        [JsonPropertyName("is_available")]
        public bool IsAvailable { get; set; }

        public decimal? Rating { get; set; }

        public int Reviews { get; set; }
    }

}
