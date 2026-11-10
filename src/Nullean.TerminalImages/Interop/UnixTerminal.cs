using System.Runtime.InteropServices;

namespace Nullean.TerminalImages.Interop;

/// <summary>
/// The libc calls needed to ask a terminal a question: raw mode, a timed read and a write. Source-generated
/// (<c>LibraryImport</c>), so it works under NativeAOT.
/// </summary>
/// <remarks>
/// <c>struct termios</c> differs between macOS and Linux, so it is treated as a block of bytes with the few offsets that are
/// needed (the local-mode flags and VMIN/VTIME). Only macOS and Linux are supported.
/// The read timeout comes from the terminal itself (VMIN=0, VTIME=1: a read returns after a tenth of a second with nothing),
/// not from <c>poll</c>: on macOS <c>poll</c> reports <c>POLLNVAL</c> at once for <c>/dev/tty</c>, which would make a read block forever.
/// </remarks>
internal static partial class UnixTerminal
{
	private const int TermiosBufferSize = 256; // larger than either platform's struct

	private const int TcsaNow = 0;

	[LibraryImport("libc", EntryPoint = "isatty")]
	private static partial int IsATty(int fd);

	[LibraryImport("libc", EntryPoint = "tcgetattr")]
	private static unsafe partial int TcGetAttr(int fd, byte* termios);

	[LibraryImport("libc", EntryPoint = "tcsetattr")]
	private static unsafe partial int TcSetAttr(int fd, int actions, byte* termios);

	[LibraryImport("libc", EntryPoint = "read")]
	private static unsafe partial nint Read(int fd, byte* buffer, nuint count);

	[LibraryImport("libc", EntryPoint = "write")]
	private static unsafe partial nint Write(int fd, byte* buffer, nuint count);

	/// <summary>Whether this platform's termios layout is known.</summary>
	public static bool IsSupported => OperatingSystem.IsMacOS() || OperatingSystem.IsLinux();

	/// <summary>Whether the file handle is a terminal.</summary>
	public static bool IsTerminal(int fd) => IsATty(fd) == 1;

	/// <summary>
	/// Writes <paramref name="request"/> to the terminal and reads until <paramref name="isComplete"/> is satisfied or the
	/// time is up. The terminal is put in non-canonical, no-echo mode for the duration and always restored.
	/// </summary>
	public static unsafe string Query(int fd, string request, Func<string, bool> isComplete, TimeSpan timeout)
	{
		var saved = stackalloc byte[TermiosBufferSize];
		var raw = stackalloc byte[TermiosBufferSize];
		if (TcGetAttr(fd, saved) != 0)
			throw new IOException("tcgetattr failed: the handle is not a terminal.");

		new ReadOnlySpan<byte>(saved, TermiosBufferSize).CopyTo(new Span<byte>(raw, TermiosBufferSize));
		MakeNonCanonical(raw);
		if (TcSetAttr(fd, TcsaNow, raw) != 0)
			throw new IOException("tcsetattr failed.");

		try
		{
			var bytes = System.Text.Encoding.UTF8.GetBytes(request);
			fixed (byte* pointer = bytes)
			{
				var written = 0;
				while (written < bytes.Length)
				{
					var n = Write(fd, pointer + written, (nuint)(bytes.Length - written));
					if (n <= 0)
						throw new IOException("Writing to the terminal failed.");
					written += (int)n;
				}
			}

			var received = new System.Text.StringBuilder();
			var buffer = stackalloc byte[128];
			var clock = System.Diagnostics.Stopwatch.StartNew();
			while (true)
			{
				if (clock.Elapsed >= timeout)
					break;

				// Returns after at most a tenth of a second (VTIME): zero bytes means nothing arrived in that time.
				var count = Read(fd, buffer, 128);
				if (count < 0)
					break;
				if (count == 0)
					continue;
				received.Append(System.Text.Encoding.Latin1.GetString(buffer, (int)count));
				if (isComplete(received.ToString()))
					break;
			}

			return received.ToString();
		}
		finally
		{
			_ = TcSetAttr(fd, TcsaNow, saved); // nothing more can be done if restoring fails, and a throw here would hide the real error
		}
	}

	/// <summary>Clears ICANON and ECHO and sets VMIN=0, VTIME=1, at the offsets of this platform's struct termios.</summary>
	private static unsafe void MakeNonCanonical(byte* termios)
	{
		if (OperatingSystem.IsMacOS())
		{
			// macOS: four 8-byte flag words (iflag, oflag, cflag, lflag), then c_cc[20].
			var lflag = (ulong*)(termios + 24);
			*lflag &= ~(0x00000100UL | 0x00000008UL); // ICANON | ECHO
			termios[32 + 16] = 0; // c_cc[VMIN]: a read may return with no data...
			termios[32 + 17] = 1; // c_cc[VTIME]: ...after a tenth of a second
		}
		else
		{
			// Linux (glibc): four 4-byte flag words, c_line (1 byte), then c_cc[32].
			var lflag = (uint*)(termios + 12);
			*lflag &= ~(0x00000002U | 0x00000008U); // ICANON | ECHO
			termios[17 + 6] = 0; // c_cc[VMIN]
			termios[17 + 5] = 1; // c_cc[VTIME]
		}
	}
}
