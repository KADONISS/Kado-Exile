using KadoExile.Services;
using Xunit;

namespace KadoExile.Tests;

public sealed class ReleaseVersionTests
{
    [Fact]
    public void AssemblyVersion_Is060()
    {
        var version = typeof(InputController).Assembly.GetName().Version;
        Assert.Equal(new Version(0, 6, 0, 0), version);
    }
}
