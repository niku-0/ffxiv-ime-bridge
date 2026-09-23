using System.Runtime.InteropServices;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;

namespace FfxivImeBridge.Capture;

/// <summary>
/// Sees every posted window message before the game's window procedure, by
/// hooking the game's <c>user32!DispatchMessageW</c> import. Dalamud uses the
/// same import to find the game window, so the chain is game → this plugin →
/// Dalamud → the real <c>DispatchMessageW</c> → Dalamud's WndProc (ImGui) → the
/// game's WndProc; Dalamud owns the hook's lifetime, so an unload restores the
/// import even if this object is never disposed. Keyboard input is always
/// posted, so <c>WM_KEYDOWN</c>, the <c>WM_CHAR</c> that <c>TranslateMessage</c>
/// queued right behind it, and <c>WM_KEYUP</c> all come through here, one call
/// each, on the game's main thread. Mouse buttons and the wheel are posted
/// too and go to the mouse filter (ticket 16); moves are never filtered.
/// </summary>
internal sealed unsafe class MessagePumpHook : IDisposable
{
    /// <summary>Called for each keyboard message; return <see langword="true"/> to drop it (the game never sees it).</summary>
    public delegate bool MessageFilter(KeyMessage message);

    /// <summary>Called for each button or wheel message; return <see langword="true"/> to drop it.</summary>
    public delegate bool MouseFilter(MouseMessage message);

    private readonly Hook<DispatchMessageWDelegate> hook;
    private readonly MessageFilter keyboard;
    private readonly MouseFilter mouse;

    public MessagePumpHook(IGameInteropProvider interop, MessageFilter keyboard, MouseFilter mouse)
    {
        this.keyboard = keyboard;
        this.mouse = mouse;
        hook = interop.HookFromImport<DispatchMessageWDelegate>(null, "user32.dll", "DispatchMessageW", 0, Detour);
        hook.Enable();
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate nint DispatchMessageWDelegate(Msg* msg);

    private nint Detour(Msg* msg)
    {
        if (msg != null && Filtered(msg)) return 0;
        return hook.Original(msg);
    }

    private bool Filtered(Msg* msg)
    {
        if (WindowMessage.IsKeyboard(msg->Message)) return keyboard(new KeyMessage(msg->Message, msg->WParam, msg->LParam));
        if (MouseWindowMessage.IsFiltered(msg->Message)) return mouse(new MouseMessage(msg->Message, msg->WParam, ClientPoint(msg)));
        return false;
    }

    /// <summary>A button message's point is already in client pixels; <c>WM_MOUSEWHEEL</c>'s is in screen pixels and is converted for the window it was posted to.</summary>
    private static System.Numerics.Vector2 ClientPoint(Msg* msg)
    {
        if (msg->Message != MouseWindowMessage.MouseWheel) return MouseMessage.PointOf(msg->LParam);
        var point = new Point { X = (short)(msg->LParam & 0xFFFF), Y = (short)((msg->LParam >> 16) & 0xFFFF) };
        ScreenToClient(msg->Hwnd, &point);
        return new System.Numerics.Vector2(point.X, point.Y);
    }

    [DllImport("user32.dll")]
    private static extern bool ScreenToClient(nint hwnd, Point* point);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    public void Dispose() => hook.Dispose();

    /// <summary>Win32 <c>MSG</c>; only the leading fields are read.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public nint Hwnd;
        public uint Message;
        public nuint WParam;
        public nint LParam;
        public uint Time;
        public int PtX;
        public int PtY;
    }
}
