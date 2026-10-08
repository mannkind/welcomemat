using NUnit.Framework;
using WelcomeMat.Services;

namespace WelcomeMat.Tests;

public class TextLinesTests
{
    [Test]
    public void Null_ReturnsEmpty() =>
        Assert.That(TextLines.Split(null), Is.Empty);

    [Test]
    public void EmptyString_ReturnsEmpty() =>
        Assert.That(TextLines.Split(""), Is.Empty);

    [Test]
    public void AllWhitespace_ReturnsEmpty() =>
        Assert.That(TextLines.Split("   \n\t\n  "), Is.Empty);

    [Test]
    public void WindowsLineEndings_NoBlankEntriesOrTrailingCarriageReturn() =>
        Assert.That(TextLines.Split("first\r\nsecond\r\n"), Is.EqualTo(new[] { "first", "second" }));

    [Test]
    public void WhitespaceOnlyLineBetweenRealLines_IsDropped() =>
        Assert.That(TextLines.Split("first\n   \nsecond"), Is.EqualTo(new[] { "first", "second" }));

    [Test]
    public void RealLinesAreTrimmedAndOrderPreserved() =>
        Assert.That(TextLines.Split("  a  \n b \nc"), Is.EqualTo(new[] { "a", "b", "c" }));
}
