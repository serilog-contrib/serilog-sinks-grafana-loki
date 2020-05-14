namespace Serilog.Sinks.Loki.Sinks.Loki
{
    public class LokiCredentials
    {
        public string Login { get; set; }

        public string Password { get; set; }

        internal bool IsEmpty => string.IsNullOrEmpty(Login) || string.IsNullOrEmpty(Password);
    }
}