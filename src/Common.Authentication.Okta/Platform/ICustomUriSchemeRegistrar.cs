namespace Common.Authentication.Okta.Platform
{
    /// <summary>Registers the OAuth redirect's custom URI scheme with Windows for the current user.</summary>
    public interface ICustomUriSchemeRegistrar
    {
        /// <summary>
        /// Ensures <c>HKCU\Software\Classes\{scheme}</c> points at the current executable.
        /// Safe to call on every launch; only writes when missing or stale.
        /// </summary>
        void EnsureRegistered();
    }
}
