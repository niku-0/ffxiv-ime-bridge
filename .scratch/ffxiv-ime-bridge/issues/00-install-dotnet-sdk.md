# Install the .NET 10 SDK on the host

Status: done
Type: task

Dalamud 15 plugins target `net10.0-windows`; the Linux .NET 10 SDK builds
them fine against `~/.xlcore/dalamud/Hooks/dev/`. Nothing in `01`+ can
build until `dotnet` is on PATH.

```
sudo pacman -S dotnet-sdk   # Arch-based; provides dotnet-sdk 10
dotnet --list-sdks
```

## Comments
