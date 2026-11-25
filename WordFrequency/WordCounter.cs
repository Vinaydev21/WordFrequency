namespace WordFrequency;

/// <summary>
/// Counts words in a stream of bytes. A word is a run of letters a-z or A-Z,
/// and case is ignored.
///
/// Some choices worth knowing about:
/// - It works on raw bytes instead of text. Anything that is not an ASCII letter
///   ends a word, same as the tr command in the bash version. Because nothing is
///   decoded, binary files can't cause errors.
/// - Words are lower-cased while reading and each distinct word is stored once in
///   one byte array, so we don't create a string for every word in the file.
/// - The lookup table is my own hash table (open addressing, linear probing),
///   not Dictionary.
/// - It is not thread safe. Reading a file is slow compared to counting, so one
///   thread is enough and keeps things simple.
/// </summary>
public sealed class WordCounter
{
    private const uint FnvOffsetBasis = 2166136261;
    private const uint FnvPrime = 16777619;

    // Hash table slots. 0 means empty, anything else is entry index + 1.
    private int[] _buckets = new int[1 << 15];

    // One slot per distinct word. These arrays all use the same index.
    private uint[] _hashes = new uint[1 << 14];
    private int[] _offsets = new int[1 << 14];
    private int[] _lengths = new int[1 << 14];
    private long[] _counts = new long[1 << 14];
    private int _entryCount;

    // All the distinct words stored one after another.
    private byte[] _arena = new byte[1 << 16];
    private int _arenaUsed;

    // The word we are in the middle of reading. Kept between calls because a
    // word can be cut in half by the end of a chunk.
    private byte[] _word = new byte[256];
    private int _wordLength;

    /// <summary>How many different words we have seen so far.</summary>
    public int DistinctWords => _entryCount;

    /// <summary>Process the next chunk of the file. A word may be split between two chunks.</summary>
    public void AddBytes(ReadOnlySpan<byte> chunk)
    {
        foreach (byte b in chunk)
        {
            if (b >= (byte)'a' && b <= (byte)'z')
            {
                Append(b);
            }
            else if (b >= (byte)'A' && b <= (byte)'Z')
            {
                Append((byte)(b | 0x20)); // make it lower case
            }
            else if (_wordLength > 0)
            {
                FlushWord();
            }
        }
    }

    /// <summary>Call this after the last chunk, so a word at the very end of the file still gets counted.</summary>
    public void Finish()
    {
        if (_wordLength > 0)
        {
            FlushWord();
        }
    }

    /// <summary>
    /// Returns the n most common words, highest count first.
    /// If two words have the same count, the one that is later in the alphabet comes
    /// first. That is what "sort -nr" does in the bash version.
    /// </summary>
    public List<(string Word, long Count)> TopN(int n)
    {
        var result = new List<(string, long)>();
        if (n <= 0)
        {
            return result;
        }

        // Keep the best n entries in a small array, best first. Each word is checked
        // against the worst one in the array first, and most fail that check straight
        // away. This way we never have to sort every word.
        var top = new int[n];
        int size = 0;

        for (int e = 0; e < _entryCount; e++)
        {
            if (size == n && !IsBetter(e, top[n - 1]))
            {
                continue;
            }

            // Use a free slot, or take the place of the worst one that gets dropped.
            int pos = size < n ? size++ : n - 1;
            while (pos > 0 && IsBetter(e, top[pos - 1]))
            {
                top[pos] = top[pos - 1];
                pos--;
            }
            top[pos] = e;
        }

        for (int i = 0; i < size; i++)
        {
            int e = top[i];
            string word = System.Text.Encoding.ASCII.GetString(_arena, _offsets[e], _lengths[e]);
            result.Add((word, _counts[e]));
        }
        return result;
    }

    /// <summary>True if entry a should be listed before entry b.</summary>
    private bool IsBetter(int a, int b)
    {
        if (_counts[a] != _counts[b])
        {
            return _counts[a] > _counts[b];
        }

        // Same count, so compare the words. Byte order is the same as alphabetical for ASCII.
        return WordAt(a).SequenceCompareTo(WordAt(b)) > 0;
    }

    private ReadOnlySpan<byte> WordAt(int entry) => new(_arena, _offsets[entry], _lengths[entry]);

    private void Append(byte b)
    {
        if (_wordLength == _word.Length)
        {
            // Binary files can have really long runs of letters, so grow when needed.
            Array.Resize(ref _word, _word.Length * 2);
        }
        _word[_wordLength++] = b;
    }

    private void FlushWord()
    {
        var word = new ReadOnlySpan<byte>(_word, 0, _wordLength);
        _wordLength = 0;

        // FNV-1a hash
        uint hash = FnvOffsetBasis;
        foreach (byte b in word)
        {
            hash = (hash ^ b) * FnvPrime;
        }

        int mask = _buckets.Length - 1;
        int slot = (int)(hash & (uint)mask);

        while (true)
        {
            int bucket = _buckets[slot];
            if (bucket == 0)
            {
                // Empty slot, so we haven't seen this word before.
                Insert(word, hash, slot);
                return;
            }

            int e = bucket - 1;
            if (_hashes[e] == hash && WordAt(e).SequenceEqual(word))
            {
                _counts[e]++;
                return;
            }

            // Slot is taken by a different word, try the next one.
            slot = (slot + 1) & mask;
        }
    }

    private void Insert(ReadOnlySpan<byte> word, uint hash, int slot)
    {
        if (_entryCount == _hashes.Length)
        {
            int newSize = _hashes.Length * 2;
            Array.Resize(ref _hashes, newSize);
            Array.Resize(ref _offsets, newSize);
            Array.Resize(ref _lengths, newSize);
            Array.Resize(ref _counts, newSize);
        }

        if (_arenaUsed + word.Length > _arena.Length)
        {
            long needed = Math.Max((long)_arena.Length * 2, (long)_arenaUsed + word.Length);
            Array.Resize(ref _arena, checked((int)needed));
        }

        word.CopyTo(new Span<byte>(_arena, _arenaUsed, word.Length));

        int e = _entryCount++;
        _hashes[e] = hash;
        _offsets[e] = _arenaUsed;
        _lengths[e] = word.Length;
        _counts[e] = 1;
        _arenaUsed += word.Length;
        _buckets[slot] = e + 1;

        // Grow the table before it gets more than half full, otherwise lookups slow down.
        if (_entryCount * 2 > _buckets.Length)
        {
            Rehash();
        }
    }

    private void Rehash()
    {
        var buckets = new int[_buckets.Length * 2];
        int mask = buckets.Length - 1;

        for (int e = 0; e < _entryCount; e++)
        {
            // We saved the hash, so there is no need to read the word again.
            int slot = (int)(_hashes[e] & (uint)mask);
            while (buckets[slot] != 0)
            {
                slot = (slot + 1) & mask;
            }
            buckets[slot] = e + 1;
        }
        _buckets = buckets;
    }
}