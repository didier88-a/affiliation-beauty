using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Playwright;
using HtmlAgilityPack;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace wsaffiliation.Controllers
{
    public class AmazonProduct
    {
        public string Name { get; set; }
        public decimal? Price { get; set; }
        public double? Rating { get; set; }
        public string Image { get; set; }
        public string AffiliateUrl { get; set; }
        public string Asin { get; set; }
    }

    public class AmazonBestSeller
    {
        public int Rank { get; set; }
        public string Name { get; set; }
        public string Image { get; set; }
        public string Url { get; set; }
        public string Asin { get; set; }
    }

    public class AmazonScraperController
    {
        public  async Task<List<AmazonProduct>> ScraperAmazon(string query)
        {
            var results = new List<AmazonProduct>();

            using var playwright = await Playwright.CreateAsync();

            await using var browser = await playwright.Chromium.LaunchAsync(new()
            {
                Headless = true
            });

            var page = await browser.NewPageAsync();

            var url = $"https://www.amazon.fr/s?k={Uri.EscapeDataString(query)}";

            await page.GotoAsync(url);
            await page.WaitForSelectorAsync("[data-component-type='s-search-result']");

            var html = await page.ContentAsync();

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var nodes = doc.DocumentNode.SelectNodes("//div[@data-component-type='s-search-result']");

            if (nodes == null) return results;

            foreach (var node in nodes)
            {
                try
                {
                    // ❌ 1. SKIP SPONSORED
                    var isSponsored =
                        node.InnerText.ToLower().Contains("sponsor") ||
                        node.InnerText.ToLower().Contains("ad");

                    if (isSponsored)
                        continue;

                    // NAME
                    var name = node.SelectSingleNode(".//h2//span")?.InnerText?.Trim();
                    if (string.IsNullOrEmpty(name)) continue;

                    // ASIN
                    var asin = node.GetAttributeValue("data-asin", null);
                    if (string.IsNullOrEmpty(asin)) continue;

                    // IMAGE
                    var image = node.SelectSingleNode(".//img")?.GetAttributeValue("src", null);

                    // PRICE
                    var priceWhole = node.SelectSingleNode(".//span[@class='a-price-whole']")?.InnerText;
                    var priceFraction = node.SelectSingleNode(".//span[@class='a-price-fraction']")?.InnerText;

                    decimal? price = null;

                    if (!string.IsNullOrEmpty(priceWhole))
                    {
                        var clean = priceWhole.Replace(".", "").Replace(",", "") + "," + priceFraction;
                        decimal.TryParse(clean, NumberStyles.Any, new CultureInfo("fr-FR"), out var parsed);
                        price = parsed;
                    }

                    // RATING ⭐
                    var ratingText =
                        node.SelectSingleNode(".//span[contains(@aria-label,'out of 5')]")
                        ?.GetAttributeValue("aria-label", null);

                    double? rating = null;

                    if (!string.IsNullOrEmpty(ratingText))
                    {
                        var split = ratingText.Split(' ');
                        double.TryParse(split[0], NumberStyles.Any, CultureInfo.InvariantCulture, out var r);
                        rating = r;
                    }

                    // CLEAN LINK
                    var affiliateUrl = $"https://www.amazon.fr/dp/{asin}";

                    results.Add(new AmazonProduct
                    {
                        Name = name,
                        Price = price,
                        Rating = rating,
                        Image = image,
                        AffiliateUrl = affiliateUrl,
                        Asin = asin
                    });

                    if (results.Count >= 10)
                        break;
                }
                catch
                {
                    continue;
                }
            }

            return results;
        }
        public static async Task<List<AmazonBestSeller>> ScrapeBestSellers()
        {
            var results = new List<AmazonBestSeller>();

            using var playwright = await Playwright.CreateAsync();

            await using var browser =
                await playwright.Chromium.LaunchAsync(
                    new BrowserTypeLaunchOptions
                    {
                        Headless = false
                    });

            var page = await browser.NewPageAsync();

            await page.GotoAsync(
                "https://www.amazon.fr/gp/bestsellers/beauty/");

            await page.WaitForSelectorAsync(
                "div.zg-grid-general-faceout");

            var html = await page.ContentAsync();

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var nodes = doc.DocumentNode.SelectNodes(
                "//div[contains(@class,'zg-grid-general-faceout')]");

            if (nodes == null)
                return results;

            int rank = 1;

            foreach (var node in nodes.Take(10))
            {
                try
                {
                    // NAME
                    var name =
                        node.SelectSingleNode(".//div[contains(@class,'_cDEzb_p13n-sc-css-line-clamp')]")
                        ?.InnerText
                        ?.Trim();

                    // IMAGE
                    var image =
                        node.SelectSingleNode(".//img")
                        ?.GetAttributeValue("src", "");

                    // LINK
                    var href =
                        node.SelectSingleNode(".//a")
                        ?.GetAttributeValue("href", "");

                    string url = null;

                    if (!string.IsNullOrEmpty(href))
                    {
                        if (href.StartsWith("/"))
                            url = "https://www.amazon.fr" + href;
                    }

                    // ASIN
                    string asin = null;

                    if (url != null && url.Contains("/dp/"))
                    {
                        asin = url
                            .Split("/dp/")[1]
                            .Split('/')[0];
                    }

                    results.Add(new AmazonBestSeller
                    {
                        Rank = rank,
                        Name = name,
                        Image = image,
                        Url = url,
                        Asin = asin
                    });

                    rank++;
                }
                catch
                {
                }
            }

            return results;
        }

        public static async Task<string> ObtenirMotsClesAvecGPT(string besoin)
        {
            //var builder = WebApplication.CreateBuilder();

            // var ApiKey = builder.Configuration["ApiKeys:OpenAi"];

            var ApiKey = Environment.GetEnvironmentVariable("OPENAI_API");

           
            Console.WriteLine($"[INFO] ApiKey : {ApiKey}");


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
                    new { role = "system", content = "Transforme le besoin du client en 10 mots-clés chaque mot separe par une virgule optimisés pour rechercher un produit sur amazon" },
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



    }
}
