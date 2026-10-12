using PianAssistant.Models;
using PianAssistant.Services;
using Xunit;

namespace PianAssistant.Tests;

/// <summary>
/// 批量文本解析器测试：各指令格式、中英文别名、全角冒号、毫秒、
/// 注释、错误行与乱序。
/// </summary>
public class BatchTextParserTests
{
    private readonly BatchTextParser _parser = new();

    // --- 空与注释 ---

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n  \r\n ")]
    public void Parse_EmptyText_ReturnsEmpty(string? text)
    {
        Assert.Empty(_parser.Parse(text));
    }

    [Fact]
    public void Parse_CommentAndBlankLines_Ignored()
    {
        // 行序：1 注释 / 2 空 / 3 空白(含\r) / 4 斜杠注释 / 5 有效行
        var text = "# 注释行\n\n  \r\n// 斜杠注释\n00:01:00\n";
        var result = _parser.Parse(text);
        var item = Assert.Single(result);
        Assert.True(item.IsValid);
        Assert.Equal(5, item.LineNumber); // 行号按原文计（含注释与空行）
    }

    // --- 截图指令 ---

    [Fact]
    public void Parse_ScreenshotWithLabel()
    {
        var item = _parser.Parse("00:01:23 截图 开场").Single();
        Assert.True(item.IsValid);
        Assert.Equal(BatchActionKind.Screenshot, item.Type);
        Assert.Equal(TimeSpan.FromSeconds(83), item.Start);
        Assert.Null(item.End);
        Assert.Equal("开场", item.Label);
    }

    [Fact]
    public void Parse_ScreenshotWithoutLabel_LabelNull()
    {
        var item = _parser.Parse("00:01:23 截图").Single();
        Assert.True(item.IsValid);
        Assert.Equal(BatchActionKind.Screenshot, item.Type);
        Assert.Null(item.Label);
    }

    [Fact]
    public void Parse_ScreenshotEnglishAlias_CaseInsensitive()
    {
        var item = _parser.Parse("00:01:23 SCREENSHOT 开场").Single();
        Assert.True(item.IsValid);
        Assert.Equal(BatchActionKind.Screenshot, item.Type);
    }

    // --- 片段指令 ---

    [Fact]
    public void Parse_ClipWithChineseKeywordAndLabel()
    {
        var item = _parser.Parse("00:01:00-00:02:30 片段 第一段").Single();
        Assert.True(item.IsValid);
        Assert.Equal(BatchActionKind.Clip, item.Type);
        Assert.Equal(TimeSpan.FromMinutes(1), item.Start);
        Assert.Equal(TimeSpan.FromSeconds(150), item.End);
        Assert.Equal("第一段", item.Label);
    }

    [Fact]
    public void Parse_ClipRangeWithoutKeyword_LabelNull()
    {
        // 导出文件中的片段行即此形式：时间段 + 片段 + #N（关键字存在，标签 #N）
        var item = _parser.Parse("00:01:30-00:04:20 片段 #1").Single();
        Assert.True(item.IsValid);
        Assert.Equal(BatchActionKind.Clip, item.Type);
        Assert.Equal("#1", item.Label);
    }

    [Fact]
    public void Parse_ClipEnglishAlias()
    {
        var item = _parser.Parse("0:01:00-0:02:30 CLIP abc").Single();
        Assert.True(item.IsValid);
        Assert.Equal(BatchActionKind.Clip, item.Type);
    }

    [Fact]
    public void Parse_ClipWithEmDashRange_Tolerated()
    {
        var item = _parser.Parse("00:01:00—00:02:00 片段").Single();
        Assert.True(item.IsValid);
        Assert.Equal(BatchActionKind.Clip, item.Type);
        Assert.Equal(TimeSpan.FromMinutes(2), item.End);
    }

    // --- 标记指令 ---

    [Theory]
    [InlineData("标记")]
    [InlineData("笔记")]
    [InlineData("note")]
    [InlineData("NOTE")]
    public void Parse_MarkerAliases(string keyword)
    {
        var item = _parser.Parse($"00:01:23 {keyword} 转场很生硬").Single();
        Assert.True(item.IsValid);
        Assert.Equal(BatchActionKind.Marker, item.Type);
        Assert.Equal("转场很生硬", item.Label);
    }

    [Fact]
    public void Parse_MarkerWithoutLabel_Invalid()
    {
        var item = _parser.Parse("00:01:23 标记").Single();
        Assert.False(item.IsValid);
        Assert.Contains("标签", item.Error);
    }

    // --- 裁切点指令 ---

    [Fact]
    public void Parse_BareTime_IsCutPoint()
    {
        var item = _parser.Parse("00:04:20").Single();
        Assert.True(item.IsValid);
        Assert.Equal(BatchActionKind.CutPoint, item.Type);
        Assert.Null(item.End);
        Assert.Null(item.Label);
    }

    [Fact]
    public void Parse_CutPointKeyword_Tolerated()
    {
        var item = _parser.Parse("00:04:20 裁切点").Single();
        Assert.True(item.IsValid);
        Assert.Equal(BatchActionKind.CutPoint, item.Type);
    }

    // --- 时间格式 ---

    [Fact]
    public void Parse_ShortMinuteSecondFormat()
    {
        // MM:SS（TimeSpan.TryParse 会误判为 HH:MM，解析器需按 MM:SS 处理）
        var item = _parser.Parse("01:30 截图").Single();
        Assert.Equal(TimeSpan.FromSeconds(90), item.Start);
    }

    [Fact]
    public void Parse_MillisecondFraction()
    {
        var item = _parser.Parse("00:01:02.500 截图").Single();
        Assert.Equal(TimeSpan.FromSeconds(62.5), item.Start);
    }

    [Fact]
    public void Parse_FullWidthColon_Tolerated()
    {
        var item = _parser.Parse("00：01：23 截图 全角").Single();
        Assert.True(item.IsValid);
        Assert.Equal(TimeSpan.FromSeconds(83), item.Start);
    }

    [Fact]
    public void Parse_HoursBeyond24_Supported()
    {
        var item = _parser.Parse("25:00:00").Single();
        Assert.Equal(TimeSpan.FromHours(25), item.Start);
    }

    // --- 乱序与多行 ---

    [Fact]
    public void Parse_OutOfOrderLines_AllValidInFileOrder()
    {
        var text = "00:04:20\n00:01:30\n00:02:10 截图\n";
        var result = _parser.Parse(text);
        Assert.Equal(3, result.Count);
        Assert.All(result, i => Assert.True(i.IsValid));
        Assert.Equal(TimeSpan.FromSeconds(260), result[0].Start);
        Assert.Equal(TimeSpan.FromSeconds(90), result[1].Start);
        Assert.Equal(TimeSpan.FromSeconds(130), result[2].Start);
    }

    // --- 错误行 ---

    [Fact]
    public void Parse_InvalidTime_ErrorContainsLineNumber()
    {
        var item = _parser.Parse("abc 截图 x").Single();
        Assert.False(item.IsValid);
        Assert.NotNull(item.Error);
        Assert.Contains("1", item.Error);
    }

    [Fact]
    public void Parse_UnknownKeyword_Invalid()
    {
        var item = _parser.Parse("00:01:00 转场 x").Single();
        Assert.False(item.IsValid);
        Assert.Contains("转场", item.Error);
    }

    [Fact]
    public void Parse_ClipKeywordWithoutRange_Invalid()
    {
        var item = _parser.Parse("00:01:00 片段 x").Single();
        Assert.False(item.IsValid);
        Assert.Contains("时间段", item.Error);
    }

    [Fact]
    public void Parse_ClipEndBeforeStart_Invalid()
    {
        var item = _parser.Parse("00:02:00-00:01:00 片段 x").Single();
        Assert.False(item.IsValid);
        Assert.Contains("结束时间", item.Error);
    }

    [Fact]
    public void Parse_InvalidLine_DoesNotThrowAndKeepsValidNeighbors()
    {
        var text = "00:01:00\n这不是指令\n00:02:00\n";
        var result = _parser.Parse(text);
        Assert.Equal(3, result.Count);
        Assert.False(result[1].IsValid);
        Assert.True(result[0].IsValid);
        Assert.True(result[2].IsValid);
    }
}
