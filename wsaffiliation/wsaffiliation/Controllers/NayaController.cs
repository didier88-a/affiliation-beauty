using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace wsaffiliation.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class NayaController : ControllerBase
    {
        private readonly IConfiguration _configuration;


        public NayaController(IConfiguration configuration)
        {
            _configuration = configuration;
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
            var supabaseUrl = Environment.GetEnvironmentVariable("SUPABASE_URL");
            var supabaseKey = Environment.GetEnvironmentVariable("SUPABASE_KEY");

            try
            {
                var url =
                    $"{supabaseUrl}/rest/v1/GuideViliora" +
                    $"?select=json_str&slug=eq.{Uri.EscapeDataString(slug)}";

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

                using var httpClient =
                    new HttpClient();

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


                // Aucun guide trouvé
                JsonElement data =
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
                            slug = slug
                        }
                    );
                }


                // Récupération du json_str
                string? jsonStr =
                    data[0]
                        .GetProperty("json_str")
                        .GetString();


                if (string.IsNullOrEmpty(jsonStr))
                {
                    return NotFound(
                        new
                        {
                            message = "JSON du guide introuvable",
                            slug = slug
                        }
                    );
                }


                // Retourne directement le JSON du guide
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
                        message = "Erreur lors de la récupération du guide",
                        error = ex.Message
                    }
                );
            }
        }


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

        public static async Task<float[]> GetEmbedding(string text)
        {            
            var apiKey = Environment.GetEnvironmentVariable("OPENAI_API");

            using var client = new HttpClient();

            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue(
                    "Bearer",
                    apiKey);

            var requestBody = new
            {
                model = "text-embedding-3-small",
                input = text
            };

            var json =
                JsonSerializer.Serialize(requestBody);

            for (int attempt = 1; attempt <= 4; attempt++)
            {
                using var content =
                    new StringContent(
                        json,
                        Encoding.UTF8,
                        "application/json");

                var response =
                    await client.PostAsync(
                        "https://api.openai.com/v1/embeddings",
                        content);

                if (response.IsSuccessStatusCode)
                {
                    var responseString =
                        await response.Content.ReadAsStringAsync();

                    using var doc =
                        JsonDocument.Parse(responseString);

                    var values =
                        doc.RootElement
                           .GetProperty("data")[0]
                           .GetProperty("embedding");

                    return values
                        .EnumerateArray()
                        .Select(x => x.GetSingle())
                        .ToArray();
                }

                if ((int)response.StatusCode == 429)
                {
                    var error =
                        await response.Content.ReadAsStringAsync();

                    Console.WriteLine(
                        $"OpenAI 429 - tentative {attempt}/4"
                    );

                    Console.WriteLine(error);

                    await Task.Delay(
                        TimeSpan.FromSeconds(
                            attempt * 3
                        ));

                    continue;
                }

                var otherError =
                    await response.Content.ReadAsStringAsync();

                throw new Exception(
                    $"OpenAI erreur {response.StatusCode}: {otherError}"
                );
            }

            throw new Exception(
                "OpenAI : trop de requêtes après plusieurs tentatives."
            );
        }



    }
}
