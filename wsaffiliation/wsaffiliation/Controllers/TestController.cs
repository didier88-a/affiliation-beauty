using Microsoft.AspNetCore.Mvc;

namespace wsaffiliation.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class TestController : ControllerBase
    {
        [HttpGet]
        public IActionResult Get()
        {
            var ApiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
            return Ok("API is working very good!" + ApiKey);
        }
    }
}
