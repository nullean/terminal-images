using AwesomeAssertions;
using CsCheck;
using Nullean.TerminalImages.Png;
using Nullean.TerminalImages.Tests.Support;

namespace Nullean.TerminalImages.Tests;

public class PngTests
{
	// 11x7: odd sizes, so packed sub-byte rows have padding and every Adam7 pass is non-empty (or empty) in a different way.
	private const int W = 11;
	private const int H = 7;

	private static int Max(int depth) => (1 << depth) - 1;

	[Test]
	[Arguments(1, false)]
	[Arguments(1, true)]
	[Arguments(2, false)]
	[Arguments(4, true)]
	[Arguments(8, false)]
	[Arguments(8, true)]
	[Arguments(16, false)]
	[Arguments(16, true)]
	public void Gray_decodes_at_every_depth_and_interlace(int depth, bool interlace)
	{
		int Sample(int x, int y, int c) => ((x * 3) + (y * 5)) % (Max(depth) + 1);
		var png = PngBuilder.Build(0, depth, interlace, W, H, Sample);

		var image = PngDecoder.Decode(png);

		image.Width.Should().Be(W);
		image.Height.Should().Be(H);
		for (var y = 0; y < H; y++)
		{
			for (var x = 0; x < W; x++)
			{
				var expected = Scaled(Sample(x, y, 0), depth);
				Pixel(image, x, y).Should().Be((expected, expected, expected, (byte)255), $"at {x},{y}");
			}
		}
	}

	[Test]
	[Arguments(8, false)]
	[Arguments(8, true)]
	[Arguments(16, false)]
	[Arguments(16, true)]
	public void Rgb_decodes(int depth, bool interlace)
	{
		int Sample(int x, int y, int c) => ((x * 17) + (y * 31) + (c * 53)) % (Max(depth) + 1);
		var image = PngDecoder.Decode(PngBuilder.Build(2, depth, interlace, W, H, Sample));

		for (var y = 0; y < H; y++)
		{
			for (var x = 0; x < W; x++)
				Pixel(image, x, y).Should().Be((Scaled(Sample(x, y, 0), depth), Scaled(Sample(x, y, 1), depth), Scaled(Sample(x, y, 2), depth), (byte)255), $"at {x},{y}");
		}
	}

	[Test]
	[Arguments(1)]
	[Arguments(2)]
	[Arguments(4)]
	[Arguments(8)]
	public void Palette_decodes_with_per_entry_alpha(int depth)
	{
		var entries = Math.Min(1 << depth, 5);
		var palette = new byte[entries * 3];
		for (var i = 0; i < palette.Length; i++)
			palette[i] = (byte)(i * 37);
		var alpha = new byte[] { 10, 200 }; // fewer entries than the palette: the rest are opaque

		int Sample(int x, int y, int c) => (x + y) % entries;
		var image = PngDecoder.Decode(PngBuilder.Build(3, depth, interlace: depth == 4, W, H, Sample, palette, alpha));

		for (var y = 0; y < H; y++)
		{
			for (var x = 0; x < W; x++)
			{
				var index = Sample(x, y, 0);
				var expectedAlpha = index < alpha.Length ? alpha[index] : (byte)255;
				Pixel(image, x, y).Should().Be((palette[index * 3], palette[(index * 3) + 1], palette[(index * 3) + 2], expectedAlpha), $"at {x},{y}");
			}
		}
	}

	[Test]
	[Arguments(8)]
	[Arguments(16)]
	public void GrayAlpha_and_Rgba_decode(int depth)
	{
		int Sample(int x, int y, int c) => ((x * 19) + (y * 7) + (c * 41)) % (Max(depth) + 1);

		var grayAlpha = PngDecoder.Decode(PngBuilder.Build(4, depth, interlace: true, W, H, Sample));
		var rgba = PngDecoder.Decode(PngBuilder.Build(6, depth, interlace: false, W, H, Sample));

		for (var y = 0; y < H; y++)
		{
			for (var x = 0; x < W; x++)
			{
				var gray = Scaled(Sample(x, y, 0), depth);
				Pixel(grayAlpha, x, y).Should().Be((gray, gray, gray, Scaled(Sample(x, y, 1), depth)), $"gray+alpha at {x},{y}");
				Pixel(rgba, x, y).Should().Be((Scaled(Sample(x, y, 0), depth), Scaled(Sample(x, y, 1), depth), Scaled(Sample(x, y, 2), depth), Scaled(Sample(x, y, 3), depth)), $"rgba at {x},{y}");
			}
		}
	}

	[Test]
	public void Colour_key_transparency_applies_to_gray_and_rgb()
	{
		// Gray 8-bit: the sample value 4 is transparent.
		var gray = PngDecoder.Decode(PngBuilder.Build(0, 8, false, W, H, (x, y, _) => (x + y) % 8, transparency: [0, 4]));
		Pixel(gray, 4, 0).Item4.Should().Be(0);
		Pixel(gray, 3, 0).Item4.Should().Be(255);

		// RGB 8-bit: only the exact triple (1,2,3) is transparent.
		static int Rgb(int x, int y, int c) => (x + y) % 2 == 0 ? c + 1 : c + 9;
		var rgb = PngDecoder.Decode(PngBuilder.Build(2, 8, false, W, H, Rgb, transparency: [0, 1, 0, 2, 0, 3]));
		Pixel(rgb, 0, 0).Item4.Should().Be(0);
		Pixel(rgb, 1, 0).Item4.Should().Be(255);
	}

	[Test]
	public void Corrupt_input_is_rejected_with_InvalidDataException()
	{
		var good = PngBuilder.Build(2, 8, false, 3, 3, (x, y, c) => x + y + c);

		var notPng = () => PngDecoder.Decode([1, 2, 3, 4, 5, 6, 7, 8, 9]);
		var truncated = () => PngDecoder.Decode(good.AsSpan(0, good.Length - 20));
		var badCrc = () =>
		{
			var copy = (byte[])good.Clone();
			copy[29] ^= 0xFF; // the first byte of the IHDR chunk's stored CRC: the data itself stays valid
			return PngDecoder.Decode(copy);
		};

		notPng.Should().Throw<InvalidDataException>();
		truncated.Should().Throw<InvalidDataException>();
		badCrc.Should().Throw<InvalidDataException>().WithMessage("*IHDR*CRC*");
	}

	[Test]
	public void Encoder_output_has_a_valid_structure_and_round_trips()
	{
		var pixels = new byte[W * H * 4];
		for (var i = 0; i < pixels.Length; i++)
			pixels[i] = (byte)((i * 37) ^ (i >> 3));
		var image = new RgbaImage(W, H, pixels);

		var png = PngEncoder.Encode(image);

		PngDecoder.IsPng(png).Should().BeTrue();
		PngDecoder.Decode(png).Pixels.ToArray().Should().Equal(pixels);
	}

	[Test]
	public void Encoder_round_trips_any_image()
	{
		var generator = Gen.Int[1, 24].SelectMany(w => Gen.Int[1, 24].SelectMany(h =>
			Gen.Byte.Array[w * h * 4].Select(bytes => new RgbaImage(w, h, bytes))));

		generator.Sample(image => PngDecoder.Decode(PngEncoder.Encode(image)).Pixels.ToArray().SequenceEqual(image.Pixels.ToArray()), iter: 200);
	}

	[Test]
	public void Crc32_matches_the_known_check_value() =>
		Crc32.Compute("123456789"u8).Should().Be(0xCBF43926u);

	private static (byte, byte, byte, byte) Pixel(RgbaImage image, int x, int y)
	{
		var span = image.Pixels.Span;
		var at = ((y * image.Width) + x) * 4;
		return (span[at], span[at + 1], span[at + 2], span[at + 3]);
	}

	private static byte Scaled(int sample, int depth) => depth switch
	{
		8 => (byte)sample,
		16 => (byte)(sample >> 8),
		_ => (byte)(sample * 255 / Max(depth)),
	};
}
