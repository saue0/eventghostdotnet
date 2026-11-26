using System.Text;

namespace EventGhostDotNet.Core.Diagnostics;

/// <summary>
/// Journal fichier limité en taille : quand le fichier dépasse la limite, il est renommé en
/// <c>.1</c>, les archives existantes sont décalées et les plus anciennes sont supprimées.
/// Les écritures sont sérialisées : le journal peut être appelé depuis plusieurs threads.
/// </summary>
public sealed class RotatingLogFile
{
    public const long DefaultMaximumBytes = 1_048_576;
    public const int DefaultMaximumArchives = 3;

    private static readonly Encoding FileEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private readonly object _gate = new();
    private readonly string _path;
    private readonly long _maximumBytes;
    private readonly int _maximumArchives;

    public RotatingLogFile(string path, long maximumBytes = DefaultMaximumBytes,
        int maximumArchives = DefaultMaximumArchives)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (maximumBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumBytes), "La taille maximale doit être positive.");
        if (maximumArchives < 0)
            throw new ArgumentOutOfRangeException(nameof(maximumArchives), "Le nombre d'archives ne peut pas être négatif.");

        _path = System.IO.Path.GetFullPath(path);
        _maximumBytes = maximumBytes;
        _maximumArchives = maximumArchives;
    }

    public string Path => _path;

    /// <summary>Chemin de l'archive numérotée, <c>.1</c> étant la plus récente.</summary>
    public string GetArchivePath(int index)
    {
        if (index < 1 || index > _maximumArchives)
            throw new ArgumentOutOfRangeException(nameof(index));
        return $"{_path}.{index}";
    }

    public void Write(string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        var text = line + Environment.NewLine;
        lock (_gate)
        {
            var directory = System.IO.Path.GetDirectoryName(_path)
                ?? throw new InvalidOperationException("Le chemin du journal ne contient pas de dossier.");
            Directory.CreateDirectory(directory);

            if (File.Exists(_path) &&
                new FileInfo(_path).Length + FileEncoding.GetByteCount(text) > _maximumBytes)
                Rotate();

            File.AppendAllText(_path, text, FileEncoding);
        }
    }

    /// <summary>Lit les dernières lignes du fichier courant (les archives ne sont pas lues).</summary>
    public IReadOnlyList<string> ReadTail(int maximumLines)
    {
        if (maximumLines <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumLines), "Le nombre de lignes doit être positif.");

        lock (_gate)
        {
            if (!File.Exists(_path))
                return [];

            using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, FileEncoding, detectEncodingFromByteOrderMarks: false);
            var lines = new Queue<string>(maximumLines);
            while (reader.ReadLine() is { } line)
            {
                lines.Enqueue(line);
                if (lines.Count > maximumLines)
                    lines.Dequeue();
            }
            return lines.ToArray();
        }
    }

    /// <summary>Supprime le fichier courant et toutes les archives.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            if (File.Exists(_path))
                File.Delete(_path);
            for (var index = 1; index <= _maximumArchives; index++)
            {
                var archive = GetArchivePath(index);
                if (File.Exists(archive))
                    File.Delete(archive);
            }
        }
    }

    private void Rotate()
    {
        if (_maximumArchives == 0)
        {
            File.Delete(_path);
            return;
        }

        var oldest = GetArchivePath(_maximumArchives);
        if (File.Exists(oldest))
            File.Delete(oldest);

        for (var index = _maximumArchives - 1; index >= 1; index--)
        {
            var source = GetArchivePath(index);
            if (File.Exists(source))
                File.Move(source, GetArchivePath(index + 1), overwrite: true);
        }

        File.Move(_path, GetArchivePath(1), overwrite: true);
    }
}
