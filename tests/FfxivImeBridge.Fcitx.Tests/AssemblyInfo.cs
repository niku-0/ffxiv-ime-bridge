using Xunit;

// Every test class talks to the one fcitx5 on the session bus and focuses a
// context of its own; focus is global, so classes must not run concurrently.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
