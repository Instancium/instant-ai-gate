using System.Collections.Generic;
using Xunit;
using InstantAIGate.Infrastructure.Inference;

namespace InstantAIGate.Infrastructure.Tests.Inference
{
    public class SimpleBpeTokenizerTests
    {
        [Theory]
        [InlineData("")]
        [InlineData(null)]
        public void Encode_EmptyString_ReturnsEmptyArray(string input)
        {
            Dictionary<string, int> vocab = new Dictionary<string, int>();
            SimpleBpeTokenizer tokenizer = new SimpleBpeTokenizer(vocab);

            int[] result = tokenizer.Encode(input);

            Assert.Empty(result);
        }

        [Fact]
        public void Encode_KnownWords_ReturnsCorrectTokens()
        {
            Dictionary<string, int> vocab = new Dictionary<string, int>
            {
                { "system", 500 },
                { "prompt", 501 }
            };
            SimpleBpeTokenizer tokenizer = new SimpleBpeTokenizer(vocab);

            int[] result = tokenizer.Encode("system prompt");

            Assert.Equal(new[] { 500, 501 }, result);
        }

        [Fact]
        public void Encode_UnknownWords_FallsBackToByteLevel()
        {
            Dictionary<string, int> vocab = new Dictionary<string, int>
            {
                { "A", 65 },
                { "B", 66 }
            };
            SimpleBpeTokenizer tokenizer = new SimpleBpeTokenizer(vocab);

            int[] result = tokenizer.Encode("AB");

            Assert.Equal(new[] { 65, 66 }, result);
        }
    }
}