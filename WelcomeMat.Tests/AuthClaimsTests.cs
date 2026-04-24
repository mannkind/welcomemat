using System;
using System.Security.Claims;
using NUnit.Framework;
using WelcomeMat.Data.Entities;
using WelcomeMat.Services;

namespace WelcomeMat.Tests;

public class AuthClaimsTests
{
    private static User MakeUser(bool isAdmin = false) => new()
    {
        Id = Guid.NewGuid(),
        Email = "test@example.com",
        Name = "Test User",
        IsAdmin = isAdmin,
    };

    [Test]
    public void NormalUser_HasEmailAndNameClaims()
    {
        var user = MakeUser();
        var principal = AuthClaims.BuildPrincipal(user);

        Assert.That(principal.FindFirstValue(ClaimTypes.Email), Is.EqualTo(user.Email));
        Assert.That(principal.FindFirstValue(ClaimTypes.Name), Is.EqualTo(user.Name));
        Assert.That(principal.FindFirstValue(ClaimTypes.NameIdentifier), Is.EqualTo(user.Id.ToString()));
        Assert.That(principal.FindFirstValue("is_admin"), Is.Null.Or.Not.EqualTo("true"));
    }

    [Test]
    public void AdminUser_HasAdminClaim()
    {
        var user = MakeUser(isAdmin: true);
        var principal = AuthClaims.BuildPrincipal(user);

        Assert.That(principal.FindFirstValue("is_admin"), Is.EqualTo("true"));
    }

    [Test]
    public void WithImpersonator_HasImpersonatorClaims()
    {
        var user = MakeUser();
        var impersonatorId = Guid.NewGuid();
        var principal = AuthClaims.BuildPrincipal(user, impersonatorId, "Admin Name");

        Assert.That(principal.FindFirstValue(AuthClaims.ImpersonatorIdClaim), Is.EqualTo(impersonatorId.ToString()));
        Assert.That(principal.FindFirstValue(AuthClaims.ImpersonatorNameClaim), Is.EqualTo("Admin Name"));
    }

    [Test]
    public void WithoutImpersonator_NoImpersonatorClaims()
    {
        var user = MakeUser();
        var principal = AuthClaims.BuildPrincipal(user);

        Assert.That(principal.FindFirstValue(AuthClaims.ImpersonatorIdClaim), Is.Null);
        Assert.That(principal.FindFirstValue(AuthClaims.ImpersonatorNameClaim), Is.Null);
    }
}
