namespace Common.Authentication.Okta.Browser
{
    /// <summary>Result of an <see cref="IBrowser"/> invocation.</summary>
    public sealed class BrowserResult
    {
        public BrowserResultType ResultType { get; init; }

        /// <summary>The full callback URI (with query string) the redirect delivered, when <see cref="ResultType"/> is <see cref="BrowserResultType.Success"/>.</summary>
        public string? Response { get; init; }

        public string? Error { get; init; }
    }
}
