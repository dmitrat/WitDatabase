namespace OutWit.Database.Core.Interfaces;

/// <summary>
/// Hands out nonce sequence numbers that are never reused, including across sessions.
/// </summary>
/// <remarks>
/// <para>
/// The encryptors used to own a counter field, set to zero in their constructor - and the
/// constructor runs on OPEN. Two sessions therefore walked the same sequence, and since the other
/// half of the nonce was derived from the password, two sessions could encrypt one page under one
/// nonce. Measured: both wrote page 0 under <c>00379B03582ABC0501000000</c>, and AES-GCM under a
/// repeated nonce hands the second plaintext to anyone holding both ciphertexts.
/// </para>
/// <para>
/// So the sequence is not the encryptor's to keep. It belongs to the file, and this is the seam
/// between the two: an encryptor asks for the next number and never learns where it came from.
/// </para>
/// </remarks>
public interface INonceSequence
{
    /// <summary>
    /// The next number no write has used. Never returns one value twice for one database, whatever
    /// happens between sessions - including a process that is killed rather than closed.
    /// </summary>
    ulong Next();
}
