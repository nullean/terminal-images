using Nullean.TerminalImages.Detection;

namespace Nullean.TerminalImages.Tests.Support;

/// <summary>A terminal that answers with scripted text, and remembers what it was asked.</summary>
internal sealed class FakeChannel(params string[] replies) : ITerminalQueryChannel
{
	private readonly Queue<string> _replies = new(replies);

	public List<string> Requests { get; } = [];

	public Exception? Throw { get; init; }

	public bool CompletenessCheckAcceptedTheReply { get; private set; }

	public ValueTask<string> QueryAsync(string request, Func<string, bool> isComplete, TimeSpan timeout, CancellationToken cancellationToken)
	{
		Requests.Add(request);
		if (Throw is not null)
			throw Throw;
		var reply = _replies.Count > 0 ? _replies.Dequeue() : string.Empty;
		CompletenessCheckAcceptedTheReply = isComplete(reply);
		return ValueTask.FromResult(reply);
	}
}
