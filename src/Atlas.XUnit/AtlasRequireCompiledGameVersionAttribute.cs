namespace Atlas.XUnit;

/// <summary>Makes a scenario assembly refuse to boot on an install whose game version is not the
/// one it was compiled against: <c>[assembly: AtlasRequireCompiledGameVersion]</c>. The boot then
/// fails before the server starts, with an <see cref="Atlas.Api.AtlasSetupException"/> that names
/// both versions and the install it found, instead of running a build against another game and
/// failing later, in some other way, for a reason the failure does not name.</summary>
/// <remarks><para>Without it nothing fails. The first boot of a process prints one line to stderr,
/// next to the <c>[Atlas] staged mod</c> lines (a later boot of the same process does not repeat
/// it), naming the game version of the install the server runs on and the version the scenarios
/// were compiled against: <c>[Atlas] game 1.22.3 from '/opt/vs/1.22.3' (scenarios compiled
/// against 1.22.7)</c>. The attribute is the opt-in for
/// failing on a difference, because running a build on another install is also a feature: a suite
/// built once against the newest game runs on older ones without a rebuild (issue #49, the version
/// compatibility recipe), and a run like that must keep working. There is no environment variable
/// for it on purpose: a variable does not ship with the project and every shell forgets it.</para>
/// <para>The compiled version is the one the build stamps into the assembly
/// (<c>build/Atlas.E2E.targets</c>, imported by the <c>Pixnop.Atlas.XUnit</c> package) as
/// <c>[assembly: AssemblyMetadata("Atlas.CompiledGameVersion", ...)]</c>, taken from the
/// referenced <c>VintagestoryAPI.dll</c> when the project compiles. A project gets it when it is C#
/// and references <c>VintagestoryAPI</c> itself, without an <c>Aliases</c> metadata other than
/// <c>global</c> on the reference (the stamp is a <c>global::</c> reference, which an extern-aliased
/// assembly does not resolve); an assembly that carries none cannot be checked, so with this
/// attribute it fails the boot too, saying so. Versions are compared as the strings the game reports
/// (<c>GameVersion.ShortGameVersion</c>), exactly: <c>1.22.3</c> and <c>1.22.7</c> differ.</para>
/// <para>What it cannot see: a server fork rebuilt at the same game version reports the same
/// version as vanilla, so the line and this check cannot tell them apart. They compare game
/// versions, not builds.</para></remarks>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class AtlasRequireCompiledGameVersionAttribute : Attribute
{
}
