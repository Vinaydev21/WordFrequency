# Word Frequency Analyzer (C#)

A command-line application that identifies and prints the 20 most frequent words in a file, following the tokenization and counting behavior of the following Bash pipeline:

```bash
cat "$1" | tr -cs 'a-zA-Z' '[\n*]' | grep -v "^$" | tr '[:upper:]' '[:lower:]' | sort | uniq -c | sort -nr | head -20
```

## Requirements

- .NET 10 SDK
- Windows, Linux, or macOS

## Project Structure

```text
WordFrequency/
├── MobyDick/
│   └── mobydick.txt
│
├── WordFrequency/
│   ├── Program.cs
│   ├── WordCounter.cs
│   └── WordFrequency.csproj
│
├── WordFrequency.Tests/
│   ├── WordCounterTests.cs
│   └── WordFrequency.Tests.csproj
│
├── .gitignore
├── README.md
└── WordFrequency.slnx
```

## Build

Run the following command from the solution/repository root directory:

```bash
dotnet build WordFrequency.slnx -c Release
```

## Run the Application

Run the application against the provided Moby-Dick sample file:

```bash
dotnet run --project WordFrequency/WordFrequency.csproj -c Release -- "MobyDick/mobydick.txt"
```

Alternatively, execute the compiled application:

```bash
dotnet WordFrequency/bin/Release/net10.0/WordFrequency.dll "MobyDick/mobydick.txt"
```

The application accepts exactly one command-line argument: the input file path.

## Run Tests

Execute the unit tests using:

```bash
dotnet test WordFrequency.slnx -c Release
```

The test suite covers:

- Empty input
- Case-insensitive word counting
- Apostrophes, digits, and punctuation as delimiters
- Input ending without a delimiter
- Frequency ordering
- Alphabetical tie-breaking
- Top-N result limitation
- Words split across input chunks
- Random binary input
- Very long words
- Hash table resizing with 50,000 distinct words

## Sample Output

The application was tested against the provided Moby-Dick text file.

```text
   4284 the
   2192 and
   2185 of
   1861 a
   1685 to
   1366 in
   1056 i
   1024 that
    889 his
    821 it
    783 he
    616 but
    603 was
    595 with
    577 s
    564 is
    551 for
    542 all
    541 as
    458 at
```

## Implementation Approach

### 1. Byte-Based Tokenization

Words are defined as consecutive ASCII letters (`a-z` and `A-Z`). Every other byte is treated as a delimiter. All letters are converted to lowercase.

Processing raw bytes instead of decoding text ensures behavior consistent with the specified Bash pipeline in the C locale and avoids decoding errors when processing binary files.

### 2. Streaming File Processing

The input file is read sequentially in 64 KB chunks rather than loading the entire file into memory.

A temporary word buffer preserves words that span multiple chunks, ensuring that chunk boundaries do not affect the results.

### 3. Custom Hash Table

The word counter uses a custom hash table with:

- Open addressing
- Linear probing
- FNV-1a hashing
- Power-of-two table sizing
- A maximum load factor of 0.5

Distinct words are stored once in a byte arena. Their offsets, lengths, hashes, and frequency counts are maintained in parallel arrays.

This avoids creating a new string for every word occurrence and provides control over memory allocation and lookup behavior.

### 4. Top 20 Selection

Instead of sorting every distinct word, the implementation maintains a sorted array containing only the best 20 entries.

Each new entry is compared against the current lowest-ranked entry, reducing unnecessary sorting work.

### 5. Tie-Breaking

Words with equal frequencies are ordered alphabetically in descending order, matching the reverse-sort behavior of the specified Bash pipeline.

## Error Handling and Exit Codes

| Exit Code | Description |
|---|---|
| 0 | Successful execution |
| 1 | Input file could not be read |
| 2 | Invalid command-line usage |

The application reports file-access errors to standard error.

## Design Decisions and Trade-offs

### Single-Threaded Processing

The implementation deliberately uses a single thread. Sequential file reading keeps the implementation straightforward and deterministic without shared mutable state or synchronization overhead.

Parallel processing could be introduced by splitting input at delimiter boundaries, processing independent chunks, and merging the resulting counts.

### Memory Usage

Memory consumption is proportional to the number of distinct words and the longest word encountered, rather than the total input file size.

### Custom Hash Table

A custom hash table avoids repeated string allocations and provides control over storage and lookup operations. The trade-off is additional implementation complexity compared with using the built-in `Dictionary`.

### Character Handling

Only ASCII letters are considered part of a word. Non-ASCII bytes are treated as delimiters, consistent with the specified Bash pipeline.

