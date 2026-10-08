using System;
using NUnit.Framework;
using WelcomeMat.Services;

namespace WelcomeMat.Tests;

public class GuestbookServiceTests
{
    private static readonly DateOnly CheckOut = new(2024, 6, 10);

    [Test]
    public void TwoDaysBeforeCheckout_ReturnsFalse() =>
        Assert.That(GuestbookService.IsInWriteWindow(CheckOut, new DateOnly(2024, 6, 8)), Is.False);

    [Test]
    public void DayBeforeCheckout_ReturnsTrue() =>
        Assert.That(GuestbookService.IsInWriteWindow(CheckOut, new DateOnly(2024, 6, 9)), Is.True);

    [Test]
    public void CheckoutDay_ReturnsTrue() =>
        Assert.That(GuestbookService.IsInWriteWindow(CheckOut, new DateOnly(2024, 6, 10)), Is.True);

    [Test]
    public void OneDayAfterCheckout_ReturnsTrue() =>
        Assert.That(GuestbookService.IsInWriteWindow(CheckOut, new DateOnly(2024, 6, 11)), Is.True);

    [Test]
    public void Day14AfterCheckout_ReturnsTrue() =>
        Assert.That(GuestbookService.IsInWriteWindow(CheckOut, new DateOnly(2024, 6, 24)), Is.True);

    [Test]
    public void Day15AfterCheckout_ReturnsFalse() =>
        Assert.That(GuestbookService.IsInWriteWindow(CheckOut, new DateOnly(2024, 6, 25)), Is.False);

    [Test]
    public void WellAfterCheckout_ReturnsFalse() =>
        Assert.That(GuestbookService.IsInWriteWindow(CheckOut, new DateOnly(2024, 12, 1)), Is.False);
}
