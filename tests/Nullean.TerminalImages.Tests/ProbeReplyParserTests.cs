using AwesomeAssertions;
using CsCheck;
using Nullean.TerminalImages.Detection;

namespace Nullean.TerminalImages.Tests;

public class ProbeReplyParserTests
{
	[Test]
	public void A_full_kitty_reply_is_understood()
	{
		// What kitty sends back to: Kitty query, XTVERSION, CSI 16 t, DA1.
		var replies = ProbeReplyParser.Parse("\e_Gi=31;OK\e\\\eP>|kitty(0.32.2)\e\\\e[6;20;10t\e[?62;4;22c");

		replies.HasDeviceAttributes.Should().BeTrue();
		replies.KittyGraphics.Should().BeTrue();
		replies.TerminalName.Should().Be("kitty");
		replies.TerminalVersionNumber.Should().Be("0.32.2");
		replies.CellSize.Should().Be(new CellPixelSize(10, 20)); // CSI 6 ; HEIGHT ; WIDTH t
		replies.DeviceAttributes.Should().Equal(62, 4, 22);
		replies.SupportsSixel.Should().BeTrue();
	}

	[Test]
	public void A_terminal_that_only_answers_DA1_gives_no_kitty_answer()
	{
		var replies = ProbeReplyParser.Parse("\e[?62;22c");

		replies.HasDeviceAttributes.Should().BeTrue();
		replies.KittyGraphics.Should().BeNull();
		replies.SupportsSixel.Should().BeFalse();
		replies.TerminalName.Should().BeNull();
		replies.CellSize.Should().BeNull();
	}

	[Test]
	public void A_kitty_error_is_a_no_and_another_graphics_id_is_not_ours()
	{
		ProbeReplyParser.Parse("\e_Gi=31;ENOTSUPPORTED:nope\e\\").KittyGraphics.Should().BeFalse();
		ProbeReplyParser.Parse("\e_Gi=99;OK\e\\").KittyGraphics.Should().BeNull();
	}

	[Test]
	[Arguments("WezTerm 20240203-110809-5046fc22", "WezTerm", "20240203-110809-5046fc22")]
	[Arguments("iTerm2 3.5.0", "iTerm2", "3.5.0")]
	[Arguments("foot(1.16.2)", "foot", "1.16.2")]
	[Arguments("XTerm(388)", "XTerm", "388")]
	[Arguments("ghostty", "ghostty", null)]
	public void XTVERSION_comes_in_two_shapes(string text, string name, string? version)
	{
		var replies = ProbeReplyParser.Parse($"\eP>|{text}\e\\\e[?1c");

		replies.TerminalName.Should().Be(name);
		replies.TerminalVersionNumber.Should().Be(version);
		replies.TerminalVersion.Should().Be(text);
	}

	[Test]
	public void Sixel_colour_registers_are_read_only_from_a_successful_answer()
	{
		ProbeReplyParser.Parse("\e[?1;0;1024S").SixelColorRegisters.Should().Be(1024);
		ProbeReplyParser.Parse("\e[?1;3;0S").SixelColorRegisters.Should().BeNull(); // status 3: failure
		ProbeReplyParser.Parse("\e[?2;0;800S").SixelColorRegisters.Should().BeNull(); // item 2 is the geometry
	}

	[Test]
	public void DA2_replies_and_unrelated_sequences_are_ignored()
	{
		var replies = ProbeReplyParser.Parse("\e[>1;10;0c\e[1;5R\e[A");

		replies.HasDeviceAttributes.Should().BeFalse();
	}

	[Test]
	public void An_incomplete_reply_is_not_complete()
	{
		ProbeReplyParser.IsComplete("\e[?62;4").Should().BeFalse();
		ProbeReplyParser.IsComplete("\e_Gi=31;OK\e\\").Should().BeFalse(); // the sentinel has not arrived
		ProbeReplyParser.IsComplete("\e_Gi=31;OK\e\\\e[?62c").Should().BeTrue();
	}

	[Test]
	public void The_parser_never_throws_on_any_text()
	{
		// Terminals send arbitrary bytes, including the user's own typing. Bias the alphabet to the characters that matter.
		var alphabet = Gen.Char["\e[]_P>?;0123456789cStG=\\ \a|"].Array[0, 60].Select(chars => new string(chars));
		alphabet.Sample(
			text =>
			{
				ProbeReplyParser.Parse(text);
				return true;
			},
			iter: 5000
		);
	}
}
