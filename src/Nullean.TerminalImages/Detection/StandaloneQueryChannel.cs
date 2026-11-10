using Nullean.TerminalImages.Interop;

namespace Nullean.TerminalImages.Detection;

/// <summary>
/// Asks the terminal directly through <c>/dev/tty</c>, for applications that do not own terminal input. Supported on macOS and Linux.
/// </summary>
/// <remarks>
/// Windows is not supported here: console input there cannot be read with a timeout without risking a hang when only
/// non-key events arrive, and ConPTY does not reliably deliver the replies. On Windows detection uses the environment (Windows
/// Terminal sets <c>WT_SESSION</c>), or the host application passes an <see cref="ITerminalQueryChannel"/> that reads its own input.
/// </remarks>
public sealed class StandaloneQueryChannel : ITerminalQueryChannel, IDisposable
{
	private readonly Microsoft.Win32.SafeHandles.SafeFileHandle _tty;

	private StandaloneQueryChannel(Microsoft.Win32.SafeHandles.SafeFileHandle tty) => _tty = tty;

	/// <summary>Opens the controlling terminal. Returns null when there is none, or the platform is unsupported.</summary>
	public static StandaloneQueryChannel? TryOpen()
	{
		if (!UnixTerminal.IsSupported)
			return null;

		try
		{
			var handle = File.OpenHandle("/dev/tty", FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
			if (!UnixTerminal.IsTerminal(checked((int)handle.DangerousGetHandle())))
			{
				handle.Dispose();
				return null;
			}

			return new StandaloneQueryChannel(handle);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
		{
			return null; // no controlling terminal: a service, a CI job, a container without a tty
		}
	}

	/// <summary>Closes the handle to the terminal.</summary>
	public void Dispose() => _tty.Dispose();

	/// <inheritdoc />
	public ValueTask<string> QueryAsync(string request, Func<string, bool> isComplete, TimeSpan timeout, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		var fd = checked((int)_tty.DangerousGetHandle());
		return new ValueTask<string>(Task.Run(() => UnixTerminal.Query(fd, request, isComplete, timeout), cancellationToken));
	}
}
