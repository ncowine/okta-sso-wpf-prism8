namespace SsoDemo.Wpf.Infrastructure
{
    /// <summary>View keys registered for navigation (see <c>App.RegisterTypes</c>).</summary>
    public static class ViewNames
    {
        /// <summary>Startup view: runs the silent-then-browser sign-in flow.</summary>
        public const string Authenticating = "AuthenticatingView";

        /// <summary>Signed-in confirmation.</summary>
        public const string Welcome = "WelcomeView";

        /// <summary>Shown after the user signs out.</summary>
        public const string SignedOut = "SignedOutView";

        /// <summary>Shown when sign-in fails.</summary>
        public const string AccessDenied = "AccessDeniedView";
    }
}
