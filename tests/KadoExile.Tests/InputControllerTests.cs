using KadoExile.Services;
using Xunit;

namespace KadoExile.Tests;

public sealed class InputControllerTests
{
    [Fact]
    public void Version030_UsesThePhysicalInputController()
    {
		var assembly = typeof(InputController).Assembly;
		Assert.NotNull(typeof(InputController).GetConstructor(Type.EmptyTypes));
		Assert.Null(assembly.GetType("KadoExile.Services.IBackgroundInputBackend"));
		Assert.Null(assembly.GetType("KadoExile.Services.InputProbeSnapshot"));
    }
}
