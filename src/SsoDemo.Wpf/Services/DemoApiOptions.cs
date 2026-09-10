namespace SsoDemo.Wpf.Services
{
    /// <summary>Configuration for the demo API client (App.config key <c>Api:BaseUrl</c>).</summary>
    public sealed class DemoApiOptions
    {
        /// <summary>DemoApi (first-party API).</summary>
        public string BaseUrl { get; set; } = "http://localhost:5006";

        /// <summary>DemoApiB (third-party API) for the direct call path.</summary>
        public string BaseUrlB { get; set; } = "http://localhost:5007";
    }
}
