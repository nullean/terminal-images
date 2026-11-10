using System.Drawing;
using Nullean.TerminalImages.Detection;
using Terminal.Gui.Drawing;
using Terminal.Gui.Drivers;

namespace Nullean.TerminalImages.TerminalGui;

/// <summary>
/// Connects this package's detection to Terminal.Gui's drivers. Terminal.Gui finds out what the terminal supports during its
/// own start-up handshake; that does not see through tmux or SSH, and checks Kitty by environment variables only. Detection
/// here can probe the terminal, so its answer can replace Terminal.Gui's.
/// </summary>
public static class TerminalGuiImages
{
	/// <summary>How Terminal.Gui should treat Sixel, given what was detected here.</summary>
	public static SixelSupportResult ToSixelSupport(TerminalImageCapabilities capabilities)
	{
		ArgumentNullException.ThrowIfNull(capabilities);
		return new SixelSupportResult
		{
			IsSupported = capabilities.Supports(TerminalImageProtocol.Sixel),
			Resolution = ToSize(capabilities.CellSize),
			MaxPaletteColors = Math.Clamp(capabilities.SixelColorRegisters ?? 256, 2, 256),
			SupportsTransparency = capabilities.Supports(TerminalImageProtocol.Sixel), // our encoder leaves transparent pixels undrawn
		};
	}

	/// <summary>How Terminal.Gui should treat the Kitty graphics protocol, given what was detected here.</summary>
	public static KittyGraphicsSupportResult ToKittySupport(TerminalImageCapabilities capabilities)
	{
		ArgumentNullException.ThrowIfNull(capabilities);
		return new KittyGraphicsSupportResult
		{
			IsSupported = capabilities.Supports(TerminalImageProtocol.Kitty),
			Resolution = ToSize(capabilities.CellSize),
		};
	}

	/// <summary>Tells the driver what was detected, replacing what Terminal.Gui worked out itself.</summary>
	public static void ApplyDetection(this IDriver driver, TerminalImageCapabilities capabilities)
	{
		ArgumentNullException.ThrowIfNull(driver);
		driver.SetSixelSupport(ToSixelSupport(capabilities));
		driver.SetKittyGraphicsSupport(ToKittySupport(capabilities));
	}

	private static Size ToSize(CellPixelSize? cell) => cell is { IsValid: true } known ? new Size(known.Width, known.Height) : new Size(10, 20);
}
