using System;
using System.Threading;
using System.Windows;
using LanFlow.Desktop.Presentation;
using Xunit;

namespace LanFlow.Desktop.Tests;

public sealed class SettingsWindowPlacementTests
{
    private sealed class StubWorkAreaProvider : MonitorWorkAreaProvider
    {
        private readonly Rect _area;
        public StubWorkAreaProvider(Rect area) => _area = area;
        public override Rect GetWorkArea(Window forWindow) => _area;
    }

    [Fact]
    public void ComputeTopRight_PlacesAtOwnerTopRightWithMargin()
    {
        var area = new Rect(0, 0, 2000, 1000);

        var (x, y) = SettingsWindowPlacement.ComputeTopRight(
            area,
            ownerLeft: 1000,
            ownerWidth: 600,
            ownerTop: 50,
            settingsWidth: 900,
            settingsHeight: 720);

        Assert.Equal(1000 + 600 - 900 - 16, x);
        Assert.Equal(50 + 16, y);
    }

    [Theory]
    [InlineData(0, 0, 600, 400, 900, 0)]
    [InlineData(200, 0, 900, 400, 900, 100)]
    public void ComputeTopRight_ClampsInsideWorkArea(
        double ownerLeft, double ownerTop, double ownerWidth, double ownerHeight, double settingsWidth, double expectedX)
    {
        var area = new Rect(0, 0, 1000, 1000);

        var (x, _) = SettingsWindowPlacement.ComputeTopRight(
            area,
            ownerLeft,
            ownerWidth,
            ownerTop,
            settingsWidth,
            settingsHeight: ownerHeight);

        Assert.Equal(expectedX, x);
    }

    [Fact]
    public void Apply_SetsManualStartupLocationAndPositionsWindow()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var area = new Rect(0, 0, 2000, 1200);
                var placement = new SettingsWindowPlacement(new StubWorkAreaProvider(area));
                var owner = new Window { Left = 300, Top = 100, Width = 800, Height = 600 };
                var settings = new Window { Width = 900, Height = 720 };

                placement.Apply(settings, owner);

                Assert.Equal(WindowStartupLocation.Manual, settings.WindowStartupLocation);
                Assert.Equal(300 + 800 - 900 - 16, settings.Left);
                Assert.Equal(100 + 16, settings.Top);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            throw failure;
        }
    }
}
