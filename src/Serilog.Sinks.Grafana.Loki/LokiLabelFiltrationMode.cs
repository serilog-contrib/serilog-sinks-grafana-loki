namespace Serilog.Sinks.Grafana.Loki
{
    /// <summary>
    /// Mode, used for labels filtration
    /// </summary>
    public enum LokiLabelFiltrationMode
    {
        /// <summary>
        /// By including specific labels
        /// </summary>
        Include = 0,
        /// <summary>
        /// By excluding specific labels
        /// </summary>
        Exclude = 1
    }
}