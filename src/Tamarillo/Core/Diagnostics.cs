using SwissTechKnife.Sdk;

namespace Tamarillo.Core
{
    /// <summary>Module-wide access to the host log (null in unit tests).</summary>
    internal static class Diagnostics
    {
        public static IModuleLog Log { get; set; }
    }
}
