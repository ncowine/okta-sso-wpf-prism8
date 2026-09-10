using System;

namespace Common.Authentication.Okta.Platform
{
    /// <summary>
    /// Enforces a single running instance and forwards command-line activation (the custom-scheme
    /// OAuth redirect) from secondary launches to the primary instance.
    /// </summary>
    public interface ISingleInstanceCoordinator : IDisposable
    {
        /// <summary>True if this process owns the single-instance lock.</summary>
        bool IsPrimaryInstance { get; }

        /// <summary>Primary instance: begin receiving activation arguments from secondary launches.</summary>
        void StartListening(Action<string> onActivationArgumentReceived);

        /// <summary>Secondary instance: hand <paramref name="argument"/> to the primary instance.</summary>
        void SignalPrimary(string argument);
    }
}
