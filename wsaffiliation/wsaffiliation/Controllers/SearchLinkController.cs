using HtmlAgilityPack;
using Microsoft.AspNetCore.Mvc;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace wsaffiliation.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SearchLinkController : ControllerBase
    {

        // GET api/searchlink?affiliation=xxx&texteRecherche=yyy
        [HttpGet]
        public async Task<IActionResult> GetAsync([FromQuery] string affiliation, [FromQuery] string texteRecherche)
        {
            return BadRequest("Les paramètres 'affiliation' et 'texteRecherche' sont requis.");

            if (string.IsNullOrEmpty(affiliation) || string.IsNullOrEmpty(texteRecherche))
            {
                return BadRequest("Les paramètres 'affiliation' et 'texteRecherche' sont requis.");
            }

            string motsCles = await ObtenirMotsClesAvecGPT(texteRecherche);
            var produits = await ScraperSephora(motsCles);

            // 3️⃣ Afficher le JSON
            string json = JsonSerializer.Serialize(produits, new JsonSerializerOptions { WriteIndented = true });

            return Ok(json);

            //return Ok(new
            //{
            //    Affiliation = affiliation,
            //    TexteRecherche = texteRecherche,
            //    Message = "Paramètres reçus avec succès."
            //});
        }

        // POST api/searchlink
        [HttpPost]
        public IActionResult Post([FromBody] SearchLinkRequest request)
        {
            if (request == null || string.IsNullOrEmpty(request.Affiliation) || string.IsNullOrEmpty(request.TexteRecherche))
            {
                return BadRequest("Les paramètres 'affiliation' et 'texteRecherche' sont requis dans le corps de la requête.");
            }

            return Ok(new
            {
                Affiliation = request.Affiliation,
                TexteRecherche = request.TexteRecherche,
                Message = "Paramètres reçus avec succès via POST."
            });
        }


        public static async Task<string> ObtenirMotsClesAvecGPT(string besoin)
        {

            var builder = WebApplication.CreateBuilder();
            var ApiKey = builder.Configuration["ApiKeys:OpenAi"];
            var Model = "gpt-4o-mini"; // Ou "gpt-5-nano" si tu veux réduire le coût

            using var client = new HttpClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);

            var requestBody = new
            {
                model = Model,
                messages = new[]
                {
            new { role = "system", content = "Transforme le besoin du client en 3 à 5 mots-clés optimisés pour rechercher un produit sur Sephora.fr" },
            new { role = "user", content = besoin }
        },
                max_tokens = 50,
                temperature = 0.3
            };

            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
            var response = await client.PostAsync("https://api.openai.com/v1/chat/completions", content);
            response.EnsureSuccessStatusCode();

            var responseString = await response.Content.ReadAsStringAsync();
            var result = JsonDocument.Parse(responseString);
            return result.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString()
                .Trim();

        }

        public static async Task<List<object>> ScraperSephora(string recherche)
        {
            string urlRecherche = "https://www.sephora.fr/recherche/?q=" + Uri.EscapeDataString(recherche);


            using var client = new HttpClient();
            var html = await client.GetStringAsync(urlRecherche);

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var produits = new List<object>();
            var nodes = doc.DocumentNode.SelectNodes("//div[contains(@class,'product-tile')]");

            if (nodes != null)
            {
                foreach (var node in nodes.Take(10))
                {
                    // Récupérer le lien
                    var aTag = node.SelectSingleNode(".//a[contains(@class,'product-tile-link')]");
                    if (aTag == null) continue;

                    var lien = aTag.GetAttributeValue("href", null);
                    if (string.IsNullOrEmpty(lien)) continue;

                    // Ajouter le domaine si lien relatif
                    if (lien.StartsWith("/"))
                        lien = "https://www.sephora.fr" + lien;

                    // Nom du produit
                    var nom = node.SelectSingleNode(".//h3[contains(@class,'product-title')]//span[contains(@class,'title-line-bold')]")?.InnerText.Trim();

                    // Description (ligne juste en dessous)
                    var description = node.SelectSingleNode(".//h3[contains(@class,'product-title')]//span[contains(@class,'title-line') and not(contains(@class,'title-line-bold'))]")?.InnerText.Trim();

                    // Image
                    var imageNode = node.SelectSingleNode(".//div[contains(@class,'product-image')]//div[contains(@class,'product-imgs')]//img[contains(@class,'product-first-img')]");
                    var image = imageNode?.GetAttributeValue("src", null);
                    if (!string.IsNullOrEmpty(image))
                    {
                        if (image.StartsWith("//"))
                            image = "https:" + image;
                        else if (image.StartsWith("/"))
                            image = "https://www.sephora.fr" + image;
                    }

                    // Vérifier que la page existe
                    try
                    {
                        var produitHtml = await client.GetStringAsync(lien);
                        if (produitHtml.Contains("Oups ! la page demandée n'existe plus")) continue;
                    }
                    catch
                    {
                        continue;
                    }

                    produits.Add(new
                    {
                        nom,
                        description,
                        image,
                        lien
                    });

                    if (produits.Count >= 5) break;
                }
            }


            return produits;
        }

    }


    public class SearchLinkRequest
    {
        public string? Affiliation { get; set; }
        public string? TexteRecherche { get; set; }
    }
}
