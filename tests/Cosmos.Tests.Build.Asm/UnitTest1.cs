using Cosmos.Build.Asm.Tasks;
using Microsoft.Build.Framework;
using Moq;
using Xunit.Sdk;

namespace Cosmos.Build.Asm.Tests;

public class UnitTest1
{
    private Mock<IBuildEngine> buildEngine;
    private List<BuildErrorEventArgs> errors;

    public UnitTest1()
    {
        buildEngine = new Mock<IBuildEngine>();
        errors = [];
        buildEngine.Setup(x => x.LogErrorEvent(It.IsAny<BuildErrorEventArgs>()))
            .Callback<BuildErrorEventArgs>(e => errors.Add(e));
    }

    private static string GetYasmPath()
    {
        var paths = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator);
        if (paths != null)
        {
            string exeName = Environment.OSVersion.Platform == PlatformID.Win32NT ? "yasm.exe" : "yasm";
            foreach (var p in paths)
            {
                var fullPath = Path.Combine(p, exeName);
                if (File.Exists(fullPath))
                {
                    return fullPath;
                }
            }
        }
        return "yasm";
    }

    [Fact]
    public void Test1()
    {
        YasmBuildTask yasm = new()
        {
            YasmPath = GetYasmPath(),
            SourceFiles = ["./asm/test.asm"],
            OutputPath = "./output",
            BuildEngine = buildEngine.Object
        };


        bool success = yasm.Execute();

        Assert.True(success);
    }
}
