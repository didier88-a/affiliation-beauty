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
        //public IActionResult ShopifyProxy(string? path)
        public async Task<IActionResult> ShopifyProxy(string? path)
        {
            try
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


                // =====================================================
                // JSON TEST
                // =====================================================

                var h = new NayaProductJsonService();
                var jsonn = await h.GetProductJson(path);
                // =========================================================
                // RÉCUPÉRATION DU JSON DU PRODUIT
                // =========================================================

                var jsonStr = @"
{
  ""page"": {
    ""type"": ""perfume"",
    ""slug"": ""valentinoborninromaextradosedonnaparfum100ml"",
    ""product_id"": 801,
    ""language"": ""fr""
  },
  ""product"": {
    ""id"": 801,
    ""brand"": ""Marques"",
    ""name"": ""Valentino Born In Roma Extradose Donna Parfum [100 ml]"",
    ""short_name"": ""Valentino Born In Roma Extradose Donna Parfum [100 ml]"",
    ""type"": ""Parfum"",
    ""image"": ""https://main.thgimages.com/?url=https://static.thcdn.com/productimg/original/15789066-1875208777029991.jpg&format=webp&width=1500&height=1500&fit=cover"",
    ""images"": [
      ""https://main.thgimages.com/?url=https://static.thcdn.com/productimg/original/15789066-1875208777029991.jpg&format=webp&width=1500&height=1500&fit=cover""
    ],
    ""description"": ""Rendant hommage à Rome au crépuscule, l’Eau de Parfum Born in Roma Extradose Donna de Valentino célèbre la Ville éternelle et son allure vibrante. Le parfum s’ouvre sur un trio de jasmins lumineux qui scintillent au fil du Tibre, tandis que des nuances chaleureuses de benjoin subliment la douceur ambrée de la senteur. Un courant sous-jacent de vanille onctueuse harmonise le sillage olfactif, tissant des accents de profondeur veloutée dans tout le parfum pour favoriser un sillage longue durée qui émane de la peau. Logé dans un flacon orné des clous prismatiques emblématiques de la marque, le parfum fait un clin d’œil aux soirées Aristo punk organisées dans la ville, mettant en lumière son allure audacieuse et impertinente. FRÉQUEMMENT ACHETÉS ENSEMBLE Cet article Valentino Born In Roma Extradose Donna Parfum [100 ml] Prix de vente : 184,00 € Prix ​​actuel : 147,20 € Économisez ( 20 % de remise ) *Option 100ml Article recommandé Valentino Born in Roma Intense UOMO Eau de Parfum 100 ml Prix de vente : 126,50 € Prix ​​actuel : 101,20 € Économisez ( 20 % de remise ) *Size 100ml Prix ​​total : 248,40 € AJOUTER LES DEUX AU PANIER D'AUTRES CLIENTS ONT ACHETÉ Valentino Born in Roma Extradose Donna Parfum [50 ml] ÉCONOMISEZ -10% Prix de vente : 138,00 € Prix ​​actuel : 124,20 € 2484,00 € par L ACHETER Valentino Born in Roma Donna Eau de Parfum for Her 100ml ÉCONOMISEZ -20% Prix de vente : 166,75 € Prix ​​actuel : 133,40 € 1334,00 € par L 30ml 50ml 10ml 100ml ACHETER Valentino Born in Roma Donna Eau de Parfum for Her 30ml ÉCONOMISEZ -20% Prix de vente : 83,95 € Prix ​​actuel : 67,16 € 2238,67 € par L 30ml 50ml 10ml 100ml ACHETER Valentino Born In Roma Extradose Donna Parfum [30 ml] ÉCONOMISEZ -20% Prix de vente : 97,75 € Prix ​​actuel : 78,20 € 2606,67 € par L ACHETER Valentino Born in Roma Donna Intense Eau de Parfum for Her 30 ml 90,85 € 3028,33 € par L 30ml ACHETER Valentino Born in Roma Donna Eau de Parfum for Her 50ml ÉCONOMISEZ -20% Prix de vente : 120,75 € Prix ​​actuel : 96,60 € 1932,00 € par L 30ml 50ml 10ml 100ml ACHETER Showing slide 1 TOUS LES AVIS CLIENTS Il n'y a actuellement aucun avis. CRÉER UN AVIS INSCRIVEZ-VOUS À NOTRE NEWSLETTER S'INSCRIRE CONNECTEZ-VOUS AVEC NOUS LOOKFANTASTIC est la boutique de beauté incontournable en Europe, proposant les meilleurs produits de soins de la peau, des cheveux et de maquillage de plus de 200 marques prestigieuses. Faites vos achats en ligne ou via l’application, avec la livraison offerte dès 55€ d'achat. Consentement aux cookies Do Not Sell or Share My Personal Information AIDE ET INFORMATIONS Service Clientèle Assistance Retours Infos de Livraison Nous contacter Suivi de commande INFORMATIONS GÉNÉRALES Termes et Conditions Politique de Confidentialité Information sur les Cookies *Produits en Exclusions Déclaration de l'esclavage moderne À PROPOS DE LOOKFANTASTIC À propos Affiliés Partenariats/Fournisseurs Blog Plan du site Marques Remise Jeunesse et Sénior Remise LOOKFANTASTIC Remise Étudiant.es et Diplômé.es 2026 THG Beauty Europe GmbH Maximilianstrasse 54 80538 Munich Payer en toute sécurité avec Personnalisez votre expérience Notre site utilise des cookies et d’autres technologies similaires afin que nous, et nos partenaires, puissions améliorer les performances opérationnelles du site et vous offrir une expérience d’achat et de publicité personnalisée.Pour modifier vos préférences, sélectionnez « Définir les préférences » où vous pourrez les modifier à tout moment. Plus d’informations ici. Définir les préférences Accepter les cookies"",
    ""category"": ""Parfums d'été"",
    ""sub_category"": ""Parfum Femme"",
    ""rating"": null,
    ""reviews"": 0,
    ""ean"": null,
    ""manufacturer_product_id"": null,
    ""price"": 184.0,
    ""original_price"": 184.0,
    ""currency"": ""EUR"",
    ""slug"": ""valentinoborninromaextradosedonnaparfum100ml""
  },
  ""details"": {
    ""gender"": ""Femme"",
    ""concentration"": ""Eau de Parfum"",
    ""fragrance_family"": [
      ""Floral"",
      ""Oriental""
    ],
    ""olfactory_family"": """",
    ""year"": null,
    ""perfumer"": null,
    ""volume"": ""100ml"",
    ""style"": [
      ""Élégant"",
      ""Séduisant""
    ],
    ""season"": [
      ""Printemps"",
      ""Été""
    ],
    ""occasion"": [
      ""Quotidien"",
      ""Soirée"",
      ""Occasion spéciale""
    ],
    ""intensity"": 3,
    ""longevity"": 4,
    ""sillage"": 4,
    ""best_for"": null
  },
  ""notes"": {
    ""top"": {
      ""title"": ""Notes de tête"",
      ""items"": [
        {
          ""name"": ""Jasmin"",
          ""image"": null
        }
      ]
    },
    ""heart"": {
      ""title"": ""Notes de cœur"",
      ""items"": [
        {
          ""name"": ""Benjoin"",
          ""image"": null
        }
      ]
    },
    ""base"": {
      ""title"": ""Notes de fond"",
      ""items"": [
        {
          ""name"": ""Vanille"",
          ""image"": ""note-fond-vanille.png""
        }
      ]
    }
  },
  ""performance"": {
    ""intensity"": 3,
    ""longevity"": 4,
    ""sillage"": 4,
    ""iconic"": false,
    ""gender"": ""Femme"",
    ""features"": [
      {
        ""type"": ""sillage"",
        ""value"": 4,
        ""label"": ""Sillage exceptionnel""
      },
      {
        ""type"": ""longevity"",
        ""value"": 4,
        ""label"": ""Très longue tenue""
      },
      {
        ""type"": ""gender"",
        ""label"": ""Femme""
      }
    ]
  },
  ""offers"": [
    {
      ""id"": 1001,
      ""marketplace"": ""LookFantastic"",
      ""marketplace_product_id"": ""15789066"",
      ""sku"": ""15789079"",
      ""variant_name"": ""100ml"",
      ""price"": 184.0,
      ""currency"": ""EUR"",
      ""original_price"": null,
      ""is_available"": true,
      ""rating"": null,
      ""reviews"": 0,
      ""product_url"": ""https://www.lookfantastic.fr/p/valentino-born-in-roma-donna-extradose-parfum-100ml/15789066/"",
      ""image"": null
    }
  ],
  ""price_comparison"": {
    ""currency"": ""EUR"",
    ""lowest_price"": 184.0,
    ""lowest_price_marketplace"": ""LookFantastic"",
    ""reference_price"": 184.0,
    ""saving_vs_reference"": 0.0,
    ""saving_percent"": 0.0,
    ""offers_count"": 1,
    ""offers"": [
      {
        ""marketplace"": ""LookFantastic"",
        ""price"": 184.0,
        ""currency"": ""EUR"",
        ""product_url"": ""https://www.lookfantastic.fr/p/valentino-born-in-roma-donna-extradose-parfum-100ml/15789066/"",
        ""is_available"": true
      }
    ]
  },
  ""alternatives"": {
    ""title"": ""Des alternatives similaires"",
    ""description"": ""Découvrez des parfums au profil olfactif proche, avec des caractéristiques et des prix différents."",
    ""reference_price"": 184.0,
    ""reference_currency"": ""EUR"",
    ""products"": [
      {
        ""product_id"": 797,
        ""brand"": ""Marques"",
        ""name"": ""Valentino Born In Roma Extradose Donna Parfum [30 ml]"",
        ""short_name"": ""Valentino Born In Roma Extradose Donna Parfum [30 ml]"",
        ""image"": ""https://main.thgimages.com/?url=https://static.thcdn.com/productimg/original/15789064-8225228672859255.jpg&format=webp&width=1500&height=1500&fit=cover"",
        ""price"": 97.75,
        ""currency"": ""EUR"",
        ""original_price"": 97.75,
        ""saving_amount"": 86.25,
        ""saving_percent"": 46.88,
        ""similarity_score"": 100.0,
        ""fragrance_family"": [
          ""Floral"",
          ""Oriental""
        ],
        ""matched_notes"": [
          ""jasmin"",
          ""benjoin"",
          ""vanille""
        ],
        ""reason"": ""même famille olfactive, notes communes : jasmin, benjoin, vanille, même profil de genre, style similaire."",
        ""product_url"": ""https://www.lookfantastic.fr/p/valentino-born-in-roma-donna-extradose-parfum-30ml/15789064/"",
        ""marketplace"": ""LookFantastic"",
        ""offers"": [
          {
            ""id"": 997,
            ""marketplace"": ""LookFantastic"",
            ""marketplace_product_id"": ""15789064"",
            ""sku"": ""15789079"",
            ""variant_name"": ""30ml"",
            ""price"": 97.75,
            ""currency"": ""EUR"",
            ""original_price"": null,
            ""is_available"": true,
            ""rating"": null,
            ""reviews"": 0,
            ""product_url"": ""https://www.lookfantastic.fr/p/valentino-born-in-roma-donna-extradose-parfum-30ml/15789064/"",
            ""image"": null
          }
        ]
      },
      {
        ""product_id"": 805,
        ""brand"": ""Marques"",
        ""name"": ""Valentino Born in Roma Extradose Donna Parfum [50 ml]"",
        ""short_name"": ""Valentino Born in Roma Extradose Donna Parfum [50 ml]"",
        ""image"": ""https://main.thgimages.com/?url=https://static.thcdn.com/productimg/original/15789079-4115208779091615.jpg&format=webp&width=1500&height=1500&fit=cover"",
        ""price"": 138.0,
        ""currency"": ""EUR"",
        ""original_price"": 138.0,
        ""saving_amount"": 46.0,
        ""saving_percent"": 25.0,
        ""similarity_score"": 98.67,
        ""fragrance_family"": [
          ""Floral"",
          ""Oriental""
        ],
        ""matched_notes"": [
          ""jasmin"",
          ""benjoin"",
          ""vanille""
        ],
        ""reason"": ""même famille olfactive, notes communes : jasmin, benjoin, vanille, même profil de genre, style similaire."",
        ""product_url"": ""https://www.lookfantastic.fr/p/valentino-born-in-roma-donna-extradose-parfum-50ml/15789079/"",
        ""marketplace"": ""LookFantastic"",
        ""offers"": [
          {
            ""id"": 1005,
            ""marketplace"": ""LookFantastic"",
            ""marketplace_product_id"": ""15789079"",
            ""sku"": ""15789079"",
            ""variant_name"": ""50ml"",
            ""price"": 138.0,
            ""currency"": ""EUR"",
            ""original_price"": null,
            ""is_available"": true,
            ""rating"": null,
            ""reviews"": 0,
            ""product_url"": ""https://www.lookfantastic.fr/p/valentino-born-in-roma-donna-extradose-parfum-50ml/15789079/"",
            ""image"": null
          }
        ]
      },
      {
        ""product_id"": 606,
        ""brand"": ""Marques"",
        ""name"": ""Tom Ford Velvet Orchid Eau de Parfum Spray 50ml"",
        ""short_name"": ""Tom Ford Velvet Orchid Eau de Parfum Spray 50ml"",
        ""image"": ""https://main.thgimages.com/?url=https://static.thcdn.com/productimg/original/12018658-1044925970986961.jpg&format=webp&width=1500&height=1500&fit=cover"",
        ""price"": 128.8,
        ""currency"": ""EUR"",
        ""original_price"": 128.8,
        ""saving_amount"": 55.2,
        ""saving_percent"": 30.0,
        ""similarity_score"": 57.33,
        ""fragrance_family"": [
          ""Floral"",
          ""Oriental""
        ],
        ""matched_notes"": [
          ""vanille""
        ],
        ""reason"": ""même famille olfactive, notes communes : vanille, même profil de genre."",
        ""product_url"": ""https://www.lookfantastic.fr/p/tom-ford-velvet-orchid-eau-de-parfum-spray-50ml/12018658/"",
        ""marketplace"": ""LookFantastic"",
        ""offers"": [
          {
            ""id"": 802,
            ""marketplace"": ""LookFantastic"",
            ""marketplace_product_id"": ""12018658"",
            ""sku"": ""12018659"",
            ""variant_name"": ""50ml"",
            ""price"": 128.8,
            ""currency"": ""EUR"",
            ""original_price"": null,
            ""is_available"": true,
            ""rating"": 4.0,
            ""reviews"": 1,
            ""product_url"": ""https://www.lookfantastic.fr/p/tom-ford-velvet-orchid-eau-de-parfum-spray-50ml/12018658/"",
            ""image"": null
          }
        ]
      },
      {
        ""product_id"": 546,
        ""brand"": ""Marques"",
        ""name"": ""Jean Paul Gaultier Classique Eau de Toilette 50ml"",
        ""short_name"": ""Jean Paul Gaultier Classique Eau de Toilette 50ml"",
        ""image"": ""https://main.thgimages.com/?url=https://static.thcdn.com/productimg/original/10043924-4735048072768298.jpg&format=webp&width=1500&height=1500&fit=cover"",
        ""price"": 100.05,
        ""currency"": ""EUR"",
        ""original_price"": 100.05,
        ""saving_amount"": 83.95,
        ""saving_percent"": 45.62,
        ""similarity_score"": 54.33,
        ""fragrance_family"": [
          ""Floral"",
          ""Oriental""
        ],
        ""matched_notes"": [
          ""vanille""
        ],
        ""reason"": ""même famille olfactive, notes communes : vanille, même profil de genre."",
        ""product_url"": ""https://www.lookfantastic.fr/p/jean-paul-gaultier-classique-eau-de-toilette-50ml/10043924/"",
        ""marketplace"": ""LookFantastic"",
        ""offers"": [
          {
            ""id"": 742,
            ""marketplace"": ""LookFantastic"",
            ""marketplace_product_id"": ""10043924"",
            ""sku"": ""10076088"",
            ""variant_name"": ""50ml"",
            ""price"": 100.05,
            ""currency"": ""EUR"",
            ""original_price"": null,
            ""is_available"": true,
            ""rating"": 5.0,
            ""reviews"": 1,
            ""product_url"": ""https://www.lookfantastic.fr/p/jean-paul-gaultier-classique-eau-de-toilette-50ml/10043924/"",
            ""image"": null
          }
        ]
      },
      {
        ""product_id"": 510,
        ""brand"": ""Marques"",
        ""name"": ""Jean Paul Gaultier La Favorite Eau de Parfum 50ml"",
        ""short_name"": ""Jean Paul Gaultier La Favorite Eau de Parfum 50ml"",
        ""image"": ""https://main.thgimages.com/?url=https://static.thcdn.com/productimg/original/17854045-9355355448310043.jpg&format=webp&width=1500&height=1500&fit=cover"",
        ""price"": 118.45,
        ""currency"": ""EUR"",
        ""original_price"": 118.45,
        ""saving_amount"": 65.55,
        ""saving_percent"": 35.62,
        ""similarity_score"": 52.77,
        ""fragrance_family"": [
          ""Floral"",
          ""Oriental""
        ],
        ""matched_notes"": [
          ""vanille""
        ],
        ""reason"": ""même famille olfactive, notes communes : vanille, même profil de genre."",
        ""product_url"": ""https://www.lookfantastic.fr/p/jean-paul-gaultier-la-favorite-eau-de-parfum-50ml/17854045/"",
        ""marketplace"": ""LookFantastic"",
        ""offers"": [
          {
            ""id"": 706,
            ""marketplace"": ""LookFantastic"",
            ""marketplace_product_id"": ""17854045"",
            ""sku"": ""17854040"",
            ""variant_name"": ""50ml"",
            ""price"": 118.45,
            ""currency"": ""EUR"",
            ""original_price"": null,
            ""is_available"": true,
            ""rating"": 3.8,
            ""reviews"": 5,
            ""product_url"": ""https://www.lookfantastic.fr/p/jean-paul-gaultier-la-favorite-eau-de-parfum-50ml/17854045/"",
            ""image"": null
          }
        ]
      },
      {
        ""product_id"": 619,
        ""brand"": ""Marques"",
        ""name"": ""Tom Ford Velvet Orchid Eau de Parfum Spray 100ml"",
        ""short_name"": ""Tom Ford Velvet Orchid Eau de Parfum Spray 100ml"",
        ""image"": ""https://main.thgimages.com/?url=https://static.thcdn.com/productimg/original/12018659-1984925971039932.jpg&format=webp&width=1500&height=1500&fit=cover"",
        ""price"": 181.7,
        ""currency"": ""EUR"",
        ""original_price"": 181.7,
        ""saving_amount"": 2.3,
        ""saving_percent"": 1.25,
        ""similarity_score"": 57.33,
        ""fragrance_family"": [
          ""Floral"",
          ""Oriental""
        ],
        ""matched_notes"": [
          ""vanille""
        ],
        ""reason"": ""même famille olfactive, notes communes : vanille, même profil de genre."",
        ""product_url"": ""https://www.lookfantastic.fr/p/tom-ford-velvet-orchid-eau-de-parfum-spray-100ml/12018659/"",
        ""marketplace"": ""LookFantastic"",
        ""offers"": [
          {
            ""id"": 815,
            ""marketplace"": ""LookFantastic"",
            ""marketplace_product_id"": ""12018659"",
            ""sku"": ""12018659"",
            ""variant_name"": ""100ml"",
            ""price"": 181.7,
            ""currency"": ""EUR"",
            ""original_price"": null,
            ""is_available"": true,
            ""rating"": 5.0,
            ""reviews"": 1,
            ""product_url"": ""https://www.lookfantastic.fr/p/tom-ford-velvet-orchid-eau-de-parfum-spray-100ml/12018659/"",
            ""image"": null
          }
        ]
      }
    ]
  },
  ""recommendations"": {
    ""title"": ""Vous pourriez aussi aimer"",
    ""description"": ""Des parfums sélectionnés selon le profil olfactif, le style et les caractéristiques de ce parfum."",
    ""products"": [
      {
        ""product_id"": 794,
        ""brand"": ""Marques"",
        ""name"": ""Valentino Born in Roma Donna Eau de Parfum for Her 100ml"",
        ""short_name"": ""Valentino Born in Roma Donna Eau de Parfum for Her 100ml"",
        ""image"": ""https://main.thgimages.com/?url=https://static.thcdn.com/productimg/original/12737209-1005335466443608.jpg&format=webp&width=1500&height=1500&fit=cover"",
        ""price"": 166.75,
        ""currency"": ""EUR"",
        ""original_price"": 166.75,
        ""saving_amount"": 17.25,
        ""saving_percent"": 9.38,
        ""similarity_score"": 51.43,
        ""fragrance_family"": [
          ""Floral"",
          ""Oriental""
        ],
        ""matched_notes"": [],
        ""reason"": ""même famille olfactive, même profil de genre, style similaire."",
        ""product_url"": ""https://www.lookfantastic.fr/p/valentino-born-in-roma-donna-eau-de-parfum-for-her-100ml/12737209/"",
        ""marketplace"": ""LookFantastic""
      },
      {
        ""product_id"": 278,
        ""brand"": ""Marques"",
        ""name"": ""Dolce&Gabbana The One Eau de Parfum Intense 75ml"",
        ""short_name"": ""Dolce&Gabbana The One Eau de Parfum Intense 75ml"",
        ""image"": ""https://main.thgimages.com/?url=https://static.thcdn.com/productimg/original/17632149-1265317612950180.jpg&format=webp&width=1500&height=1500&fit=cover"",
        ""price"": 162.15,
        ""currency"": ""EUR"",
        ""original_price"": 162.15,
        ""saving_amount"": 21.85,
        ""saving_percent"": 11.88,
        ""similarity_score"": 49.77,
        ""fragrance_family"": [
          ""Gourmand"",
          ""Floral"",
          ""Épicé""
        ],
        ""matched_notes"": [
          ""vanille""
        ],
        ""reason"": ""notes communes : vanille, même profil de genre."",
        ""product_url"": ""https://www.lookfantastic.fr/p/dolce-gabbana-the-one-eau-de-parfum-intense-75ml/17632149/"",
        ""marketplace"": ""LookFantastic""
      },
      {
        ""product_id"": 368,
        ""brand"": ""Marques"",
        ""name"": ""Armani Code Femme Eau de Parfum - 50ml"",
        ""short_name"": ""Armani Code Femme Eau de Parfum - 50ml"",
        ""image"": ""https://main.thgimages.com/?url=https://static.thcdn.com/productimg/original/10026247-2105318191362198.jpg&format=webp&width=1500&height=1500&fit=cover"",
        ""price"": 111.55,
        ""currency"": ""EUR"",
        ""original_price"": 111.55,
        ""saving_amount"": 72.45,
        ""saving_percent"": 39.38,
        ""similarity_score"": 49.33,
        ""fragrance_family"": [
          ""Floral"",
          ""Oriental""
        ],
        ""matched_notes"": [
          ""vanille""
        ],
        ""reason"": ""même famille olfactive, notes communes : vanille, même profil de genre, style similaire."",
        ""product_url"": ""https://www.lookfantastic.fr/p/armani-code-femme-eau-de-parfum-50ml/10026247/"",
        ""marketplace"": ""LookFantastic""
      },
      {
        ""product_id"": 367,
        ""brand"": ""Marques"",
        ""name"": ""Armani Code Femme Eau de Parfum - 30ml"",
        ""short_name"": ""Armani Code Femme Eau de Parfum - 30ml"",
        ""image"": ""https://main.thgimages.com/?url=https://static.thcdn.com/productimg/original/10002628-1145318191231494.jpg&format=webp&width=1500&height=1500&fit=cover"",
        ""price"": 79.35,
        ""currency"": ""EUR"",
        ""original_price"": 79.35,
        ""saving_amount"": 104.65,
        ""saving_percent"": 56.88,
        ""similarity_score"": 49.33,
        ""fragrance_family"": [
          ""Floral"",
          ""Oriental""
        ],
        ""matched_notes"": [
          ""vanille""
        ],
        ""reason"": ""même famille olfactive, notes communes : vanille, même profil de genre, style similaire."",
        ""product_url"": ""https://www.lookfantastic.fr/p/armani-code-femme-eau-de-parfum-30ml/10002628/"",
        ""marketplace"": ""LookFantastic""
      },
      {
        ""product_id"": 538,
        ""brand"": ""Marques"",
        ""name"": ""Jean Paul Gaultier Le Male Eau de Parfum 125ml"",
        ""short_name"": ""Jean Paul Gaultier Le Male Eau de Parfum 125ml"",
        ""image"": ""https://main.thgimages.com/?url=https://static.thcdn.com/productimg/original/12644211-1864925751194169.jpg&format=webp&width=1500&height=1500&fit=cover"",
        ""price"": 128.8,
        ""currency"": ""EUR"",
        ""original_price"": 128.8,
        ""saving_amount"": 55.2,
        ""saving_percent"": 30.0,
        ""similarity_score"": 48.77,
        ""fragrance_family"": [
          ""Boisé"",
          ""Oriental""
        ],
        ""matched_notes"": [
          ""vanille""
        ],
        ""reason"": ""notes communes : vanille, style similaire."",
        ""product_url"": ""https://www.lookfantastic.fr/p/jean-paul-gaultier-le-male-eau-de-parfum-125ml/12644211/"",
        ""marketplace"": ""LookFantastic""
      },
      {
        ""product_id"": 487,
        ""brand"": ""Marques"",
        ""name"": ""Narciso Rodriguez for Her Musc Noir Rose Eau de Parfum 100ml"",
        ""short_name"": ""Narciso Rodriguez for Her Musc Noir Rose Eau de Parfum 100ml"",
        ""image"": ""https://main.thgimages.com/?url=https://static.thcdn.com/productimg/original/13491245-8384936886696355.jpg&format=webp&width=1500&height=1500&fit=cover"",
        ""price"": 138.0,
        ""currency"": ""EUR"",
        ""original_price"": 138.0,
        ""saving_amount"": 46.0,
        ""saving_percent"": 25.0,
        ""similarity_score"": 48.0,
        ""fragrance_family"": [
          ""Floral"",
          ""Fruité"",
          ""Oriental""
        ],
        ""matched_notes"": [
          ""vanille""
        ],
        ""reason"": ""même famille olfactive, notes communes : vanille, même profil de genre."",
        ""product_url"": ""https://www.lookfantastic.fr/p/narciso-rodriguez-for-her-musc-noir-rose-eau-de-parfum-100ml/13491245/"",
        ""marketplace"": ""LookFantastic""
      }
    ]
  },
  ""guide"": {
    ""available"": true,
    ""id"": ""guide-801"",
    ""slug"": ""valentinoborninromaextradosedonnaparfum100ml"",
    ""title"": ""Guide complet Valentino Born In Roma Extradose Donna Parfum [100 ml]"",
    ""subtitle"": ""Tout savoir sur Valentino Born In Roma Extradose Donna Parfum [100 ml]"",
    ""image"": ""https://main.thgimages.com/?url=https://static.thcdn.com/productimg/original/15789066-1875208777029991.jpg&format=webp&width=1500&height=1500&fit=cover"",
    ""url"": ""/guide/valentinoborninromaextradosedonnaparfum100ml"",
    ""reading_time"": 5,
    ""sections"": []
  },
  ""seo"": {
    ""title"": ""Marques Valentino Born In Roma Extradose Donna Parfum [100 ml] Eau de Parfum"",
    ""description"": ""Découvrez Valentino Born In Roma Extradose Donna Parfum [100 ml] de Marques : notes olfactives, performance, prix et alternatives similaires."",
    ""canonical"": ""/parfum/valentinoborninromaextradosedonnaparfum100ml"",
    ""keywords"": [
      ""Valentino Born In Roma Extradose Donna Parfum [100 ml]"",
      ""Marques"",
      ""parfum"",
      ""fragrance"",
      ""notes olfactives"",
      ""alternative parfum"",
      ""comparateur parfum""
    ]
  }
}
";

                //                var jsonStr = @"
                //{
                //  ""page"": {
                //    ""type"": ""perfume"",
                //    ""slug"": ""baccarat-rouge-540"",
                //    ""product_id"": 659,
                //    ""language"": ""fr""
                //  },

                //  ""product"": {
                //    ""id"": 659,
                //    ""brand"": ""Maison Francis Kurkdjian"",
                //    ""name"": ""Baccarat Rouge 540"",
                //    ""short_name"": ""Baccarat Rouge 540"",
                //    ""type"": ""Eau de Parfum"",
                //    ""image"": ""https://media.sephora.eu/content/dam/gdam/europe/digital/pim/published/C/CHANEL/92482/92586-media_swatch-1.jpg?scaleWidth=750&scaleHeight=750&scaleMode=fit"",
                //    ""images"": [
                //      ""https://media.sephora.eu/content/dam/gdam/europe/digital/pim/published/C/CHANEL/92482/92586-media_swatch-1.jpg?scaleWidth=750&scaleHeight=750&scaleMode=fit"",
                //      ""https://media.sephora.eu/content/dam/gdam/europe/digital/pim/published/C/CHANEL/92482/92586-media_swatch-1.jpg?scaleWidth=750&scaleHeight=750&scaleMode=fit"",
                //      ""https://media.sephora.eu/content/dam/gdam/europe/digital/pim/published/C/CHANEL/92482/92586-media_swatch-1.jpg?scaleWidth=750&scaleHeight=750&scaleMode=fit"",
                //      ""https://media.sephora.eu/content/dam/gdam/europe/digital/pim/published/C/CHANEL/92482/92586-media_swatch-1.jpg?scaleWidth=750&scaleHeight=750&scaleMode=fit""
                //    ],
                //    ""description"": ""Une fragrance iconique et envoûtante, au sillage unique, mêlant le safran, l'ambre et des notes boisées."",
                //    ""category"": ""Parfum"",
                //    ""sub_category"": ""Parfum femme et homme"",
                //    ""rating"": 4.8,
                //    ""reviews"": 2341,
                //    ""ean"": null,
                //    ""manufacturer_product_id"": null
                //  },

                //  ""details"": {
                //    ""gender"": ""Unisexe"",
                //    ""concentration"": ""Eau de Parfum"",
                //    ""fragrance_family"": ""Ambré Floral"",
                //    ""olfactory_family"": ""Ambré Floral"",
                //    ""year"": 2015,
                //    ""perfumer"": ""Francis Kurkdjian"",
                //    ""volume"": ""70 ml"",
                //    ""style"": [
                //      ""Luxe"",
                //      ""Élégant"",
                //      ""Sophistiqué""
                //    ],
                //    ""season"": [
                //      ""Automne"",
                //      ""Hiver"",
                //      ""Printemps""
                //    ],
                //    ""occasion"": [
                //      ""Soirée"",
                //      ""Sortie"",
                //      ""Occasion spéciale""
                //    ],
                //    ""intensity"": ""Forte"",
                //    ""longevity"": ""Très longue"",
                //    ""sillage"": ""Exceptionnel"",
                //    ""best_for"": ""Pour celles et ceux qui recherchent un parfum élégant, reconnaissable et très persistant.""
                //  },

                //  ""notes"": {
                //    ""top"": [
                //      ""Gingembre"",
                //      ""Bergamote""
                //    ],
                //    ""heart"": [
                //      ""Jasmin"",
                //      ""Ambre gris""
                //    ],
                //    ""base"": [
                //      ""cèdre"",
                //      ""Résines""
                //    ]
                //  },

                //  ""performance"": {
                //    ""intensity"": 4,
                //    ""longevity"": 5,
                //    ""sillage"": 5,
                //    ""labels"": {
                //      ""intensity"": ""Intensité"",
                //      ""longevity"": ""Longévité"",
                //      ""sillage"": ""Sillage""
                //    }
                //  },

                //  ""offers"": [
                //    {
                //      ""id"": 368,
                //      ""marketplace"": ""sephora"",
                //      ""marketplace_product_id"": ""123456"",
                //      ""sku"": ""BR540-70"",
                //      ""variant_name"": ""70 ml"",
                //      ""price"": 245.00,
                //      ""currency"": ""EUR"",
                //      ""original_price"": 250.00,
                //      ""is_available"": true,
                //      ""rating"": 4.8,
                //      ""reviews"": 2341,
                //      ""product_url"": ""https://www.sephora.fr/example"",
                //      ""image"": ""https://example.com/sephora-baccarat.jpg""
                //    },
                //    {
                //      ""id"": 349,
                //      ""marketplace"": ""lookfantastic"",
                //      ""marketplace_product_id"": ""LF123456"",
                //      ""sku"": ""BR540-70"",
                //      ""variant_name"": ""70 ml"",
                //      ""price"": 238.00,
                //      ""currency"": ""EUR"",
                //      ""original_price"": 245.00,
                //      ""is_available"": true,
                //      ""rating"": 4.7,
                //      ""reviews"": 1820,
                //      ""product_url"": ""https://www.lookfantastic.fr/example"",
                //      ""image"": ""https://example.com/lookfantastic-baccarat.jpg""
                //    },
                //    {
                //      ""id"": 355,
                //      ""marketplace"": ""amazon"",
                //      ""marketplace_product_id"": ""B08XXXXXXX"",
                //      ""sku"": ""BR540-70"",
                //      ""variant_name"": ""70 ml"",
                //      ""price"": 189.00,
                //      ""currency"": ""EUR"",
                //      ""original_price"": 230.00,
                //      ""is_available"": true,
                //      ""rating"": 4.6,
                //      ""reviews"": 5320,
                //      ""product_url"": ""https://www.amazon.fr/example"",
                //      ""image"": ""https://example.com/amazon-baccarat.jpg""
                //    }
                //  ],

                //  ""price_comparison"": {
                //    ""currency"": ""EUR"",
                //    ""lowest_price"": 189.00,
                //    ""lowest_price_marketplace"": ""amazon"",
                //    ""saving_vs_reference"": 56.00,
                //    ""offers_count"": 3,

                //    ""offers"": [
                //      {
                //        ""marketplace"": ""amazon"",
                //        ""label"": ""Amazon"",
                //        ""price"": 189.00,
                //        ""currency"": ""EUR"",
                //        ""variant"": ""70 ml"",
                //        ""availability"": ""En stock"",
                //        ""url"": ""https://www.amazon.fr/example""
                //      },
                //      {
                //        ""marketplace"": ""lookfantastic"",
                //        ""label"": ""LookFantastic"",
                //        ""price"": 238.00,
                //        ""currency"": ""EUR"",
                //        ""variant"": ""70 ml"",
                //        ""availability"": ""En stock"",
                //        ""url"": ""https://www.lookfantastic.fr/example""
                //      },
                //      {
                //        ""marketplace"": ""sephora"",
                //        ""label"": ""Sephora"",
                //        ""price"": 245.00,
                //        ""currency"": ""EUR"",
                //        ""variant"": ""70 ml"",
                //        ""availability"": ""En stock"",
                //        ""url"": ""https://www.sephora.fr/example""
                //      }
                //    ]
                //  },

                //  ""alternatives"": {
                //    ""title"": ""5 alternatives au même style"",
                //    ""description"": ""Des parfums inspirés de Baccarat Rouge 540, avec des notes similaires et un prix plus accessible."",
                //    ""reference_price"": 245.00,
                //    ""reference_currency"": ""EUR"",

                //    ""products"": [
                //      {
                //        ""product_id"": 721,
                //        ""brand"": ""Lattafa"",
                //        ""name"": ""Ana Abiyedh Rouge"",
                //        ""image"": ""https://m.media-amazon.com/images/I/41YdnQMDfJL._AC_SY300_SX300_QL70_ML2_.jpg"",
                //        ""price"": 29.00,
                //        ""currency"": ""EUR"",
                //        ""original_price"": 65.00,
                //        ""saving_amount"": 36.00,
                //        ""saving_percent"": 55,
                //        ""similarity_score"": 92,
                //        ""fragrance_family"": ""Ambré"",
                //        ""matched_notes"": [
                //          ""Safran"",
                //          ""Ambre"",
                //          ""Jasmin""
                //        ],
                //        ""reason"": ""Une alternative très proche avec une signature ambrée et musquée."",
                //        ""offers"": [
                //          {
                //            ""marketplace"": ""amazon"",
                //            ""price"": 29.00,
                //            ""currency"": ""EUR"",
                //            ""url"": ""https://www.amazon.fr/example""
                //          }
                //        ]
                //      },

                //      {
                //        ""product_id"": 722,
                //        ""brand"": ""Ariana Grande"",
                //        ""name"": ""Cloud"",
                //        ""image"": ""https://m.media-amazon.com/images/I/61Q3ckTbVeL._AC_SX425_.jpg"",
                //        ""price"": 45.00,
                //        ""currency"": ""EUR"",
                //        ""original_price"": 79.00,
                //        ""saving_amount"": 34.00,
                //        ""saving_percent"": 43,
                //        ""similarity_score"": 89,
                //        ""fragrance_family"": ""Ambré Floral"",
                //        ""matched_notes"": [
                //          ""Ambre"",
                //          ""Bois"",
                //          ""Sucré""
                //        ],
                //        ""reason"": ""Une fragrance douce et ambrée partageant plusieurs caractéristiques avec Baccarat Rouge 540."",
                //        ""offers"": [
                //          {
                //            ""marketplace"": ""sephora"",
                //            ""price"": 45.00,
                //            ""currency"": ""EUR"",
                //            ""url"": ""https://www.sephora.fr/example""
                //          }
                //        ]
                //      },

                //      {
                //        ""product_id"": 723,
                //        ""brand"": ""Dossier"",
                //        ""name"": ""BR540 Extrait Inspiration"",
                //        ""image"": ""https://m.media-amazon.com/images/I/61PFsPG3y1L._AC_UL450_SY450_QL70_.jpg"",
                //        ""price"": 39.00,
                //        ""currency"": ""EUR"",
                //        ""original_price"": 59.00,
                //        ""saving_amount"": 20.00,
                //        ""saving_percent"": 34,
                //        ""similarity_score"": 87,
                //        ""fragrance_family"": ""Ambré"",
                //        ""matched_notes"": [
                //          ""Safran"",
                //          ""Ambre"",
                //          ""Boisé""
                //        ],
                //        ""reason"": ""Une composition inspirée directement de l'univers olfactif de Baccarat Rouge 540."",
                //        ""offers"": []
                //      },

                //      {
                //        ""product_id"": 724,
                //        ""brand"": ""Al Haramain"",
                //        ""name"": ""Amber Oud Rouge"",
                //        ""image"": ""https://m.media-amazon.com/images/I/61OsbRY7MYL._AC_SX425_.jpg"",
                //        ""price"": 49.00,
                //        ""currency"": ""EUR"",
                //        ""original_price"": 79.00,
                //        ""saving_amount"": 30.00,
                //        ""saving_percent"": 38,
                //        ""similarity_score"": 84,
                //        ""fragrance_family"": ""Ambré Boisé"",
                //        ""matched_notes"": [
                //          ""Safran"",
                //          ""Ambre"",
                //          ""Résineux""
                //        ],
                //        ""reason"": ""Une alternative riche et intense avec une forte présence ambrée."",
                //        ""offers"": []
                //      },

                //      {
                //        ""product_id"": 725,
                //        ""brand"": ""Zara"",
                //        ""name"": ""Red Temptation"",
                //        ""image"": ""https://m.media-amazon.com/images/I/61PU7gv-+uL._AC_SX425_.jpg"",
                //        ""price"": 25.00,
                //        ""currency"": ""EUR"",
                //        ""original_price"": 39.00,
                //        ""saving_amount"": 14.00,
                //        ""saving_percent"": 36,
                //        ""similarity_score"": 82,
                //        ""fragrance_family"": ""Ambré Floral"",
                //        ""matched_notes"": [
                //          ""Ambre"",
                //          ""Jasmin"",
                //          ""Boisé""
                //        ],
                //        ""reason"": ""Une option accessible avec une signature ambrée et florale similaire."",
                //        ""offers"": []
                //      }
                //    ]
                //  },

                //  ""guide"": {
                //    ""available"": true,
                //    ""id"": ""baccarat-rouge-540"",
                //    ""slug"": ""baccarat-rouge-540"",
                //    ""title"": ""Tout savoir sur Baccarat Rouge 540"",
                //    ""subtitle"": ""Son histoire, ses notes, ses alternatives et nos conseils pour bien le choisir."",
                //    ""image"": ""https://example.com/guide-baccarat.jpg"",
                //    ""url"": ""/guide/baccarat-rouge-540"",
                //    ""reading_time"": ""6 min"",
                //    ""sections"": [
                //      ""Présentation"",
                //      ""Notes olfactives"",
                //      ""Longévité et sillage"",
                //      ""Alternatives"",
                //      ""Conseils""
                //    ]
                //  },

                //  ""recommendations"": {
                //    ""title"": ""Vous pourriez aussi aimer"",
                //    ""description"": ""D'autres parfums qui pourraient vous plaire."",

                //    ""products"": [
                //      {
                //        ""product_id"": 801,
                //        ""brand"": ""Tom Ford"",
                //        ""name"": ""Lost Cherry"",
                //        ""image"": ""https://main.thgimages.com/?url=https://static.thcdn.com/productimg/1600/1600/12709517-1484810185123876.jpg&format=webp&width=1500&height=1500&fit=cover"",
                //        ""rating"": 4.7,
                //        ""url"": ""/parfum/lost-cherry""
                //      },
                //      {
                //        ""product_id"": 802,
                //        ""brand"": ""Yves Saint Laurent"",
                //        ""name"": ""Libre"",
                //        ""image"": ""https://media.sephora.eu/content/dam/gdam/europe/digital/emerch/fr/productset/ysl_libre_loveshine_1.jpg"",
                //        ""rating"": 4.6,
                //        ""url"": ""/parfum/ysl-libre""
                //      },
                //      {
                //        ""product_id"": 803,
                //        ""brand"": ""Dior"",
                //        ""name"": ""Sauvage"",
                //        ""image"": ""https://media.sephora.eu/content/dam/gdam/europe/digital/pim/published/D/DIOR/421404/10391-media_swatch.jpg"",
                //        ""rating"": 4.7,
                //        ""url"": ""/parfum/dior-sauvage""
                //      },
                //      {
                //        ""product_id"": 804,
                //        ""brand"": ""Chanel"",
                //        ""name"": ""Coco Mademoiselle"",
                //        ""image"": ""https://media.sephora.eu/content/dam/gdam/europe/digital/pim/published/C/CHANEL/92482/92586-media_swatch-1.jpg?scaleWidth=750&scaleHeight=750&scaleMode=fit"",
                //        ""rating"": 4.8,
                //        ""url"": ""/parfum/coco-mademoiselle""
                //      },
                //      {
                //        ""product_id"": 805,
                //        ""brand"": ""Parfums de Marly"",
                //        ""name"": ""Delina"",
                //        ""image"": ""https://m.media-amazon.com/images/I/419m37SbQ-L._AC_SY300_SX300_QL70_ML2_.jpg"",
                //        ""rating"": 4.7,
                //        ""url"": ""/parfum/delina""
                //      },
                //      {
                //        ""product_id"": 806,
                //        ""brand"": ""Lancôme"",
                //        ""name"": ""La Vie Est Belle"",
                //        ""image"": ""https://media.sephora.eu/content/dam/gdam/europe/digital/pim/published/L/LANCO/254230/45891-media_swatch.jpg?scaleWidth=750&scaleHeight=750&scaleMode=fit"",
                //        ""rating"": 4.6,
                //        ""url"": ""/parfum/la-vie-est-belle""
                //      }
                //    ]
                //  },

                //  ""seo"": {
                //    ""title"": ""Baccarat Rouge 540 : prix, alternatives et avis | Viliora"",
                //    ""description"": ""Découvrez Baccarat Rouge 540, comparez les prix chez les meilleurs revendeurs et trouvez 5 alternatives moins chères."",
                //    ""canonical"": ""/parfum/baccarat-rouge-540"",
                //    ""keywords"": [
                //      ""Baccarat Rouge 540"",
                //      ""Baccarat Rouge 540 prix"",
                //      ""Baccarat Rouge 540 alternative"",
                //      ""Baccarat Rouge 540 dupe"",
                //      ""parfum similaire Baccarat Rouge 540""
                //    ]
                //  }
                //}";


                // =====================================================
                // LIQUID
                // =====================================================

                var liquid = @"
<script>
window.NAYA_PROXY_SLUG = " +
                    JsonSerializer.Serialize(cleanSlug) +
                    @";

window.NAYA_GUIDE = " +
                    jsonStr +
                    @";
</script>

{% section 'naya-perfume-hero' %}
{% section 'naya-perfume-details' %}
{% section 'naya-perfume-alternatives' %}
{% section 'naya-price-profile' %}
{% section 'naya-guide-banner' %}
{% section 'naya-recommendations' %}

<script>
document.dispatchEvent(
    new CustomEvent('naya:guide-loaded', {
        detail: window.NAYA_GUIDE
    })
);
</script>
";


                return Content(
                    liquid,
                    "application/liquid"
                );
            }
            catch (Exception ex)
            {
                return StatusCode(
                    500,
                    "Shopify Proxy ERROR: " + ex.ToString()
                );
            }
        }


        //        [HttpGet]
        //        [Route("~/api/shopify/proxy/{*path}")]
        //        public async Task<IActionResult> ShopifyProxy(string? path)
        //        {
        //            if (string.IsNullOrWhiteSpace(path))
        //            {
        //                return BadRequest("Slug manquant");
        //            }

        //            var cleanSlug =
        //                CreateUrlSlug(path.Trim('/'));

        //            if (string.IsNullOrWhiteSpace(cleanSlug))
        //            {
        //                return BadRequest("Slug invalide");
        //            }

        //            // =========================================================
        //            // SHOPIFY STORE
        //            // =========================================================

        //            var forwardedHost =
        //                Request.Headers["X-Forwarded-Host"].FirstOrDefault();

        //            if (string.IsNullOrWhiteSpace(forwardedHost))
        //            {
        //                forwardedHost =
        //                    Request.Host.Host;
        //            }

        //            var storefrontOrigin =
        //                "https://" + forwardedHost;


        //            // =========================================================
        //            // RÉCUPÉRATION DU JSON DU PRODUIT
        //            // =========================================================

        //            var jsonStr = @"
        //{
        //  ""page"": {
        //    ""type"": ""perfume"",
        //    ""slug"": ""baccarat-rouge-540"",
        //    ""product_id"": 659,
        //    ""language"": ""fr""
        //  },
        //  ""product"": {
        //    ""id"": 659,
        //    ""brand"": ""Maison Francis Kurkdjian"",
        //    ""name"": ""Baccarat Rouge 540"",
        //    ""short_name"": ""Baccarat Rouge 540"",
        //    ""type"": ""Eau de Parfum"",
        //    ""image"": ""https://media.sephora.eu/content/dam/gdam/europe/digital/pim/published/M/MAISON_FRANCIS_KURKDJIAN/558477/241051-media_swatch-4.jpg"",
        //    ""images"": [
        //      ""https://media.sephora.eu/content/dam/gdam/europe/digital/pim/published/M/MAISON_FRANCIS_KURKDJIAN/558477/241051-media_swatch-4.jpg""
        //    ],
        //    ""description"": ""Une fragrance iconique et envoûtante, au sillage unique, mêlant le safran, l'ambre et des notes boisées."",
        //    ""category"": ""Parfum"",
        //    ""sub_category"": ""Parfum femme et homme"",
        //    ""rating"": 4.8,
        //    ""reviews"": 2341,
        //    ""ean"": null,
        //    ""manufacturer_product_id"": null
        //  },
        //  ""details"": {
        //    ""gender"": ""Unisexe"",
        //    ""concentration"": ""Eau de Parfum"",
        //    ""fragrance_family"": ""Ambré Floral"",
        //    ""olfactory_family"": ""Ambré Floral"",
        //    ""year"": 2015,
        //    ""perfumer"": ""Francis Kurkdjian"",
        //    ""volume"": ""70 ml"",
        //    ""style"": [
        //      ""Luxe"",
        //      ""Élégant"",
        //      ""Sophistiqué""
        //    ],
        //    ""season"": [
        //      ""Automne"",
        //      ""Hiver"",
        //      ""Printemps""
        //    ],
        //    ""occasion"": [
        //      ""Soirée"",
        //      ""Sortie"",
        //      ""Occasion spéciale""
        //    ],
        //    ""intensity"": ""Forte"",
        //    ""longevity"": ""Très longue"",
        //    ""sillage"": ""Exceptionnel"",
        //    ""best_for"": ""Pour celles et ceux qui recherchent un parfum élégant, reconnaissable et très persistant.""
        //  },
        //  ""notes"": {
        //    ""top"": [
        //      ""Safran"",
        //      ""Bergamote""
        //    ],
        //    ""heart"": [
        //      ""Jasmin"",
        //      ""Ambre gris""
        //    ],
        //    ""base"": [
        //      ""Bois de cèdre"",
        //      ""Résines""
        //    ]
        //  },
        //  ""performance"": {
        //    ""intensity"": 4,
        //    ""longevity"": 5,
        //    ""sillage"": 5,
        //    ""labels"": {
        //      ""intensity"": ""Intensité"",
        //      ""longevity"": ""Longévité"",
        //      ""sillage"": ""Sillage""
        //    }
        //  },
        //  ""offers"": [
        //    {
        //      ""id"": 368,
        //      ""marketplace"": ""sephora"",
        //      ""marketplace_product_id"": ""123456"",
        //      ""sku"": ""BR540-70"",
        //      ""variant_name"": ""70 ml"",
        //      ""price"": 245.00,
        //      ""currency"": ""EUR"",
        //      ""original_price"": 250.00,
        //      ""is_available"": true,
        //      ""rating"": 4.8,
        //      ""reviews"": 2341,
        //      ""product_url"": ""https://www.sephora.fr/example"",
        //      ""image"": ""https://example.com/sephora-baccarat.jpg""
        //    },
        //    {
        //      ""id"": 349,
        //      ""marketplace"": ""lookfantastic"",
        //      ""marketplace_product_id"": ""LF123456"",
        //      ""sku"": ""BR540-70"",
        //      ""variant_name"": ""70 ml"",
        //      ""price"": 238.00,
        //      ""currency"": ""EUR"",
        //      ""original_price"": 245.00,
        //      ""is_available"": true,
        //      ""rating"": 4.7,
        //      ""reviews"": 1820,
        //      ""product_url"": ""https://www.lookfantastic.fr/example"",
        //      ""image"": ""https://example.com/lookfantastic-baccarat.jpg""
        //    },
        //    {
        //      ""id"": 355,
        //      ""marketplace"": ""amazon"",
        //      ""marketplace_product_id"": ""B08XXXXXXX"",
        //      ""sku"": ""BR540-70"",
        //      ""variant_name"": ""70 ml"",
        //      ""price"": 189.00,
        //      ""currency"": ""EUR"",
        //      ""original_price"": 230.00,
        //      ""is_available"": true,
        //      ""rating"": 4.6,
        //      ""reviews"": 5320,
        //      ""product_url"": ""https://www.amazon.fr/example"",
        //      ""image"": ""https://example.com/amazon-baccarat.jpg""
        //    }
        //  ],
        //  ""price_comparison"": {
        //    ""currency"": ""EUR"",
        //    ""lowest_price"": 189.00,
        //    ""lowest_price_marketplace"": ""amazon"",
        //    ""saving_vs_reference"": 56.00,
        //    ""offers_count"": 3,
        //    ""offers"": [
        //      {
        //        ""marketplace"": ""amazon"",
        //        ""label"": ""Amazon"",
        //        ""price"": 189.00,
        //        ""currency"": ""EUR"",
        //        ""variant"": ""70 ml"",
        //        ""availability"": ""En stock"",
        //        ""url"": ""https://www.amazon.fr/example""
        //      },
        //      {
        //        ""marketplace"": ""lookfantastic"",
        //        ""label"": ""LookFantastic"",
        //        ""price"": 238.00,
        //        ""currency"": ""EUR"",
        //        ""variant"": ""70 ml"",
        //        ""availability"": ""En stock"",
        //        ""url"": ""https://www.lookfantastic.fr/example""
        //      },
        //      {
        //        ""marketplace"": ""sephora"",
        //        ""label"": ""Sephora"",
        //        ""price"": 245.00,
        //        ""currency"": ""EUR"",
        //        ""variant"": ""70 ml"",
        //        ""availability"": ""En stock"",
        //        ""url"": ""https://www.sephora.fr/example""
        //      }
        //    ]
        //  },
        //  ""alternatives"": {
        //    ""title"": ""5 alternatives au même style"",
        //    ""description"": ""Des parfums inspirés de Baccarat Rouge 540, avec des notes similaires et un prix plus accessible."",
        //    ""reference_price"": 245.00,
        //    ""reference_currency"": ""EUR"",
        //    ""products"": [
        //      {
        //        ""product_id"": 721,
        //        ""brand"": ""Lattafa"",
        //        ""name"": ""Ana Abiyedh Rouge"",
        //        ""image"": ""https://m.media-amazon.com/images/I/41YdnQMDfJL._AC_SY300_SX300_QL70_ML2_.jpg"",
        //        ""price"": 29.00,
        //        ""currency"": ""EUR"",
        //        ""original_price"": 65.00,
        //        ""saving_amount"": 36.00,
        //        ""saving_percent"": 55,
        //        ""similarity_score"": 92,
        //        ""fragrance_family"": ""Ambré"",
        //        ""matched_notes"": [
        //          ""Safran"",
        //          ""Ambre"",
        //          ""Jasmin""
        //        ],
        //        ""reason"": ""Une alternative très proche avec une signature ambrée et musquée."",
        //        ""offers"": [
        //          {
        //            ""marketplace"": ""amazon"",
        //            ""price"": 29.00,
        //            ""currency"": ""EUR"",
        //            ""url"": ""https://www.amazon.fr/example""
        //          }
        //        ]
        //      }
        //    ]
        //  },
        //  ""guide"": {
        //    ""available"": true,
        //    ""id"": ""baccarat-rouge-540"",
        //    ""slug"": ""baccarat-rouge-540"",
        //    ""title"": ""Tout savoir sur Baccarat Rouge 540"",
        //    ""subtitle"": ""Son histoire, ses notes, ses alternatives et nos conseils pour bien le choisir."",
        //    ""image"": ""https://example.com/guide-baccarat.jpg"",
        //    ""url"": ""/guide/baccarat-rouge-540"",
        //    ""reading_time"": ""6 min"",
        //    ""sections"": [
        //      ""Présentation"",
        //      ""Notes olfactives"",
        //      ""Longévité et sillage"",
        //      ""Alternatives"",
        //      ""Conseils""
        //    ]
        //  },
        //  ""recommendations"": {
        //    ""title"": ""Vous pourriez aussi aimer"",
        //    ""description"": ""D'autres parfums qui pourraient vous plaire."",
        //    ""products"": []
        //  },
        //  ""seo"": {
        //    ""title"": ""Baccarat Rouge 540 : prix, alternatives et avis | Viliora"",
        //    ""description"": ""Découvrez Baccarat Rouge 540, comparez les prix chez les meilleurs revendeurs et trouvez 5 alternatives moins chères."",
        //    ""canonical"": ""/parfum/baccarat-rouge-540"",
        //    ""keywords"": [
        //      ""Baccarat Rouge 540"",
        //      ""Baccarat Rouge 540 prix"",
        //      ""Baccarat Rouge 540 alternative"",
        //      ""Baccarat Rouge 540 dupe"",
        //      ""parfum similaire Baccarat Rouge 540""
        //    ]
        //  }
        //}";

        //            if (string.IsNullOrWhiteSpace(jsonStr))
        //            {
        //                return NotFound(
        //                    new
        //                    {
        //                        message = "Produit introuvable",
        //                        slug = cleanSlug
        //                    }
        //                );
        //            }


        //            // =========================================================
        //            // SEO
        //            // =========================================================

        //            var seo =
        //                BuildGuideSeo(
        //                    jsonStr,
        //                    cleanSlug,
        //                    storefrontOrigin
        //                );

        //            var seoJson =
        //                JsonSerializer.Serialize(
        //                    seo,
        //                    new JsonSerializerOptions
        //                    {
        //                        PropertyNamingPolicy =
        //                            JsonNamingPolicy.CamelCase,

        //                        Encoder =
        //                            JavaScriptEncoder.Default
        //                    }
        //                );


        //            // =========================================================
        //            // SHOPIFY LIQUID
        //            // =========================================================

        //            var liquid = $@"

        //        {{% section 'naya-perfume-hero' %}}

        //        {{% section 'naya-perfume-details' %}}

        //        {{% section 'naya-perfume-alternatives' %}}

        //        {{% section 'naya-price-profile' %}}

        //        {{% section 'naya-guide-banner' %}}


        //        <script>

        //            window.NAYA_PROXY_SLUG =
        //                {JsonSerializer.Serialize(cleanSlug)};

        //            window.NAYA_GUIDE =
        //                {jsonStr};

        //            window.NAYA_SEO =
        //                {seoJson};

        //        </script>
        //    ";


        //            return Content(
        //                liquid,
        //                "application/liquid"
        //            );
        //        }


        //    [HttpGet]
        //    [Route("~/api/shopify/proxy/{*path}")]
        //    public async Task<IActionResult> ShopifyProxy(
        //string? path)
        //    {
        //        if (string.IsNullOrWhiteSpace(path))
        //        {
        //            return BadRequest("Slug manquant");
        //        }

        //        var cleanSlug =
        //            CreateUrlSlug(path.Trim('/'));

        //        if (string.IsNullOrWhiteSpace(cleanSlug))
        //        {
        //            return BadRequest("Slug invalide");
        //        }

        //        var forwardedHost =
        //                 Request.Headers["X-Forwarded-Host"].FirstOrDefault();

        //        if (string.IsNullOrWhiteSpace(forwardedHost))
        //        {
        //            forwardedHost =
        //                Request.Host.Host;
        //        }

        //        var storefrontOrigin =
        //            "https://" + forwardedHost;

        //        // =========================================================
        //        // Récupération du JSON du guide
        //        // =========================================================

        //        var jsonStr =
        //            await GetGuideJsonByCleanSlug(cleanSlug);

        //        if (string.IsNullOrWhiteSpace(jsonStr))
        //        {
        //            return NotFound(
        //                new
        //                {
        //                    message = "Guide introuvable",
        //                    slug = cleanSlug
        //                }
        //            );
        //        }


        //        // =========================================================
        //        // SEO
        //        // =========================================================

        //        var seo =
        //            BuildGuideSeo(
        //                jsonStr,
        //                cleanSlug,
        //                storefrontOrigin
        //            );


        //        var seoJson =
        //            JsonSerializer.Serialize(
        //                seo,
        //                new JsonSerializerOptions
        //                {
        //                    PropertyNamingPolicy =
        //                        JsonNamingPolicy.CamelCase,

        //                    Encoder =
        //                        JavaScriptEncoder.Default
        //                }
        //            );


        //        // =========================================================
        //        // Liquid Shopify
        //        // =========================================================

        //        var liquid = $@"
        //                {{% section 'naya-ai-search' %}}
        //                {{% section 'naya-guides-results' %}}
        //                {{% section 'viliora-guide-hero' %}}
        //                {{% section 'viliora-top-5' %}}
        //                {{% section 'viliora-comparison' %}}
        //                {{% section 'viliora-reviews' %}}
        //                {{% section 'viliora-evaluation' %}}
        //                {{% section 'viliora-guide-info' %}}
        //                {{% section 'viliora-final-verdict' %}}

        //                <script>
        //                window.NAYA_PROXY_SLUG = {JsonSerializer.Serialize(cleanSlug)};
        //                window.VILIORA_SEO = {seoJson};
        //                </script>
        //                ";

        //        return Content(
        //            liquid,
        //            "application/liquid"
        //        );
        //    }









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
    string cleanSlug,
    string storefrontOrigin)
        {
            using var doc =
                JsonDocument.Parse(jsonStr);

            var root =
                doc.RootElement;

            // =========================================================
            // GUIDE
            // =========================================================

            JsonElement guide;

            if (
                root.TryGetProperty(
                    "guide",
                    out var guideElement
                )
            )
            {
                guide = guideElement;
            }
            else
            {
                guide = root;
            }


            // =========================================================
            // GUIDE TITLE
            // =========================================================

            var guideTitle =
                guide.TryGetProperty(
                    "title",
                    out var titleElement
                )
                ? titleElement.GetString() ?? ""
                : "";


            // =========================================================
            // GUIDE DESCRIPTION
            // =========================================================

            var guideDescription =
                guide.TryGetProperty(
                    "description",
                    out var descriptionElement
                )
                ? descriptionElement.GetString() ?? ""
                : "";


            // =========================================================
            // HERO IMAGE
            // =========================================================

            string? heroImage = null;

            if (
                guide.TryGetProperty(
                    "hero",
                    out var heroElement
                )
                &&
                heroElement.ValueKind ==
                    JsonValueKind.Object
                &&
                heroElement.TryGetProperty(
                    "image",
                    out var heroImageElement
                )
            )
            {
                heroImage =
                    heroImageElement.GetString();
            }


            // =========================================================
            // PRODUCTS
            // =========================================================

            var itemList =
                new List<object>();

            var productCount = 0;

            if (
                guide.TryGetProperty(
                    "products",
                    out var productsElement
                )
                &&
                productsElement.ValueKind ==
                    JsonValueKind.Array
            )
            {
                foreach (
                    var product
                    in productsElement.EnumerateArray()
                )
                {
                    productCount++;

                    var productName =
                        product.TryGetProperty(
                            "name",
                            out var nameElement
                        )
                        ? nameElement.GetString() ?? ""
                        : "";

                    var productBrand =
                        product.TryGetProperty(
                            "brand",
                            out var brandElement
                        )
                        ? brandElement.GetString() ?? ""
                        : "";

                    var productImage =
                        product.TryGetProperty(
                            "image",
                            out var imageElement
                        )
                        ? imageElement.GetString()
                        : null;

                    var productUrl =
                        product.TryGetProperty(
                            "amazon_url",
                            out var amazonUrlElement
                        )
                        && amazonUrlElement.ValueKind ==
                            JsonValueKind.String
                            ? amazonUrlElement.GetString()
                            : null;

                    if (
                        string.IsNullOrWhiteSpace(productUrl)
                        &&
                        product.TryGetProperty(
                            "sephora_url",
                            out var sephoraUrlElement
                        )
                        &&
                        sephoraUrlElement.ValueKind ==
                            JsonValueKind.String
                    )
                    {
                        productUrl =
                            sephoraUrlElement.GetString();
                    }


                    // =====================================================
                    // PRODUCT
                    // =====================================================

                    var productData =
                        new Dictionary<string, object?>();


                    if (!string.IsNullOrWhiteSpace(productName))
                    {
                        productData["name"] =
                            productName;
                    }


                    if (!string.IsNullOrWhiteSpace(productImage))
                    {
                        productData["image"] =
                            productImage;
                    }


                    if (!string.IsNullOrWhiteSpace(productBrand))
                    {
                        productData["brand"] =
                            new Dictionary<string, object?>
                            {
                                ["@type"] = "Brand",
                                ["name"] = productBrand
                            };
                    }


                    // =====================================================
                    // RATING
                    // =====================================================

                    double? ratingValue =
                        null;

                    int? reviewCount =
                        null;


                    if (
                        product.TryGetProperty(
                            "rating",
                            out var ratingElement
                        )
                    )
                    {
                        if (
                            ratingElement.ValueKind ==
                            JsonValueKind.Number
                        )
                        {
                            if (
                                ratingElement.TryGetDouble(
                                    out var rating
                                )
                            )
                            {
                                ratingValue =
                                    rating;
                            }
                        }
                        else if (
                            ratingElement.ValueKind ==
                            JsonValueKind.String
                        )
                        {
                            var ratingText =
                                ratingElement.GetString();

                            if (
                                double.TryParse(
                                    ratingText,
                                    NumberStyles.Any,
                                    CultureInfo.InvariantCulture,
                                    out var rating
                                )
                            )
                            {
                                ratingValue =
                                    rating;
                            }
                        }
                    }


                    if (
                        product.TryGetProperty(
                            "reviews",
                            out var reviewsElement
                        )
                    )
                    {
                        if (
                            reviewsElement.ValueKind ==
                            JsonValueKind.Number
                        )
                        {
                            if (
                                reviewsElement.TryGetInt32(
                                    out var reviews
                                )
                            )
                            {
                                reviewCount =
                                    reviews;
                            }
                        }
                        else if (
                            reviewsElement.ValueKind ==
                            JsonValueKind.String
                        )
                        {
                            var reviewsText =
                                reviewsElement.GetString();

                            if (
                                int.TryParse(
                                    reviewsText,
                                    NumberStyles.Any,
                                    CultureInfo.InvariantCulture,
                                    out var reviews
                                )
                            )
                            {
                                reviewCount =
                                    reviews;
                            }
                        }
                    }


                    // =====================================================
                    // AGGREGATE RATING
                    // =====================================================

                    if (
                        ratingValue.HasValue &&
                        reviewCount.HasValue &&
                        reviewCount.Value > 0
                    )
                    {
                        productData["aggregateRating"] =
                            new Dictionary<string, object?>
                            {
                                ["@type"] =
                                    "AggregateRating",

                                ["ratingValue"] =
                                    ratingValue.Value,

                                ["reviewCount"] =
                                    reviewCount.Value,

                                ["bestRating"] =
                                    5,

                                ["worstRating"] =
                                    1
                            };
                    }


                    // =====================================================
                    // OFFER / PRICE
                    // =====================================================

                    decimal? priceValue =
                        null;

                    var currency =
                        "EUR";


                    if (
                        product.TryGetProperty(
                            "price",
                            out var priceElement
                        ))
                    {
                        if (
                            priceElement.ValueKind ==
                            JsonValueKind.Number
                        )
                        {
                            if (
                                priceElement.TryGetDecimal(
                                    out var price
                                )
                            )
                            {
                                priceValue =
                                    price;
                            }
                        }
                        else if (
                            priceElement.ValueKind ==
                            JsonValueKind.String
                        )
                        {
                            var priceText =
                                priceElement.GetString();

                            if (
                                !string.IsNullOrWhiteSpace(
                                    priceText
                                )
                            )
                            {
                                // Nettoyage d'un prix du type :
                                // "129,90 €"
                                // "129.90"
                                // "129,90"

                                var cleanedPrice =
                                    priceText
                                        .Replace(
                                            "€",
                                            ""
                                        )
                                        .Replace(
                                            "\u00A0",
                                            ""
                                        )
                                        .Trim()
                                        .Replace(
                                            ",",
                                            "."
                                        );

                                if (
                                    decimal.TryParse(
                                        cleanedPrice,
                                        NumberStyles.Any,
                                        CultureInfo.InvariantCulture,
                                        out var price
                                    )
                                )
                                {
                                    priceValue =
                                        price;
                                }
                            }
                        }
                    }


                    if (
                        product.TryGetProperty(
                            "currency",
                            out var currencyElement
                        )
                    )
                    {
                        var currencyText =
                            currencyElement.GetString();

                        if (
                            !string.IsNullOrWhiteSpace(
                                currencyText
                            )
                        )
                        {
                            currency =
                                currencyText;
                        }
                    }


                    // =====================================================
                    // OFFER
                    // =====================================================

                    if (
                        priceValue.HasValue ||
                        !string.IsNullOrWhiteSpace(productUrl)
                    )
                    {
                        var offer =
                            new Dictionary<string, object?>
                            {
                                ["@type"] =
                                    "Offer"
                            };


                        if (
                            priceValue.HasValue
                        )
                        {
                            offer["price"] =
                                priceValue.Value;

                            offer["priceCurrency"] =
                                currency;
                        }


                        if (
                            !string.IsNullOrWhiteSpace(productUrl)
                        )
                        {
                            offer["url"] =
                                productUrl;
                        }


                        productData["offers"] =
                            offer;
                    }


                    // =====================================================
                    // LIST ITEM
                    // =====================================================

                    var listItem =
                        new Dictionary<string, object?>
                        {
                            ["@type"] =
                                "ListItem",

                            ["position"] =
                                productCount,

                            ["item"] =
                                productData
                        };


                    if (
                        !string.IsNullOrWhiteSpace(
                            productName
                        )
                    )
                    {
                        listItem["name"] =
                            productName;
                    }


                    if (
                        !string.IsNullOrWhiteSpace(
                            productUrl
                        )
                    )
                    {
                        listItem["url"] =
                            productUrl;
                    }


                    itemList.Add(
                        listItem
                    );
                }
            }

            // =========================================================
            // FAQ
            // =========================================================

            var faqEntities =
                new List<object>();

            if (
                guide.TryGetProperty(
                    "faq",
                    out var faqElement
                )
                &&
                faqElement.ValueKind ==
                    JsonValueKind.Array
            )
            {
                foreach (
                    var faq
                    in faqElement.EnumerateArray()
                )
                {
                    var question =
                        faq.TryGetProperty(
                            "question",
                            out var questionElement
                        )
                        ? questionElement.GetString()
                        : null;

                    var answer =
                        faq.TryGetProperty(
                            "answer",
                            out var answerElement
                        )
                        ? answerElement.GetString()
                        : null;


                    // -----------------------------------------------------
                    // On ignore les FAQ incomplètes
                    // -----------------------------------------------------

                    if (
                        string.IsNullOrWhiteSpace(
                            question
                        )
                        ||
                        string.IsNullOrWhiteSpace(
                            answer
                        )
                    )
                    {
                        continue;
                    }


                    faqEntities.Add(
                        new Dictionary<string, object?>
                        {
                            ["@type"] =
                                "Question",

                            ["name"] =
                                question,

                            ["acceptedAnswer"] =
                                new Dictionary<string, object?>
                                {
                                    ["@type"] =
                                        "Answer",

                                    ["text"] =
                                        answer
                                }
                        }
                    );
                }
            }



            // =========================================================
            // JSON-LD
            // =========================================================

            var webPageEntity =
    new Dictionary<string, object?>
    {
        ["@type"] =
            "WebPage",

        ["@id"] =
            storefrontOrigin +
            "/apps/naya-guide/" +
            cleanSlug +
            "#webpage",

        ["name"] =
            guideTitle,

        ["description"] =
            guideDescription,

        ["url"] =
            storefrontOrigin +
            "/apps/naya-guide/" +
            cleanSlug,

        ["mainEntity"] =
            new Dictionary<string, object?>
            {
                ["@type"] =
                    "ItemList",

                ["name"] =
                    guideTitle,

                ["numberOfItems"] =
                    itemList.Count,

                ["itemListElement"] =
                    itemList
            }
    };


            // =========================================================
            // GRAPH
            // =========================================================

            var graph =
                new List<object>
                {
        webPageEntity
                };


            // =========================================================
            // FAQ PAGE
            // =========================================================

            if (faqEntities.Count > 0)
            {
                graph.Add(
                    new Dictionary<string, object?>
                    {
                        ["@type"] =
                            "FAQPage",

                        ["@id"] =
                            storefrontOrigin +
                            "/apps/naya-guide/" +
                            cleanSlug +
                            "#faq",

                        ["mainEntity"] =
                            faqEntities
                    }
                );
            }


            // =========================================================
            // JSON-LD
            // =========================================================

            var jsonLd =
                new Dictionary<string, object?>
                {
                    ["@context"] =
                        "https://schema.org",

                    ["@graph"] =
                        graph
                };


            // =========================================================
            // SERIALIZE JSON-LD
            // =========================================================

            var jsonLdString =
                JsonSerializer.Serialize(
                    jsonLd,
                    new JsonSerializerOptions
                    {
                        Encoder =
                            JavaScriptEncoder.Default
                    }
                );


            // =========================================================
            // SEO TITLE
            // =========================================================

            var metaTitle =
                string.IsNullOrWhiteSpace(
                    guideTitle
                )
                ? "Guide beauté | Naya"
                : $"{guideTitle} | Naya";


            // =========================================================
            // RETURN
            // =========================================================

            return new GuideSeoPayload
            {
                Title =
                    metaTitle,

                Description =
                    guideDescription,

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

        [HttpGet("test")]
        public IActionResult Test()
        {
            return Ok(new
            {
                success = true,
                message = "Naya Guide Proxy OK",
                date = DateTime.UtcNow
            });
        }


    }
}
