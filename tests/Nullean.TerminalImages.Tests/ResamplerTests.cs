using AwesomeAssertions;
using CsCheck;

namespace Nullean.TerminalImages.Tests;

public class ResamplerTests
{
	[Test]
	[Arguments(100, 50, 40, 40, false, 40, 20)]
	[Arguments(50, 100, 40, 40, false, 20, 40)]
	[Arguments(10, 10, 40, 40, false, 10, 10)] // never grows unless asked
	[Arguments(10, 10, 40, 40, true, 40, 40)]
	[Arguments(1000, 1, 10, 10, false, 10, 1)] // never below 1x1
	public void FitWithin_keeps_the_aspect_ratio(int w, int h, int boxW, int boxH, bool upscale, int expectedW, int expectedH) =>
		Resampler.FitWithin(w, h, boxW, boxH, upscale).Should().Be((expectedW, expectedH));

	[Test]
	public void Resize_to_the_same_size_returns_the_same_image()
	{
		var image = RgbaImage.Solid(3, 3, 1, 2, 3);

		Resampler.Resize(image, 3, 3).Should().BeSameAs(image);
	}

	[Test]
	public void Area_averaging_blends_black_and_white_to_grey()
	{
		var image = new RgbaImage(2, 1, new byte[] { 0, 0, 0, 255, 255, 255, 255, 255 });

		var result = Resampler.Resize(image, 1, 1);

		result.Pixels.ToArray().Should().Equal(128, 128, 128, 255);
	}

	[Test]
	public void Area_averaging_ignores_the_colour_of_transparent_pixels()
	{
		// Opaque red next to fully transparent blue: the result is half-transparent red, not purple.
		var image = new RgbaImage(2, 1, new byte[] { 255, 0, 0, 255, 0, 0, 255, 0 });

		var result = Resampler.Resize(image, 1, 1);

		result.Pixels.ToArray().Should().Equal(255, 0, 0, 128);
	}

	[Test]
	public void Nearest_keeps_hard_edges_when_growing()
	{
		var image = new RgbaImage(2, 1, new byte[] { 255, 0, 0, 255, 0, 0, 255, 255 });

		var result = Resampler.Resize(image, 4, 1, ResampleMode.Nearest);

		result.Pixels.ToArray().Should().Equal(255, 0, 0, 255, 255, 0, 0, 255, 0, 0, 255, 255, 0, 0, 255, 255);
	}

	[Test]
	public void Bilinear_growing_makes_intermediate_colours()
	{
		var image = new RgbaImage(2, 1, new byte[] { 0, 0, 0, 255, 255, 255, 255, 255 });

		var result = Resampler.Resize(image, 4, 1);

		var pixels = result.Pixels.ToArray();
		pixels[0].Should().Be(0); // the edges stay put
		pixels[12].Should().Be(255);
		pixels[4].Should().BeInRange(1, 254);
		pixels[8].Should().BeInRange(1, 254);
		pixels[4].Should().BeLessThan(pixels[8]);
	}

	[Test]
	public void Resizing_a_solid_colour_keeps_the_colour_at_any_size()
	{
		Gen.Select(Gen.Int[1, 40], Gen.Int[1, 40], Gen.Int[1, 40], Gen.Int[1, 40], Gen.Byte, Gen.Byte, Gen.Byte)
			.Sample(t =>
			{
				var (w, h, nw, nh, r, g, b) = t;
				var resized = Resampler.Resize(RgbaImage.Solid(w, h, r, g, b), nw, nh);
				var span = resized.Pixels.Span;
				for (var i = 0; i < span.Length; i += 4)
				{
					if (Math.Abs(span[i] - r) > 1 || Math.Abs(span[i + 1] - g) > 1 || Math.Abs(span[i + 2] - b) > 1 || span[i + 3] != 255)
						return false;
				}

				return resized.Width == nw && resized.Height == nh;
			}, iter: 300);
	}

	[Test]
	public void CompositeOver_blends_with_the_background()
	{
		var image = new RgbaImage(2, 1, new byte[] { 255, 0, 0, 255, 255, 0, 0, 0 });

		image.CompositeOver(0, 0, 255).Pixels.ToArray().Should().Equal(255, 0, 0, 255, 0, 0, 255, 255);
	}
}
