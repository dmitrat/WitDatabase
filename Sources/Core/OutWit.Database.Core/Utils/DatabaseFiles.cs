namespace OutWit.Database.Core.Utils
{
    /// <summary>
    /// The files a file-backed database owns.
    ///
    /// A database is not one file: indexes live in a sibling directory and the journal in a sibling
    /// file, both named after the data file. The naming rule lives here so that whoever creates
    /// those paths and whoever deletes them cannot drift apart - deleting only the data file left
    /// the index directory behind, and a database recreated at the same path then inherited every
    /// index from the one that had been deleted.
    /// </summary>
    public static class DatabaseFiles
    {
        #region Constants

        /// <summary>
        /// Suffix appended to the data file name to form the index directory.
        /// </summary>
        public const string INDEX_DIRECTORY_SUFFIX = "_indexes";

        /// <summary>
        /// Extension of the journal file that sits beside the data file.
        /// </summary>
        public const string JOURNAL_EXTENSION = ".journal";

        #endregion

        #region Paths

        /// <summary>
        /// Gets the directory holding the indexes of a database file.
        /// </summary>
        /// <param name="filePath">Path of the database data file.</param>
        /// <returns>The index directory path, or null when the data file path is empty.</returns>
        public static string? GetIndexDirectory(string? filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return null;

            var directory = Path.GetDirectoryName(filePath);
            var fileName = Path.GetFileName(filePath) + INDEX_DIRECTORY_SUFFIX;

            return string.IsNullOrEmpty(directory)
                ? fileName
                : Path.Combine(directory, fileName);
        }

        /// <summary>
        /// Gets the journal file that sits beside a database file.
        /// </summary>
        /// <param name="filePath">Path of the database data file.</param>
        /// <returns>The journal path, or null when the data file path is empty.</returns>
        public static string? GetJournalPath(string? filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return null;

            var directory = Path.GetDirectoryName(filePath);
            var fileName = Path.GetFileNameWithoutExtension(filePath) + JOURNAL_EXTENSION;

            return string.IsNullOrEmpty(directory)
                ? fileName
                : Path.Combine(directory, fileName);
        }

        #endregion

        #region Delete

        /// <summary>
        /// Deletes a database and everything that belongs to it.
        /// </summary>
        /// <param name="filePath">Path of the database data file.</param>
        /// <returns>True if anything was deleted; false if there was nothing there.</returns>
        public static bool Delete(string? filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return false;

            var deleted = false;

            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                deleted = true;
            }

            var journalPath = GetJournalPath(filePath);
            if (journalPath != null && File.Exists(journalPath))
            {
                File.Delete(journalPath);
                deleted = true;
            }

            var indexDirectory = GetIndexDirectory(filePath);
            if (indexDirectory != null && Directory.Exists(indexDirectory))
            {
                Directory.Delete(indexDirectory, recursive: true);
                deleted = true;
            }

            return deleted;
        }

        #endregion
    }
}
