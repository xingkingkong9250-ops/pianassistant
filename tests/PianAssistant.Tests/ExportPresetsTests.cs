using PianAssistant.Services;
using Xunit;

namespace PianAssistant.Tests;

public class ExportPresetsTests
{
    [Fact]
    public void BuildScaleFilter_Source_ReturnsNull()
    {
        Assert.Null(ExportPresets.BuildScaleFilter(ExportPresets.SourceResolution));
        Assert.Null(ExportPresets.BuildScaleFilter("SOURCE"));
    }

    [Theory]
    [InlineData("1080p", 1920)]
    [InlineData("720p", 1280)]
    [InlineData("480p", 854)]
    public void BuildScaleFilter_Preset_CapsLongEdgeAndKeepsEvenDimensions(string preset, int longEdge)
    {
        var filter = ExportPresets.BuildScaleFilter(preset);
        Assert.NotNull(filter);
        Assert.Contains($"min({longEdge},iw)", filter);
        Assert.Contains($"min({longEdge},ih)", filter);
        // 只缩不放 + 偶数对齐
        Assert.Contains("force_original_aspect_ratio=decrease", filter);
        Assert.Contains("scale=trunc(iw/2)*2:trunc(ih/2)*2", filter);
    }

    [Fact]
    public void NormalizeVideoFormat_InvalidValue_FallsBackToMp4()
    {
        Assert.Equal("mp4", ExportPresets.NormalizeVideoFormat(null));
        Assert.Equal("mp4", ExportPresets.NormalizeVideoFormat("gif"));
        Assert.Equal("mkv", ExportPresets.NormalizeVideoFormat(" MKV "));
    }

    [Fact]
    public void NormalizeResolution_InvalidValue_FallsBackTo1080p()
    {
        Assert.Equal("1080p", ExportPresets.NormalizeResolution(null));
        Assert.Equal("1080p", ExportPresets.NormalizeResolution("2160p"));
        Assert.Equal("source", ExportPresets.NormalizeResolution(" Source "));
    }

    [Fact]
    public void EnsureValid_RejectsUnknownValues()
    {
        Assert.Throws<ArgumentException>(() => ExportPresets.EnsureValidVideoFormat("avi"));
        Assert.Throws<ArgumentException>(() => ExportPresets.EnsureValidResolution("4k"));
    }
}
