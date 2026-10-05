using System.Net;
using AkReporting.Desktop;

namespace AkReporting.Tests;

public sealed class DesktopRecoveryTests
{
    private sealed class EditorState
    {
        public Guid ReportId = Guid.NewGuid();
        public int Revision = 2;
        public int CaseOffset;
        public string RawInput = "unfinished decimal: 3.";
        public string Reason = "Unsaved synthetic correction";
        public object Preview = new();
    }

    [Theory]
    [InlineData("disconnect")]
    [InlineData("timeout")]
    [InlineData("invalid pinned template")]
    public async Task FailedDestinationLoadKeepsUnparsedEditorAndRestoresVisibleSelection(string failure)
    {
        var editor = new EditorState();
        var navigation = new WorkspaceNavigation();
        var original = editor;
        var visibleSelection = Guid.NewGuid(); // The list selection has moved, but the editor has not.
        var prompts = 0;
        Exception error = failure switch
        {
            "disconnect" => new HttpRequestException("Synthetic host outage", null, HttpStatusCode.ServiceUnavailable),
            "timeout" => new TaskCanceledException("Synthetic timeout"),
            _ => new InvalidOperationException("Synthetic pinned template unavailable")
        };

        var actual = await Record.ExceptionAsync(() => navigation.TryReplace(
            () => Task.FromException<EditorState>(error),
            () => { prompts++; return true; },
            destination => editor = destination,
            () => visibleSelection = editor.ReportId));

        Assert.Same(error, actual);
        Assert.Same(original, editor);
        Assert.Equal("unfinished decimal: 3.", editor.RawInput);
        Assert.Equal("Unsaved synthetic correction", editor.Reason);
        Assert.Same(original.Preview, editor.Preview);
        Assert.Equal(2, editor.Revision);
        Assert.Equal(original.ReportId, visibleSelection);
        Assert.Equal(0, prompts);
    }

    [Fact]
    public async Task PendingLoadRetainsEditorAndDeclinedReplacementRetainsRevisionContext()
    {
        var editor = new EditorState();
        var transitions = new WorkspaceNavigation();
        var original = editor;
        var destination = new EditorState { Revision = 5, RawInput = "saved synthetic result", Reason = "" };
        var response = new TaskCompletionSource<EditorState>(TaskCreationOptions.RunContinuationsAsynchronously);
        var prompts = 0;
        var visibleSelection = destination.ReportId;

        var navigation = transitions.TryReplace(() => response.Task,
            () => { prompts++; return false; }, value => editor = value,
            () => visibleSelection = editor.ReportId);

        Assert.False(navigation.IsCompleted);
        Assert.Same(original, editor);
        Assert.Equal(0, prompts);
        response.SetResult(destination);

        Assert.False(await navigation);
        Assert.Same(original, editor);
        Assert.Equal(original.ReportId, visibleSelection);
        Assert.Equal(2, editor.Revision);
        Assert.Equal(1, prompts);
    }

    [Fact]
    public async Task FailedCasePageDoesNotAdvanceOffsetAndRetryLoadsTheSamePage()
    {
        var editor = new EditorState { CaseOffset = 100 };
        var navigation = new WorkspaceNavigation();
        var requestedOffsets = new List<int>();
        var unavailable = true;
        var replacements = 0;

        Task<bool> Older() => navigation.TryReplace(async () =>
        {
            var offset = editor.CaseOffset + 100;
            requestedOffsets.Add(offset);
            await Task.Yield();
            if (unavailable) throw new HttpRequestException("Synthetic disconnected host");
            return offset;
        }, () => true, offset => { editor = new EditorState { CaseOffset = offset }; replacements++; }, () => { });

        await Assert.ThrowsAsync<HttpRequestException>(() => Older());
        Assert.Equal(100, editor.CaseOffset);
        Assert.Equal(0, replacements);
        unavailable = false;
        Assert.True(await Older());
        Assert.Equal(new[] { 200, 200 }, requestedOffsets);
        Assert.Equal(200, editor.CaseOffset);
        Assert.Equal(1, replacements);
    }

    [Fact]
    public async Task LatestReloadUsesFetchedRevisionOnlyAfterConfirmationAndNeverMergesRawInput()
    {
        var editor = new EditorState();
        var navigation = new WorkspaceNavigation();
        var previous = editor;
        var latest = new EditorState { ReportId = editor.ReportId, Revision = 7, RawInput = "saved synthetic value", Reason = "" };
        var confirmations = 0;
        var restored = false;

        Assert.True(await navigation.TryReplace(() => Task.FromResult(latest), () =>
        {
            Assert.Same(previous, editor);
            confirmations++;
            return true;
        }, value => editor = value, () => restored = true));

        Assert.Same(latest, editor);
        Assert.Equal(7, editor.Revision);
        Assert.Equal("saved synthetic value", editor.RawInput);
        Assert.Equal("", editor.Reason);
        Assert.Equal("unfinished decimal: 3.", previous.RawInput);
        Assert.Equal(1, confirmations);
        Assert.False(restored);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SessionLockDuringFetchOrConfirmationCannotRepopulateProtectedState(bool lockDuringConfirmation)
    {
        var navigation = new WorkspaceNavigation();
        EditorState? editor = new();
        Guid? selection = editor.ReportId;
        var response = new TaskCompletionSource<EditorState>(TaskCreationOptions.RunContinuationsAsynchronously);
        var prompts = 0;
        var restores = 0;
        void LockSession() { navigation.Invalidate(); editor = null; selection = null; }

        var pending = navigation.TryReplace(() => response.Task, () =>
        {
            prompts++;
            if (lockDuringConfirmation) LockSession();
            return true;
        }, value => { editor = value; selection = value.ReportId; }, () => restores++);

        if (!lockDuringConfirmation) LockSession();
        response.SetResult(new EditorState());
        Assert.False(await pending);
        Assert.Null(editor);
        Assert.Null(selection);
        Assert.Equal(lockDuringConfirmation ? 1 : 0, prompts);
        Assert.Equal(0, restores);
    }
}
