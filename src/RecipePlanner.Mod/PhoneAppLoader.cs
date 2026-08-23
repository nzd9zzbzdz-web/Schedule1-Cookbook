using System;
using System.IO;
using System.Reflection;
using RecipePlanner.Game.Binding;

namespace RecipePlanner.Mod
{
    /// <summary>
    /// Reaches the phone UI without linking it.
    ///
    /// <c>RecipePlanner.PhoneApp</c> references Assembly-CSharp directly, which exists only on the
    /// Mono ("alternate") branch. If this assembly named it at compile time, the reference would be
    /// baked into RecipePlanner.dll's metadata and the JIT would try to resolve it on the default
    /// IL2CPP branch — taking down the whole mod, tracking included, before a single hook ran.
    ///
    /// So the UI is loaded by name at runtime instead, and its absence is a logged line rather than
    /// a crash. Nothing in this file may reference a PhoneApp type. See docs/05-RELEASE-ROADMAP.md R1.
    /// </summary>
    internal sealed class PhoneAppLoader
    {
        private const string AssemblyFile = "RecipePlanner.PhoneApp.dll";
        private const string InstallerType = "RecipePlanner.PhoneApp.CookbookAppInstaller";
        private const string InstallMethod = "TryInstall";

        private readonly ILog _log;

        /// <summary>Install throws tolerated before the UI is written off for this session.</summary>
        private const int MaxInstallFailures = 5;

        private MethodInfo _tryInstall;
        private bool _resolved;
        private bool _givenUp;
        private int _failures;

        public PhoneAppLoader(ILog log)
        {
            _log = log ?? NullLog.Instance;
        }

        /// <summary>
        /// Whether the UI is even worth attempting. Reported once at startup so a player on the
        /// default branch is told why there is no Cookbook app, instead of assuming it broke.
        /// </summary>
        public bool Available => !_givenUp;

        /// <summary>
        /// Attempts to install the app. Safe to call repeatedly — the mod polls until a save is
        /// loaded and the phone exists — and stops trying for good once it is clear it cannot work.
        /// </summary>
        public bool TryInstall()
        {
            if (_givenUp) return false;

            if (!_resolved)
            {
                _resolved = true;
                _tryInstall = Resolve();
                if (_tryInstall == null) { _givenUp = true; return false; }
            }

            try
            {
                return _tryInstall.Invoke(null, null) is bool ok && ok;
            }
            catch (Exception ex)
            {
                // Unwrap: reflection reports the callee's failure as the inner exception, and the
                // outer TargetInvocationException says nothing useful.
                var real = (ex as TargetInvocationException)?.InnerException ?? ex;
                _failures++;

                // Bounded rather than permanent. Installing depends on the phone's own objects
                // existing, so a throw can mean "the UI genuinely cannot build" or merely "asked
                // during a bad moment of a scene change". Giving up forever on the first one would
                // cost the player the app for the rest of the session — including on every save
                // they load afterwards — which is far worse than a few extra log lines.
                if (_failures >= MaxInstallFailures)
                {
                    _givenUp = true;
                    _log.Error($"Cookbook app failed to install {_failures} times; giving up for this " +
                               "session. Production tracking is unaffected. " + real);
                }
                else
                {
                    _log.Warn($"Cookbook app install attempt {_failures} failed; will retry. " + real.Message);
                }
                return false;
            }
        }

        /// <summary>
        /// Loads the UI assembly from beside this one. <c>Assembly.LoadFrom</c> rather than
        /// <c>Assembly.Load</c> because MelonLoader's Mods folder is not on the probing path.
        /// </summary>
        private MethodInfo Resolve()
        {
            try
            {
                var here = OwnDirectory();
                if (here == null)
                {
                    _log.Warn("Could not work out where the mod is installed, so the Cookbook app " +
                              "cannot be located. Production tracking is unaffected.");
                    return null;
                }

                var path = Path.Combine(here, AssemblyFile);

                if (!File.Exists(path))
                {
                    _log.Warn($"{AssemblyFile} is not next to the mod, so there will be no Cookbook " +
                              "app. Production tracking is unaffected.");
                    return null;
                }

                var type = Assembly.LoadFrom(path).GetType(InstallerType, throwOnError: false);
                if (type == null)
                {
                    _log.Warn($"{AssemblyFile} loaded but {InstallerType} was not in it — the file is " +
                              "probably from a different version of the mod.");
                    return null;
                }

                var method = type.GetMethod(InstallMethod, BindingFlags.Public | BindingFlags.Static,
                                            null, Type.EmptyTypes, null);
                if (method == null)
                    _log.Warn($"{InstallerType}.{InstallMethod}() was not found.");

                return method;
            }
            catch (Exception ex)
            {
                // The expected case on the IL2CPP branch: the assembly is present but its own
                // Assembly-CSharp reference cannot be satisfied. Not an error — an unsupported
                // configuration, and one the player was already told about at startup.
                _log.Warn("Cookbook app unavailable on this branch; production tracking is running " +
                          "normally. " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// The folder this assembly was loaded from — MelonLoader's <c>Mods\</c>.
        ///
        /// <c>Location</c> first because <c>CodeBase</c> is obsolete on .NET 5+ and throws outright
        /// for assemblies loaded from bytes; <c>CodeBase</c> second because it is the one that works
        /// on the older Mono host. Either can come back empty, so both are checked.
        /// </summary>
        private static string OwnDirectory()
        {
            var self = typeof(PhoneAppLoader).Assembly;

            try
            {
                var location = self.Location;
                if (!string.IsNullOrEmpty(location)) return Path.GetDirectoryName(location);
            }
            catch { /* fall through to CodeBase */ }

            try
            {
#pragma warning disable SYSLIB0012
                var codeBase = self.CodeBase;
#pragma warning restore SYSLIB0012
                if (!string.IsNullOrEmpty(codeBase))
                    return Path.GetDirectoryName(new Uri(codeBase).LocalPath);
            }
            catch { /* neither worked */ }

            return null;
        }
    }
}
