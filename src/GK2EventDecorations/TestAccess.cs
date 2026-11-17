// The mod's in-game tests (tools/GK2QOLMods.Tests, a developer's plugin that is never shipped) read the mod's internal
// types: the events, the Halloween settings, the status. The mod knows the tests only by this name; without that
// plugin in the game nothing uses it.
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("GK2QOLMods.Tests")]
