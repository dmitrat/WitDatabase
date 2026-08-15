namespace OutWit.Database.Core.Interfaces
{
    /// <summary>
    /// Something in an open database's chain that can change the password by rewrapping the data
    /// key, without touching a single encrypted page.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why this can exist at all.</b> Since the format change the data key is drawn at random and
    /// the password only WRAPS it - see <c>CryptoHeader.CreateWrapping</c>. So changing a password is
    /// a rewrite of 60 bytes in the preamble and nothing else. Before that, the password WAS the key
    /// and the only honest answer was to build a new database and migrate into it.
    /// </para>
    /// <para>
    /// <b>Why it is a method on an OPEN database and not a static over a path.</b> The preamble holds
    /// the header in memory and writes it back whenever it reserves another block of nonce numbers.
    /// A rewrap applied to the file from outside while a session is open therefore survives only
    /// until that session exhausts its block - 65,536 page encryptions - and is then silently undone.
    /// The safe route is through the live preamble, which is what this capability reaches.
    /// </para>
    /// <para>
    /// <b>What it does NOT do.</b> It cannot encrypt a database that is not encrypted: there is no
    /// wrapped key to rewrap and no preamble to put one in. Going from no password to a password
    /// stays a migration, and a consumer has to keep offering both.
    /// </para>
    /// </remarks>
    public interface IPasswordRewrapSource
    {
        /// <summary>
        /// Whether this database has a wrapped key to rewrap - true for an encrypted database whose
        /// key is wrapped, false for an unencrypted one and for one whose caller owns the key material.
        /// </summary>
        bool CanRewrapPassword { get; }

        /// <summary>
        /// Replaces the wrapped key with one wrapped under <paramref name="newPassword"/>. The pages
        /// are untouched.
        /// </summary>
        /// <param name="currentPassword">
        /// The password in force. It is not taken on trust: it has to unwrap the key, and a wrong one
        /// throws before anything is written.
        /// </param>
        /// <param name="newPassword">The password that will unwrap the key from now on.</param>
        /// <param name="iterations">
        /// PBKDF2 iterations for the new wrap. Null keeps the count the database already records, so
        /// a password change does not quietly weaken - or slow down - a database it was not asked to.
        /// </param>
        /// <exception cref="System.Security.Cryptography.CryptographicException">
        /// <paramref name="currentPassword"/> does not unwrap the key. Nothing has been written.
        /// </exception>
        /// <exception cref="System.NotSupportedException">
        /// There is no wrapped key here - see <see cref="CanRewrapPassword"/>.
        /// </exception>
        void RewrapPassword(string currentPassword, string newPassword, int? iterations = null);
    }
}
