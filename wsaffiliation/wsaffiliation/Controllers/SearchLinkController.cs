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
            if (string.IsNullOrEmpty(affiliation) || string.IsNullOrEmpty(texteRecherche))
            {
                return BadRequest("Les paramètres 'affiliation' et 'texteRecherche' sont requis.");
            }

            try
            {
                Console.WriteLine($"[INFO] Requête reçue → affiliation={affiliation}, texteRecherche={texteRecherche}");

                // 1️⃣ Générer des mots-clés via GPT
                string motsCles = await ObtenirMotsClesAvecGPT(texteRecherche);
                Console.WriteLine($"[INFO] Mots-clés générés : {motsCles}");

                // 2️⃣ Scraper Sephora
                var produits = await ScraperSephora(motsCles);
                Console.WriteLine($"[INFO] Produits trouvés : {produits.Count}");

                // 3️⃣ Retour JSON
                return Ok(produits);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERREUR] {ex.Message}\n{ex.StackTrace}");
                return Problem("Erreur inconnue lors du traitement : " + ex.Message);
            }
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

        // 🔹 Appel OpenAI pour générer des mots-clés
        public static async Task<string> ObtenirMotsClesAvecGPT(string besoin)
        {
            //var builder = WebApplication.CreateBuilder();

           // var ApiKey = builder.Configuration["ApiKeys:OpenAi"];

            var ApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
            if (string.IsNullOrEmpty(ApiKey))
                throw new Exception("Clé OpenAI introuvable dans les variables d'environnement !");

            var Model = "gpt-4o-mini"; // ou "gpt-5-nano"

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

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                throw new Exception($"Erreur OpenAI ({response.StatusCode}) : {errorContent}");
            }

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

            // 👉 Simuler un vrai navigateur
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) " +
                "AppleWebKit/537.36 (KHTML, like Gecko) " +
                "Chrome/116.0.0.0 Safari/537.36");

            client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
            client.DefaultRequestHeaders.AcceptEncoding.ParseAdd("gzip, deflate, br");
            client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("fr-FR,fr;q=0.9");

            var html = await client.GetStringAsync(urlRecherche);

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var produits = new List<object>();
            var nodes = doc.DocumentNode.SelectNodes("//div[contains(@class,'product-tile')]");

            if (nodes != null)
            {
                foreach (var node in nodes.Take(10))
                {
                    var aTag = node.SelectSingleNode(".//a[contains(@class,'product-tile-link')]");
                    if (aTag == null) continue;

                    var lien = aTag.GetAttributeValue("href", null);
                    if (string.IsNullOrEmpty(lien)) continue;

                    if (lien.StartsWith("/"))
                        lien = "https://www.sephora.fr" + lien;

                    var nom = node.SelectSingleNode(".//h3[contains(@class,'product-title')]//span[contains(@class,'title-line-bold')]")?.InnerText.Trim();
                    var description = node.SelectSingleNode(".//h3[contains(@class,'product-title')]//span[contains(@class,'title-line') and not(contains(@class,'title-line-bold'))]")?.InnerText.Trim();

                    var imageNode = node.SelectSingleNode(".//div[contains(@class,'product-image')]//div[contains(@class,'product-imgs')]//img[contains(@class,'product-first-img')]");
                    var image = imageNode?.GetAttributeValue("src", null);

                    if (!string.IsNullOrEmpty(image))
                    {
                        if (image.StartsWith("//"))
                            image = "https:" + image;
                        else if (image.StartsWith("/"))
                            image = "https://www.sephora.fr" + image;
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

        public static async Task<List<object>> GetProduitsSephora(string recherche)
        {
            string apiKey = Environment.GetEnvironmentVariable("CJ_API_KEY");
            string websiteId = Environment.GetEnvironmentVariable("CJ_WEBSITE_ID");

            string url = $"https://link-search.api.cj.com/v2/product-search?website-id={websiteId}&advertiser-ids=joined&keywords={Uri.EscapeDataString(recherche)}";

            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("Authorization", apiKey);
            client.DefaultRequestHeaders.Add("Accept", "application/json");

            var response = await client.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(json);
            var produits = new List<object>();

            foreach (var item in doc.RootElement.GetProperty("products").EnumerateArray())
            {
                produits.Add(new
                {
                    nom = item.GetProperty("name").GetString(),
                    description = item.GetProperty("description").GetString(),
                    image = item.GetProperty("image-url").GetString(),
                    prix = item.GetProperty("price").GetString(),
                    lien = item.GetProperty("buy-url").GetString()
                });
            }

            return produits;
        }

        public static async Task<List<object>> GetProduitsAwin(string recherche)
        {
            string apiKey = Environment.GetEnvironmentVariable("AWIN_API_KEY");
            string websiteId = Environment.GetEnvironmentVariable("AWIN_WEBSITE_ID");

            // Endpoint exemple (Product Search API)
            string url = $"https://api.awin.com/publishers/{websiteId}/products?search={Uri.EscapeDataString(recherche)}&format=json";

            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
            client.DefaultRequestHeaders.Add("Accept", "application/json");

            var response = await client.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(json);
            var produits = new List<object>();

            foreach (var item in doc.RootElement.GetProperty("products").EnumerateArray())
            {
                produits.Add(new
                {
                    nom = item.GetProperty("name").GetString(),
                    description = item.GetProperty("description").GetString(),
                    image = item.GetProperty("imageUrl").GetString(),
                    prix = item.GetProperty("price").GetString(),
                    lien = item.GetProperty("affiliateUrl").GetString() // lien tracké AWIN
                });
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
