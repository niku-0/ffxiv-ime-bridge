using System.Xml.Linq;
using Xunit;
using Xunit.Abstractions;

namespace FfxivImeBridge.Fcitx.Tests;

/// <summary>
/// Runs against the real fcitx5 on the developer's session bus. Switches the
/// global input method to Mozc for the duration of a test and restores it.
/// </summary>
public sealed class InputContextTests(ITestOutputHelper output) : IAsyncLifetime
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private FcitxConnection connection = null!;

    public async Task InitializeAsync() => connection = await FcitxConnection.ConnectAsync(cancellationToken: Cts().Token);

    public Task DisposeAsync()
    {
        connection.Dispose();
        return Task.CompletedTask;
    }

    private static CancellationTokenSource Cts() => new(Timeout);

    [FcitxFact]
    public async Task Creates_a_context_whose_interface_matches_what_the_library_assumes()
    {
        await using var context = await connection.CreateInputContextAsync("ffxiv-ime-bridge-test", cancellationToken: Cts().Token);

        Assert.StartsWith("/org/freedesktop/portal/inputcontext/", context.Path);
        Assert.Equal(16, context.Uuid.Length);

        var xml = XDocument.Parse(await connection.IntrospectAsync(context.Path, Cts().Token));
        var iface = xml.Descendants("interface").Single(i => (string?)i.Attribute("name") == "org.fcitx.Fcitx.InputContext1");
        string Signature(string kind, string name) => string.Concat(
            iface.Elements(kind).Single(e => (string?)e.Attribute("name") == name)
                 .Elements("arg").Where(a => (string?)a.Attribute("direction") != "out").Select(a => (string)a.Attribute("type")!));

        Assert.Equal("a(si)ia(si)a(si)a(ss)iibb", Signature("signal", "UpdateClientSideUI"));
        Assert.Equal("s", Signature("signal", "CommitString"));
        Assert.Equal("uub", Signature("signal", "ForwardKey"));
        Assert.Equal("sss", Signature("signal", "CurrentIM"));
        Assert.Equal("iu", Signature("signal", "DeleteSurroundingText"));
        Assert.Equal("uuubu", Signature("method", "ProcessKeyEvent"));
        Assert.Equal("t", Signature("method", "SetCapability"));
        Assert.Equal("i", Signature("method", "SelectCandidate"));
    }

    [FcitxFact]
    public async Task Reports_version_and_current_input_method()
    {
        var version = await connection.GetVersionAsync(Cts().Token);
        // Controller1.CurrentInputMethod describes the *focused* context; with none focused it is empty.
        var unfocused = await connection.GetCurrentInputMethodAsync(Cts().Token);
        await using var context = await connection.CreateInputContextAsync("ffxiv-ime-bridge-test", cancellationToken: Cts().Token);
        await context.FocusInAsync(Cts().Token);
        var focused = await connection.GetCurrentInputMethodAsync(Cts().Token);
        output.WriteLine($"InputMethod1.Version = {version}, CurrentInputMethod unfocused = '{unfocused}', focused = '{focused}'");
        Assert.True(version >= 1);
        Assert.NotEmpty(focused);
        Assert.Equal(focused, (await WaitFor(() => context.CurrentInputMethod)).UniqueName);
        await context.FocusOutAsync(Cts().Token);
    }

    [FcitxFact]
    public async Task Composes_and_commits_through_mozc()
    {
        await using var session = await MozcSession.OpenAsync(connection);
        var context = session.Context;

        var committed = new List<string>();
        context.Committed += committed.Add;
        Assert.Equal("mozc", context.CurrentInputMethod?.UniqueName);

        // "ka" in romaji becomes か (Mozc shows the pending "k" full-width and underlined);
        // the preedit is already updated when the key call returns.
        Assert.True(await Tap(context, KeyEvent.Char('k')));
        Assert.Equal("ｋ", context.State.Preedit.Text);
        Assert.Equal(TextFormat.Underline, context.State.Preedit.Segments.Single().Format);
        Assert.True(await Tap(context, KeyEvent.Char('a')));
        Assert.Equal("か", context.State.Preedit.Text);
        Assert.Equal(3, context.State.Preedit.CursorByteOffset); // fcitx5 counts UTF-8 bytes
        Assert.Equal(1, context.State.Preedit.CursorIndex);
        Assert.True(context.State.IsComposing);
        output.WriteLine($"aux: {string.Concat(context.State.AuxUp.Select(a => a.Text))}; predictions: {string.Join(" | ", context.State.Candidates.Select(c => c.Text))}");

        // First Space converts (highlighted segment); second Space opens the candidate list.
        Assert.True(await Tap(context, KeyEvent.Press(KeySym.Space, KeyCode.Space)));
        Assert.Contains(context.State.Preedit.Segments, s => s.Format.HasFlag(TextFormat.Highlight));
        Assert.True(await Tap(context, KeyEvent.Press(KeySym.Space, KeyCode.Space)));
        var withCandidates = context.State;
        output.WriteLine($"candidates: {string.Join(" | ", withCandidates.Candidates.Select(c => $"{c.Label}{c.Text}"))} layout={withCandidates.Layout} selected={withCandidates.SelectedCandidate} prev={withCandidates.HasPreviousPage} next={withCandidates.HasNextPage}");
        Assert.NotEmpty(withCandidates.Candidates);
        Assert.InRange(withCandidates.SelectedCandidate, 0, withCandidates.Candidates.Length - 1);
        Assert.Equal(CandidateLayout.Vertical, withCandidates.Layout);
        // Mozc appends its annotation to the candidate text ("か [ひらがな]"); only the word is committed.
        var selectedText = withCandidates.Candidates[withCandidates.SelectedCandidate].Text;

        // Enter commits the selected candidate, empties the preedit and dismisses the candidates.
        Assert.True(await Tap(context, KeyEvent.Press(KeySym.Return, KeyCode.Enter)));
        var text = Assert.Single(committed);
        output.WriteLine($"committed: {text}");
        Assert.StartsWith(text, selectedText);
        Assert.False(context.State.IsComposing);
        Assert.Empty(context.State.Candidates);

        // With nothing composed, Enter is not fcitx5's to handle: this is what lets the game send the message.
        Assert.False(await Tap(context, KeyEvent.Press(KeySym.Return, KeyCode.Enter)));
        Assert.Single(committed);
    }

    [FcitxFact]
    public async Task Escape_cancels_a_composition_without_committing()
    {
        await using var session = await MozcSession.OpenAsync(connection);
        var context = session.Context;
        var committed = new List<string>();
        context.Committed += committed.Add;

        await Tap(context, KeyEvent.Char('n'));
        await Tap(context, KeyEvent.Char('i'));
        Assert.Equal("に", context.State.Preedit.Text);

        Assert.True(await Tap(context, KeyEvent.Press(KeySym.Escape, KeyCode.Esc)));
        Assert.False(context.State.IsComposing);
        Assert.Empty(committed);
    }

    [FcitxFact]
    public async Task Focus_out_commits_the_composition_but_reset_discards_it()
    {
        await using var session = await MozcSession.OpenAsync(connection);
        var context = session.Context;
        var committed = new List<string>();
        context.Committed += committed.Add;

        // Mozc commits whatever is composed when focus leaves, ClientUnfocusCommit or not...
        await Tap(context, KeyEvent.Char('a'));
        Assert.Equal("あ", context.State.Preedit.Text);
        await context.FocusOutAsync(Cts().Token);
        await WaitFor(() => context.State.IsComposing ? null : context.State);
        Assert.Equal(["あ"], committed);

        // ...so a client that wants "focus loss cancels" must Reset before FocusOut.
        await context.FocusInAsync(Cts().Token);
        await Tap(context, KeyEvent.Char('i'));
        Assert.Equal("い", context.State.Preedit.Text);
        await context.ResetAsync(Cts().Token);
        await WaitFor(() => context.State.IsComposing ? null : context.State);
        await context.FocusOutAsync(Cts().Token);
        Assert.Equal(["あ"], committed);
        await context.FocusInAsync(Cts().Token); // MozcSession restores the IM and focuses out again
    }

    [FcitxFact]
    public async Task Dispose_destroys_the_context_on_the_bus()
    {
        var context = await connection.CreateInputContextAsync("ffxiv-ime-bridge-test", cancellationToken: Cts().Token);
        var path = context.Path;
        Assert.Contains("org.fcitx.Fcitx.InputContext1", await connection.IntrospectAsync(path, Cts().Token));

        await context.DisposeAsync();

        var error = await Assert.ThrowsAsync<Tmds.DBus.Protocol.DBusErrorReplyException>(() => connection.IntrospectAsync(path, Cts().Token));
        Assert.Contains("UnknownObject", error.Message);
        await context.DisposeAsync(); // idempotent
    }

    [FcitxFact]
    public async Task Key_releases_are_never_handled_and_a_zero_keycode_composes_nothing()
    {
        await using var session = await MozcSession.OpenAsync(connection);
        var context = session.Context;

        Assert.True(await context.ProcessKeyEventAsync(KeyEvent.Char('a'), Cts().Token));
        Assert.False(await context.ProcessKeyEventAsync(KeyEvent.Char('a').AsRelease(), Cts().Token));
        Assert.Equal("あ", context.State.Preedit.Text);
        await Tap(context, KeyEvent.Press(KeySym.Escape, KeyCode.Esc));

        // Documented Mozc quirk: without a keycode the key is swallowed but nothing is composed.
        Assert.True(await context.ProcessKeyEventAsync(new KeyEvent(KeySym.FromChar('a'), KeyCode: 0), Cts().Token));
        Assert.False(context.State.IsComposing);
    }

    /// <summary>Press and release, returning whether the press was handled.</summary>
    private static async Task<bool> Tap(InputContext context, KeyEvent press)
    {
        var handled = await context.ProcessKeyEventAsync(press, Cts().Token);
        await context.ProcessKeyEventAsync(press.AsRelease(), Cts().Token);
        return handled;
    }

    private static async Task<T> WaitFor<T>(Func<T?> probe) where T : class
    {
        using var cts = Cts();
        while (true)
        {
            if (probe() is { } value) return value;
            cts.Token.ThrowIfCancellationRequested();
            await Task.Delay(10, cts.Token);
        }
    }

    /// <summary>
    /// A focused context with Mozc active. fcitx5 keeps input-method state per
    /// context by default (ShareInputState=No), so <c>SetCurrentIM</c> applies to
    /// the focused context; the previous method is restored before focus-out in
    /// case the user shares state across programs.
    /// </summary>
    private sealed class MozcSession(FcitxConnection connection, InputContext context, string previous) : IAsyncDisposable
    {
        public InputContext Context => context;

        public static async Task<MozcSession> OpenAsync(FcitxConnection connection)
        {
            var context = await connection.CreateInputContextAsync("ffxiv-ime-bridge-test", cancellationToken: Cts().Token);
            await context.FocusInAsync(Cts().Token);
            var previous = (await WaitFor(() => context.CurrentInputMethod)).UniqueName;
            if (previous != "mozc")
            {
                await connection.SetCurrentInputMethodAsync("mozc", Cts().Token);
                await WaitFor(() => context.CurrentInputMethod?.UniqueName == "mozc" ? context.CurrentInputMethod : null);
            }
            return new MozcSession(connection, context, previous);
        }

        public async ValueTask DisposeAsync()
        {
            if (previous != "mozc") await connection.SetCurrentInputMethodAsync(previous, Cts().Token);
            await context.FocusOutAsync(Cts().Token);
            await context.DisposeAsync();
        }
    }
}
