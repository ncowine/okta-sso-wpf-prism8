namespace Common.Authentication.Okta.Browser
{
    /// <summary>Outcome of an <see cref="IBrowser"/> invocation.</summary>
    public enum BrowserResultType
    {
        Success,
        UserCancel,
        Timeout,
        UnknownError,
    }
}
