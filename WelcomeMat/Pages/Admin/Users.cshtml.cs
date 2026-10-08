using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using WelcomeMat.Data;
using WelcomeMat.Data.Entities;
using WelcomeMat.Services;

namespace WelcomeMat.Pages.Admin;

[Authorize(Policy = "AdminOnly")]
/// <summary>
/// An class representing the page where the allowlist and users are managed.
/// </summary>
public class UsersModel : PageModel
{
    /// <summary>
    /// Initializes a new instance of the UsersModel class.
    /// </summary>
    /// <param name="db"></param>
    public UsersModel(AppDbContext db)
    {
        this.Db = db;
    }

    public List<AllowlistEntry> Allowlist { get; private set; } = [];
    public List<User> Users { get; private set; } = [];
    public Guid CurrentUserId { get; private set; }

    /// <summary>
    /// Show the allowlist and the users on it.
    /// </summary>
    public void OnGet()
    {
        this.CurrentUserId = Guid.Parse(this.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? Guid.Empty.ToString());
        this.Allowlist = this.Db.Allowlist.OrderBy(a => a.Email).ToList();
        this.Users = this.Db.Users.OrderBy(u => u.Email).ToList();
        this.ViewData["Title"] = "Users";
        this.ViewData["SiteTitle"] = this.Db.Settings.FirstOrDefault(s => s.Key == "site_title")?.Value ?? "WelcomeMat";
    }

    /// <summary>
    /// Add an email to the allowlist.
    /// </summary>
    /// <param name="email"></param>
    /// <returns></returns>
    public IActionResult OnPostAddUser([FromForm] string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return AjaxResults.ErrorOrFallback(this.HttpContext, "Email is required.", this.RedirectToPage());
        }

        var adminId = Guid.Parse(this.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? Guid.Empty.ToString());
        var normalizedEmail = EmailNormalizer.Normalize(email);
        if (this.Db.Allowlist.Any(a => a.Email == normalizedEmail))
        {
            return AjaxResults.ErrorOrFallback(this.HttpContext, "That email is already on the allowlist.", this.RedirectToPage());
        }

        this.Db.Allowlist.Add(new AllowlistEntry
        {
            Email = normalizedEmail,
            AddedBy = adminId,
            CreatedAt = DateTime.UtcNow,
        });
        if (!this.Db.Users.Any(u => u.Email == normalizedEmail))
        {
            this.Db.Users.Add(new User
            {
                Email = normalizedEmail,
                Name = "",
                Picture = "",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            });
        }
        this.Db.SaveChanges();
        return AjaxResults.OkOrFallback(this.HttpContext, this.RedirectToPage());
    }

    /// <summary>
    /// Take a user off the allowlist and end their sessions.
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    public IActionResult OnPostRemoveUser([FromQuery] Guid id)
    {
        var entry = this.Db.Allowlist.Find(id);
        if (entry is null)
        {
            return this.RedirectToPage();
        }

        var myId = Guid.Parse(this.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? Guid.Empty.ToString());
        var user = this.Db.Users
            .Include(u => u.Reservations)
            .Include(u => u.GuestDoorCodes)
            .Include(u => u.AllowlistEntries)
            .FirstOrDefault(u => u.Email == entry.Email);
        if (user is not null)
        {
            if (user.Id == myId)
            {
                return AjaxResults.ErrorOrFallback(this.HttpContext, "You can't remove your own account.", this.Forbid());
            }

            using var transaction = this.Db.Database.BeginTransaction();
            this.Db.BlackoutDates.RemoveRange(this.Db.BlackoutDates.Where(b => b.CreatedBy == user.Id));
            this.Db.Sessions.RemoveRange(this.Db.Sessions.Where(s => s.UserId == user.Id));
            this.Db.Allowlist.Remove(entry);
            this.Db.Users.Remove(user);
            this.Db.SaveChanges();
            transaction.Commit();
        }
        else
        {
            this.Db.Allowlist.Remove(entry);
            this.Db.SaveChanges();
        }
        return AjaxResults.OkOrFallback(this.HttpContext, this.RedirectToPage());
    }

    /// <summary>
    /// Grant or revoke admin.
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    public IActionResult OnPostSetAdmin([FromQuery] Guid id)
    {
        var myId = Guid.Parse(this.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? Guid.Empty.ToString());
        if (id == myId)
        {
            return AjaxResults.ErrorOrFallback(this.HttpContext, "You can't change your own admin status.", this.Forbid());
        }

        var user = this.Db.Users.Find(id);
        if (user is not null)
        {
            using var transaction = this.Db.Database.BeginTransaction();
            user.IsAdmin = !user.IsAdmin;
            user.SecurityVersion++;
            this.Db.Sessions.RemoveRange(this.Db.Sessions.Where(s => s.UserId == user.Id));
            this.Db.SaveChanges();
            transaction.Commit();
        }
        return AjaxResults.OkOrFallback(this.HttpContext, this.RedirectToPage());
    }

    /// <summary>
    /// Sign in as another user, keeping the admin's identity on the ticket.
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    public async Task<IActionResult> OnPostImpersonateAsync([FromQuery] Guid id)
    {
        var adminId = Guid.Parse(this.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? Guid.Empty.ToString());
        var adminName = this.User.FindFirstValue(ClaimTypes.Name) ?? "";

        var target = this.Db.Users.Find(id);
        if (target is null || target.Id == adminId || target.IsAdmin)
        {
            return AjaxResults.ErrorOrFallback(this.HttpContext, "That user can't be impersonated.", this.Forbid());
        }

        await this.HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            AuthClaims.BuildPrincipal(target, adminId, adminName));

        return AjaxResults.RedirectOrFetch(this.HttpContext, "/", this.Redirect("/"));
    }

    /// <summary>
    /// The database used internally.
    /// </summary>
    private readonly AppDbContext Db;
}
