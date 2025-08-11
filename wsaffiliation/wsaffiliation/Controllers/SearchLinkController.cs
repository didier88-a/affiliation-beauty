using Microsoft.AspNetCore.Mvc;

namespace wsaffiliation.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SearchLinkController : ControllerBase
    {
        // GET api/searchlink?affiliation=xxx&texteRecherche=yyy
        [HttpGet]
        public IActionResult Get([FromQuery] string affiliation, [FromQuery] string texteRecherche)
        {
            if (string.IsNullOrEmpty(affiliation) || string.IsNullOrEmpty(texteRecherche))
            {
                return BadRequest("Les paramètres 'affiliation' et 'texteRecherche' sont requis.");
            }

            return Ok(new
            {
                Affiliation = affiliation,
                TexteRecherche = texteRecherche,
                Message = "Paramètres reçus avec succès."
            });
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
    }

    public class SearchLinkRequest
    {
        public string? Affiliation { get; set; }
        public string? TexteRecherche { get; set; }
    }
}
