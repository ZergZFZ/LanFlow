using LanFlow.Desktop.Models;
using LanFlow.Desktop.Presentation;
using Xunit;

namespace LanFlow.Desktop.Tests;

public sealed class SettingsCloseFlowTests
{
    [Fact]
    public void TryComplete_WithoutChangesClosesWithoutPersisting()
    {
        var session = new SettingsPreviewSession(new Settings { TextSize = 13 });
        var flushCount = 0;
        var persistCount = 0;

        var shouldClose = SettingsCloseFlow.TryComplete(
            session,
            UnsavedCloseDecision.KeepEditing,
            () => flushCount++,
            settings =>
            {
                persistCount++;
                return settings;
            });

        Assert.True(shouldClose);
        Assert.Equal(1, flushCount);
        Assert.Equal(0, persistCount);
        Assert.False(session.HasChanges);
    }

    [Fact]
    public void TryComplete_KeepEditingLeavesPreviewAndCancelsClose()
    {
        var session = ChangedSession();
        var persistCount = 0;

        var shouldClose = SettingsCloseFlow.TryComplete(
            session,
            UnsavedCloseDecision.KeepEditing,
            () => { },
            settings =>
            {
                persistCount++;
                return settings;
            });

        Assert.False(shouldClose);
        Assert.Equal(0, persistCount);
        Assert.Equal(18, session.Working.TextSize);
        Assert.True(session.HasChanges);
    }

    [Fact]
    public void TryComplete_DiscardCancelsPreviewWithoutPersisting()
    {
        var session = ChangedSession();
        var persistCount = 0;
        Settings? restoredPreview = null;
        session.PreviewRequested += (_, settings) => restoredPreview = settings;

        var shouldClose = SettingsCloseFlow.TryComplete(
            session,
            UnsavedCloseDecision.Discard,
            () => { },
            settings =>
            {
                persistCount++;
                return settings;
            });

        Assert.True(shouldClose);
        Assert.Equal(0, persistCount);
        Assert.Equal(13, session.Working.TextSize);
        Assert.Equal(13, Assert.IsType<Settings>(restoredPreview).TextSize);
        Assert.False(session.HasChanges);
    }

    [Fact]
    public void TryComplete_ApplyAndCloseFlushesPersistsOnceAndCommitsAppliedBaseline()
    {
        var session = ChangedSession();
        var flushCount = 0;
        var persistCount = 0;

        var shouldClose = SettingsCloseFlow.TryComplete(
            session,
            UnsavedCloseDecision.ApplyAndClose,
            () => flushCount++,
            settings =>
            {
                persistCount++;
                settings.TextSize = 17;
                return settings;
            });

        Assert.True(shouldClose);
        Assert.Equal(1, flushCount);
        Assert.Equal(1, persistCount);
        Assert.Equal(17, session.Original.TextSize);
        Assert.Equal(17, session.Working.TextSize);
        Assert.False(session.HasChanges);
    }

    [Fact]
    public void TryComplete_WhenPersistenceFailsDoesNotAdvanceBaseline()
    {
        var session = ChangedSession();

        Assert.Throws<InvalidOperationException>(() =>
            SettingsCloseFlow.TryComplete(
                session,
                UnsavedCloseDecision.ApplyAndClose,
                () => { },
                _ => throw new InvalidOperationException("save failed")));

        Assert.Equal(13, session.Original.TextSize);
        Assert.Equal(18, session.Working.TextSize);
        Assert.True(session.HasChanges);
    }

    private static SettingsPreviewSession ChangedSession()
    {
        var session = new SettingsPreviewSession(new Settings { TextSize = 13 });
        session.Update(settings => settings.TextSize = 18);
        return session;
    }
}
