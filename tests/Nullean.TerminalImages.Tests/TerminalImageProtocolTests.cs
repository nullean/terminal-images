using AwesomeAssertions;

namespace Nullean.TerminalImages.Tests;

public class TerminalImageProtocolTests
{
	[Test]
	public void None_is_the_default_value() => default(TerminalImageProtocol).Should().Be(TerminalImageProtocol.None);
}
