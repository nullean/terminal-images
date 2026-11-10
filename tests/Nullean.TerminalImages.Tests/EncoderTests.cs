using System.IO.Compression;
using AwesomeAssertions;
using Nullean.TerminalImages.Encoders;
using Nullean.TerminalImages.Png;
using Nullean.TerminalImages.Tests.Support;

namespace Nullean.TerminalImages.Tests;

public class EncoderTests
{
	private static string Write(ITerminalImageEncoder encoder, RgbaImage image, CellSize cells = default)
	{
		var writer = new StringWriter();
		encoder.Encode(image, cells, writer);
		return writer.ToString();
	}

	private static RgbaImage Pattern(int width, int height)
	{
		var pixels = new byte[width * height * 4];
		for (var i = 0; i < pixels.Length; i++)
			pixels[i] = (byte)((i * 31) ^ (i >> 2));
		return new RgbaImage(width, height, pixels);
	}

	// ---------- Kitty ----------

	[Test]
	public void Kitty_sends_a_small_image_in_one_chunk()
	{
		var image = RgbaImage.Solid(2, 2, 1, 2, 3);

		var text = Write(new KittyEncoder(), image);

		text.Should().Be($"\e_Ga=T,f=32,s=2,v=2,q=2,m=0;{Convert.ToBase64String(image.Pixels.ToArray())}\e\\");
	}

	[Test]
	public void Kitty_chunks_large_images_and_the_chunks_rebuild_the_pixels()
	{
		var image = Pattern(100, 100); // 40,000 bytes: 53,336 base64 characters, so 14 chunks

		var text = Write(new KittyEncoder(), image);

		var chunks = text.Split("\e\\", StringSplitOptions.RemoveEmptyEntries);
		chunks.Length.Should().Be(14);
		var payload = new System.Text.StringBuilder();
		for (var i = 0; i < chunks.Length; i++)
		{
			chunks[i].Should().StartWith("\e_G");
			var separator = chunks[i].IndexOf(';');
			var keys = chunks[i][3..separator];
			var data = chunks[i][(separator + 1)..];
			data.Length.Should().BeLessThanOrEqualTo(4096);
			if (i == 0)
				keys.Should().StartWith("a=T,f=32,s=100,v=100");
			else
				keys.Should().Be(i < chunks.Length - 1 ? "m=1" : "m=0"); // later chunks carry only the continuation flag
			if (i < chunks.Length - 1)
			{
				keys.Should().EndWith("m=1");
				data.Length.Should().Be(4096);
			}
			else
			{
				keys.Should().EndWith("m=0");
			}

			payload.Append(data);
		}

		Convert.FromBase64String(payload.ToString()).Should().Equal(image.Pixels.ToArray());
	}

	[Test]
	public void Kitty_compression_deflates_the_pixels_and_says_so()
	{
		var image = RgbaImage.Solid(64, 64, 9, 9, 9);

		var text = Write(new KittyEncoder { Compress = true }, image);

		text.Should().Contain(",o=z");
		var data = text[(text.IndexOf(';') + 1)..^2];
		var compressed = Convert.FromBase64String(data);
		compressed.Length.Should().BeLessThan(image.Pixels.Length / 10);
		using var zlib = new ZLibStream(new MemoryStream(compressed), CompressionMode.Decompress);
		using var inflated = new MemoryStream();
		zlib.CopyTo(inflated);
		inflated.ToArray().Should().Equal(image.Pixels.ToArray());
	}

	[Test]
	public void Kitty_adds_id_and_cell_size_to_the_first_chunk_only()
	{
		var text = Write(new KittyEncoder { ImageId = 7 }, RgbaImage.Solid(100, 100, 1, 1, 1), new CellSize(10, 5));

		var chunks = text.Split("\e\\", StringSplitOptions.RemoveEmptyEntries);
		chunks[0].Should().Contain(",i=7").And.Contain(",c=10,r=5");
		chunks[1..].Should().OnlyContain(chunk => !chunk.Contains(",i=") && !chunk.Contains("c=10"));
	}

	[Test]
	public void Kitty_png_pass_through_does_not_re_encode_and_delete_frees_the_data()
	{
		var png = PngEncoder.Encode(Pattern(3, 3));
		var writer = new StringWriter();

		new KittyEncoder().EncodePng(png, default, writer);

		var text = writer.ToString();
		text.Should().StartWith("\e_Ga=T,f=100,q=2,m=");
		Convert.FromBase64String(text[(text.IndexOf(';') + 1)..^2]).Should().Equal(png);
		KittyEncoder.Delete(7).Should().Be("\e_Ga=d,d=I,i=7,q=2\e\\");
	}

	// ---------- iTerm2 ----------

	[Test]
	public void ITerm2_sends_a_png_with_its_size_and_cell_box()
	{
		var image = Pattern(5, 4);

		var text = Write(new ITerm2Encoder(), image, new CellSize(10, 3));

		text.Should().StartWith("\e]1337;File=inline=1;size=").And.EndWith("\a");
		var colon = text.IndexOf(':');
		var png = Convert.FromBase64String(text[(colon + 1)..^1]);
		text[..colon].Should().Be($"\e]1337;File=inline=1;size={png.Length};width=10;height=3;preserveAspectRatio=1");
		PngDecoder.Decode(png).Pixels.ToArray().Should().Equal(image.Pixels.ToArray());
	}

	[Test]
	public void ITerm2_falls_back_to_pixel_size_without_cells_and_passes_files_through()
	{
		var text = Write(new ITerm2Encoder(), RgbaImage.Solid(6, 2, 0, 0, 0));
		text.Should().Contain(";width=6px;height=2px;");

		var writer = new StringWriter();
		ITerm2Encoder.EncodeFile([1, 2, 3], new CellSize(4, 2), writer);
		writer.ToString().Should().Be($"\e]1337;File=inline=1;size=3;width=4;height=2;preserveAspectRatio=1:{Convert.ToBase64String([1, 2, 3])}\a");
	}

	// ---------- Half blocks ----------

	[Test]
	public void HalfBlocks_draw_two_pixels_per_cell()
	{
		var image = new RgbaImage(1, 2, new byte[] { 255, 0, 0, 255, 0, 0, 255, 255 });

		Write(new HalfBlockEncoder(), image).Should().Be("\e[38;2;255;0;0m\e[48;2;0;0;255m▀\e[0m\n");
	}

	[Test]
	public void HalfBlocks_leave_the_terminal_background_for_transparent_pixels()
	{
		var image = new RgbaImage(1, 2, new byte[] { 0, 0, 0, 0, 0, 0, 255, 255 }); // top transparent, bottom blue

		Write(new HalfBlockEncoder(), image).Should().Be("\e[38;2;0;0;255m\e[49m▄\e[0m\n");
		// An odd last row has a transparent partner.
		Write(new HalfBlockEncoder(), new RgbaImage(1, 1, new byte[] { 1, 2, 3, 255 })).Should().Be("\e[38;2;1;2;3m\e[49m▀\e[0m\n");
	}

	[Test]
	public void HalfBlocks_repeat_colour_codes_only_when_the_colour_changes()
	{
		var image = RgbaImage.Solid(3, 2, 10, 20, 30);

		Write(new HalfBlockEncoder(), image).Should().Be("\e[38;2;10;20;30m\e[48;2;10;20;30m▀▀▀\e[0m\n");
	}

	[Test]
	public void HalfBlocks_in_256_colours_use_the_xterm_palette()
	{
		var image = new RgbaImage(1, 2, new byte[] { 255, 0, 0, 255, 0, 0, 0, 255 });

		Write(new HalfBlockEncoder { ColorDepth = HalfBlockColorDepth.Palette256 }, image).Should().Be("\e[38;5;196m\e[48;5;16m▀\e[0m\n");
		HalfBlockEncoder.ToXterm256(255, 255, 255).Should().Be(231);
		HalfBlockEncoder.ToXterm256(128, 128, 128).Should().Be(244); // closer to the grey ramp than to the colour cube
	}

	// ---------- Sixel ----------

	private static RgbaImage Quadrants(int width, int height, bool withHole)
	{
		var pixels = new byte[width * height * 4];
		for (var y = 0; y < height; y++)
		{
			for (var x = 0; x < width; x++)
			{
				var (r, g, b) = (x < width / 2, y < height / 2) switch
				{
					(true, true) => (0, 0, 0),
					(false, true) => (255, 0, 0),
					(true, false) => (0, 255, 0),
					_ => (255, 255, 255),
				};
				var a = withHole && x == 0 && y == 0 ? 0 : 255;
				var at = ((y * width) + x) * 4;
				pixels[at] = (byte)r;
				pixels[at + 1] = (byte)g;
				pixels[at + 2] = (byte)b;
				pixels[at + 3] = (byte)a;
			}
		}

		return new RgbaImage(width, height, pixels);
	}

	[Test]
	[Arguments(SixelPalette.MedianCut, true)]
	[Arguments(SixelPalette.MedianCut, false)]
	[Arguments(SixelPalette.Rgb332, true)]
	[Arguments(SixelPalette.Rgb332, false)]
	public void Sixel_decodes_back_to_the_image(SixelPalette palette, bool runLength)
	{
		// 13x14: the last band has only 2 rows, and the quadrants split a band in the middle.
		var image = Quadrants(13, 14, withHole: false);

		var decoded = SixelDecoder.Decode(Write(new SixelEncoder { Palette = palette, RunLengthEncoding = runLength }, image));

		decoded.Width.Should().Be(13);
		decoded.Height.Should().Be(14);
		decoded.TransparentBackground.Should().BeFalse();
		var source = image.Pixels.Span;
		for (var y = 0; y < 14; y++)
		{
			for (var x = 0; x < 13; x++)
			{
				var at = ((y * 13) + x) * 4;
				decoded.Pixels[x, y].Should().Be(((int)source[at], (int)source[at + 1], (int)source[at + 2]), $"at {x},{y}");
			}
		}
	}

	[Test]
	public void Sixel_defines_colours_as_rounded_percentages()
	{
		// 150/255 = 58.8% and 100/255 = 39.2%: rounding gives 59 and 39, truncating would give 58.
		var text = Write(new SixelEncoder(), RgbaImage.Solid(2, 2, 150, 100, 255));

		// Two rows tall, so each column is the bits 0b11: the character 'B' (63 + 3). A run of two is too short for "!".
		text.Should().Be("\ePq\"1;1;2;2#0;2;59;39;100#0BB\e\\");
	}

	[Test]
	public void Sixel_leaves_transparent_pixels_undrawn_and_asks_for_a_transparent_background()
	{
		var decoded = SixelDecoder.Decode(Write(new SixelEncoder(), Quadrants(8, 8, withHole: true)));

		decoded.TransparentBackground.Should().BeTrue();
		decoded.Pixels[0, 0].Should().BeNull();
		decoded.Pixels[1, 0].Should().NotBeNull();
	}

	[Test]
	public void Sixel_run_length_encoding_shrinks_flat_areas_without_changing_the_picture()
	{
		var image = RgbaImage.Solid(600, 12, 5, 5, 5);

		var plain = Write(new SixelEncoder { RunLengthEncoding = false }, image);
		var packed = Write(new SixelEncoder(), image);

		packed.Length.Should().BeLessThan(plain.Length / 4);
		packed.Should().Contain("!600");
	}

	[Test]
	public void Sixel_respects_the_colour_register_limit_and_dithering_stays_inside_the_palette()
	{
		var image = Pattern(32, 32); // many different colours
		var opaque = image.CompositeOver(0, 0, 0);

		foreach (var dither in new[] { false, true })
		{
			var decoded = SixelDecoder.Decode(Write(new SixelEncoder { MaxColors = 16, Dither = dither }, opaque));
			decoded.RegistersDefined.Should().BeLessThanOrEqualTo(16);
			var distinct = new HashSet<(int, int, int)>();
			foreach (var pixel in decoded.Pixels)
				distinct.Add(pixel!.Value);
			distinct.Count.Should().BeInRange(2, 16);
		}
	}

	[Test]
	public void Sixel_of_a_fully_transparent_image_draws_nothing_and_still_terminates()
	{
		var text = Write(new SixelEncoder(), RgbaImage.Solid(4, 4, 0, 0, 0, 0));

		var decoded = SixelDecoder.Decode(text);
		foreach (var pixel in decoded.Pixels)
			pixel.Should().BeNull();
	}

	// ---------- tmux ----------

	[Test]
	public void Tmux_passthrough_doubles_every_escape_and_wraps_each_sequence_on_its_own()
	{
		TmuxPassthrough.Wrap("\e_Gabc\e\\\e_Gdef\e\\")
			.Should().Be("\ePtmux;\e\e_Gabc\e\e\\\e\\\ePtmux;\e\e_Gdef\e\e\\\e\\");
		TmuxPassthrough.Wrap("\e]1337;File=x\a").Should().Be("\ePtmux;\e\e]1337;File=x\a\e\\"); // BEL-terminated: one piece
		TmuxPassthrough.Wrap("").Should().Be("");
	}
}
