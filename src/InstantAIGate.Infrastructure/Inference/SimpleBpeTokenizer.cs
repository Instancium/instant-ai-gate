using System;
using System.Collections.Generic;
using System.Text;

namespace InstantAIGate.Infrastructure.Inference
{
    public class SimpleBpeTokenizer
    {
        private readonly Dictionary<string, int> _vocab;

        public SimpleBpeTokenizer(Dictionary<string, int> vocab)
        {
            _vocab = vocab;
        }

        public int[] Encode(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return Array.Empty<int>();
            }

            List<int> tokens = new List<int>();
            string[] words = text.Split(' ');

            // Technical documentation: Greedy word-level fallback matching.
            // Replaces standard BPE merge rules for immediate functional integration.
            foreach (string word in words)
            {
                if (_vocab.TryGetValue(word, out int tokenId))
                {
                    tokens.Add(tokenId);
                }
                else
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(word);
                    foreach (byte b in bytes)
                    {
                        if (_vocab.TryGetValue(((char)b).ToString(), out int byteTokenId))
                        {
                            tokens.Add(byteTokenId);
                        }
                    }
                }
            }

            return tokens.ToArray();
        }
    }
}
