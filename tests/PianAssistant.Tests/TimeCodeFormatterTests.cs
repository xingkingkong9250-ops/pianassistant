using PianAssistant.Helpers;
using Xunit;

namespace PianAssistant.Tests;

public class TimeCodeFormatterTests
{
    [Theory]
    [InlineData(0, "00:00:00.000")]
    [InlineData(1.5, "00:00:01.500")]
    [InlineData(3661.234, "01:01:01.234")]
    public void FormatSmtc_ValidTimeSpan(double seconds, string expected)
    {
        var ts = TimeSpan.FromSeconds(seconds);
        Assert.Equal(expected, TimeCodeFormatter.FormatSmtc(ts));
    }

    [Fact]
    public void FormatSmtc_NegativeClampsToZero()
    {
        var ts = TimeSpan.FromSeconds(-5);
        Assert.Equal("00:00:00.000", TimeCodeFormatter.FormatSmtc(ts));
    }

    [Fact]
    public void FormatFrames_25Fps_Boundary()
    {
        var ts = TimeSpan.FromSeconds(1.0); // 25 frames at 25fps
        Assert.Equal("00:00:01:00", TimeCodeFormatter.FormatFrames(ts, 25));
    }

    [Fact]
    public void FormatFrames_24Fps()
    {
        var ts = TimeSpan.FromSeconds(1.0 / 24 * 12); // 12 frames into 1 second
        var result = TimeCodeFormatter.FormatFrames(ts, 24);
        Assert.Equal("00:00:00:12", result);
    }

    [Fact]
    public void FormatFrames_ZeroFps_DefaultsTo25()
    {
        var ts = TimeSpan.FromSeconds(1.0);
        var result = TimeCodeFormatter.FormatFrames(ts, 0);
        Assert.Equal("00:00:01:00", result);
    }

    [Fact]
    public void ToFileNameFragment_Format()
    {
        var ts = TimeSpan.FromMilliseconds(3661234);
        var result = TimeCodeFormatter.ToFileNameFragment(ts);
        Assert.Equal("010101234", result);
        Assert.Equal(9, result.Length);
    }

    [Fact]
    public void ToFileNameFragment_NegativeClampsToZero()
    {
        var ts = TimeSpan.FromSeconds(-10);
        var result = TimeCodeFormatter.ToFileNameFragment(ts);
        Assert.Equal("000000000", result);
    }
}
