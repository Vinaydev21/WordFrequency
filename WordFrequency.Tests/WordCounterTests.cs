using System.Text;

namespace WordFrequency.Tests;

public class WordCounterTests
{
    // Makes a counter and feeds it the whole text.
    // Latin1 turns each character into one byte, so the bytes match what we typed.
    private static WordCounter CreateCounterWith(string text)
    {
        var counter = new WordCounter();
        counter.AddBytes(Encoding.Latin1.GetBytes(text));
        counter.Finish();
        return counter;
    }

    [Fact]
    public void Given_EmptyInput_When_GettingTopWords_Then_ResultIsEmpty()
    {
        // Arrange
        var counter = CreateCounterWith("");

        // Act
        var actual = counter.TopN(20);

        // Assert
        Assert.Empty(actual);
    }

    [Fact]
    public void Given_SameWordInMixedCase_When_GettingTopWords_Then_ItIsCountedAsOneWord()
    {
        // Arrange
        var counter = CreateCounterWith("The the THE tHe");
        var expected = ("the", 4L);

        // Act
        var actual = counter.TopN(20);

        // Assert
        Assert.Single(actual);
        Assert.Equal(expected, actual[0]);
    }

    [Fact]
    public void Given_DigitsAndApostrophes_When_GettingTopWords_Then_TheySplitWords()
    {
        // Arrange
        // Anything that is not a letter ends a word, like tr in the bash version.
        var counter = CreateCounterWith("don't stop2go");
        var expected = new[] { "don", "go", "stop", "t" };

        // Act
        var actual = counter.TopN(20).Select(r => r.Word).OrderBy(w => w).ToArray();

        // Assert
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Given_InputEndingWithoutDelimiter_When_Finished_Then_LastWordIsCounted()
    {
        // Arrange
        var counter = CreateCounterWith("alpha beta beta");
        var expected = new[] { ("beta", 2L), ("alpha", 1L) };

        // Act
        var actual = counter.TopN(20);

        // Assert
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Given_WordsWithDifferentCounts_When_GettingTopWords_Then_HighestCountComesFirst()
    {
        // Arrange
        var counter = CreateCounterWith("a b b c c c");
        var expected = new[] { "c", "b", "a" };

        // Act
        var actual = counter.TopN(20).Select(r => r.Word).ToArray();

        // Assert
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Given_WordsWithEqualCounts_When_GettingTopWords_Then_TiesAreOrderedByWordDescending()
    {
        // Arrange
        // When counts are the same, "sort -nr" puts the later word in the
        // alphabet first, so we do the same.
        var counter = CreateCounterWith("apple banana cherry");
        var expected = new[] { "cherry", "banana", "apple" };

        // Act
        var actual = counter.TopN(20).Select(r => r.Word).ToArray();

        // Assert
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Given_MoreDistinctWordsThanRequested_When_GettingTopThree_Then_OnlyThreeAreReturned()
    {
        // Arrange
        var counter = CreateCounterWith("a b c d e f g");
        const int expected = 3;

        // Act
        var actual = counter.TopN(3).Count;

        // Assert
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Given_InputFedOneByteAtATime_When_GettingTopWords_Then_ResultMatchesSingleChunk()
    {
        // Arrange
        const string text = "whale whale whale harpoon harpoon sea";
        var expected = CreateCounterWith(text).TopN(20);

        var chunkedCounter = new WordCounter();
        byte[] bytes = Encoding.ASCII.GetBytes(text);

        // Act
        // One byte per call, so every word gets split between calls.
        foreach (byte b in bytes)
        {
            chunkedCounter.AddBytes(new[] { b });
        }
        chunkedCounter.Finish();
        var actual = chunkedCounter.TopN(20);

        // Assert
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Given_RandomBinaryData_When_Counting_Then_NoExceptionIsThrown()
    {
        // Arrange
        var data = new byte[10_000];
        new Random(42).NextBytes(data);
        var counter = new WordCounter();

        // Act
        var actual = Record.Exception(() =>
        {
            counter.AddBytes(data);
            counter.Finish();
            counter.TopN(20);
        });

        // Assert
        // The data is random, so we only check that nothing crashes.
        Assert.Null(actual);
    }

    [Fact]
    public void Given_WordLongerThanInitialBuffer_When_Counting_Then_WordIsCountedCorrectly()
    {
        // Arrange
        // The word buffer starts at 256 bytes, so this makes it grow.
        string longWord = new string('x', 100_000);
        var counter = CreateCounterWith(longWord + " " + longWord);
        var expected = (longWord, 2L);

        // Act
        var actual = counter.TopN(20)[0];

        // Assert
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Given_FiftyThousandDistinctWords_When_Counting_Then_CountsSurviveTableGrowth()
    {
        // Arrange
        // That many different words makes the hash table resize a few times.
        // The counts should still be right after each resize.
        var sb = new StringBuilder();
        for (int i = 0; i < 50_000; i++)
        {
            string word = ToWord(i);
            sb.Append(word).Append(' ').Append(word).Append(' ');
        }
        sb.Append("popular popular popular");
        var counter = CreateCounterWith(sb.ToString());

        const int expectedDistinctWords = 50_001;
        var expectedTop = ("popular", 3L);

        // Act
        var actualTop = counter.TopN(1)[0];

        // Assert
        Assert.Equal(expectedDistinctWords, counter.DistinctWords);
        Assert.Equal(expectedTop, actualTop);
    }

    // Turns a number into a 4 letter word (0 = aaaa, 1 = aaab ...), so each number
    // gives a different word.
    private static string ToWord(int i)
    {
        var chars = new char[4];
        for (int p = 3; p >= 0; p--)
        {
            chars[p] = (char)('a' + i % 26);
            i /= 26;
        }
        return new string(chars);
    }
}