using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OnlineBanking.Web.Entities;
using OnlineBanking.Web.Infrastructure;
using OnlineBanking.Web.Services.Assistant;

namespace OnlineBanking.Web.Controllers;

/// <summary>JSON endpoints for the chat panel. Customer-only; the antiforgery token travels in the RequestVerificationToken header.</summary>
[Authorize(Roles = Roles.Customer)]
public class AssistantController(BankingAssistant assistant) : Controller
{
    public sealed class SendRequest
    {
        [Required, StringLength(2000)]
        public string Message { get; set; } = "";
    }

    [HttpPost]
    public async Task<IActionResult> Send([FromBody] SendRequest request, CancellationToken ct)
    {
        var result = await assistant.SendAsync(User.GetUserId(), User.DisplayName(), request?.Message, ct);
        var body = new
        {
            reply = result.Reply,
            transfer = result.Transfer is { } d ? new
            {
                from = d.FromAccount,
                to = Format.AccountNumber(d.ToAccountNumber),
                payee = d.PayeeName,
                amount = d.AmountText,
                remarks = d.Remarks,
                url = d.Url,
            } : null,
            remaining = result.RemainingToday,
        };

        return result.Status switch
        {
            AssistantStatus.Ok => Json(body),
            AssistantStatus.Invalid => BadRequest(body),
            AssistantStatus.RateLimited => StatusCode(StatusCodes.Status429TooManyRequests, body),
            _ => StatusCode(StatusCodes.Status503ServiceUnavailable, body),
        };
    }

    [HttpGet]
    public IActionResult History() =>
        Json(assistant.History(User.GetUserId()).Select(m => new { role = m.Role, text = m.Text }));

    [HttpPost]
    public IActionResult Reset()
    {
        assistant.Reset(User.GetUserId());
        return NoContent();
    }
}
