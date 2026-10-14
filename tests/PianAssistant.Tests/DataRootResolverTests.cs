using System.IO;
using PianAssistant.Services;
using Xunit;

namespace PianAssistant.Tests;

/// <summary>
/// M7 数据根解析测试：优先级 env &gt; config.DataRoot &gt; 引导根。
/// 注：测试进程不设置 <c>PIAN_ASSISTANT_DATA_ROOT</c>，故显式参数分支可稳定断言。
/// </summary>
public class DataRootResolverTests
{
    [Fact]
    public void ResolveDataRoot_ConfigValueUsed_WhenProvided()
    {
        Assert.Equal(@"C:\custom-data",
            DataRootResolver.ResolveDataRoot(@"C:\custom-data", @"C:\bootstrap"));
    }

    [Fact]
    public void ResolveDataRoot_FallsBackToBootstrapOverride()
    {
        Assert.Equal(@"C:\bootstrap",
            DataRootResolver.ResolveDataRoot(null, @"C:\bootstrap"));
    }

    [Fact]
    public void ResolveDataRoot_WhitespaceConfig_FallsBackToBootstrap()
    {
        Assert.Equal(@"C:\bootstrap",
            DataRootResolver.ResolveDataRoot("   ", @"C:\bootstrap"));
    }

    [Fact]
    public void ResolveDataRoot_NoArgs_EqualsBootstrapRoot()
    {
        // 无 env/无参时，两者解析同一默认值，故恒等（不依赖具体机器路径）。
        Assert.Equal(DataRootResolver.BootstrapRoot, DataRootResolver.ResolveDataRoot());
    }

    [Fact]
    public void ConfigPath_IsUnderBootstrapRoot()
    {
        Assert.Equal(
            Path.Combine(DataRootResolver.BootstrapRoot, "config.json"),
            DataRootResolver.ConfigPath);
    }

    [Fact]
    public void DataRootSubPaths_HaveExpectedLeafNames()
    {
        Assert.EndsWith("pianassistant.db", DataRootResolver.DatabasePath);
        Assert.EndsWith("screenshots", DataRootResolver.ScreenshotsDir);
        Assert.EndsWith("clips", DataRootResolver.ClipsDir);
        Assert.EndsWith("logs", DataRootResolver.LogsDir);
    }
}
