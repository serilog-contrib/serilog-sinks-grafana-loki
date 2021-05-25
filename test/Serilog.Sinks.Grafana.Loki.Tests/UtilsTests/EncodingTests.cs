using Serilog.Sinks.Grafana.Loki.Utils;
using Shouldly;
using Xunit;

namespace Serilog.Sinks.Grafana.Loki.Tests.UtilsTests
{
    public class EncodingTests
    {
        [Fact]
        public void Utf8WithoutBomShouldNotHasPreamble()
        {
            var encoding = Encoding.UTF8WithoutBom;

            var preamble = encoding.GetPreamble();

            preamble.Length.ShouldBe(0);
        }
    }
}