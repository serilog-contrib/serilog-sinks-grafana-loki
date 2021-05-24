using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Serilog.Sinks.Grafana.Loki.Utils
{
    [SuppressMessage("ReSharper", "InconsistentNaming", Justification = "By design.")]
    internal static class Encoding
    {
        public static readonly System.Text.Encoding UTF8WithoutBom = new UTF8Encoding(false);
    }
}