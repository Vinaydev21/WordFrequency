using WordFrequency;

const int TopCount = 20;
const int BufferSize = 64 * 1024;

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: WordFrequency <file>");
    return 2;
}

try
{
    var counter = new WordCounter();
    var buffer = new byte[BufferSize];

    // Read the file in small pieces so memory use doesn't depend on file size.
    // SequentialScan tells Windows we are reading from start to end.
    using (var stream = new FileStream(args[0], FileMode.Open, FileAccess.Read, FileShare.Read,
                                       BufferSize, FileOptions.SequentialScan))
    {
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            counter.AddBytes(buffer.AsSpan(0, read));
        }
    }
    counter.Finish();

    // Same format as "uniq -c": count in 7 columns, then the word.
    foreach (var (word, count) in counter.TopN(TopCount))
    {
        Console.WriteLine($"{count,7} {word}");
    }
    return 0;
}
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                              or ArgumentException or NotSupportedException)
{
    Console.Error.WriteLine($"Error reading '{args[0]}': {ex.Message}");
    return 1;
}