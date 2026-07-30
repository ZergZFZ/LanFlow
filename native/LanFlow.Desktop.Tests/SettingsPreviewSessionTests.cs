using LanFlow.Desktop.Models;
using LanFlow.Desktop.Presentation;
using Xunit;

namespace LanFlow.Desktop.Tests;

public sealed class SettingsPreviewSessionTests
{
    [Fact]
    public void Update_ChangesWorkingCopyAndRaisesPreviewWithoutMutatingOriginalInput()
    {
        var source = new Settings { LayeredOpacity = 0.85 };
        var session = new SettingsPreviewSession(source);
        Settings? preview = null;
        session.PreviewRequested += (_, value) => preview = value;

        session.Update(settings => settings.LayeredOpacity = 0.62);

        Assert.Equal(0.85, source.LayeredOpacity, 3);
        Assert.Equal(0.62, session.Working.LayeredOpacity, 3);
        Assert.Equal(0.62, Assert.IsType<Settings>(preview).LayeredOpacity, 3);
        Assert.True(session.HasChanges);
    }

    [Theory]
    [InlineData(SettingsOptionValues.TransparencyLayered)]
    [InlineData(SettingsOptionValues.TransparencyWholeWindow)]
    public void LegacyOpacityChange_UpdatesActiveModeAndCompatibilityValue(string transparencyMode)
    {
        var settings = new Settings
        {
            TransparencyMode = transparencyMode,
            LayeredOpacity = 0.85,
            WholeWindowOpacity = 0.75,
            Opacity = 0.85,
        };

        LegacySettingsControlMapper.ApplyOpacity(settings, 0.62);

        Assert.Equal(0.62, settings.Opacity, 3);
        if (transparencyMode == SettingsOptionValues.TransparencyWholeWindow)
        {
            Assert.Equal(0.85, settings.LayeredOpacity, 3);
            Assert.Equal(0.62, settings.WholeWindowOpacity, 3);
        }
        else
        {
            Assert.Equal(0.62, settings.LayeredOpacity, 3);
            Assert.Equal(0.75, settings.WholeWindowOpacity, 3);
        }
    }

    [Theory]
    [InlineData(SettingsOptionValues.ListLayout, false, false, SettingsOptionValues.ListLayout)]
    [InlineData(SettingsOptionValues.ListLayout, true, true, SettingsOptionValues.CardLayout)]
    [InlineData(SettingsOptionValues.CardLayout, false, true, SettingsOptionValues.GridLayout)]
    public void LegacyLayoutToggle_OnlyChangesLayoutWhenExplicitlyTriggered(
        string initialLayout,
        bool cardEnabled,
        bool isExplicitLayoutChange,
        string expectedLayout)
    {
        var settings = new Settings { LayoutMode = initialLayout };

        LegacySettingsControlMapper.ApplyLayoutToggle(settings, cardEnabled, isExplicitLayoutChange);

        Assert.Equal(expectedLayout, settings.LayoutMode);
    }

    [Fact]
    public void Commit_WithAppliedSettingsUsesActualNormalizedValuesAsBaseline()
    {
        var session = new SettingsPreviewSession(new Settings { Theme = "dark", IconSize = 44, TextSize = 13 });
        session.Update(settings =>
        {
            settings.Theme = "custom";
            settings.IconSize = 96;
            settings.TextSize = 20;
        });
        var applied = session.Working.Clone();
        applied.Theme = "dark";
        applied.IconSize = 72;
        applied.TextSize = 18;

        var committed = session.Commit(applied);
        session.Update(settings => settings.TextSize = 15);
        var restored = session.Cancel();

        Assert.Equal("dark", committed.Theme);
        Assert.Equal(72, committed.IconSize);
        Assert.Equal(18, committed.TextSize);
        Assert.Equal(18, restored.TextSize);
        Assert.Equal(18, session.Working.TextSize);
        Assert.False(session.HasChanges);
    }

    [Fact]
    public void Complete_WhenApplyAndPersistThrows_DoesNotAdvanceBaseline()
    {
        var session = new SettingsPreviewSession(new Settings { TextSize = 13 });
        session.Update(settings => settings.TextSize = 20);

        Assert.Throws<InvalidOperationException>(() =>
            SettingsPreviewTransaction.Complete(
                session,
                accepted: true,
                _ => throw new InvalidOperationException("save failed")));

        Assert.Equal(13, session.Original.TextSize);
        Assert.Equal(20, session.Working.TextSize);
        Assert.True(session.HasChanges);
    }

    [Fact]
    public void Complete_WhenCancelledRestoresPreviewWithoutInvokingPersistentCallback()
    {
        var session = new SettingsPreviewSession(new Settings { StartWithWindows = false, TextSize = 13 });
        session.Update(settings =>
        {
            settings.StartWithWindows = true;
            settings.TextSize = 17;
        });
        var persistentCallbackCount = 0;

        var restored = SettingsPreviewTransaction.Complete(
            session,
            accepted: false,
            settings =>
            {
                persistentCallbackCount++;
                return settings;
            });

        Assert.Equal(0, persistentCallbackCount);
        Assert.False(restored.StartWithWindows);
        Assert.Equal(13, restored.TextSize);
        Assert.False(session.HasChanges);
    }

    [Fact]
    public void Commit_UpdatesBaselineSoLaterCancelReturnsLastAppliedSettings()
    {
        var session = new SettingsPreviewSession(new Settings { TextSize = 13 });
        session.Update(settings => settings.TextSize = 15);
        var committed = session.Commit();
        session.Update(settings => settings.TextSize = 17);

        var restored = session.Cancel();

        Assert.Equal(15, committed.TextSize);
        Assert.Equal(15, restored.TextSize);
        Assert.False(session.HasChanges);
    }

    [Fact]
    public void Cancel_RaisesPreviewForRestoredSnapshot()
    {
        var session = new SettingsPreviewSession(new Settings { GroupLabelSize = 36 });
        Settings? lastPreview = null;
        session.PreviewRequested += (_, value) => lastPreview = value;
        session.Update(settings => settings.GroupLabelSize = 48);

        session.Cancel();

        Assert.Equal(36, Assert.IsType<Settings>(lastPreview).GroupLabelSize);
    }
}
