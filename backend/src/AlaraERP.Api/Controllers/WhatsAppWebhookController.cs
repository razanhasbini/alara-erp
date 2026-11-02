using AlaraERP.Api.Security;
using AlaraERP.Application.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AlaraERP.Api.Controllers;

/// <summary>Called by Meta, not by the app, so nobody is signed in: each request proves itself with the verify token or Meta's signature.</summary>
[Route("api/whatsapp/webhook")]
[AllowAnonymous]
[EnableRateLimiting(RateLimitPolicies.Webhook)]
public sealed class WhatsAppWebhookController(WhatsAppWebhook webhook) : ApiControllerBase
{
    [HttpGet]
    public IActionResult Verify(
        [FromQuery(Name = "hub.mode")] string? mode,
        [FromQuery(Name = "hub.verify_token")] string? verifyToken,
        [FromQuery(Name = "hub.challenge")] string? challenge) =>
        webhook.AcceptsSubscription(mode, verifyToken) ? Content(challenge ?? "", "text/plain") : Forbid403();

    [HttpPost]
    [SkipCsrf]
    [RequestSizeLimit(1_000_000)]
    public async Task<IActionResult> Receive(CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        await Request.Body.CopyToAsync(buffer, ct);
        var body = buffer.ToArray(); // the signature covers these exact bytes
        if (!webhook.IsSignedByMeta(body, Request.Headers["X-Hub-Signature-256"].FirstOrDefault())) return Forbid403();

        await webhook.ApplyAsync(body, ct);
        return Ok();
    }

    private StatusCodeResult Forbid403() => StatusCode(StatusCodes.Status403Forbidden);
}
