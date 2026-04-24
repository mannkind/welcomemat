using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;
using WelcomeMat.Services;

namespace WelcomeMat.Tests;

public class EmailFingerprintTests
{
    [TestCase("super-secret-guest@example.com", "sup*************st@example.com")]
    [TestCase("dustin@example.com", "du***n@example.com")]
    [TestCase("dust@example.com", "****@example.com")]
    [TestCase("ab@example.com", "**@example.com")]
    [TestCase("a@example.com", "*@example.com")]
    public void Mask_KeepsTheDomainAndHidesTheLocalPart(string email, string expected) =>
        Assert.That(EmailFingerprint.Mask(email), Is.EqualTo(expected));

    [TestCase("dustin.brewer@example.com", "dustinbrewer@example.com")]
    [TestCase("dustin.brewer@example.com", "dustin.b.brewer@example.com")]
    [TestCase("j.smith@example.com", "jo.smith@example.com")]
    public void Mask_TellsSimilarHouseholdAddressesApart(string first, string second) =>
        Assert.That(EmailFingerprint.Mask(first), Is.Not.EqualTo(EmailFingerprint.Mask(second)));

    [TestCase("")]
    [TestCase("   ")]
    [TestCase("not-an-address")]
    [TestCase("@example.com")]
    [TestCase("trailing@")]
    public void Mask_RevealsNothingWhenTheInputIsntAnAddress(string email) =>
        Assert.That(EmailFingerprint.Mask(email), Is.AnyOf("***", "(empty)"));

    [Test]
    public void Mask_RevealsAtMostFiveCharactersAndAlwaysHidesTwo()
    {
        foreach (var length in Enumerable.Range(1, 40))
        {
            var local = new string('x', length);
            var masked = EmailFingerprint.Mask($"{local}@example.com");
            var maskedLocal = masked[..masked.LastIndexOf('@')];
            var hidden = maskedLocal.Count(c => c == '*');

            Assert.That(maskedLocal, Has.Length.EqualTo(length), "mask should preserve length");
            Assert.That(length - hidden, Is.LessThanOrEqualTo(5), $"length {length} revealed too much");
            Assert.That(hidden, Is.GreaterThanOrEqualTo(Math.Min(2, length)), $"length {length} hid too little");
        }
    }

    [Test]
    public void Mask_NeverContainsTheWholeLocalPart()
    {
        foreach (var local in new[] { "a", "ab", "abc", "abcd", "abcde", "abcdef" })
        {
            var masked = EmailFingerprint.Mask($"{local}@example.com");
            var maskedLocal = masked[..masked.LastIndexOf('@')];
            Assert.That(maskedLocal, Does.Not.Contain(local), $"local part '{local}' survived masking");
        }
    }

    [Test]
    public void Sha256Hex_MatchesAPlainShellSha256()
    {
        const string email = "dustin@example.com";
        var expected = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(email)));

        Assert.That(EmailFingerprint.Sha256Hex(email), Is.EqualTo(expected));
        Assert.That(EmailFingerprint.Sha256Hex(email), Has.Length.EqualTo(64));
        Assert.That(EmailFingerprint.Sha256Hex(email), Does.Not.Contain(email));
    }

    [Test]
    public void Sha256Hex_AgreesAcrossNormalizedSpellings() =>
        Assert.That(
            EmailFingerprint.Sha256Hex(EmailNormalizer.Normalize("  Dustin@Example.COM ")),
            Is.EqualTo(EmailFingerprint.Sha256Hex("dustin@example.com")));
}
