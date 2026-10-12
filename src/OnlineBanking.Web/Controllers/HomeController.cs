using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using OnlineBanking.Web.Data;
using OnlineBanking.Web.Entities;
using OnlineBanking.Web.Infrastructure;
using OnlineBanking.Web.Models;

namespace OnlineBanking.Web.Controllers;

public class HomeController(AppDbContext db, ILogger<HomeController> logger) : Controller
{
    public IActionResult Index()
    {
        if (User.Identity?.IsAuthenticated == true)
            return User.IsAdmin() ? RedirectToAction("Index", "Dashboard", new { area = "Admin" }) : RedirectToAction("Index", "Dashboard");
        return View();
    }

    public IActionResult About() => View();

    [HttpGet]
    public IActionResult Contact() => View(new ContactViewModel
    {
        Name = User.Identity?.IsAuthenticated == true ? User.DisplayName() : "",
    });

    [HttpPost]
    public async Task<IActionResult> Contact(ContactViewModel model, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return View(model);

        // Stored for the admin inbox (the 2022 version emailed it through Gmail SMTP with a hard-coded password)
        db.ContactMessages.Add(new ContactMessage
        {
            Name = model.Name.Trim(),
            Email = model.Email.Trim(),
            Subject = model.Subject.Trim(),
            Message = model.Message.Trim(),
        });
        await db.SaveChangesAsync(ct);

        TempData.Flash("Thanks! Your message has reached the bank. We'll reply by email within one working day.");
        return RedirectToAction(nameof(Contact));
    }

    [Route("Home/Status/{code:int}")]
    public IActionResult Status(int code)
    {
        Response.StatusCode = code;
        return View(code);
    }

    public IActionResult Error()
    {
        var feature = HttpContext.Features.Get<IExceptionHandlerPathFeature>();
        if (feature?.Error is not null)
            logger.LogError(feature.Error, "Unhandled error on {Path}", feature.Path);
        ViewBag.RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;
        return View();
    }
}
