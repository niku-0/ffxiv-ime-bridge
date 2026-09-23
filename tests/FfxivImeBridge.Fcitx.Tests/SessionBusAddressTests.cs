using Xunit;

namespace FfxivImeBridge.Fcitx.Tests;

public sealed class SessionBusAddressTests
{
    [Fact]
    public void Parses_the_unix_path_and_the_uid_of_a_systemd_user_bus()
    {
        var address = SessionBusAddress.Parse("unix:path=/run/user/1000/bus");

        Assert.Equal("/run/user/1000/bus", address.UnixPath);
        Assert.Equal(1000u, address.UnixUserId);
        Assert.Equal(@"Z:\run\user\1000\bus", address.WinePath);
    }

    [Fact]
    public void Ignores_a_guid_suffix_and_a_second_entry()
    {
        // dbus-daemon style: key=value pairs separated by commas, entries by semicolons.
        var address = SessionBusAddress.Parse("unix:path=/tmp/dbus-XyZ,guid=0123456789abcdef0123456789abcdef;tcp:host=localhost,port=1234");

        Assert.Equal("/tmp/dbus-XyZ", address.UnixPath);
        Assert.Null(address.UnixUserId); // not a /run/user/<uid>/ path: the caller has to supply the uid
    }

    [Fact]
    public void Has_no_unix_path_for_tcp_or_abstract_addresses()
    {
        Assert.Null(SessionBusAddress.Parse("tcp:host=localhost,port=1234").UnixPath);
        Assert.Null(SessionBusAddress.Parse("unix:abstract=/tmp/dbus-XyZ").UnixPath);
        Assert.Null(SessionBusAddress.Parse("unix:abstract=/tmp/dbus-XyZ").WinePath);
    }

    [Fact]
    public void Unescapes_percent_encoded_paths()
    {
        Assert.Equal("/tmp/with space", SessionBusAddress.Parse("unix:path=/tmp/with%20space").UnixPath);
    }
}
