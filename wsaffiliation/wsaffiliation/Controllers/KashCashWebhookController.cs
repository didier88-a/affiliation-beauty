using Microsoft.AspNetCore.Mvc;
using System.Text.Json.Serialization;

namespace wsaffiliation.Controllers;

public class KashCashWebhook
{
    [JsonPropertyName("transactionId")]
    public string TransactionId { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public int Status { get; set; }

    [JsonPropertyName("externalId")]
    public string ExternalId { get; set; } = string.Empty;

    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }

    [JsonPropertyName("genericParams")]
    public string? GenericParams { get; set; }

    [JsonPropertyName("type")]
    public int Type { get; set; }
}

[ApiController]
[Route("api/kashcash")]
public class KashCashWebhookController : ControllerBase
{
    // Pour tester dans le navigateur
    [HttpGet("callback")]
    public IActionResult Test()
    {
        return Ok("KashCash webhook is working");
    }

    // SafeCash appelle cette URL en POST
    [HttpPost("callback")]
    public IActionResult Callback(
        [FromBody] KashCashWebhook webhook)
    {
        Console.WriteLine("=== KASHCASH WEBHOOK RECEIVED ===");

        Console.WriteLine($"TransactionId: {webhook.TransactionId}");
        Console.WriteLine($"Status: {webhook.Status}");
        Console.WriteLine($"ExternalId: {webhook.ExternalId}");
        Console.WriteLine($"Amount: {webhook.Amount}");

        return Ok();
    }
}