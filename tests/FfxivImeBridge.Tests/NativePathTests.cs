using FfxivImeBridge.Capture;
using Xunit;

namespace FfxivImeBridge.Tests;

public sealed class NativePathTests
{
    [Theory]
    [InlineData("@im=fcitx")]
    [InlineData("@im=ibus")]
    [InlineData("@im=fcitx@foo=bar")]
    public void An_input_method_server_takes_keys(string xmodifiers)
    {
        Assert.True(NativePath.TakesKeys(xmodifiers));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("@im=null")]
    [InlineData("@im=none")]
    [InlineData("@im=")]
    [InlineData("@foo=bar")]
    public void No_server_or_a_null_one_leaves_keys_alone(string? xmodifiers)
    {
        Assert.False(NativePath.TakesKeys(xmodifiers));
    }
}
