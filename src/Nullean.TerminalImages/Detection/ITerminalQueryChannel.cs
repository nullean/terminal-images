namespace Nullean.TerminalImages.Detection;

/// <summary>
/// Sends a query to the terminal and collects its answer. The standalone implementation (<see cref="StandaloneQueryChannel"/>)
/// reads the terminal itself; an application that already owns terminal input (a TUI framework) provides its own, because two
/// readers on one input stream steal bytes from each other.
/// </summary>
public interface ITerminalQueryChannel
{
	/// <summary>Writes <paramref name="request"/> to the terminal and returns everything it sent back, up to the point where <paramref name="isComplete"/> says the answer is whole or the time runs out.</summary>
	/// <param name="request">The escape sequences to send.</param>
	/// <param name="isComplete">Called with the text received so far. Return true to stop waiting.</param>
	/// <param name="timeout">The longest to wait for the answer.</param>
	/// <param name="cancellationToken">Cancels the wait.</param>
	/// <returns>The reply text, possibly empty when the terminal does not answer.</returns>
	ValueTask<string> QueryAsync(string request, Func<string, bool> isComplete, TimeSpan timeout, CancellationToken cancellationToken);
}
