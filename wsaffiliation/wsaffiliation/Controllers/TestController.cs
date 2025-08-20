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
            return Ok("API is working very good!");
        }
    }
}
