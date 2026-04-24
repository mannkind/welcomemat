using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace WelcomeMat.E2E;

[TestFixture]
public class FreshStartupTests
{
    [Test]
    public void Fixture_UsesAnIsolatedStateTree() =>
        Assert.That(WebAppFixture.StatePath, Does.StartWith(Path.GetTempPath()));

    [Test]
    public async Task Restart_PreservesTheSameDataProtectionKeyFiles()
    {
        var keyDirectory = Path.Combine(
            WebAppFixture.StatePath,
            "data-protection-keys");
        var before = Directory.GetFiles(keyDirectory, "*.xml")
            .ToDictionary(path => Path.GetFileName(path)!, File.ReadAllText);

        await WebAppFixture.RestartAsync();

        var after = Directory.GetFiles(keyDirectory, "*.xml")
            .ToDictionary(path => Path.GetFileName(path)!, File.ReadAllText);
        Assert.Multiple(() =>
        {
            Assert.That(before, Is.Not.Empty);
            Assert.That(after, Is.EqualTo(before));
        });
    }
}
