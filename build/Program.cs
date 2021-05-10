using System.IO;
using static Bullseye.Targets;
using static SimpleExec.Command;

namespace Build
{
    internal static class Program
    {
        private const string PackOutput = "./artifacts";
        private const string Solution = "Serilog.Sinks.Grafana.Loki.sln";

        internal static void Main(string[] args)
        {
            Target(Targets.CleanBuildOutput, () => { Run("dotnet", $"clean {Solution} -c Release -v m --nologo"); });

            Target(Targets.Build, DependsOn(Targets.CleanBuildOutput), () =>
            {
                Run("dotnet", $"build {Solution} -c Release --nologo");
            });

            Target(Targets.Test, DependsOn(Targets.Build), () =>
            {
                Run("dotnet", $"test {Solution} -c Release --no-build --nologo");
            });

            Target(Targets.CleanPackOutput, () =>
            {
                if (Directory.Exists(PackOutput))
                {
                    Directory.Delete(PackOutput, true);
                }
            });

            Target(Targets.Pack, DependsOn(Targets.CleanPackOutput), () =>
            {
                Run("dotnet", $"pack ./src/Serilog.Sinks.Grafana.Loki/Serilog.Sinks.Grafana.Loki.csproj -c Release -o {Directory.CreateDirectory(PackOutput).FullName} --no-build --nologo");
            });

            Target("default", DependsOn(Targets.Test, Targets.Pack));

            RunTargetsAndExit(args);
        }
    }
}