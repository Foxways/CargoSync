using System;

namespace OrganizationImportTool.Auth
{
    /// <summary>An authenticated application user.</summary>
    public class User
    {
        public int Id { get; set; }
        public string Username { get; set; } = string.Empty;

        /// <summary>Administrators can approve password resets for other users.</summary>
        public bool IsAdmin { get; set; }
    }

    /// <summary>Outcome of a sign-in attempt, including lockout state for the UI.</summary>
    public class AuthResult
    {
        /// <summary>The signed-in user, or null when the attempt failed.</summary>
        public User? User { get; set; }

        /// <summary>True when the account is temporarily locked (too many wrong passwords).</summary>
        public bool Locked { get; set; }
        public TimeSpan LockRemaining { get; set; }

        public int FailedCount { get; set; }

        /// <summary>Wrong-password attempts left before the account locks (0 = not applicable).</summary>
        public int AttemptsRemaining { get; set; }
    }
}
