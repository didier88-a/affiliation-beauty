using Microsoft.AspNetCore.Mvc;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace wsaffiliation.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class NayaController : ControllerBase
    {
        private readonly IConfiguration _configuration;

        private class GuideSeoPayload
        {
            public string Title { get; set; } = "";
            public string Description { get; set; } = "";
            public string Image { get; set; } = "";
            public string CanonicalPath { get; set; } = "";
            public string JsonLd { get; set; } = "";
        }

        private class GuideSeoProduct
        {
            public string Name { get; set; } = "";
            public string Brand { get; set; } = "";
            public string Image { get; set; } = "";
            public string Url { get; set; } = "";
            public double? Rating { get; set; }
            public int Reviews { get; set; }
            public decimal? Price { get; set; }
            public string Currency { get; set; } = "";
        }



        public NayaController(IConfiguration configuration)
        {
            _configuration = configuration;
        }


        [HttpGet]
        [Route("~/api/shopify/proxy/{*path}")]
        public async Task<IActionResult> ShopifyProxy(
    string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return BadRequest("Slug manquant");
            }

            var cleanSlug =
                CreateUrlSlug(path.Trim('/'));

            if (string.IsNullOrWhiteSpace(cleanSlug))
            {
                return BadRequest("Slug invalide");
            }


            // =========================================================
            // Récupération du JSON du guide
            // =========================================================

            var jsonStr =
                await GetGuideJsonByCleanSlug(cleanSlug);

            if (string.IsNullOrWhiteSpace(jsonStr))
            {
                return NotFound(
                    new
                    {
                        message = "Guide introuvable",
                        slug = cleanSlug
                    }
                );
            }


            // =========================================================
            // SEO
            // =========================================================

            var seo =
                BuildGuideSeo(
                    jsonStr,
                    cleanSlug
                );


            var seoJson =
                JsonSerializer.Serialize(
                    seo,
                    new JsonSerializerOptions
                    {
                        PropertyNamingPolicy =
                            JsonNamingPolicy.CamelCase,

                        Encoder =
                            JavaScriptEncoder.Default
                    }
                );


            // =========================================================
            // Liquid Shopify
            // =========================================================

            var liquid = $@"
                    {{% section 'naya-ai-search' %}}
                    {{% section 'naya-guides-results' %}}
                    {{% section 'viliora-guide-hero' %}}
                    {{% section 'viliora-top-5' %}}
                    {{% section 'viliora-comparison' %}}
                    {{% section 'viliora-reviews' %}}
                    {{% section 'viliora-evaluation' %}}
                    {{% section 'viliora-guide-info' %}}
                    {{% section 'viliora-final-verdict' %}}

                    <script>
                    window.NAYA_PROXY_SLUG = {JsonSerializer.Serialize(cleanSlug)};
                    window.VILIORA_SEO = {seoJson};
                    </script>
                    ";

            return Content(
                liquid,
                "application/liquid"
            );
        }

        //[HttpGet]
        //[Route("~/api/shopify/proxy/{*path}")]
        //public IActionResult ShopifyProxy(string? path)
        //{
        //    if (string.IsNullOrWhiteSpace(path))
        //    {
        //        return BadRequest("Slug manquant");
        //    }

        //    var slug = path.Trim('/');

        //    var liquid = $@"
        //            {{% section 'naya-ai-search' %}}
        //            {{% section 'naya-guides-results' %}}
        //            {{% section 'viliora-guide-hero' %}}
        //            {{% section 'viliora-top-5' %}}
        //            {{% section 'viliora-comparison' %}}
        //            {{% section 'viliora-reviews' %}}
        //            {{% section 'viliora-evaluation' %}}
        //            {{% section 'viliora-guide-info' %}}
        //            {{% section 'viliora-final-verdict' %}}

        //            <script>
        //                window.NAYA_PROXY_SLUG = {System.Text.Json.JsonSerializer.Serialize(slug)};
        //            </script>
        //            ";

        //    return Content(
        //        liquid,
        //        "application/liquid"
        //    );
        //}



        private static string CreateUrlSlug(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return "";
            }

            var normalized =
                text.Normalize(
                    System.Text.NormalizationForm.FormD
                );

            var chars =
                normalized
                    .Where(c =>
                        CharUnicodeInfo.GetUnicodeCategory(c)
                        != UnicodeCategory.NonSpacingMark)
                    .ToArray();

            var result =
                new string(chars)
                    .ToLowerInvariant();

            result =
                Regex.Replace(
                    result,
                    @"[^a-z0-9]+",
                    "-"
                );

            result =
                result.Trim('-');

            return result;
        }


        [HttpGet("popular-guides")]
        public async Task<IActionResult> GetPopularGuides()
        {

            var supabaseUrl = Environment.GetEnvironmentVariable("SUPABASE_URL");
            var supabaseKey = Environment.GetEnvironmentVariable("SUPABASE_KEY");


            var url =
                $"{supabaseUrl}/rest/v1/rpc/get_popular_guides";

            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                url
            );

            request.Headers.Add(
                "apikey",
                supabaseKey
            );

            request.Headers.Authorization =
                new AuthenticationHeaderValue(
                    "Bearer",
                    supabaseKey
                );

            using var httpClient = new HttpClient();

            var response =
                await httpClient.SendAsync(request);

            var json =
                await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                return StatusCode(
                    (int)response.StatusCode,
                    json
                );
            }

            return Content(
                json,
                "application/json"
            );
        }

        [HttpGet("best-guides")]
        public async Task<IActionResult> GetBestGuidesByCategory()
        {

            var supabaseUrl = Environment.GetEnvironmentVariable("SUPABASE_URL");
            var supabaseKey = Environment.GetEnvironmentVariable("SUPABASE_KEY");


            var url =
                $"{supabaseUrl}/rest/v1/rpc/get_best_guides_by_category";

            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                url
            );

            request.Headers.Add(
                "apikey",
                supabaseKey
            );

            request.Headers.Authorization =
                new AuthenticationHeaderValue(
                    "Bearer",
                    supabaseKey
                );

            using var httpClient = new HttpClient();

            var response =
                await httpClient.SendAsync(request);

            var json =
                await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                return StatusCode(
                    (int)response.StatusCode,
                    json
                );
            }

            return Content(
                json,
                "application/json"
            );
        }


        [HttpGet("guide/{slug}")]
        public async Task<IActionResult> GetGuide(string slug)
        {
            var supabaseUrl =
                Environment.GetEnvironmentVariable("SUPABASE_URL");

            var supabaseKey =
                Environment.GetEnvironmentVariable("SUPABASE_KEY");

            try
            {
                if (string.IsNullOrWhiteSpace(slug))
                {
                    return BadRequest("Slug manquant");
                }

                using var httpClient = new HttpClient();

                // =========================================================
                // Normalisation du slug reçu
                // =========================================================

                var normalizedSlug = NormalizeGuideSlug(slug);

                // =========================================================
                // IMPORTANT :
                // On demande à PostgreSQL de normaliser slug directement.
                // On ne récupère PAS tous les guides.
                // =========================================================

                var filter =
                    $"eq.{Uri.EscapeDataString(normalizedSlug)}";

                var url =
                    $"{supabaseUrl}/rest/v1/GuideViliora" +
                    $"?select=json_str,slug" +
                    $"&slug_normalized={filter}" +
                    $"&limit=1";

                using var request =
                    new HttpRequestMessage(
                        HttpMethod.Get,
                        url
                    );

                request.Headers.Add(
                    "apikey",
                    supabaseKey
                );

                request.Headers.Authorization =
                    new AuthenticationHeaderValue(
                        "Bearer",
                        supabaseKey
                    );

                var response =
                    await httpClient.SendAsync(request);

                var result =
                    await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    return StatusCode(
                        (int)response.StatusCode,
                        result
                    );
                }

                var data =
                    JsonSerializer.Deserialize<JsonElement>(
                        result
                    );

                if (
                    data.ValueKind != JsonValueKind.Array ||
                    data.GetArrayLength() == 0
                )
                {
                    return NotFound(
                        new
                        {
                            message = "Guide introuvable",
                            slug = slug,
                            normalizedSlug = normalizedSlug
                        }
                    );
                }

                var jsonStr =
                    data[0]
                        .GetProperty("json_str")
                        .GetString();

                if (string.IsNullOrWhiteSpace(jsonStr))
                {
                    return NotFound(
                        new
                        {
                            message = "JSON du guide introuvable",
                            slug = slug
                        }
                    );
                }

                return Content(
                    jsonStr,
                    "application/json"
                );
            }
            catch (Exception ex)
            {
                return StatusCode(
                    500,
                    new
                    {
                        message =
                            "Erreur lors de la récupération du guide",
                        error = ex.Message
                    }
                );
            }
        }

        //private static string NormalizeGuideSlug(string text)
        //{
        //    if (string.IsNullOrWhiteSpace(text))
        //        return "";

        //    var normalized =
        //        text.Normalize(
        //            System.Text.NormalizationForm.FormD
        //        );

        //    var chars =
        //        normalized
        //            .Where(c =>
        //                System.Globalization.CharUnicodeInfo
        //                    .GetUnicodeCategory(c)
        //                != System.Globalization.UnicodeCategory.NonSpacingMark)
        //            .ToArray();

        //    return new string(chars)
        //        .ToLowerInvariant()
        //        .Where(char.IsLetterOrDigit)
        //        .ToArray()
        //        .Aggregate(
        //            new System.Text.StringBuilder(),
        //            (sb, c) => sb.Append(c)
        //        )
        //        .ToString();
        //}


        private static string NormalizeGuideSlug(
    string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return "";
            }

            var normalized =
                text.Normalize(
                    System.Text.NormalizationForm.FormD
                );

            var chars =
                normalized
                    .Where(c =>
                        CharUnicodeInfo.GetUnicodeCategory(c)
                        != UnicodeCategory.NonSpacingMark)
                    .ToArray();

            return new string(chars)
                .ToLowerInvariant()
                .Where(char.IsLetterOrDigit)
                .ToArray()
                .Aggregate(
                    new StringBuilder(),
                    (builder, c) =>
                        builder.Append(c))
                .ToString();
        }

        private static string SlugToReadableText(string slug)
        {
            if (string.IsNullOrWhiteSpace(slug))
                return "";

            var words =
                slug
                    .Split(
                        '-',
                        StringSplitOptions.RemoveEmptyEntries
                    );

            var text =
                string.Join(
                    " ",
                    words
                );

            // Cas DIOR
            if (
                text.StartsWith(
                    "dior ",
                    StringComparison.OrdinalIgnoreCase
                )
            )
            {
                text =
                    "DIOR " +
                    text.Substring(5);
            }

            // Première lettre de chaque mot en majuscule
            var culture =
                System.Globalization.CultureInfo.InvariantCulture;

            text =
                culture.TextInfo.ToTitleCase(
                    text.ToLowerInvariant()
                );

            // Correction DIOR après TitleCase
            text = text.Replace(
                "Dior ",
                "DIOR "
            );

            return text;
        }


        private static string CreateSlug(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return "";
            }

            var normalized =
                text.Normalize(
                    System.Text.NormalizationForm.FormD
                );

            var chars =
                normalized
                    .Where(c =>
                        System.Globalization.CharUnicodeInfo
                            .GetUnicodeCategory(c)
                        != System.Globalization.UnicodeCategory.NonSpacingMark)
                    .ToArray();

            var result =
                new string(chars)
                    .ToLowerInvariant();

            result =
                System.Text.RegularExpressions.Regex.Replace(
                    result,
                    @"[^a-z0-9]+",
                    "-"
                );

            result =
                result.Trim('-');

            return result;
        }

        //[HttpGet("guide/{slug}")]
        //public async Task<IActionResult> GetGuide(string slug)
        //{
        //    var supabaseUrl = Environment.GetEnvironmentVariable("SUPABASE_URL");
        //    var supabaseKey = Environment.GetEnvironmentVariable("SUPABASE_KEY");

        //    try
        //    {
        //        var url =
        //            $"{supabaseUrl}/rest/v1/GuideViliora" +
        //            $"?select=json_str&slug=eq.{Uri.EscapeDataString(slug)}";

        //        using var request =
        //            new HttpRequestMessage(
        //                HttpMethod.Get,
        //                url
        //            );

        //        request.Headers.Add(
        //            "apikey",
        //            supabaseKey
        //        );

        //        request.Headers.Authorization =
        //            new AuthenticationHeaderValue(
        //                "Bearer",
        //                supabaseKey
        //            );

        //        using var httpClient =
        //            new HttpClient();

        //        var response =
        //            await httpClient.SendAsync(request);

        //        var result =
        //            await response.Content.ReadAsStringAsync();

        //        if (!response.IsSuccessStatusCode)
        //        {
        //            return StatusCode(
        //                (int)response.StatusCode,
        //                result
        //            );
        //        }


        //        // Aucun guide trouvé
        //        JsonElement data =
        //            JsonSerializer.Deserialize<JsonElement>(
        //                result
        //            );

        //        if (
        //            data.ValueKind != JsonValueKind.Array ||
        //            data.GetArrayLength() == 0
        //        )
        //        {
        //            return NotFound(
        //                new
        //                {
        //                    message = "Guide introuvable",
        //                    slug = slug
        //                }
        //            );
        //        }


        //        // Récupération du json_str
        //        string? jsonStr =
        //            data[0]
        //                .GetProperty("json_str")
        //                .GetString();


        //        if (string.IsNullOrEmpty(jsonStr))
        //        {
        //            return NotFound(
        //                new
        //                {
        //                    message = "JSON du guide introuvable",
        //                    slug = slug
        //                }
        //            );
        //        }


        //        // Retourne directement le JSON du guide
        //        return Content(
        //            jsonStr,
        //            "application/json"
        //        );
        //    }
        //    catch (Exception ex)
        //    {
        //        return StatusCode(
        //            500,
        //            new
        //            {
        //                message = "Erreur lors de la récupération du guide",
        //                error = ex.Message
        //            }
        //        );
        //    }
        //}


        [HttpGet("category-guides")]
        public async Task<IActionResult> GetCategoryGuides()
        {

            var supabaseUrl = Environment.GetEnvironmentVariable("SUPABASE_URL");
            var supabaseKey = Environment.GetEnvironmentVariable("SUPABASE_KEY");


            var url =
                $"{supabaseUrl}/rest/v1/rpc/get_category_guides";

            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                url
            );

            request.Headers.Add(
                "apikey",
                supabaseKey
            );

            request.Headers.Authorization =
                new AuthenticationHeaderValue(
                    "Bearer",
                    supabaseKey
                );

            using var httpClient = new HttpClient();

            var response =
                await httpClient.SendAsync(request);

            var json =
                await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                return StatusCode(
                    (int)response.StatusCode,
                    json
                );
            }

            return Content(
                json,
                "application/json"
            );
        }

        [HttpGet("getguidebycategory/{categoryName}")]
        public async Task<IActionResult> GetGuideByCategory(string categoryName)
        {
            var supabaseUrl = Environment.GetEnvironmentVariable("SUPABASE_URL");
            var supabaseKey = Environment.GetEnvironmentVariable("SUPABASE_KEY");

            try
            {
                if (string.IsNullOrWhiteSpace(categoryName))
                {
                    return BadRequest(new
                    {
                        message = "La catégorie est obligatoire"
                    });
                }


                // =====================================================
                // SUPABASE RPC
                // =====================================================

                var url =
                    $"{supabaseUrl}/rest/v1/rpc/get_guide_by_category";


                // =====================================================
                // PARAMETRE DE LA FONCTION
                // =====================================================

                var body = new
                {
                    p_category = categoryName
                };


                var json =
                    JsonSerializer.Serialize(body);


                using var request =
                    new HttpRequestMessage(
                        HttpMethod.Post,
                        url
                    );


                // =====================================================
                // HEADERS
                // =====================================================

                request.Headers.Add(
                    "apikey",
                    supabaseKey
                );

                request.Headers.Authorization =
                    new AuthenticationHeaderValue(
                        "Bearer",
                        supabaseKey
                    );


                // =====================================================
                // BODY
                // =====================================================

                request.Content =
                    new StringContent(
                        json,
                        Encoding.UTF8,
                        "application/json"
                    );


                // =====================================================
                // APPEL SUPABASE
                // =====================================================

                using var httpClient =
                    new HttpClient();


                var response =
                    await httpClient.SendAsync(
                        request
                    );


                var responseContent =
                    await response.Content.ReadAsStringAsync();


                // =====================================================
                // ERREUR SUPABASE
                // =====================================================

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine(
                        "SUPABASE ERROR: " +
                        responseContent
                    );

                    return StatusCode(
                        (int)response.StatusCode,
                        new
                        {
                            message =
                                "Erreur lors de la récupération des guides",
                            error =
                                responseContent
                        }
                    );
                }


                // =====================================================
                // RETOURNER LES GUIDES
                // =====================================================

                return Content(
                    responseContent,
                    "application/json"
                );
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    "GET GUIDE BY CATEGORY ERROR: " +
                    ex
                );

                return StatusCode(
                    500,
                    new
                    {
                        message =
                            "Erreur lors de la récupération des guides",
                        error =
                            ex.Message
                    }
                );
            }
        }


        [HttpGet("search")]
        public async Task<IActionResult> SearchGuides(
        [FromQuery] string searchText,
        [FromQuery] int matchCount = 5)
        {
            if (string.IsNullOrWhiteSpace(searchText))
            {
                return BadRequest("searchText est obligatoire.");
            }

            if (matchCount <= 0)
            {
                matchCount = 5;
            }


            // =====================================================
            // 1. Récupérer la configuration
            // =====================================================

            var supabaseUrl = Environment.GetEnvironmentVariable("SUPABASE_URL");
            var supabaseKey = Environment.GetEnvironmentVariable("SUPABASE_KEY");


            if (string.IsNullOrWhiteSpace(supabaseUrl))
            {
                return StatusCode(
                    500,
                    "Supabase URL non configurée."
                );
            }

            if (string.IsNullOrWhiteSpace(supabaseKey))
            {
                return StatusCode(
                    500,
                    "Supabase Key non configurée."
                );
            }


            // =====================================================
            // 2. Générer l'embedding de la recherche
            // =====================================================

            float[] embedding;

            try
            {
                embedding = await GetEmbedding(searchText);
            }
            catch (Exception ex)
            {
                return StatusCode(
                    500,
                    $"Erreur génération embedding : {ex.Message}"
                );
            }


            if (embedding == null || embedding.Length != 1536)
            {
                return StatusCode(
                    500,
                    $"Embedding incorrect : {embedding?.Length ?? 0} dimensions."
                );
            }


            // =====================================================
            // 3. Appeler Supabase RPC
            // =====================================================

            var url =
                    $"{supabaseUrl}/rest/v1/rpc/search_guides";


            var requestBody = new
            {
                query_embedding = embedding,
                match_count = matchCount
            };


            var requestJson =
                JsonSerializer.Serialize(requestBody);


            using var request =
                new HttpRequestMessage(
                    HttpMethod.Post,
                    url
                );


            request.Headers.Add(
                "apikey",
                supabaseKey
            );


            request.Headers.Authorization =
                new AuthenticationHeaderValue(
                    "Bearer",
                    supabaseKey
                );


            request.Content =
                new StringContent(
                    requestJson,
                    Encoding.UTF8,
                    "application/json"
                );


            // =====================================================
            // 4. Envoyer la requête
            // =====================================================

            using var httpClient =
                    new HttpClient();

            using var response =
                await httpClient.SendAsync(request);


            var result =
                await response.Content.ReadAsStringAsync();


            // =====================================================
            // 5. Vérifier l'erreur Supabase
            // =====================================================

            if (!response.IsSuccessStatusCode)
            {
                return StatusCode(
                    (int)response.StatusCode,
                    new
                    {
                        error = "Erreur Supabase",
                        details = result
                    }
                );
            }


            // =====================================================
            // 6. Retourner les guides trouvés
            // =====================================================

            try
            {
                var guides =
                    JsonSerializer.Deserialize<JsonElement>(result);

                return Ok(guides);
            }
            catch
            {
                return Ok(result);
            }
        }


        // =========================================================
        // Ton système actuel de génération d'embedding
        // =========================================================

        private static readonly HttpClient HttpClient =
     new HttpClient();

        public static async Task<float[]> GetEmbedding(
            string text
        )
        {
            var apiKey =
                Environment.GetEnvironmentVariable(
                    "OPENAI_API"
                );

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new Exception(
                    "OPENAI_API est introuvable."
                );
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                throw new Exception(
                    "Le texte pour l'embedding est vide."
                );
            }


            var requestBody = new
            {
                model = "text-embedding-3-small",
                input = text
            };


            var json =
                JsonSerializer.Serialize(
                    requestBody
                );


            for (
                int attempt = 1;
                attempt <= 5;
                attempt++
            )
            {
                using var request =
                    new HttpRequestMessage(
                        HttpMethod.Post,
                        "https://api.openai.com/v1/embeddings"
                    );


                request.Headers.Authorization =
                    new AuthenticationHeaderValue(
                        "Bearer",
                        apiKey
                    );


                request.Content =
                    new StringContent(
                        json,
                        Encoding.UTF8,
                        "application/json"
                    );


                try
                {
                    var response =
                        await HttpClient.SendAsync(
                            request
                        );


                    var responseString =
                        await response.Content
                            .ReadAsStringAsync();


                    if (response.IsSuccessStatusCode)
                    {
                        using var doc =
                            JsonDocument.Parse(
                                responseString
                            );


                        var values =
                            doc.RootElement
                               .GetProperty("data")[0]
                               .GetProperty("embedding");


                        return values
                            .EnumerateArray()
                            .Select(
                                x => x.GetSingle()
                            )
                            .ToArray();
                    }


                    /*
                     * ============================
                     * RATE LIMIT
                     * ============================
                     */

                    if (
                        response.StatusCode ==
                        System.Net.HttpStatusCode
                            .TooManyRequests
                    )
                    {
                        Console.WriteLine(
                            $"OpenAI 429 - tentative {attempt}/5"
                        );

                        Console.WriteLine(
                            responseString
                        );


                        /*
                         * Si OpenAI indique Retry-After,
                         * on utilise cette valeur.
                         */

                        var retryAfter =
                            response.Headers
                                .RetryAfter?
                                .Delta;


                        if (
                            retryAfter == null
                        )
                        {
                            /*
                             * Backoff progressif :
                             *
                             * tentative 1 → 2 secondes
                             * tentative 2 → 4 secondes
                             * tentative 3 → 8 secondes
                             * tentative 4 → 16 secondes
                             * tentative 5 → erreur
                             */

                            retryAfter =
                                TimeSpan.FromSeconds(
                                    Math.Pow(
                                        2,
                                        attempt
                                    )
                                );
                        }


                        if (attempt < 5)
                        {
                            Console.WriteLine(
                                $"Nouvelle tentative dans " +
                                $"{retryAfter.Value.TotalSeconds} secondes"
                            );


                            await Task.Delay(
                                retryAfter.Value
                            );

                            continue;
                        }
                    }


                    /*
                     * ============================
                     * AUTRES ERREURS
                     * ============================
                     */

                    throw new Exception(
                        $"OpenAI erreur " +
                        $"{(int)response.StatusCode}: " +
                        responseString
                    );
                }
                catch (
                    HttpRequestException ex
                )
                {
                    Console.WriteLine(
                        $"Erreur réseau OpenAI : " +
                        ex.Message
                    );


                    if (attempt == 5)
                    {
                        throw;
                    }


                    await Task.Delay(
                        TimeSpan.FromSeconds(
                            Math.Pow(
                                2,
                                attempt
                            )
                        )
                    );
                }
            }


            throw new Exception(
                "OpenAI : trop de requêtes après plusieurs tentatives."
            );
        }

        [HttpGet("search-marketplace")]
        public async Task<IActionResult> SearchGuidesByMarketplace([FromQuery] string marketplace)
        {
            // =====================================================
            // 1. Vérifier le marketplace
            // =====================================================

            if (string.IsNullOrWhiteSpace(marketplace))
            {
                return BadRequest(
                    "marketplace est obligatoire."
                );
            }

            marketplace =
                marketplace.Trim();


            // =====================================================
            // 2. Récupérer la configuration Supabase
            // =====================================================

            var supabaseUrl =
                Environment.GetEnvironmentVariable(
                    "SUPABASE_URL"
                );

            var supabaseKey =
                Environment.GetEnvironmentVariable(
                    "SUPABASE_KEY"
                );


            if (string.IsNullOrWhiteSpace(
                supabaseUrl))
            {
                return StatusCode(
                    500,
                    "Supabase URL non configurée."
                );
            }


            if (string.IsNullOrWhiteSpace(
                supabaseKey))
            {
                return StatusCode(
                    500,
                    "Supabase Key non configurée."
                );
            }


            // =====================================================
            // 3. Appeler Supabase RPC
            // =====================================================

            var url =
                $"{supabaseUrl}/rest/v1/rpc/get_guides_by_marketplace";


            var requestBody = new
            {
                p_marketplace = marketplace
            };


            var requestJson =
                JsonSerializer.Serialize(
                    requestBody
                );


            using var request =
                new HttpRequestMessage(
                    HttpMethod.Post,
                    url
                );


            request.Headers.Add(
                "apikey",
                supabaseKey
            );


            request.Headers.Authorization =
                new AuthenticationHeaderValue(
                    "Bearer",
                    supabaseKey
                );


            request.Content =
                new StringContent(
                    requestJson,
                    Encoding.UTF8,
                    "application/json"
                );


            // =====================================================
            // 4. Envoyer la requête
            // =====================================================

            using var httpClient =
                new HttpClient();


            using var response =
                await httpClient.SendAsync(
                    request
                );


            var result =
                await response.Content
                    .ReadAsStringAsync();


            // =====================================================
            // 5. Vérifier l'erreur Supabase
            // =====================================================

            if (!response.IsSuccessStatusCode)
            {
                return StatusCode(
                    (int)response.StatusCode,
                    new
                    {
                        error = "Erreur Supabase",
                        details = result
                    }
                );
            }


            // =====================================================
            // 6. Retourner les guides
            // =====================================================

            try
            {
                var guides =
                    JsonSerializer.Deserialize<JsonElement>(
                        result
                    );

                return Ok(guides);
            }
            catch
            {
                return Ok(result);
            }
        }

        private static GuideSeoPayload BuildGuideSeo(
    string jsonStr,
    string cleanSlug)
        {
            using var document =
                JsonDocument.Parse(jsonStr);

            var root =
                document.RootElement;

            var guide =
                root.GetProperty("guide");


            // =========================================================
            // GUIDE
            // =========================================================

            var guideTitle =
                guide.TryGetProperty(
                    "title",
                    out var titleElement)
                    ? titleElement.GetString() ?? ""
                    : "";

            var guideDescription =
                guide.TryGetProperty(
                    "description",
                    out var descriptionElement)
                    ? descriptionElement.GetString() ?? ""
                    : "";

            var heroImage = "";

            if (
                guide.TryGetProperty(
                    "hero",
                    out var heroElement) &&
                heroElement.TryGetProperty(
                    "image",
                    out var imageElement)
            )
            {
                heroImage =
                    imageElement.GetString() ?? "";
            }


            // =========================================================
            // TITLE SEO
            // =========================================================

            var metaTitle =
                string.IsNullOrWhiteSpace(guideTitle)
                    ? "Guide beauté | Viliora"
                    : $"{guideTitle} | Viliora";


            // =========================================================
            // DESCRIPTION SEO
            // =========================================================

            var metaDescription =
                string.IsNullOrWhiteSpace(
                    guideDescription)
                    ? "Découvrez notre guide Viliora avec sélection, comparaison et conseils."
                    : guideDescription.Trim();


            // =========================================================
            // PRODUCTS
            // =========================================================

            var products =
                new List<GuideSeoProduct>();

            if (
                guide.TryGetProperty(
                    "products",
                    out var productsElement) &&
                productsElement.ValueKind ==
                JsonValueKind.Array
            )
            {
                foreach (
                    var product
                    in productsElement.EnumerateArray())
                {
                    var seoProduct =
                        new GuideSeoProduct();

                    if (product.TryGetProperty(
                            "name",
                            out var productName))
                    {
                        seoProduct.Name =
                            productName.GetString() ?? "";
                    }

                    if (product.TryGetProperty(
                            "brand",
                            out var brand))
                    {
                        seoProduct.Brand =
                            brand.GetString() ?? "";
                    }

                    if (product.TryGetProperty(
                            "image",
                            out var productImage))
                    {
                        seoProduct.Image =
                            productImage.GetString() ?? "";
                    }

                    if (product.TryGetProperty(
                            "sephora_url",
                            out var productUrl))
                    {
                        seoProduct.Url =
                            productUrl.GetString() ?? "";
                    }

                    if (product.TryGetProperty(
                            "rating",
                            out var rating) &&
                        rating.ValueKind ==
                        JsonValueKind.Number)
                    {
                        seoProduct.Rating =
                            rating.GetDouble();
                    }

                    if (product.TryGetProperty(
                            "reviews",
                            out var reviews) &&
                        reviews.ValueKind ==
                        JsonValueKind.Number)
                    {
                        seoProduct.Reviews =
                            reviews.GetInt32();
                    }

                    if (product.TryGetProperty(
                            "price",
                            out var price) &&
                        price.ValueKind ==
                        JsonValueKind.Number)
                    {
                        seoProduct.Price =
                            price.GetDecimal();
                    }

                    if (product.TryGetProperty(
                            "currency",
                            out var currency))
                    {
                        seoProduct.Currency =
                            currency.GetString() ?? "";
                    }

                    products.Add(seoProduct);
                }
            }


            // =========================================================
            // JSON-LD
            // =========================================================

            var itemList =
                new List<Dictionary<string, object?>>();

            for (int i = 0; i < products.Count; i++)
            {
                var product =
                    products[i];

                var item =
                    new Dictionary<string, object?>
                    {
                        ["@type"] = "ListItem",
                        ["position"] = i + 1,
                        ["name"] = product.Name
                    };

                if (!string.IsNullOrWhiteSpace(product.Url))
                {
                    item["url"] =
                        product.Url;
                }

                itemList.Add(item);
            }


            var jsonLd =
                new Dictionary<string, object?>
                {
                    ["@context"] =
                        "https://schema.org",

                    ["@type"] =
                        "WebPage",

                    ["name"] =
                        guideTitle,

                    ["description"] =
                        guideDescription,

                    ["url"] =
                        "/apps/naya-guide/" + cleanSlug,

                    ["mainEntity"] =
                        new Dictionary<string, object?>
                        {
                            ["@type"] =
                                "ItemList",

                            ["name"] =
                                guideTitle,

                            ["numberOfItems"] =
                                products.Count,

                            ["itemListElement"] =
                                itemList
                        }
                };


            var jsonLdString =
                JsonSerializer.Serialize(
                    jsonLd,
                    new JsonSerializerOptions
                    {
                        Encoder =
                            JavaScriptEncoder.Default
                    }
                );


            return new GuideSeoPayload
            {
                Title =
                    metaTitle,

                Description =
                    metaDescription,

                Image =
                    heroImage,

                CanonicalPath =
                    "/apps/naya-guide/" +
                    cleanSlug,

                JsonLd =
                    jsonLdString
            };
        }

        private async Task<string?> GetGuideJsonByCleanSlug(
    string cleanSlug)
        {
            var supabaseUrl =
                Environment.GetEnvironmentVariable(
                    "SUPABASE_URL");

            var supabaseKey =
                Environment.GetEnvironmentVariable(
                    "SUPABASE_KEY");

            if (
                string.IsNullOrWhiteSpace(
                    supabaseUrl) ||
                string.IsNullOrWhiteSpace(
                    supabaseKey))
            {
                throw new Exception(
                    "Variables SUPABASE_URL / SUPABASE_KEY manquantes.");
            }

            using var httpClient =
                new HttpClient();


            // =========================================================
            // Fonction interne : requête Supabase
            // =========================================================

            async Task<JsonElement> GetSupabase(
                string url)
            {
                using var request =
                    new HttpRequestMessage(
                        HttpMethod.Get,
                        url
                    );

                request.Headers.Add(
                    "apikey",
                    supabaseKey
                );

                request.Headers.Authorization =
                    new AuthenticationHeaderValue(
                        "Bearer",
                        supabaseKey
                    );

                var response =
                    await httpClient.SendAsync(
                        request
                    );

                var text =
                    await response.Content
                        .ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception(
                        $"Supabase error {(int)response.StatusCode}: {text}");
                }

                return JsonSerializer.Deserialize<JsonElement>(
                    text
                );
            }


            // =========================================================
            // 1. Recherche exacte
            // =========================================================

            var exactUrl =
                $"{supabaseUrl}/rest/v1/GuideViliora" +
                $"?select=slug" +
                $"&slug=eq.{Uri.EscapeDataString(cleanSlug)}" +
                $"&limit=1";

            var exactData =
                await GetSupabase(exactUrl);

            string? databaseSlug = null;

            if (
                exactData.ValueKind ==
                    JsonValueKind.Array &&
                exactData.GetArrayLength() > 0
            )
            {
                databaseSlug =
                    exactData[0]
                        .GetProperty("slug")
                        .GetString();
            }


            // =========================================================
            // 2. Si pas trouvé :
            //    comparaison des slugs sans charger json_str
            // =========================================================

            if (string.IsNullOrWhiteSpace(databaseSlug))
            {
                const int pageSize = 1000;

                int offset = 0;

                while (true)
                {
                    var url =
                        $"{supabaseUrl}/rest/v1/GuideViliora" +
                        $"?select=slug" +
                        $"&limit={pageSize}" +
                        $"&offset={offset}";

                    var data =
                        await GetSupabase(url);

                    if (
                        data.ValueKind !=
                        JsonValueKind.Array)
                    {
                        break;
                    }

                    var count =
                        data.GetArrayLength();

                    foreach (
                        var row
                        in data.EnumerateArray())
                    {
                        if (!row.TryGetProperty(
                                "slug",
                                out var slugElement))
                        {
                            continue;
                        }

                        var dbSlug =
                            slugElement.GetString();

                        if (
                            string.IsNullOrWhiteSpace(
                                dbSlug))
                        {
                            continue;
                        }

                        var normalizedDbSlug =
                            NormalizeGuideSlug(dbSlug);

                        var normalizedRequestedSlug =
                            NormalizeGuideSlug(cleanSlug);

                        if (
                            normalizedDbSlug ==
                            normalizedRequestedSlug)
                        {
                            databaseSlug =
                                dbSlug;

                            break;
                        }
                    }

                    if (
                        !string.IsNullOrWhiteSpace(
                            databaseSlug))
                    {
                        break;
                    }

                    if (count < pageSize)
                    {
                        break;
                    }

                    offset += pageSize;
                }
            }


            if (string.IsNullOrWhiteSpace(databaseSlug))
            {
                return null;
            }


            // =========================================================
            // 3. Maintenant seulement :
            //    récupération du JSON du guide trouvé
            // =========================================================

            var guideUrl =
                $"{supabaseUrl}/rest/v1/GuideViliora" +
                $"?select=json_str" +
                $"&slug=eq.{Uri.EscapeDataString(databaseSlug)}" +
                $"&limit=1";

            var guideData =
                await GetSupabase(guideUrl);

            if (
                guideData.ValueKind !=
                    JsonValueKind.Array ||
                guideData.GetArrayLength() == 0)
            {
                return null;
            }

            return guideData[0]
                .GetProperty("json_str")
                .GetString();
        }




    }
}
