namespace FfxivImeBridge.Capture;

/// <summary>Reads the modifier state for the message in hand; see <see cref="Modifiers"/>.</summary>
internal interface IKeyStateReader
{
    Modifiers Read();
}
