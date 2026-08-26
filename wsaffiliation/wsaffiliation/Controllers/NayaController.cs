using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Headers;
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


    }
}
