using KadoExile.Services;
using Xunit;

namespace KadoExile.Tests;

public sealed class RollbackVersionTests
{
    [Fact]
    public void AssemblyVersion_Is030()
    {
		var version = typeof(InputController).Assembly.GetName().Version;
		Assert.NotNull(version);
		Assert.Equal(0, version.Major);
		Assert.Equal(3, version.Minor);
    }
}
