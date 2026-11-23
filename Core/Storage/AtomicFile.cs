using System.Text;

namespace EventGhostDotNet.Core.Storage;

/// <summary>
/// Écriture de fichier sans risque de troncature : le contenu est écrit dans un
/// fichier temporaire puis remplacé atomiquement, l'ancienne version partant en <c>.bak</c>.
/// </summary>
public static class AtomicFile
{
    public const string TemporarySuffix = ".tmp";
    public const string BackupSuffix = ".bak";

    public static void WriteAllText(string path, string contents)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(contents);

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new ArgumentException("Le chemin ne contient pas de dossier.", nameof(path));
        Directory.CreateDirectory(directory);

        var temporaryPath = fullPath + TemporarySuffix;
        var backupPath = fullPath + BackupSuffix;
        using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
        {
            writer.Write(contents);
            writer.Flush();
            stream.Flush(flushToDisk: true);
        }

        if (File.Exists(fullPath))
        {
            try
            {
                File.Replace(temporaryPath, fullPath, backupPath, ignoreMetadataErrors: true);
                return;
            }
            catch (PlatformNotSupportedException)
            {
            }
            catch (IOException)
            {
                // Certains systèmes de fichiers refusent File.Replace : on retombe sur une copie de sauvegarde.
                File.Copy(fullPath, backupPath, overwrite: true);
            }
        }

        File.Move(temporaryPath, fullPath, overwrite: true);
    }
}
