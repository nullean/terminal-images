using AwesomeAssertions;
using Nullean.TerminalImages.Detection;
using Nullean.TerminalImages.Tests.Support;
using P = Nullean.TerminalImages.TerminalImageProtocol;

namespace Nullean.TerminalImages.Tests;

public class DetectionTests
{
	private static TerminalEnvironment Env(params (string, string)[] variables) => TerminalEnvironment.From(variables.ToDictionary(v => v.Item1, v => v.Item2));

	private static TerminalImageCapabilities FromEnv(params (string, string)[] variables) =>
		TerminalImageDetector.DetectFromEnvironment(Env(variables), isOutputRedirected: false);

	// ---------- environment ----------

	[Test]
	public void Kitty_is_recognised_from_its_window_id_and_gets_truecolor()
	{
		var caps = FromEnv(("KITTY_WINDOW_ID", "3"));

		caps.Supported.Should().Equal(P.Kitty, P.HalfBlocks);
		caps.Preferred.Should().Be(P.Kitty);
		caps.Brand.Should().Be("kitty");
		caps.TrueColor.Should().BeTrue();
		caps.Confidence.Should().Be(DetectionConfidence.Environment);
	}

	[Test]
	[Arguments("TERM", "xterm-kitty", "kitty", P.Kitty)]
	[Arguments("TERM", "xterm-ghostty", "ghostty", P.Kitty)]
	[Arguments("TERM_PROGRAM", "ghostty", "ghostty", P.Kitty)]
	[Arguments("TERM_PROGRAM", "iTerm.app", "iterm2", P.ITerm2)]
	[Arguments("LC_TERMINAL", "iTerm2", "iterm2", P.ITerm2)] // survives SSH
	[Arguments("TERM_PROGRAM", "WezTerm", "wezterm", P.ITerm2)]
	[Arguments("WT_SESSION", "abc", "windows-terminal", P.Sixel)]
	[Arguments("TERM", "foot-extra", "foot", P.Sixel)]
	[Arguments("KONSOLE_VERSION", "230800", "konsole", P.ITerm2)]
	[Arguments("TERM_PROGRAM", "WarpTerminal", "warp", P.ITerm2)]
	public void Terminals_are_recognised_from_their_environment(string variable, string value, string brand, P preferred)
	{
		var caps = FromEnv((variable, value));

		caps.Brand.Should().Be(brand);
		caps.Preferred.Should().Be(preferred);
		caps.Supported[^1].Should().Be(P.HalfBlocks); // half blocks are always the last resort
	}

	[Test]
	public void WezTerm_prefers_iTerm2_images_over_its_rougher_Kitty_and_Sixel()
	{
		FromEnv(("TERM_PROGRAM", "WezTerm")).Supported.Should().Equal(P.ITerm2, P.Sixel, P.HalfBlocks);
	}

	[Test]
	public void A_terminal_without_image_support_gets_half_blocks()
	{
		var apple = FromEnv(("TERM_PROGRAM", "Apple_Terminal"));
		apple.Supported.Should().Equal(P.HalfBlocks);
		apple.Brand.Should().Be("apple-terminal");

		var unknown = FromEnv(("TERM", "xterm-256color"));
		unknown.Supported.Should().Equal(P.HalfBlocks);
		unknown.Confidence.Should().Be(DetectionConfidence.Default);
		unknown.TrueColor.Should().BeFalse();

		FromEnv(("TERM", "xterm-256color"), ("COLORTERM", "truecolor")).TrueColor.Should().BeTrue();
	}

	[Test]
	public void Nothing_is_drawn_when_output_is_redirected_or_the_terminal_is_dumb()
	{
		TerminalImageDetector.DetectFromEnvironment(Env(("KITTY_WINDOW_ID", "1")), isOutputRedirected: true).Preferred.Should().Be(P.None);
		FromEnv(("TERM", "dumb"), ("KITTY_WINDOW_ID", "1")).Preferred.Should().Be(P.None);
	}

	[Test]
	public void The_override_variable_forces_a_protocol_and_beats_the_environment()
	{
		var caps = FromEnv(("NULLEAN_TERMINAL_IMAGES", "sixel"), ("KITTY_WINDOW_ID", "1"));

		caps.Supported.Should().Equal(P.Sixel);
		caps.Confidence.Should().Be(DetectionConfidence.Forced);
		FromEnv(("NULLEAN_TERMINAL_IMAGES", "none")).Preferred.Should().Be(P.None);
		FromEnv(("NULLEAN_TERMINAL_IMAGES", "blocks")).Preferred.Should().Be(P.HalfBlocks);
	}

	[Test]
	public void A_forced_protocol_from_code_beats_the_variable_and_the_gates()
	{
		var caps = TerminalImageDetector.DetectFromEnvironment(Env(("NULLEAN_TERMINAL_IMAGES", "sixel")), isOutputRedirected: true, forced: P.Kitty);

		caps.Supported.Should().Equal(P.Kitty);
	}

	[Test]
	public void A_bad_override_is_ignored_with_a_diagnostic_and_auto_means_detect()
	{
		var bad = FromEnv(("NULLEAN_TERMINAL_IMAGES", "banana"), ("KITTY_WINDOW_ID", "1"));
		bad.Preferred.Should().Be(P.Kitty);
		bad.Diagnostics.Should().Contain(line => line.Contains("banana"));

		FromEnv(("NULLEAN_TERMINAL_IMAGES", "auto"), ("KITTY_WINDOW_ID", "1")).Confidence.Should().Be(DetectionConfidence.Environment);
	}

	[Test]
	public void Multiplexer_and_ssh_are_flagged()
	{
		var caps = FromEnv(("TMUX", "/tmp/tmux-1/default,1,0"), ("SSH_CONNECTION", "1.2.3.4 1 5.6.7.8 22"), ("KITTY_WINDOW_ID", "1"));

		caps.IsMultiplexer.Should().BeTrue();
		caps.IsRemote.Should().BeTrue();
		caps.Diagnostics.Should().Contain(line => line.Contains("allow-passthrough"));
	}

	[Test]
	[Arguments("9x18", 9, 18)]
	[Arguments("10X20", 10, 20)]
	public void The_cell_size_can_be_given(string text, int width, int height) =>
		FromEnv(("NULLEAN_TERMINAL_CELL_SIZE", text)).CellSize.Should().Be(new CellPixelSize(width, height));

	[Test]
	[Arguments("nine by eighteen")]
	[Arguments("0x18")]
	[Arguments("9x")]
	public void A_bad_cell_size_is_ignored(string text)
	{
		var caps = FromEnv(("NULLEAN_TERMINAL_CELL_SIZE", text));

		caps.CellSize.Should().BeNull();
		caps.Diagnostics.Should().Contain(line => line.Contains("NULLEAN_TERMINAL_CELL_SIZE"));
	}

	private static readonly string[] ProtocolNames = ["kitty", "ITERM2", "sixel", "blocks", "none", "off", "x"];

	[Test]
	public void ParseProtocol_knows_its_names() =>
		ProtocolNames.Select(TerminalImageDetector.ParseProtocol).Should().Equal(P.Kitty, P.ITerm2, P.Sixel, P.HalfBlocks, P.None, P.None, null);

	// ---------- probing ----------

	private static Task<TerminalImageCapabilities> Probe(FakeChannel channel, params (string, string)[] variables) =>
		TerminalImageDetector.DetectAsync(new TerminalImageDetectionOptions
		{
			Environment = Env(variables),
			IsOutputRedirected = false,
			Channel = channel,
			ProbeKitty = true
		}).AsTask();

	[Test]
	public async Task The_probe_ends_with_DA1_and_includes_the_kitty_query()
	{
		var channel = new FakeChannel("\e[?62c");

		await Probe(channel, ("TERM", "xterm-256color"));

		var request = channel.Requests.Single();
		request.Should().EndWith("\e[c"); // the sentinel is last
		request.Should().Contain("\e_Gi=31,s=1,v=1,a=q,t=d,f=24;AAAA\e\\").And.Contain("\e[>0q").And.Contain("\e[16t");
		channel.CompletenessCheckAcceptedTheReply.Should().BeTrue();
	}

	[Test]
	public async Task A_kitty_that_answers_everything_is_probed_and_gives_its_cell_size()
	{
		var channel = new FakeChannel("\e_Gi=31;OK\e\\\eP>|kitty(0.32.2)\e\\\e[6;20;10t\e[?62;22c");

		var caps = await Probe(channel, ("KITTY_WINDOW_ID", "1"));

		caps.Confidence.Should().Be(DetectionConfidence.Probed);
		caps.Preferred.Should().Be(P.Kitty);
		caps.Brand.Should().Be("kitty");
		caps.BrandVersion.Should().Be("0.32.2");
		caps.CellSize.Should().Be(new CellPixelSize(10, 20));
	}

	[Test]
	public async Task A_stale_environment_is_corrected_by_the_terminals_own_name()
	{
		// KITTY_WINDOW_ID is left over (say, from a parent shell), but the terminal is xterm with Sixel on.
		var channel = new FakeChannel("\eP>|XTerm(388)\e\\\e[?64;4;22c", "\e[?1;0;256S\e[?64c");

		var caps = await Probe(channel, ("KITTY_WINDOW_ID", "1"));

		caps.Brand.Should().Be("xterm");
		caps.Supported.Should().Equal(P.Sixel, P.HalfBlocks);
		caps.Diagnostics.Should().Contain(line => line.Contains("believing the terminal"));
	}

	[Test]
	public async Task Kitty_is_ruled_out_when_the_terminal_answers_DA1_but_ignores_the_kitty_query()
	{
		// No XTVERSION answer, so the environment's "kitty" stands as the brand: only the missing Kitty reply contradicts it.
		var channel = new FakeChannel("\e[?64;4;22c", "\e[?1;0;256S\e[?64c");

		var caps = await Probe(channel, ("KITTY_WINDOW_ID", "1"));

		caps.Supported.Should().Equal(P.Sixel, P.HalfBlocks);
		caps.Diagnostics.Should().Contain(line => line.Contains("not the Kitty query"));
	}

	[Test]
	public async Task Sixel_colour_registers_are_asked_for_only_when_DA1_lists_sixel()
	{
		var withSixel = new FakeChannel("\e[?62;4c", "\e[?1;0;1024S\e[?62c");
		var caps = await Probe(withSixel, ("TERM", "xterm-256color"));
		caps.SixelColorRegisters.Should().Be(1024);
		withSixel.Requests.Should().HaveCount(2);
		withSixel.Requests[1].Should().Be("\e[?1;1;0S\e[c");

		var without = new FakeChannel("\e[?62c");
		await Probe(without, ("TERM", "xterm-256color"));
		without.Requests.Should().HaveCount(1);
	}

	[Test]
	public async Task A_silent_terminal_falls_back_to_the_environment()
	{
		var caps = await Probe(new FakeChannel(), ("KITTY_WINDOW_ID", "1"));

		caps.Confidence.Should().Be(DetectionConfidence.Environment);
		caps.Preferred.Should().Be(P.Kitty);
		caps.Diagnostics.Should().Contain(line => line.Contains("did not answer"));
	}

	[Test]
	public async Task A_probe_that_throws_falls_back_to_the_environment()
	{
		var caps = await Probe(new FakeChannel { Throw = new IOException("no tty") }, ("KITTY_WINDOW_ID", "1"));

		caps.Preferred.Should().Be(P.Kitty);
		caps.Diagnostics.Should().Contain(line => line.Contains("probe failed"));
	}

	[Test]
	public async Task The_iTerm2_brand_skips_the_kitty_query_that_would_garble_its_title()
	{
		var channel = new FakeChannel("\eP>|iTerm2 3.5.0\e\\\e[?62c");

		await Probe(channel, ("TERM_PROGRAM", "iTerm.app"));

		channel.Requests.Single().Should().NotContain("_G");
	}

	[Test]
	public async Task In_tmux_the_whole_batch_is_wrapped_so_the_outer_DA1_is_the_sentinel_and_kitty_is_not_ruled_out()
	{
		var channel = new FakeChannel("\e[?62c"); // the outer terminal answered DA1 but we cannot tell about Kitty through tmux

		var caps = await Probe(channel, ("TMUX", "x"), ("KITTY_WINDOW_ID", "1"));

		channel.Requests.Single().Should().StartWith("\ePtmux;").And.EndWith("\e\\");
		channel.Requests.Single().Should().Contain("\e\e[c"); // DA1 inside the wrapper, its ESC doubled
		caps.Supported.Should().Contain(P.Kitty); // no verdict: the environment stands
	}

	[Test]
	public async Task A_brand_keeps_its_own_order_and_the_probe_only_adds_to_it()
	{
		// WezTerm: iTerm2 first by the quirk table, even though it also answers the Kitty query.
		var channel = new FakeChannel("\e_Gi=31;OK\e\\\eP>|WezTerm 20240203\e\\\e[?65;4c", "\e[?1;0;256S\e[?65c");

		var caps = await Probe(channel, ("TERM_PROGRAM", "WezTerm"));

		caps.Supported.Should().Equal(P.ITerm2, P.Sixel, P.Kitty, P.HalfBlocks);
	}

	[Test]
	public async Task A_terminal_that_is_not_in_the_table_orders_what_it_proved_by_default()
	{
		var channel = new FakeChannel("\e_Gi=31;OK\e\\\eP>|someterm 1.0\e\\\e[?62;4c", "\e[?1;0;256S\e[?62c");

		var caps = await Probe(channel, ("TERM", "xterm-256color"));

		caps.Supported.Should().Equal(P.Kitty, P.Sixel, P.HalfBlocks);
		caps.Diagnostics.Should().Contain(line => line.Contains("someterm"));
	}

	[Test]
	public async Task When_the_terminal_names_itself_differently_it_wins_over_the_environment()
	{
		var channel = new FakeChannel("\eP>|foot(1.16.2)\e\\\e[?62;4c", "\e[?1;0;256S\e[?62c");

		var caps = await Probe(channel, ("TERM_PROGRAM", "WezTerm"));

		caps.Brand.Should().Be("foot");
		caps.Preferred.Should().Be(P.Sixel);
		caps.Diagnostics.Should().Contain(line => line.Contains("believing the terminal"));
	}

	[Test]
	public async Task A_forced_protocol_or_redirected_output_is_never_probed()
	{
		var forced = new FakeChannel("\e[?62c");
		await TerminalImageDetector.DetectAsync(new TerminalImageDetectionOptions { Environment = Env(), Protocol = P.Sixel, Channel = forced });
		forced.Requests.Should().BeEmpty();

		var redirected = new FakeChannel("\e[?62c");
		await TerminalImageDetector.DetectAsync(new TerminalImageDetectionOptions { Environment = Env(), IsOutputRedirected = true, Channel = redirected });
		redirected.Requests.Should().BeEmpty();
	}

	[Test]
	public async Task NoProbing_detects_from_the_environment_alone()
	{
		var caps = await TerminalImageDetector.DetectAsync(new TerminalImageDetectionOptions
		{
			Environment = Env(("KITTY_WINDOW_ID", "1")),
			IsOutputRedirected = false,
			Channel = TerminalImageDetectionOptions.NoProbing,
		});

		caps.Preferred.Should().Be(P.Kitty);
		caps.Confidence.Should().Be(DetectionConfidence.Environment);
	}
}
