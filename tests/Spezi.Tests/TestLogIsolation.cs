using System.Runtime.CompilerServices;

namespace Spezi.Tests;

static class TestLogIsolation
{
    // Unit tests must never write to the user's %LocalAppData%\Spezi\spezi.log.
    [ModuleInitializer]
    internal static void Init() =>
        Environment.SetEnvironmentVariable("SPEZI_LOG_DIR", Path.Combine(Path.GetTempPath(), "SpeziUnitTests"));
}
