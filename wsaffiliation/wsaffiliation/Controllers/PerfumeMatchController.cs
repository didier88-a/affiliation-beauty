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

    // ================================================================
    // POST /api/perfume-match
    // ================================================================

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
                return StatusCode(
                    500,
                    new
                    {
                        error =
                            "SUPABASE_URL or SUPABASE_KEY is missing."
                    });
            }

            // --------------------------------------------------------
            // SUPABASE
            // --------------------------------------------------------

            var url =
                $"{supabaseUrl.TrimEnd('/')}/rest/v1/products" +
                "?select=id,brand,name,short_name,image,slug," +
                "category,sub_category,type,naya_attributes";

            using var httpRequest =
                new HttpRequestMessage(
                    HttpMethod.Get,
                    url);

            httpRequest.Headers.Add(
                "apikey",
                supabaseKey);

            httpRequest.Headers.Add(
                "Authorization",
                $"Bearer {supabaseKey}");

            var response =
                await _httpClient.SendAsync(httpRequest);

            if (!response.IsSuccessStatusCode)
            {
                var error =
                    await response.Content.ReadAsStringAsync();

                return StatusCode(
                    (int)response.StatusCode,
                    new
                    {
                        error = "Supabase error",
                        details = error
                    });
            }

            var json =
                await response.Content.ReadAsStringAsync();

            var products =
                JsonSerializer.Deserialize<List<Product>>(
                    json,
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    })
                ?? new List<Product>();


            // ========================================================
            // 1. FILTRAGE DES VRAIS PARFUMS
            // ========================================================

            products = products
                .Where(IsPerfume)
                .Where(p => !IsClearlyNonPerfume(p))
                .ToList();

            var totalProducts =
                products.Count;


            // ========================================================
            // 2. NETTOYAGE REQUEST
            // ========================================================

            request.Moods =
                CleanList(request.Moods);

            request.Styles =
                CleanList(request.Styles);

            request.Notes =
                CleanList(request.Notes);

            request.Families =
                CleanList(request.Families);


            // ========================================================
            // 3. OCCASIONS
            // ========================================================

            var occasions =
                new List<string>();

            if (!string.IsNullOrWhiteSpace(
                request.Occasion))
            {
                occasions.Add(
                    request.Occasion);
            }

            if (request.Occasions != null)
            {
                occasions.AddRange(
                    request.Occasions);
            }

            occasions =
                CleanList(occasions);


            // ========================================================
            // 4. SCORE MAXIMUM
            // ========================================================

            var maximumScore =
                CalculateMaximumScore(
                    request,
                    occasions);


            // ========================================================
            // 5. MATCHING
            // ========================================================

            var results =
                new List<PerfumeMatchResult>();

            foreach (var product in products)
            {
                var result =
                    CalculateScore(
                        product,
                        request,
                        occasions,
                        maximumScore);

                // Aucun intérêt à afficher un produit
                // qui n'a aucun match.
                if (result.Score > 0)
                {
                    results.Add(result);
                }
            }


            // ========================================================
            // 6. DEDUPLICATION
            // ========================================================

            results =
                results
                    .GroupBy(x =>
                        $"{Normalize(x.Brand)}|" +
                        $"{Normalize(
                            x.ShortName ?? x.Name)}")
                    .Select(g =>
                        g
                            .OrderByDescending(
                                x => x.Score)
                            .ThenByDescending(
                                x => x.MatchPercent)
                            .First())
                    .ToList();


            // ========================================================
            // 7. TRI
            // ========================================================

            results =
                results
                    .OrderByDescending(
                        x => x.Score)
                    .ThenByDescending(
                        x => x.MatchPercent)
                    .Take(24)
                    .ToList();


            // ========================================================
            // RESPONSE
            // ========================================================

            return Ok(
                new
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

    private static int CalculateScore(
     Product product,
     PerfumeMatchRequest request,
     PerfumeMatchResult result)
    {
        int score = 0;

        // =========================================================
        // GENRE
        // =========================================================

        if (HasExplicitGenderMatch(product, request.Whom))
        {
            score += 10;
            result.MatchedGender = true;
        }
        else
        {
            // Genre inconnu ou non correspondant :
            // aucun point, mais le produit reste éligible.
            result.MatchedGender = false;
        }

        // =========================================================
        // NOTES DE TÊTE
        // =========================================================

        foreach (var requestedNote in request.Notes)
        {
            if (ContainsAny(product.NayaAttributes.TopNotes, requestedNote))
            {
                if (!result.MatchedTopNotes.Contains(requestedNote))
                {
                    result.MatchedTopNotes.Add(requestedNote);
                }

                score += 10;
            }
        }

        // =========================================================
        // NOTES DE CŒUR
        // =========================================================

        foreach (var requestedNote in request.Notes)
        {
            if (ContainsAny(product.NayaAttributes.HeartNotes, requestedNote))
            {
                if (!result.MatchedHeartNotes.Contains(requestedNote))
                {
                    result.MatchedHeartNotes.Add(requestedNote);
                }

                score += 12;
            }
        }

        // =========================================================
        // NOTES DE FOND
        // =========================================================

        foreach (var requestedNote in request.Notes)
        {
            if (ContainsAny(product.NayaAttributes.BaseNotes, requestedNote))
            {
                if (!result.MatchedBaseNotes.Contains(requestedNote))
                {
                    result.MatchedBaseNotes.Add(requestedNote);
                }

                score += 10;
            }
        }

        // =========================================================
        // STYLE
        // =========================================================

        foreach (var requestedStyle in request.Styles)
        {
            if (ContainsAny(product.NayaAttributes.Style, requestedStyle))
            {
                if (!result.MatchedStyles.Contains(requestedStyle))
                {
                    result.MatchedStyles.Add(requestedStyle);
                }

                score += 8;
            }
        }

        // =========================================================
        // FAMILLE OLFACTIVE
        // =========================================================

        foreach (var requestedStyle in request.Styles)
        {
            if (ContainsAny(product.NayaAttributes.FragranceFamily, requestedStyle))
            {
                if (!result.MatchedFamilies.Contains(requestedStyle))
                {
                    result.MatchedFamilies.Add(requestedStyle);
                }

                score += 4;
            }
        }

        // =========================================================
        // MOODS
        // =========================================================

        foreach (var mood in request.Moods)
        {
            if (MoodMatchesProduct(product, mood))
            {
                if (!result.MatchedMoods.Contains(mood))
                {
                    result.MatchedMoods.Add(mood);
                }

                score += 4;
            }
        }

        // =========================================================
        // OCCASION
        // =========================================================

        foreach (var occasion in request.Occasions)
        {
            if (ContainsAny(product.NayaAttributes.Occasion, occasion))
            {
                if (!result.MatchedOccasions.Contains(occasion))
                {
                    result.MatchedOccasions.Add(occasion);
                }

                score += 3;
            }
        }

        return score;
    }


    private static bool HasExplicitGenderMatch(
     Product product,
     string? requestedGender)
    {
        if (string.IsNullOrWhiteSpace(requestedGender))
        {
            return false;
        }

        // Le gender se trouve dans naya_attributes
        var productGender =
            GetString(
                product.NayaAttributes,
                "gender");

        var requested =
            Normalize(requestedGender);

        // Gender absent :
        // aucun bonus, mais le produit reste dans les résultats.
        if (string.IsNullOrWhiteSpace(productGender))
        {
            return false;
        }

        productGender =
            Normalize(productGender);


        // =========================================================
        // UNISEXE
        // =========================================================

        if (productGender == "unisexe" ||
            productGender == "unisex")
        {
            return true;
        }


        // =========================================================
        // FEMME
        // =========================================================

        if (requested == "femme" ||
            requested == "for-her" ||
            requested == "her" ||
            requested == "woman" ||
            requested == "women")
        {
            return
                productGender == "femme" ||
                productGender == "feminin" ||
                productGender == "female" ||
                productGender == "woman" ||
                productGender == "women";
        }


        // =========================================================
        // HOMME
        // =========================================================

        if (requested == "homme" ||
            requested == "for-him" ||
            requested == "him" ||
            requested == "man" ||
            requested == "men")
        {
            return
                productGender == "homme" ||
                productGender == "masculin" ||
                productGender == "male" ||
                productGender == "man" ||
                productGender == "men";
        }


        return false;
    }

    // ================================================================
    // MAXIMUM SCORE
    // ================================================================

    private static int CalculateMaximumScore(
        PerfumeMatchRequest request,
        List<string> occasions)
    {
        var score = 0;


        // GENDER
        if (!string.IsNullOrWhiteSpace(
            request.Whom))
        {
            score += 10;
        }


        // NOTES
        // Une note peut être top / coeur / fond.
        // On utilise le poids maximum = 10.

        score +=
            request.Notes.Count * 10;


        // STYLES

        score +=
            request.Styles.Count * 6;


        // MOODS

        score +=
            request.Moods.Count * 6;


        // OCCASIONS

        score +=
            occasions.Count * 4;


        // FAMILIES

        score +=
            request.Families.Count * 4;


        return score;
    }


    // ================================================================
    // FILTRE PARFUM
    // ================================================================

    // ================================================================
    // FILTRE DES VRAIS PARFUMS
    // ================================================================

    private static bool IsPerfume(
        Product product)
    {
        var category =
            Normalize(product.Category);

        var subCategory =
            Normalize(product.SubCategory);

        var type =
            Normalize(product.Type);

        var name =
            Normalize(product.Name);

        var shortName =
            Normalize(product.ShortName);


        // ============================================================
        // 1. LES CHAMPS DE CATEGORIE SONT PRIORITAIRES
        // ============================================================

        var perfumeCategory =
            IsPerfumeCategory(category) ||
            IsPerfumeCategory(subCategory) ||
            IsPerfumeCategory(type);


        if (perfumeCategory)
        {
            return true;
        }


        // ============================================================
        // 2. APPELLATIONS EXPLICITES DANS LE NOM
        // ============================================================

        string[] explicitPerfumeNames =
            new string[]
            {
            "eau-de-parfum",
            "eau-de-toilette",
            "eau-de-cologne",
            "extrait-de-parfum",
            "extrait",
            "parfum",
            "perfume"
            };


        foreach (var keyword in explicitPerfumeNames)
        {
            if (name.Contains(keyword) ||
                shortName.Contains(keyword))
            {
                return true;
            }
        }


        return false;
    }


    // ================================================================
    // CATEGORIE PARFUM
    // ================================================================

    private static bool IsPerfumeCategory(
        string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }


        // ------------------------------------------------------------
        // CATEGORIES EXPLICITES
        // ------------------------------------------------------------

        if (value == "parfum" ||
            value == "parfums" ||
            value == "parfumerie" ||
            value == "fragrance" ||
            value == "fragrances")
        {
            return true;
        }


        // ------------------------------------------------------------
        // EAU DE PARFUM
        // ------------------------------------------------------------

        if (value.Contains("eau-de-parfum"))
        {
            return true;
        }


        // ------------------------------------------------------------
        // EAU DE TOILETTE
        // ------------------------------------------------------------

        if (value.Contains("eau-de-toilette"))
        {
            return true;
        }


        // ------------------------------------------------------------
        // EAU DE COLOGNE
        // ------------------------------------------------------------

        if (value.Contains("eau-de-cologne"))
        {
            return true;
        }


        // ------------------------------------------------------------
        // EXTRAIT
        // ------------------------------------------------------------

        if (value.Contains("extrait-de-parfum"))
        {
            return true;
        }


        if (value == "extrait")
        {
            return true;
        }


        return false;
    }

    // ================================================================
    // EXCLUSIONS
    // ================================================================

    private static bool IsClearlyNonPerfume(
     Product product)
    {
        var raw =
            $"{product.Category} " +
            $"{product.SubCategory} " +
            $"{product.Type} " +
            $"{product.Name} " +
            $"{product.ShortName}";

        var all = Normalize(raw);


        // ============================================================
        // PRODUITS QUI NE SONT PAS DES PARFUMS
        // ============================================================

        string[] excluded =
            new string[]
            {
            // ----------------------------------------------------
            // BODY SPRAY / BODY MIST / BODY SPLASH
            // ----------------------------------------------------

            "body-spray",
            "body-mist",
            "body-splash",
            "body-fragrance",
            "spray-corps",
            "spray-pour-le-corps",
            "vaporisateur-corps",
            "vaporisateur-pour-le-corps",
            "brume-corps",
            "brume-pour-le-corps",
            "brume-parfumee",
            "brume-parfumee-pour-le-corps",


            // ----------------------------------------------------
            // PRODUITS CORPS
            // ----------------------------------------------------

            "lait-corps",
            "lait-pour-le-corps",
            "lait-parfumant",
            "lait-parfumant-pour-le-corps",
            "lotion-corps",
            "lotion-pour-le-corps",
            "lotion-parfumee",
            "lotion-parfumee-pour-le-corps",
            "creme-corps",
            "creme-pour-le-corps",
            "creme-parfumee",
            "creme-parfumee-pour-le-corps",
            "huile-corps",
            "huile-pour-le-corps",
            "huile-parfumee",
            "huile-parfumee-pour-le-corps",
            "gel-douche",
            "savon",


            // ----------------------------------------------------
            // DEODORANTS
            // ----------------------------------------------------

            "deodorant",
            "deodorant-spray",
            "deodorant-stick",
            "deodorant-roll-on",
            "deo",


            // ----------------------------------------------------
            // CHEVEUX
            // ----------------------------------------------------

            "shampoo",
            "shampoing",
            "conditioner",
            "apres-shampooing",
            "masque-capillaire",
            "masque-cheveux",
            "huile-capillaire",
            "spray-capillaire",
            "serum-capillaire",


            // ----------------------------------------------------
            // MAISON
            // ----------------------------------------------------

            "bougie",
            "candle",
            "diffuseur",
            "diffuser",
            "home-fragrance",
            "fragrance-d-interieur",
            "parfum-interieur",
            "parfum-d-interieur",
            "spray-interieur",
            "desodorisant",


            // ----------------------------------------------------
            // COFFRETS / GIFT SET
            // ----------------------------------------------------

            "coffret",
            "coffret-parfum",
            "gift-set",
            "giftset",
            "set-cadeau",
            "cadeau-set",


            // ----------------------------------------------------
            // MINI / SAMPLE
            // ----------------------------------------------------

            "miniature",
            "mini-splash",
            "mini-spray",
            "mini-parfum",
            "mini-eau-de-parfum",
            "mini-eau-de-toilette",
            "echantillon",
            "sample",
            "sample-size",
            "travel-size",
            "travel-spray",
            "travel-parfum"
            };


        // ============================================================
        // TEST DES EXCLUSIONS
        // ============================================================

        foreach (var keyword in excluded)
        {
            var normalizedKeyword =
                Normalize(keyword);

            if (all.Contains(normalizedKeyword))
            {
                return true;
            }
        }


        // ============================================================
        // GIFT SET
        // ============================================================

        if (all.Contains("gift") &&
            all.Contains("set"))
        {
            return true;
        }


        // ============================================================
        // COFFRET
        // ============================================================

        if (all.Contains("coffret"))
        {
            return true;
        }


        // ============================================================
        // MINI
        // ============================================================

        if (all.Contains("mini") &&
            (
                all.Contains("splash") ||
                all.Contains("spray") ||
                all.Contains("parfum")
            ))
        {
            return true;
        }


        // ============================================================
        // BODY + SPRAY / MIST / SPLASH
        // ============================================================

        if (all.Contains("body") &&
            (
                all.Contains("spray") ||
                all.Contains("mist") ||
                all.Contains("splash")
            ))
        {
            return true;
        }


        // ============================================================
        // CORPS + PRODUIT
        // ============================================================

        if (all.Contains("corps") &&
            (
                all.Contains("lait") ||
                all.Contains("lotion") ||
                all.Contains("creme") ||
                all.Contains("huile") ||
                all.Contains("spray") ||
                all.Contains("brume")
            ))
        {
            return true;
        }


        // ============================================================
        // POUR LE CORPS
        // ============================================================

        if (all.Contains("pour-le-corps"))
        {
            return true;
        }


        return false;
    }


    // ================================================================
    // GENDER
    // ================================================================

    private static bool GenderMatches(
     string selected,
     string? productGender)
    {
        // Gender absent = pas de correspondance
        // mais le produit n'est PAS éliminé.
        if (string.IsNullOrWhiteSpace(
            productGender))
        {
            return false;
        }

        var selectedValue =
            Normalize(selected);

        var productValue =
            Normalize(productGender);


        // =========================================================
        // UNISEXE
        // =========================================================

        if (productValue == "unisexe" ||
            productValue == "unisex")
        {
            return true;
        }


        // =========================================================
        // FEMME
        // =========================================================

        if (selectedValue == "her" ||
            selectedValue == "femme" ||
            selectedValue == "woman" ||
            selectedValue == "women")
        {
            return
                productValue.Contains("femme") ||
                productValue.Contains("female") ||
                productValue.Contains("woman");
        }


        // =========================================================
        // HOMME
        // =========================================================

        if (selectedValue == "him" ||
            selectedValue == "homme" ||
            selectedValue == "man" ||
            selectedValue == "men")
        {
            return
                productValue.Contains("homme") ||
                productValue.Contains("male") ||
                productValue.Contains("man");
        }


        return false;
    }


    // ================================================================
    // STYLE MATCHING
    // ================================================================

    private static bool StyleMatches(
        string selectedStyle,
        List<string> styles,
        List<string> families)
    {
        var possibleValues =
            GetStyleMapping(
                selectedStyle);


        return possibleValues.Any(
            value =>
                ContainsAny(
                    styles,
                    value)
                ||
                ContainsAny(
                    families,
                    value));
    }


    // ================================================================
    // STYLE MAPPING
    // ================================================================

    private static List<string> GetStyleMapping(
        string style)
    {
        switch (Normalize(style))
        {
            case "aquatic":
                return new List<string>
                {
                    "aquatique",
                    "aquatic"
                };

            case "aromatic":
                return new List<string>
                {
                    "aromatique",
                    "aromatic"
                };

            case "chypre":
                return new List<string>
                {
                    "chypre",
                    "chypree"
                };

            case "citrus":
                return new List<string>
                {
                    "agrume",
                    "agrumes",
                    "citrus"
                };

            case "creamy":
                return new List<string>
                {
                    "cremeux",
                    "cremeuse",
                    "creme",
                    "creamy"
                };

            case "earthy":
                return new List<string>
                {
                    "terreux",
                    "terreuse",
                    "earthy"
                };

            case "floral":
                return new List<string>
                {
                    "floral",
                    "florale"
                };

            case "fresh":
                return new List<string>
                {
                    "frais",
                    "fraiche",
                    "fresh"
                };

            case "fruity":
                return new List<string>
                {
                    "fruite",
                    "fruitee",
                    "fruity"
                };

            case "gourmand":
                return new List<string>
                {
                    "gourmand",
                    "gourmande"
                };

            case "green":
                return new List<string>
                {
                    "vert",
                    "verte",
                    "green"
                };

            case "iris-makeup":
                return new List<string>
                {
                    "iris",
                    "maquillage"
                };

            case "leather":
                return new List<string>
                {
                    "cuir",
                    "cuire",
                    "cuiree",
                    "leather"
                };

            case "musky":
                return new List<string>
                {
                    "musc",
                    "musque",
                    "musquee",
                    "musky"
                };

            case "oriental-amber":
                return new List<string>
                {
                    "oriental",
                    "orientale",
                    "ambre",
                    "ambree"
                };

            case "oud":
                return new List<string>
                {
                    "oud"
                };

            case "powdery":
                return new List<string>
                {
                    "poudre",
                    "poudree"
                };

            case "smoky":
                return new List<string>
                {
                    "fume",
                    "fumee",
                    "smoky"
                };

            case "spicy":
                return new List<string>
                {
                    "epice",
                    "epicee",
                    "epices",
                    "spicy"
                };

            case "aldehydic":
                return new List<string>
                {
                    "aldehydique",
                    "aldehyde"
                };

            case "woody":
                return new List<string>
                {
                    "boise",
                    "boisee",
                    "woody"
                };

            default:
                return new List<string>
                {
                    style
                };
        }
    }


    // ================================================================
    // MOOD MATCHING
    // ================================================================

    private static bool MoodMatches(
        string mood,
        List<string> styles,
        List<string> families)
    {
        var values =
            GetMoodMapping(
                mood);


        return values.Any(
            value =>
                ContainsAny(
                    styles,
                    value)
                ||
                ContainsAny(
                    families,
                    value));
    }


    // ================================================================
    // MOOD MAPPING
    // ================================================================

    private static List<string> GetMoodMapping(
        string mood)
    {
        switch (Normalize(mood))
        {
            case "bold":
                return new List<string>
                {
                    "intense",
                    "puissant",
                    "puissante"
                };

            case "calm":
                return new List<string>
                {
                    "doux",
                    "douce",
                    "frais",
                    "fraiche"
                };

            case "chic":
                return new List<string>
                {
                    "elegant",
                    "elegante",
                    "sophistique",
                    "sophistiquee"
                };

            case "clean":
                return new List<string>
                {
                    "frais",
                    "fraiche",
                    "aromatique"
                };

            case "comforting":
                return new List<string>
                {
                    "doux",
                    "douce",
                    "ambre",
                    "ambree"
                };

            case "confident":
                return new List<string>
                {
                    "elegant",
                    "intense",
                    "puissant",
                    "puissante",
                    "luxueux",
                    "luxueuse"
                };

            case "cozy":
                return new List<string>
                {
                    "doux",
                    "douce",
                    "ambre",
                    "ambree",
                    "oriental",
                    "orientale"
                };

            case "dramatic":
                return new List<string>
                {
                    "intense",
                    "oriental",
                    "orientale",
                    "boise",
                    "boisee",
                    "oud"
                };

            case "elegant":
                return new List<string>
                {
                    "elegant",
                    "elegante",
                    "sophistique",
                    "sophistiquee"
                };

            case "energizing":
                return new List<string>
                {
                    "frais",
                    "fraiche",
                    "agrumes",
                    "aromatique"
                };

            case "fresh-invigorating":
                return new List<string>
                {
                    "frais",
                    "fraiche",
                    "agrumes",
                    "aromatique"
                };

            case "luxury":
                return new List<string>
                {
                    "luxueux",
                    "luxueuse",
                    "elegant",
                    "elegante",
                    "sophistique",
                    "sophistiquee"
                };

            case "modern":
                return new List<string>
                {
                    "moderne",
                    "sophistique",
                    "sophistiquee"
                };

            case "addictive":
                return new List<string>
                {
                    "seduisant",
                    "seduisante",
                    "sensuel",
                    "sensuelle",
                    "gourmand",
                    "gourmande"
                };

            case "mysterious":
                return new List<string>
                {
                    "oriental",
                    "orientale",
                    "ambre",
                    "ambree",
                    "boise",
                    "boisee",
                    "oud"
                };

            case "playful":
                return new List<string>
                {
                    "fruite",
                    "fruitee",
                    "gourmand",
                    "gourmande"
                };

            case "powerful":
                return new List<string>
                {
                    "intense",
                    "puissant",
                    "puissante"
                };

            case "relaxing":
                return new List<string>
                {
                    "doux",
                    "douce",
                    "frais",
                    "fraiche"
                };

            case "romantic":
                return new List<string>
                {
                    "floral",
                    "florale",
                    "sensuel",
                    "sensuelle",
                    "seduisant",
                    "seduisante"
                };

            case "seductive":
                return new List<string>
                {
                    "seduisant",
                    "seduisante",
                    "sensuel",
                    "sensuelle"
                };

            case "sensual":
                return new List<string>
                {
                    "sensuel",
                    "sensuelle",
                    "seduisant",
                    "seduisante"
                };

            case "sexy":
                return new List<string>
                {
                    "sensuel",
                    "sensuelle",
                    "seduisant",
                    "seduisante"
                };

            case "sophisticated":
                return new List<string>
                {
                    "sophistique",
                    "sophistiquee",
                    "elegant",
                    "elegante"
                };

            case "timeless":
                return new List<string>
                {
                    "elegant",
                    "elegante",
                    "sophistique",
                    "sophistiquee"
                };

            case "warm":
                return new List<string>
                {
                    "ambre",
                    "ambree",
                    "oriental",
                    "orientale",
                    "chaud",
                    "chaude",
                    "epice",
                    "epicee"
                };

            case "youthful":
                return new List<string>
                {
                    "fruite",
                    "fruitee",
                    "frais",
                    "fraiche"
                };

            default:
                return new List<string>
                {
                    mood
                };
        }
    }


    // ================================================================
    // JSON HELPERS
    // ================================================================

    private static List<string> GetStringList(
        JsonElement json,
        string property)
    {
        if (json.ValueKind !=
            JsonValueKind.Object)
        {
            return new List<string>();
        }


        if (!json.TryGetProperty(
            property,
            out var value))
        {
            return new List<string>();
        }


        if (value.ValueKind !=
            JsonValueKind.Array)
        {
            return new List<string>();
        }


        var result =
            new List<string>();


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


    private static string? GetString(
        JsonElement json,
        string property)
    {
        if (json.ValueKind !=
            JsonValueKind.Object)
        {
            return null;
        }


        if (!json.TryGetProperty(
            property,
            out var value))
        {
            return null;
        }


        return value.ValueKind ==
               JsonValueKind.String
            ? value.GetString()
            : null;
    }


    // ================================================================
    // STRING MATCH
    // ================================================================

    private static bool ContainsAny(
    List<string>? values,
    string search)
    {
        if (values == null ||
            values.Count == 0 ||
            string.IsNullOrWhiteSpace(search))
        {
            return false;
        }

        var normalizedSearch = Normalize(search);

        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var normalizedValue = Normalize(value);

            if (normalizedValue == normalizedSearch)
            {
                return true;
            }

            // Correspondance seulement si l'une des valeurs
            // est réellement une expression contenant l'autre.
            if (normalizedValue.Contains("-" + normalizedSearch) ||
                normalizedValue.Contains(normalizedSearch + "-") ||
                normalizedSearch.Contains("-" + normalizedValue) ||
                normalizedSearch.Contains(normalizedValue + "-"))
            {
                return true;
            }
        }

        return false;
    }


    // ================================================================
    // CLEAN LIST
    // ================================================================

    private static List<string> CleanList(
        IEnumerable<string>? values)
    {
        if (values == null)
        {
            return new List<string>();
        }


        return values
            .Where(
                x =>
                    !string.IsNullOrWhiteSpace(
                        x))
            .Select(
                x =>
                    x.Trim())
            .Distinct(
                StringComparer.OrdinalIgnoreCase)
            .ToList();
    }


    // ================================================================
    // NORMALIZE
    // ================================================================

    private static string Normalize(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(
            value))
        {
            return "";
        }


        var normalized =
            value.Normalize(
                NormalizationForm.FormD);


        var sb =
            new StringBuilder();


        foreach (var c
                 in normalized)
        {
            var category =
                CharUnicodeInfo.GetUnicodeCategory(
                    c);


            if (category !=
                UnicodeCategory.NonSpacingMark)
            {
                sb.Append(c);
            }
        }


        return sb
            .ToString()
            .Normalize(
                NormalizationForm.FormC)
            .ToLowerInvariant()
            .Trim()
            .Replace(
                " ",
                "-");
    }
}


// ====================================================================
// REQUEST
// ====================================================================

public class PerfumeMatchRequest
{
    public string? Whom { get; set; }

    public string? Occasion { get; set; }

    public List<string> Occasions { get; set; } =
        new List<string>();

    public List<string> Moods { get; set; } =
        new List<string>();

    public List<string> Styles { get; set; } =
        new List<string>();

    public List<string> Notes { get; set; } =
        new List<string>();

    public List<string> Families { get; set; } =
        new List<string>();
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

    public List<string> MatchedTopNotes { get; set; } =
        new List<string>();

    public List<string> MatchedHeartNotes { get; set; } =
        new List<string>();

    public List<string> MatchedBaseNotes { get; set; } =
        new List<string>();

    public List<string> MatchedStyles { get; set; } =
        new List<string>();

    public List<string> MatchedMoods { get; set; } =
        new List<string>();

    public List<string> MatchedOccasions { get; set; } =
        new List<string>();

    public List<string> MatchedFamilies { get; set; } =
        new List<string>();
}