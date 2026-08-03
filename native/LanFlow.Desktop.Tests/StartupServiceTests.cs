using System.Linq;
using LanFlow.Desktop.Services;
using Microsoft.Win32;
using Xunit;

namespace LanFlow.Desktop.Tests;

public class StartupServiceTests
{
    private static (string Name, RegistryValueKind Kind, object Data)[] MakeEntries(
        params string[] names)
        => names.Select(n => (n, RegistryValueKind.String, (object)("\"" + n + "\""))).ToArray();

    private static string[] Names((string Name, RegistryValueKind Kind, object Data)[] entries)
        => entries.Select(e => e.Name).ToArray();

    [Fact]
    public void ReorderToFront_MovesTargetFirst_AndKeepsRestRelativeOrder()
    {
        var result = StartupService.ReorderToFront(
            MakeEntries("A", "B", "LanFlow", "C", "D"),
            "LanFlow");

        Assert.Equal(new[] { "LanFlow", "A", "B", "C", "D" }, Names(result));
    }

    [Fact]
    public void ReorderToFront_TargetAlreadyFirst_KeepsOrderUnchanged()
    {
        var input = MakeEntries("LanFlow", "A", "B");
        var result = StartupService.ReorderToFront(input, "LanFlow");

        Assert.Equal(new[] { "LanFlow", "A", "B" }, Names(result));
    }

    [Fact]
    public void ReorderToFront_TargetMissing_KeepsOriginalOrder()
    {
        var input = MakeEntries("A", "B", "C");
        var result = StartupService.ReorderToFront(input, "LanFlow");

        Assert.Equal(new[] { "A", "B", "C" }, Names(result));
    }

    [Fact]
    public void ReorderToFront_PreservesValueDataAndKind()
    {
        var input = new[]
        {
            ("A", RegistryValueKind.String, (object)"1"),
            ("LanFlow", RegistryValueKind.ExpandString, (object)"\"x\" --silent"),
            ("B", RegistryValueKind.DWord, (object)7),
        };

        var result = StartupService.ReorderToFront(input, "LanFlow");

        Assert.Equal(RegistryValueKind.ExpandString, result[0].Kind);
        Assert.Equal("\"x\" --silent", result[0].Data);
        Assert.Equal(RegistryValueKind.DWord, result[2].Kind);
        Assert.Equal(7, result[2].Data);
    }
}
