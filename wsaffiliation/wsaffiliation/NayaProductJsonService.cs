using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Text;
using System.Xml;
using System.Net;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;

namespace wsaffiliation
{

    public class NayaProductJsonService
    {
        private readonly string? supabaseUrl =
     Environment.GetEnvironmentVariable("SUPABASE_URL");

        private readonly string? supabaseKey =
            Environment.GetEnvironmentVariable("SUPABASE_KEY");

        private readonly HttpClient httpClient;

        public NayaProductJsonService()
        {
            httpClient = new HttpClient();

            httpClient.Timeout = TimeSpan.FromSeconds(30);

            httpClient.DefaultRequestHeaders.Add(
                "apikey",
                supabaseKey);

            httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue(
                    "Bearer",
                    supabaseKey);
        }

        // ============================================================
        // PUBLIC
        // ============================================================

        public async Task<string?> GetProductJson(string slug)
        {
            if (string.IsNullOrWhiteSpace(slug))
                return null;

            slug = NormalizeSlug(slug);

            // --------------------------------------------------------
            // 1. Tous les produits
            // --------------------------------------------------------

            List<AllProduct> allProducts =
                await GetAllProducts();

            if (!allProducts.Any())
                return null;

            // --------------------------------------------------------
            // 2. Toutes les offres
            // --------------------------------------------------------

            List<AllProductOffer> allOffers =
                await GetAllOffers();

            List<PerfumeNote> perfumeNotes =
                await GetPerfumeNotes();

            // --------------------------------------------------------
            // 3. Attacher les offres aux produits
            // --------------------------------------------------------

            Dictionary<long, List<AllProductOffer>> offersByProduct =
                allOffers
                    .GroupBy(x => x.ProductId)
                    .ToDictionary(
                        x => x.Key,
                        x => x.ToList()
                    );

            foreach (AllProduct product in allProducts)
            {
                if (offersByProduct.TryGetValue(
                        product.Id,
                        out List<AllProductOffer>? offers))
                {
                    product.ProductOffers = offers;
                }
            }

            // --------------------------------------------------------
            // 4. Produit demandé
            // --------------------------------------------------------

            AllProduct? currentProduct =
                allProducts.FirstOrDefault(x =>
                    NormalizeSlug(
                        x.SlugNormalized ??
                        x.Slug ??
                        x.Name
                    ) == slug);

            // fallback : slug exact
            if (currentProduct == null)
            {
                currentProduct =
                    allProducts.FirstOrDefault(x =>
                        NormalizeSlug(x.Slug ?? "") == slug);
            }

            if (currentProduct == null)
                return null;

            // --------------------------------------------------------
            // 5. Calculs
            // --------------------------------------------------------

            List<AllProductOffer> currentOffers =
                currentProduct.ProductOffers
                    .Where(x => x.IsAvailable)
                    .OrderBy(x => x.Price ?? decimal.MaxValue)
                    .ToList();

            JObject naya =
                currentProduct.NayaAttributes ??
                new JObject();

            JObject productData =
                currentProduct.ProductData ??
                new JObject();

            // --------------------------------------------------------
            // 6. Produit
            // --------------------------------------------------------

            JObject productJson =
                BuildProductSection(
                    currentProduct,
                    currentOffers);

            // --------------------------------------------------------
            // 7. Details
            // --------------------------------------------------------

            JObject detailsJson =
                BuildDetailsSection(
                    currentProduct,
                    naya,
                    productData);

            // --------------------------------------------------------
            // 8. Notes
            // --------------------------------------------------------

            JObject notesJson =
                BuildNotesSection(
                    naya,
                    productData,
                    perfumeNotes);

            // --------------------------------------------------------
            // 9. Performance
            // --------------------------------------------------------

            JObject performanceJson =
                BuildPerformanceSection(naya, productData);

            // --------------------------------------------------------
            // 10. Offers
            // --------------------------------------------------------

            JArray offersJson =
                BuildOffersSection(currentOffers);

            // --------------------------------------------------------
            // 11. Price comparison
            // --------------------------------------------------------

            JObject priceComparison =
                BuildPriceComparison(currentOffers);

            // --------------------------------------------------------
            // 12. Alternatives
            // --------------------------------------------------------

            List<ProductMatch> matches =
                FindSimilarProducts(
                    currentProduct,
                    allProducts);

            JObject alternatives =
                BuildAlternativesSection(
                    currentProduct,
                    matches);

            // --------------------------------------------------------
            // 13. Recommendations
            // --------------------------------------------------------

            JObject recommendations =
                BuildRecommendationsSection(
                    currentProduct,
                    matches);

            // --------------------------------------------------------
            // 14. Guide
            // --------------------------------------------------------

            JObject guide =
                BuildGuideSection(currentProduct);

            // --------------------------------------------------------
            // 15. SEO
            // --------------------------------------------------------

            JObject seo =
                BuildSeoSection(
                    currentProduct,
                    detailsJson);

            // --------------------------------------------------------
            // 16. PAGE
            // --------------------------------------------------------

            JObject page =
                new JObject
                {
                    ["type"] = "perfume",
                    ["slug"] = GetProductSlug(currentProduct),
                    ["product_id"] = currentProduct.Id,
                    ["language"] = "fr"
                };

            // --------------------------------------------------------
            // FINAL JSON
            // --------------------------------------------------------

            JObject result =
                new JObject
                {
                    ["page"] = page,
                    ["product"] = productJson,
                    ["details"] = detailsJson,
                    ["notes"] = notesJson,
                    ["performance"] = performanceJson,
                    ["offers"] = offersJson,
                    ["price_comparison"] = priceComparison,
                    ["alternatives"] = alternatives,
                    ["recommendations"] = recommendations,
                    ["guide"] = guide,
                    ["seo"] = seo
                };

            return result.ToString(Newtonsoft.Json.Formatting.Indented);
        }


        // ============================================================
        // PRODUCTS
        // ============================================================

        private async Task<List<AllProduct>> GetAllProducts()
        {
            try
            {
                var result = new List<AllProduct>();

                int offset = 0;
                int pageSize = 1000;

                while (true)
                {
                    string url =
                        $"{supabaseUrl}/rest/v1/products" +
                        "?select=*" +
                        $"&offset={offset}" +
                        $"&limit={pageSize}";

                    Console.WriteLine("URL : " + url);

                    using HttpRequestMessage request =
                        CreateRequest(HttpMethod.Get, url);

                    Console.WriteLine("Avant SendAsync");

                    using HttpResponseMessage response =
                        await httpClient.SendAsync(request);

                    Console.WriteLine("Après SendAsync");

                    string responseBody =
                        await response.Content.ReadAsStringAsync();

                    Console.WriteLine(
                        $"Status : {(int)response.StatusCode} {response.ReasonPhrase}");

                    Console.WriteLine("Response : " + responseBody);

                    if (!response.IsSuccessStatusCode)
                    {
                        throw new Exception(
                            $"Supabase error {(int)response.StatusCode} " +
                            $"{response.ReasonPhrase}: {responseBody}");
                    }

                    var page =
                        JsonConvert.DeserializeObject<List<AllProduct>>(responseBody);

                    if (page == null || page.Count == 0)
                        break;

                    result.AddRange(page);

                    if (page.Count < pageSize)
                        break;

                    offset += pageSize;
                }

                Console.WriteLine($"TOTAL PRODUCTS : {result.Count}");

                return result;
            }
            catch (Exception ex)
            {
                Console.WriteLine("================================");
                Console.WriteLine("ERREUR GetAllProducts");
                Console.WriteLine("Message : " + ex.Message);
                Console.WriteLine("Type    : " + ex.GetType().FullName);
                Console.WriteLine("Stack   : " + ex.StackTrace);

                if (ex.InnerException != null)
                {
                    Console.WriteLine("INNER : " + ex.InnerException.Message);
                    Console.WriteLine("INNER TYPE : " +
                                      ex.InnerException.GetType().FullName);
                }

                Console.WriteLine("================================");

                // Pour Visual Studio, permet de voir l'exception immédiatement
                throw;
            }
        }


        // ============================================================
        // OFFERS
        // ============================================================

        private async Task<List<AllProductOffer>> GetAllOffers()
        {
            var result = new List<AllProductOffer>();

            const int pageSize = 1000;

            int offset = 0;

            while (true)
            {
                try
                {


                    string url =
                        $"{supabaseUrl}/rest/v1/product_offers" +
                        "?select=*" +
                        $"&offset={offset}" +
                        $"&limit={pageSize}";

                    using HttpRequestMessage request =
                        CreateRequest(HttpMethod.Get, url);

                    using HttpResponseMessage response =
                        await httpClient.SendAsync(request);

                    string json =
                        await response.Content.ReadAsStringAsync();

                    if (!response.IsSuccessStatusCode)
                    {
                        throw new Exception(
                            $"Supabase offers error {response.StatusCode}: {json}");
                    }

                    List<AllProductOffer>? page =
                        JsonConvert.DeserializeObject<List<AllProductOffer>>(json);

                    if (page == null || page.Count == 0)
                        break;

                    result.AddRange(page);

                    if (page.Count < pageSize)
                        break;

                    offset += pageSize;
                }
                catch (Exception ex)
                {
                    Console.WriteLine(
                        $"Error fetching offers: {ex.Message}");
                    break;
                }
            }

            return result;
        }


        // ============================================================
        // SUPABASE REQUEST
        // ============================================================

        private HttpRequestMessage CreateRequest(
            HttpMethod method,
            string url)
        {
            var request =
                new HttpRequestMessage(method, url);

            request.Headers.Add(
                "apikey",
                supabaseKey);

            request.Headers.Add(
                "Authorization",
                $"Bearer {supabaseKey}");

            request.Headers.Add(
                "Accept",
                "application/json");

            return request;
        }


        // ============================================================
        // PRODUCT
        // ============================================================

        private JObject BuildProductSection(
            AllProduct product,
            List<AllProductOffer> offers)
        {
            decimal? price =
                GetLowestPrice(offers);

            decimal? originalPrice =
                GetReferencePrice(offers);

            return new JObject
            {
                ["id"] = product.Id,

                ["brand"] =
                             GetDisplayBrand(product),

                ["name"] =
                    product.Name,

                ["short_name"] =
                    product.ShortName,

                ["type"] =
                    product.Type,

                ["image"] =
                    product.Image,

                ["images"] =
                    GetImages(product),

                ["description"] =
                    product.Description,

                ["category"] =
                    product.Category,

                ["sub_category"] =
                    product.SubCategory,

                ["rating"] =
                    GetAverageRating(offers),

                ["reviews"] =
                    GetTotalReviews(offers),

                ["ean"] =
                    GetEan(product),

                ["manufacturer_product_id"] =
                    GetManufacturerProductId(product),

                ["price"] =
                    price,

                ["original_price"] =
                    originalPrice,

                ["currency"] =
                    GetReferenceCurrency(offers),

                ["slug"] =
                    GetProductSlug(product)
            };
        }


        // ============================================================
        // DETAILS
        // ============================================================

        private JObject BuildDetailsSection(
            AllProduct product,
            JObject naya,
            JObject productData)
        {
            JObject sourceDetails =
                productData["details"] as JObject
                ?? new JObject();

            return new JObject
            {
                ["gender"] =
                    GetString(
                        naya,
                        "gender",
                        sourceDetails["Gender"]),

                ["concentration"] =
                    GetString(
                        naya,
                        "concentration",
                        sourceDetails["Type"] ??
                        product.Type),

                ["fragrance_family"] =
                    GetArrayOrValue(
                        naya["fragrance_family"],
                        sourceDetails["OlfactoryFamily"]),

                ["olfactory_family"] =
                    GetString(
                        naya,
                        "olfactory_family",
                        sourceDetails["OlfactoryFamily"]),

                ["year"] =
                    GetValue(
                        sourceDetails,
                        "Year"),

                ["perfumer"] =
                    GetValue(
                        sourceDetails,
                        "Perfumer"),

                ["volume"] =
                    GetVolume(
                        product,
                        sourceDetails),

                ["style"] =
                    GetArray(
                        naya["style"]),

                ["season"] =
                    GetArray(
                        naya["season"]),

                ["occasion"] =
                    GetArray(
                        naya["occasion"]),

                ["intensity"] =
                    GetNumber(
                        naya,
                        "intensity"),

                ["longevity"] =
                    GetNumber(
                        naya,
                        "longevity"),

                ["sillage"] =
                    GetNumber(
                        naya,
                        "sillage"),

                ["best_for"] =
                    GetValue(
                        sourceDetails,
                        "BestFor")
                    ?? GetArray(
                        naya["occasion"])
            };
        }


        // ============================================================
        // NOTES
        // ============================================================

        private JObject BuildNotesSection(
            JObject naya,
            JObject productData,
            List<PerfumeNote> perfumeNotes)
        {
            JObject sourceNotes =
                productData["notes"] as JObject
                ?? productData["Notes"] as JObject
                ?? new JObject();

            JToken? topSource =
                sourceNotes["top"];

            JToken? heartSource =
                sourceNotes["heart"];

            JToken? baseSource =
                sourceNotes["base"];

            JArray topNotes =
                GetArrayOrValue(
                    naya["top_notes"],
                    topSource) as JArray
                ?? new JArray();

            JArray heartNotes =
                GetArrayOrValue(
                    naya["heart_notes"],
                    heartSource) as JArray
                ?? new JArray();

            JArray baseNotes =
                GetArrayOrValue(
                    naya["base_notes"],
                    baseSource) as JArray
                ?? new JArray();

            JArray topItems =
                BuildNoteItems(
                    topNotes,
                    "tete",
                    perfumeNotes);

            JArray heartItems =
                BuildNoteItems(
                    heartNotes,
                    "coeur",
                    perfumeNotes);

            JArray baseItems =
                BuildNoteItems(
                    baseNotes,
                    "fond",
                    perfumeNotes);

            return new JObject
            {
                ["top"] = new JObject
                {
                    ["title"] = "Notes de tête",
                    ["items"] = topItems
                },

                ["heart"] = new JObject
                {
                    ["title"] = "Notes de cœur",
                    ["items"] = heartItems
                },

                ["base"] = new JObject
                {
                    ["title"] = "Notes de fond",
                    ["items"] = baseItems
                }
            };
        }


        private JArray BuildNoteItems(
            JArray notes,
            string type,
            List<PerfumeNote> perfumeNotes)
        {
            var result =
                new JArray();

            foreach (JToken note in notes)
            {
                string name =
                    note?.ToString()?.Trim() ?? "";

                if (string.IsNullOrWhiteSpace(name))
                    continue;

                result.Add(
                    BuildPerfumeNote(
                        name,
                        type,
                        perfumeNotes));
            }

            return result;
        }


        private JObject BuildPerfumeNote(
            string noteName,
            string type,
            List<PerfumeNote> perfumeNotes)
        {
            PerfumeNote? note =
                FindPerfumeNote(
                    noteName,
                    type,
                    perfumeNotes);

            return new JObject
            {
                ["name"] = noteName,
                ["image"] = note?.ImageUrl
            };
        }


        private PerfumeNote? FindPerfumeNote(
            string noteName,
            string type,
            List<PerfumeNote> perfumeNotes)
        {
            string normalizedName =
                NormalizeNoteName(noteName);

            string normalizedType =
                NormalizeNoteType(type);

            return perfumeNotes.FirstOrDefault(x =>
                x.IsActive &&
                NormalizeNoteType(x.Type) == normalizedType &&
                (
                    NormalizeNoteName(x.NameEn) == normalizedName ||
                    NormalizeNoteName(x.NameFr) == normalizedName
                ));
        }


        private string NormalizeNoteName(
            string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";

            string normalized =
                value.Trim().ToLowerInvariant();

            normalized =
                RemoveAccents(normalized);

            normalized =
                normalized
                    .Replace("-", " ")
                    .Replace("_", " ");

            normalized =
                Regex.Replace(
                    normalized,
                    @"[^a-z0-9]+",
                    " ");

            normalized =
                Regex.Replace(
                    normalized,
                    @"\s+",
                    " ");

            return normalized.Trim();
        }


        private string NormalizeNoteType(
            string? type)
        {
            if (string.IsNullOrWhiteSpace(type))
                return "";

            string value =
                type.Trim().ToLowerInvariant();

            value =
                RemoveAccents(value);

            return value switch
            {
                "top" => "tete",
                "tetes" => "tete",
                "heart" => "coeur",
                "base" => "fond",
                _ => value
            };
        }


        // ============================================================
        // PERFORMANCE
        // ============================================================

        private JObject BuildPerformanceSection(
     JObject naya,
     JObject productData)
        {
            JObject sourceDetails =
                productData["details"] as JObject
                ?? new JObject();

            int? intensity =
                GetNumber(naya, "intensity");

            int? longevity =
                GetNumber(naya, "longevity");

            int? sillage =
                GetNumber(naya, "sillage");

            string? gender =
                naya["gender"]?.ToString();

            bool iconic =
                naya["iconic"]?.Value<bool>() ?? false;

            // Fallback depuis details
            if (!intensity.HasValue)
                intensity =
                    sourceDetails["intensity"]?.Value<int?>();

            if (!longevity.HasValue)
                longevity =
                    sourceDetails["longevity"]?.Value<int?>();

            if (!sillage.HasValue)
                sillage =
                    sourceDetails["sillage"]?.Value<int?>();

            if (string.IsNullOrWhiteSpace(gender))
                gender =
                    sourceDetails["gender"]?.ToString();

            var features =
                new JArray();

            // ----------------------------------------------------
            // SILLAGE
            // ----------------------------------------------------

            if (sillage.HasValue)
            {
                features.Add(
                    new JObject
                    {
                        ["type"] = "sillage",
                        ["value"] = sillage.Value,
                        ["label"] =
                            GetSillageLabel(
                                sillage.Value)
                    });
            }

            // ----------------------------------------------------
            // LONGEVITY
            // ----------------------------------------------------

            if (longevity.HasValue)
            {
                features.Add(
                    new JObject
                    {
                        ["type"] = "longevity",
                        ["value"] = longevity.Value,
                        ["label"] =
                            GetLongevityLabel(
                                longevity.Value)
                    });
            }

            // ----------------------------------------------------
            // ICONIQUE
            // ----------------------------------------------------

            if (iconic)
            {
                features.Add(
                    new JObject
                    {
                        ["type"] = "iconic",
                        ["label"] = "Parfum iconique"
                    });
            }

            // ----------------------------------------------------
            // GENRE
            // ----------------------------------------------------

            if (!string.IsNullOrWhiteSpace(gender))
            {
                features.Add(
                    new JObject
                    {
                        ["type"] = "gender",
                        ["label"] = gender
                    });
            }

            return new JObject
            {
                ["intensity"] = intensity,
                ["longevity"] = longevity,
                ["sillage"] = sillage,
                ["iconic"] = iconic,
                ["gender"] = gender,
                ["features"] = features
            };
        }


        private string GetPerformanceLabel(
            string name,
            int value)
        {
            return value switch
            {
                1 => $"{name} faible",
                2 => $"{name} légère",
                3 => $"{name} moyenne",
                4 => $"{name} forte",
                5 => $"{name} très forte",
                _ => name
            };
        }


        // ============================================================
        // OFFERS
        // ============================================================

        private JArray BuildOffersSection(
            List<AllProductOffer> offers)
        {
            var result =
                new JArray();

            foreach (AllProductOffer offer in offers)
            {
                JObject marketplaceData =
                    offer.MarketplaceData ??
                    new JObject();

                decimal? originalPrice =
                    GetDecimal(
                        marketplaceData,
                        "original_price");

                string? image =
                    GetString(
                        marketplaceData,
                        "image");

                result.Add(
                    new JObject
                    {
                        ["id"] =
                            offer.Id,

                        ["marketplace"] =
                            offer.Marketplace,

                        ["marketplace_product_id"] =
                            offer.MarketplaceProductId,

                        ["sku"] =
                            offer.Sku,

                        ["variant_name"] =
                            offer.VariantName,

                        ["price"] =
                            offer.Price,

                        ["currency"] =
                            offer.Currency,

                        ["original_price"] =
                            originalPrice,

                        ["is_available"] =
                            offer.IsAvailable,

                        ["rating"] =
                            offer.Rating,

                        ["reviews"] =
                            offer.Reviews,

                        ["product_url"] =
                            offer.ProductUrl,

                        ["image"] =
                            image
                    });
            }

            return result;
        }


        // ============================================================
        // PRICE COMPARISON
        // ============================================================

        private JObject BuildPriceComparison(
            List<AllProductOffer> offers)
        {
            List<AllProductOffer> validOffers =
                offers
                    .Where(x =>
                        x.IsAvailable &&
                        x.Price.HasValue &&
                        x.Price.Value > 0)
                    .OrderBy(x => x.Price)
                    .ToList();

            if (!validOffers.Any())
            {
                return new JObject
                {
                    ["currency"] = "EUR",
                    ["lowest_price"] = null,
                    ["lowest_price_marketplace"] = null,
                    ["reference_price"] = null,
                    ["saving_vs_reference"] = null,
                    ["saving_percent"] = null,
                    ["offers_count"] = 0,
                    ["offers"] = new JArray()
                };
            }

            AllProductOffer cheapest =
                validOffers.First();

            decimal lowest =
                cheapest.Price!.Value;

            decimal reference =
                GetReferencePrice(validOffers)
                ?? lowest;

            decimal saving =
                Math.Max(
                    0,
                    reference - lowest);

            decimal savingPercent =
                reference > 0
                    ? Math.Round(
                        saving / reference * 100,
                        2)
                    : 0;

            return new JObject
            {
                ["currency"] =
                    cheapest.Currency ?? "EUR",

                ["lowest_price"] =
                    lowest,

                ["lowest_price_marketplace"] =
                    cheapest.Marketplace,

                ["reference_price"] =
                    reference,

                ["saving_vs_reference"] =
                    saving,

                ["saving_percent"] =
                    savingPercent,

                ["offers_count"] =
                    validOffers.Count,

                ["offers"] =
                    BuildPriceOfferList(validOffers)
            };
        }


        private JArray BuildPriceOfferList(
            List<AllProductOffer> offers)
        {
            var result =
                new JArray();

            foreach (AllProductOffer offer in offers)
            {
                result.Add(
                    new JObject
                    {
                        ["marketplace"] =
                            offer.Marketplace,

                        ["price"] =
                            offer.Price,

                        ["currency"] =
                            offer.Currency,

                        ["product_url"] =
                            offer.ProductUrl,

                        ["is_available"] =
                            offer.IsAvailable
                    });
            }

            return result;
        }


        // ============================================================
        // SIMILAR PRODUCTS
        // ============================================================

        // ============================================================
        // SIMILAR PRODUCTS
        // ============================================================

        // ============================================================
        // SIMILAR PRODUCTS
        // ============================================================

        private List<ProductMatch> FindSimilarProducts(
            AllProduct current,
            List<AllProduct> allProducts)
        {
            var result = new List<ProductMatch>();

            foreach (AllProduct candidate in allProducts)
            {
                if (candidate.Id == current.Id)
                    continue;

                if (!IsPerfume(candidate))
                    continue;

                double score =
                    CalculateSimilarity(
                        current.NayaAttributes,
                        candidate.NayaAttributes);

                List<string> matchedNotes =
                    GetMatchedNotes(
                        current.NayaAttributes,
                        candidate.NayaAttributes);

                // IMPORTANT :
                // Ne plus éliminer les produits avec score < 50.
                result.Add(
                    new ProductMatch
                    {
                        Product = candidate,

                        SimilarityScore =
                            Math.Round(score, 2),

                        MatchedNotes =
                            matchedNotes,

                        Reason =
                            BuildSimilarityReason(
                                current,
                                candidate,
                                score,
                                matchedNotes)
                    });
            }

            return result
                .OrderByDescending(x => x.SimilarityScore)
                .ToList();
        }


        // ============================================================
        // SIMILARITY
        // ============================================================

        private double CalculateSimilarity(
            JObject a,
            JObject b)
        {
            double score = 0;

            // --------------------------------------------------------
            // Fragrance family 20
            // --------------------------------------------------------

            score +=
                SimilarityArray(
                    a["fragrance_family"],
                    b["fragrance_family"]) * 20;

            // --------------------------------------------------------
            // Top notes 10
            // --------------------------------------------------------

            score +=
                SimilarityArray(
                    a["top_notes"],
                    b["top_notes"]) * 10;

            // --------------------------------------------------------
            // Heart notes 15
            // --------------------------------------------------------

            score +=
                SimilarityArray(
                    a["heart_notes"],
                    b["heart_notes"]) * 15;

            // --------------------------------------------------------
            // Base notes 20
            // --------------------------------------------------------

            score +=
                SimilarityArray(
                    a["base_notes"],
                    b["base_notes"]) * 20;

            // --------------------------------------------------------
            // Gender 5
            // --------------------------------------------------------

            score +=
                SimilarityValue(
                    a["gender"],
                    b["gender"]) * 5;

            // --------------------------------------------------------
            // Concentration 5
            // --------------------------------------------------------

            score +=
                SimilarityValue(
                    a["concentration"],
                    b["concentration"]) * 5;

            // --------------------------------------------------------
            // Style 8
            // --------------------------------------------------------

            score +=
                SimilarityArray(
                    a["style"],
                    b["style"]) * 8;

            // --------------------------------------------------------
            // Season 4
            // --------------------------------------------------------

            score +=
                SimilarityArray(
                    a["season"],
                    b["season"]) * 4;

            // --------------------------------------------------------
            // Occasion 4
            // --------------------------------------------------------

            score +=
                SimilarityArray(
                    a["occasion"],
                    b["occasion"]) * 4;

            // --------------------------------------------------------
            // Intensity 3
            // --------------------------------------------------------

            score +=
                NumericSimilarity(
                    a["intensity"],
                    b["intensity"]) * 3;

            // --------------------------------------------------------
            // Longevity 3
            // --------------------------------------------------------

            score +=
                NumericSimilarity(
                    a["longevity"],
                    b["longevity"]) * 3;

            // --------------------------------------------------------
            // Sillage 3
            // --------------------------------------------------------

            score +=
                NumericSimilarity(
                    a["sillage"],
                    b["sillage"]) * 3;

            return Math.Min(
                100,
                Math.Max(
                    0,
                    score));
        }


        // ============================================================
        // ARRAY SIMILARITY
        // ============================================================

        private double SimilarityArray(
            JToken? a,
            JToken? b)
        {
            HashSet<string> setA =
                ToStringSet(a);

            HashSet<string> setB =
                ToStringSet(b);

            if (!setA.Any() || !setB.Any())
                return 0;

            int intersection =
                setA.Intersect(setB).Count();

            int union =
                setA.Union(setB).Count();

            if (union == 0)
                return 0;

            return (double)intersection / union;
        }


        private double SimilarityValue(
            JToken? a,
            JToken? b)
        {
            string? valueA =
                NormalizeValue(
                    a?.ToString());

            string? valueB =
                NormalizeValue(
                    b?.ToString());

            if (string.IsNullOrWhiteSpace(valueA) ||
                string.IsNullOrWhiteSpace(valueB))
            {
                return 0;
            }

            return valueA == valueB
                ? 1
                : 0;
        }


        private double NumericSimilarity(
            JToken? a,
            JToken? b)
        {
            if (!int.TryParse(
                    a?.ToString(),
                    out int valueA))
            {
                return 0;
            }

            if (!int.TryParse(
                    b?.ToString(),
                    out int valueB))
            {
                return 0;
            }

            int difference =
                Math.Abs(valueA - valueB);

            return difference switch
            {
                0 => 1,
                1 => 0.7,
                2 => 0.4,
                3 => 0.2,
                _ => 0
            };
        }


        // ============================================================
        // MATCHED NOTES
        // ============================================================

        private List<string> GetMatchedNotes(
            JObject a,
            JObject b)
        {
            var result =
                new List<string>();

            string[] keys =
            {
            "top_notes",
            "heart_notes",
            "base_notes"
        };

            foreach (string key in keys)
            {
                HashSet<string> setA =
                    ToStringSet(a[key]);

                HashSet<string> setB =
                    ToStringSet(b[key]);

                foreach (string note in
                         setA.Intersect(setB))
                {
                    if (!result.Contains(note))
                        result.Add(note);
                }
            }

            return result
                .Take(8)
                .ToList();
        }


        // ============================================================
        // REASON
        // ============================================================

        private string BuildSimilarityReason(
            AllProduct current,
            AllProduct candidate,
            double score,
            List<string> matchedNotes)
        {
            var reasons =
                new List<string>();

            JObject a =
                current.NayaAttributes;

            JObject b =
                candidate.NayaAttributes;

            double familyScore =
                SimilarityArray(
                    a["fragrance_family"],
                    b["fragrance_family"]);

            if (familyScore >= 0.5)
            {
                reasons.Add(
                    "même famille olfactive");
            }

            if (matchedNotes.Any())
            {
                reasons.Add(
                    $"notes communes : {string.Join(", ", matchedNotes.Take(3))}");
            }

            if (SimilarityValue(
                    a["gender"],
                    b["gender"]) == 1)
            {
                reasons.Add(
                    "même profil de genre");
            }

            if (SimilarityArray(
                    a["style"],
                    b["style"]) >= 0.5)
            {
                reasons.Add(
                    "style similaire");
            }

            if (!reasons.Any())
            {
                reasons.Add(
                    "profil olfactif proche");
            }

            return string.Join(
                ", ",
                reasons)
                + ".";
        }


        // ============================================================
        // ALTERNATIVES
        // ============================================================

        private JObject BuildAlternativesSection(
            AllProduct current,
            List<ProductMatch> matches)
        {
            List<ProductMatch> selected =
                SelectSixAlternatives(
                    current,
                    matches);

            var products =
                new JArray();

            decimal referencePrice =
                GetReferencePrice(
                    current.ProductOffers)
                ?? 0;

            foreach (ProductMatch match in selected)
            {
                products.Add(
                    BuildAlternativeProduct(
                        current,
                        match,
                        referencePrice));
            }

            return new JObject
            {
                ["title"] =
                    "Des alternatives similaires",

                ["description"] =
                    "Découvrez des parfums au profil olfactif proche, avec des caractéristiques et des prix différents.",

                ["reference_price"] =
                    referencePrice,

                ["reference_currency"] =
                    GetReferenceCurrency(
                        current.ProductOffers),

                ["products"] =
                    products
            };
        }


        // ============================================================
        // SELECT 6 ALTERNATIVES
        // ============================================================

        private List<ProductMatch> SelectSixAlternatives(
            AllProduct current,
            List<ProductMatch> matches)
        {
            var candidates =
                matches
                    .Where(x =>
                        x.Product.ProductOffers.Any(o =>
                            o.IsAvailable &&
                            o.Price.HasValue &&
                            o.Price.Value > 0))
                    .ToList();

            decimal currentPrice =
                GetLowestPrice(
                    current.ProductOffers)
                ?? decimal.MaxValue;

            // --------------------------------------------------------
            // On favorise :
            // 1. forte similarité
            // 2. prix inférieur
            // 3. économie
            // --------------------------------------------------------

            foreach (ProductMatch match in candidates)
            {
                decimal candidatePrice =
                    GetLowestPrice(
                        match.Product.ProductOffers)
                    ?? decimal.MaxValue;

                bool cheaper =
                    candidatePrice < currentPrice;

                match.IsCheaper =
                    cheaper;

                match.Price =
                    candidatePrice;

                match.Savings =
                    currentPrice != decimal.MaxValue &&
                    candidatePrice != decimal.MaxValue
                        ? Math.Max(
                            0,
                            currentPrice -
                            candidatePrice)
                        : 0;

                match.SelectionScore =
                    match.SimilarityScore;

                if (cheaper)
                {
                    match.SelectionScore += 5;

                    if (candidatePrice <= currentPrice * 0.85m)
                        match.SelectionScore += 5;
                }
            }

            return candidates
                .OrderByDescending(x =>
                    x.SelectionScore)
                .ThenByDescending(x =>
                    x.SimilarityScore)
                .Take(6)
                .ToList();
        }


        // ============================================================
        // ALTERNATIVE PRODUCT JSON
        // ============================================================

        private JObject BuildAlternativeProduct(
            AllProduct current,
            ProductMatch match,
            decimal referencePrice)
        {
            AllProduct product =
                match.Product;

            List<AllProductOffer> offers =
                product.ProductOffers
                    .Where(x =>
                        x.IsAvailable &&
                        x.Price.HasValue)
                    .OrderBy(x => x.Price)
                    .ToList();

            decimal? lowestPrice =
                GetLowestPrice(offers);

            decimal saving =
                0;

            decimal savingPercent =
                0;

            if (referencePrice > 0 &&
                lowestPrice.HasValue)
            {
                saving =
                    Math.Max(
                        0,
                        referencePrice -
                        lowestPrice.Value);

                savingPercent =
                    Math.Round(
                        saving /
                        referencePrice *
                        100,
                        2);
            }

            return new JObject
            {
                ["product_id"] =
                    product.Id,

                ["brand"] =
                        GetDisplayBrand(product),

                ["name"] =
                    product.Name,

                ["short_name"] =
                    product.ShortName,

                ["image"] =
                    product.Image,

                ["price"] =
                    lowestPrice,

                ["currency"] =
                    GetReferenceCurrency(
                        offers),

                ["original_price"] =
                    GetReferencePrice(offers),

                ["saving_amount"] =
                    saving,

                ["saving_percent"] =
                    savingPercent,

                ["similarity_score"] =
                    match.SimilarityScore,

                ["fragrance_family"] =
                    product.NayaAttributes[
                        "fragrance_family"],

                ["matched_notes"] =
                    new JArray(
                        match.MatchedNotes),

                ["reason"] =
                    match.Reason,

                ["product_url"] =
                    GetBestOfferUrl(offers),

                ["marketplace"] =
                    GetBestMarketplace(offers),

                ["offers"] =
                    BuildOffersSection(offers)
            };
        }


        // ============================================================
        // RECOMMENDATIONS
        // ============================================================

        private JObject BuildRecommendationsSection(
     AllProduct current,
     List<ProductMatch> matches)
        {
            // --------------------------------------------------------
            // 1. On récupère d'abord les alternatives
            // --------------------------------------------------------

            List<ProductMatch> alternatives =
                SelectSixAlternatives(
                    current,
                    matches);

            HashSet<long> alternativeIds =
                alternatives
                    .Select(x => x.Product.Id)
                    .ToHashSet();

            // --------------------------------------------------------
            // 2. Les recommandations doivent être différentes
            //    des alternatives et du produit actuel
            // --------------------------------------------------------

            List<ProductMatch> recommendationCandidates =
                matches
                    .Where(x =>
                        x.Product.Id != current.Id &&
                        !alternativeIds.Contains(x.Product.Id))
                    .ToList();

            // --------------------------------------------------------
            // 3. Sélection des recommandations
            // --------------------------------------------------------

            List<ProductMatch> selected =
                SelectSixRecommendations1(
                    current,
                    recommendationCandidates);

            var products =
                new JArray();

            foreach (ProductMatch match in selected)
            {
                AllProduct product =
                    match.Product;

                List<AllProductOffer> offers =
                    product.ProductOffers
                        .Where(x =>
                            x.IsAvailable &&
                            x.Price.HasValue &&
                            x.Price.Value > 0)
                        .OrderBy(x => x.Price)
                        .ToList();

                decimal? price =
                    GetLowestPrice(offers);

                decimal? currentPrice =
                    GetLowestPrice(
                        current.ProductOffers);

                decimal saving = 0;
                decimal savingPercent = 0;

                if (price.HasValue &&
                    currentPrice.HasValue &&
                    currentPrice.Value > price.Value)
                {
                    saving =
                        currentPrice.Value -
                        price.Value;

                    savingPercent =
                        Math.Round(
                            saving /
                            currentPrice.Value *
                            100,
                            2);
                }

                products.Add(
                    new JObject
                    {
                        ["product_id"] =
                            product.Id,

                        ["brand"] =
                           GetDisplayBrand(product),

                        ["name"] =
                            product.Name,

                        ["short_name"] =
                            product.ShortName,

                        ["image"] =
                            product.Image,

                        ["price"] =
                            price,

                        ["currency"] =
                            GetReferenceCurrency(
                                offers),

                        ["original_price"] =
                            GetReferencePrice(
                                offers),

                        ["saving_amount"] =
                            saving,

                        ["saving_percent"] =
                            savingPercent,

                        ["similarity_score"] =
                            match.SimilarityScore,

                        ["fragrance_family"] =
                            product.NayaAttributes[
                                "fragrance_family"],

                        ["matched_notes"] =
                            new JArray(
                                match.MatchedNotes),

                        ["reason"] =
                            match.Reason,

                        ["product_url"] =
                            GetBestOfferUrl(offers),

                        ["marketplace"] =
                            GetBestMarketplace(offers)
                    });
            }

            return new JObject
            {
                ["title"] =
                    "Vous pourriez aussi aimer",

                ["description"] =
                    "Des parfums sélectionnés selon le profil olfactif, le style et les caractéristiques de ce parfum.",

                ["products"] =
                    products
            };
        }

        private List<ProductMatch> SelectSixRecommendations1(
        AllProduct current,
        List<ProductMatch> matches)
        {
            // Les produits déjà utilisés comme alternatives
            HashSet<long> alternativeIds =
                SelectSixAlternatives(
                    current,
                    matches)
                .Select(x => x.Product.Id)
                .ToHashSet();

            return matches
                .Where(x =>
                    x.Product.Id != current.Id &&
                    !alternativeIds.Contains(x.Product.Id) &&
                    x.Product.ProductOffers.Any(o =>
                        o.IsAvailable &&
                        o.Price.HasValue &&
                        o.Price.Value > 0))
                .OrderByDescending(x =>
                    x.SimilarityScore)
                .Take(6)
                .ToList();
        }
        private List<ProductMatch> SelectSixRecommendations(
            AllProduct current,
            List<ProductMatch> matches)
        {
            return matches
                .Where(x =>
                    x.Product.ProductOffers.Any(o =>
                        o.IsAvailable &&
                        o.Price.HasValue))
                .OrderByDescending(x =>
                    x.SimilarityScore)
                .Take(6)
                .ToList();
        }


        // ============================================================
        // GUIDE
        // ============================================================

        private JObject BuildGuideSection(
            AllProduct product)
        {
            string slug =
                GetProductSlug(product);

            return new JObject
            {
                ["available"] = true,

                ["id"] =
                    $"guide-{product.Id}",

                ["slug"] =
                    slug,

                ["title"] =
                    $"Guide complet {product.Name}",

                ["subtitle"] =
                    $"Tout savoir sur {product.Name}",

                ["image"] =
                    product.Image,

                ["url"] =
                    $"/guide/{slug}",

                ["reading_time"] =
                    5,

                ["sections"] =
                    new JArray()
            };
        }


        // ============================================================
        // SEO
        // ============================================================

        private JObject BuildSeoSection(
            AllProduct product,
            JObject details)
        {
            string brand =
                product.Brand ?? "";

            string name =
                product.Name;

            string concentration =
                details["concentration"]?.ToString()
                ?? "";

            string title =
                $"{brand} {name}";

            if (!string.IsNullOrWhiteSpace(concentration) &&
                !title.Contains(
                    concentration,
                    StringComparison.OrdinalIgnoreCase))
            {
                title +=
                    $" {concentration}";
            }

            string description =
                $"Découvrez {name} de {brand} : notes olfactives, performance, prix et alternatives similaires.";

            string slug =
                GetProductSlug(product);

            return new JObject
            {
                ["title"] =
                    title,

                ["description"] =
                    description,

                ["canonical"] =
                    $"/parfum/{slug}",

                ["keywords"] =
                    new JArray
                    {
                    name,
                    brand,
                    "parfum",
                    "fragrance",
                    "notes olfactives",
                    "alternative parfum",
                    "comparateur parfum"
                    }
            };
        }


        // ============================================================
        // PRODUCT IDENTITY
        // ============================================================

        private bool IsPerfume(
            AllProduct product)
        {
            string text =
                $"{product.Category} " +
                $"{product.SubCategory} " +
                $"{product.Type} " +
                $"{product.Name}";

            string normalized =
                NormalizeValue(text) ?? "";

            string[] keywords =
            {
            "parfum",
            "parfumerie",
            "eau de parfum",
            "eau de toilette",
            "fragrance",
            "perfume",
            "cologne"
        };

            return keywords.Any(
                normalized.Contains);
        }


        private string GetProductSlug(
            AllProduct product)
        {
            if (!string.IsNullOrWhiteSpace(
                    product.SlugNormalized))
            {
                return product.SlugNormalized;
            }

            if (!string.IsNullOrWhiteSpace(
                    product.Slug))
            {
                return product.Slug;
            }

            return NormalizeSlug(
                product.Name);
        }


        // ============================================================
        // PRICES
        // ============================================================

        private decimal? GetLowestPrice(
            IEnumerable<AllProductOffer> offers)
        {
            return offers
                .Where(x =>
                    x.IsAvailable &&
                    x.Price.HasValue &&
                    x.Price.Value > 0)
                .Select(x => x.Price)
                .OrderBy(x => x)
                .FirstOrDefault();
        }


        private decimal? GetReferencePrice(
            IEnumerable<AllProductOffer> offers)
        {
            List<decimal> prices =
                offers
                    .Where(x =>
                        x.IsAvailable &&
                        x.Price.HasValue &&
                        x.Price.Value > 0)
                    .Select(x => x.Price!.Value)
                    .OrderByDescending(x => x)
                    .ToList();

            if (!prices.Any())
                return null;

            // Le prix de référence est le prix
            // le plus élevé actuellement disponible.
            return prices.First();
        }


        private string GetReferenceCurrency(
            IEnumerable<AllProductOffer> offers)
        {
            return offers
                .Where(x =>
                    x.IsAvailable &&
                    !string.IsNullOrWhiteSpace(
                        x.Currency))
                .Select(x => x.Currency)
                .FirstOrDefault()
                ?? "EUR";
        }


        private string? GetBestOfferUrl(
            IEnumerable<AllProductOffer> offers)
        {
            return offers
                .Where(x =>
                    x.IsAvailable &&
                    !string.IsNullOrWhiteSpace(
                        x.ProductUrl))
                .OrderBy(x => x.Price)
                .Select(x => x.ProductUrl)
                .FirstOrDefault();
        }


        private string? GetBestMarketplace(
            IEnumerable<AllProductOffer> offers)
        {
            return offers
                .Where(x =>
                    x.IsAvailable &&
                    x.Price.HasValue)
                .OrderBy(x => x.Price)
                .Select(x => x.Marketplace)
                .FirstOrDefault();
        }


        // ============================================================
        // RATING / REVIEWS
        // ============================================================

        private decimal? GetAverageRating(
            IEnumerable<AllProductOffer> offers)
        {
            List<decimal> ratings =
                offers
                    .Where(x =>
                        x.Rating.HasValue &&
                        x.Rating.Value > 0)
                    .Select(x => x.Rating!.Value)
                    .ToList();

            if (!ratings.Any())
                return null;

            return Math.Round(
                ratings.Average(),
                2);
        }


        private int GetTotalReviews(
            IEnumerable<AllProductOffer> offers)
        {
            return offers
                .Where(x => x.Reviews > 0)
                .Sum(x => x.Reviews);
        }


        // ============================================================
        // IMAGES
        // ============================================================

        private JArray GetImages(
            AllProduct product)
        {
            var result =
                new JArray();

            if (!string.IsNullOrWhiteSpace(
                    product.Image))
            {
                result.Add(
                    product.Image);
            }

            // Cherche éventuellement les images
            // dans product_data
            JObject data =
                product.ProductData ??
                new JObject();

            JToken? images =
                data["images"];

            if (images is JArray array)
            {
                foreach (JToken image in array)
                {
                    string? url =
                        image.ToString();

                    if (!string.IsNullOrWhiteSpace(url) &&
                        !result.Any(x =>
                            x.ToString() == url))
                    {
                        result.Add(url);
                    }
                }
            }

            return result;
        }


        // ============================================================
        // EAN
        // ============================================================

        private string? GetEan(
            AllProduct product)
        {
            JObject data =
                product.ProductData ??
                new JObject();

            return
                data["ean"]?.ToString()
                ?? data["EAN"]?.ToString()
                ?? data["gtin"]?.ToString()
                ?? data["GTIN"]?.ToString();
        }


        private string? GetManufacturerProductId(
            AllProduct product)
        {
            JObject data =
                product.ProductData ??
                new JObject();

            return
                data["manufacturer_product_id"]?.ToString()
                ?? data["manufacturerProductId"]?.ToString()
                ?? data["product_id"]?.ToString();
        }


        // ============================================================
        // VOLUME
        // ============================================================

        private string? GetVolume(
            AllProduct product,
            JObject details)
        {
            string? volume =
                details["Volume"]?.ToString();

            if (!string.IsNullOrWhiteSpace(volume))
                return volume;

            JObject comparison =
                product.ProductData?["comparison"]
                as JObject
                ?? new JObject();

            return
                comparison["volume"]?.ToString()
                ?? product.ProductData?["volume"]?.ToString();
        }


        // ============================================================
        // JSON HELPERS
        // ============================================================

        private string? GetString(
            JObject obj,
            string key,
            JToken? fallback = null)
        {
            string? value =
                obj[key]?.ToString();

            if (!string.IsNullOrWhiteSpace(value))
                return value;

            return fallback?.ToString();
        }


        private JToken? GetValue(
            JObject obj,
            string key)
        {
            return obj[key];
        }


        private int? GetNumber(
            JObject obj,
            string key)
        {
            if (obj[key] == null)
                return null;

            if (int.TryParse(
                    obj[key]!.ToString(),
                    out int value))
            {
                return value;
            }

            return null;
        }


        private decimal? GetDecimal(
            JObject obj,
            string key)
        {
            if (obj[key] == null)
                return null;

            if (decimal.TryParse(
                    obj[key]!.ToString(),
                    NumberStyles.Any,
                    CultureInfo.InvariantCulture,
                    out decimal value))
            {
                return value;
            }

            return null;
        }


        private JToken? GetArrayOrValue(
            JToken? preferred,
            JToken? fallback)
        {
            if (preferred != null &&
                preferred.Type != JTokenType.Null)
            {
                return preferred;
            }

            return fallback;
        }


        private JArray GetArray(
            JToken? token)
        {
            if (token == null)
                return new JArray();

            if (token is JArray array)
                return array;

            if (token.Type == JTokenType.String)
            {
                string text =
                    token.ToString();

                if (string.IsNullOrWhiteSpace(text))
                    return new JArray();

                return new JArray(text);
            }

            return new JArray();
        }


        // ============================================================
        // STRING SET
        // ============================================================

        private HashSet<string> ToStringSet(
            JToken? token)
        {
            var result =
                new HashSet<string>();

            if (token == null)
                return result;

            if (token is JArray array)
            {
                foreach (JToken item in array)
                {
                    string? value =
                        NormalizeValue(
                            item.ToString());

                    if (!string.IsNullOrWhiteSpace(value))
                        result.Add(value);
                }
            }
            else
            {
                string? value =
                    NormalizeValue(
                        token.ToString());

                if (!string.IsNullOrWhiteSpace(value))
                    result.Add(value);
            }

            return result;
        }


        // ============================================================
        // NORMALIZATION
        // ============================================================

        private string? NormalizeValue(
            string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            string normalized =
                value
                    .Trim()
                    .ToLowerInvariant();

            normalized =
                RemoveAccents(normalized);

            normalized =
                normalized
                    .Replace("é", "e")
                    .Replace("è", "e")
                    .Replace("ê", "e")
                    .Replace("à", "a")
                    .Replace("â", "a")
                    .Replace("î", "i")
                    .Replace("ï", "i")
                    .Replace("ô", "o")
                    .Replace("û", "u")
                    .Replace("ü", "u")
                    .Replace("ç", "c");

            normalized =
                Regex.Replace(
                    normalized,
                    @"[^a-z0-9]+",
                    " ");

            normalized =
                Regex.Replace(
                    normalized,
                    @"\s+",
                    " ");

            return normalized.Trim();
        }


        private string RemoveAccents(
            string text)
        {
            string normalized =
                text.Normalize(
                    NormalizationForm.FormD);

            var builder =
                new StringBuilder();

            foreach (char c in normalized)
            {
                UnicodeCategory category =
                    CharUnicodeInfo.GetUnicodeCategory(c);

                if (category !=
                    UnicodeCategory.NonSpacingMark)
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
        // SLUG
        // ============================================================

        private string NormalizeSlug(
            string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";

            string normalized =
                value.Trim().ToLowerInvariant();

            normalized =
                RemoveAccents(normalized);

            normalized =
                Regex.Replace(
                    normalized,
                    @"[^a-z0-9]+",
                    "-");

            normalized =
                Regex.Replace(
                    normalized,
                    @"-+",
                    "-");

            return normalized.Trim('-');
        }


        private async Task<List<PerfumeNote>> GetPerfumeNotes()
        {
            string url =
                $"{supabaseUrl}/rest/v1/perfume_notes" +
                "?select=*" +
                "&is_active=eq.true" +
                "&order=sort_order.asc";

            using HttpRequestMessage request =
                CreateRequest(
                    HttpMethod.Get,
                    url);

            using HttpResponseMessage response =
                await httpClient.SendAsync(request);

            string json =
                await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new Exception(
                    $"Supabase perfume_notes error " +
                    $"{response.StatusCode}: {json}");
            }

            return
                JsonConvert.DeserializeObject<List<PerfumeNote>>(json)
                ?? new List<PerfumeNote>();
        }


        // ============================================================
        // CLASSES
        // ============================================================

        private class PerfumeNote
        {
            [JsonProperty("id")]
            public long Id { get; set; }

            [JsonProperty("name_en")]
            public string NameEn { get; set; } = "";

            [JsonProperty("name_fr")]
            public string NameFr { get; set; } = "";

            [JsonProperty("type")]
            public string Type { get; set; } = "";

            [JsonProperty("sort_order")]
            public int SortOrder { get; set; }

            [JsonProperty("image_url")]
            public string? ImageUrl { get; set; }

            [JsonProperty("is_active")]
            public bool IsActive { get; set; }
        }


        private class ProductMatch
        {
            public AllProduct Product { get; set; } = null!;

            public double SimilarityScore { get; set; }

            public double SelectionScore { get; set; }

            public bool IsCheaper { get; set; }

            public decimal Price { get; set; }

            public decimal Savings { get; set; }

            public List<string> MatchedNotes { get; set; } =
                new();

            public string Reason { get; set; } = "";
        }


        public class AllProduct
        {
            [JsonProperty("id")]
            public long Id { get; set; }

            [JsonProperty("brand")]
            public string? Brand { get; set; }

            [JsonProperty("name")]
            public string Name { get; set; } = "";

            [JsonProperty("short_name")]
            public string? ShortName { get; set; }

            [JsonProperty("category")]
            public string? Category { get; set; }

            [JsonProperty("sub_category")]
            public string? SubCategory { get; set; }

            [JsonProperty("type")]
            public string? Type { get; set; }

            [JsonProperty("image")]
            public string? Image { get; set; }

            [JsonProperty("description")]
            public string? Description { get; set; }

            [JsonProperty("product_data")]
            public JObject ProductData { get; set; } =
                new JObject();

            [JsonProperty("naya_attributes")]
            public JObject NayaAttributes { get; set; } =
                new JObject();

            [JsonProperty("created_at")]
            public DateTime CreatedAt { get; set; }

            [JsonProperty("updated_at")]
            public DateTime UpdatedAt { get; set; }

            [JsonProperty("slug")]
            public string? Slug { get; set; }

            [JsonProperty("slug_normalized")]
            public string? SlugNormalized { get; set; }

            [System.Text.Json.Serialization.JsonIgnore]
            public List<AllProductOffer> ProductOffers { get; set; } =
                new();
        }


        public class AllProductOffer
        {
            [JsonProperty("id")]
            public long Id { get; set; }

            [JsonProperty("product_id")]
            public long ProductId { get; set; }

            [JsonProperty("marketplace")]
            public string Marketplace { get; set; } = "";

            [JsonProperty("marketplace_product_id")]
            public string? MarketplaceProductId { get; set; }

            [JsonProperty("sku")]
            public string? Sku { get; set; }

            [JsonProperty("price")]
            public decimal? Price { get; set; }

            [JsonProperty("currency")]
            public string Currency { get; set; } = "EUR";

            [JsonProperty("variant_name")]
            public string? VariantName { get; set; }

            [JsonProperty("product_url")]
            public string? ProductUrl { get; set; }

            [JsonProperty("is_available")]
            public bool IsAvailable { get; set; }

            [JsonProperty("rating")]
            public decimal? Rating { get; set; }

            [JsonProperty("reviews")]
            public int Reviews { get; set; }

            [JsonProperty("marketplace_data")]
            public JObject MarketplaceData { get; set; } =
                new JObject();

            [JsonProperty("created_at")]
            public DateTime CreatedAt { get; set; }

            [JsonProperty("updated_at")]
            public DateTime UpdatedAt { get; set; }
        }

        private string GetDisplayBrand(AllProduct product)
        {
            if (!string.IsNullOrWhiteSpace(product.Brand) &&
                !product.Brand.Equals(
                    "Marques",
                    StringComparison.OrdinalIgnoreCase))
            {
                return product.Brand;
            }

            string name = product.Name ?? "";

            string[] brands =
            {
        "Givenchy",
        "Valentino",
        "Chanel",
        "Dior",
        "Yves Saint Laurent",
        "Giorgio Armani",
        "Jean Paul Gaultier",
        "Tom Ford",
        "Narciso Rodriguez",
        "Azzaro",
        "Dolce&Gabbana",
        "Lancôme",
        "Prada",
        "Burberry",
        "Hugo Boss",
        "Carolina Herrera",
        "Rabanne",
        "Paco Rabanne",
        "Mugler",
        "Versace",
        "Gucci",
        "Hermès",
        "Issey Miyake",
        "Maison Francis Kurkdjian"
    };

            return brands.FirstOrDefault(brand =>
                name.StartsWith(
                    brand,
                    StringComparison.OrdinalIgnoreCase))
                ?? "";
        }

        private string GetSillageLabel(int value)
        {
            return value switch
            {
                1 => "Sillage discret",
                2 => "Sillage modéré",
                3 => "Sillage présent",
                4 => "Sillage exceptionnel",
                5 => "Sillage exceptionnel",
                _ => "Sillage"
            };
        }

        private string GetLongevityLabel(int value)
        {
            return value switch
            {
                1 => "Tenue légère",
                2 => "Bonne tenue",
                3 => "Bonne tenue",
                4 => "Très longue tenue",
                5 => "Très longue tenue",
                _ => "Tenue"
            };
        }
    }
}

