using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace GraduationPlatform.Member2_ProjectIdeas
{
    public class SimilarityEngine
    {
        public static double CalculateCosineSimilarity(string textA, string textB, out List<string> overlappingWords)
        {
            overlappingWords = new List<string>();
            var tokensA = TokenizeAndNormalize(textA);
            var tokensB = TokenizeAndNormalize(textB);

            if (tokensA.Count == 0 || tokensB.Count == 0) return 0.0;

            var freqA = GetTermFrequency(tokensA);
            var freqB = GetTermFrequency(tokensB);

            var allTerms = new HashSet<string>(freqA.Keys.Concat(freqB.Keys));

            double dotProduct = 0.0;
            double normA = 0.0;
            double normB = 0.0;

            foreach (var term in allTerms)
            {
                double valA = freqA.ContainsKey(term) ? freqA[term] : 0.0;
                double valB = freqB.ContainsKey(term) ? freqB[term] : 0.0;

                if (valA > 0 && valB > 0)
                {
                    overlappingWords.Add(term);
                }

                dotProduct += valA * valB;
                normA += valA * valA;
                normB += valB * valB;
            }

            if (normA == 0.0 || normB == 0.0) return 0.0;

            double score = dotProduct / (Math.Sqrt(normA) * Math.Sqrt(normB));
            return Math.Round(Math.Min(1.0, score), 4);
        }

        public static double CalculateWeightedSimilarity(
            string titleA, string descA, List<string> techA,
            string titleB, string descB, List<string> techB,
            out List<string> overlappingTerms)
        {
            return TextSimilarityAnalysis.ComparePair(
                new SimilarityQuery { Title = titleA, Description = descA, Technologies = techA },
                new SimilarityDocument { Title = titleB, Description = descB, Technologies = techB },
                out overlappingTerms);
        }

        public static List<string> TokenizeAndNormalize(string input)
        {
            return TextSimilarityAnalysis.Tokenize(input);
        }

        private static Dictionary<string, double> GetTermFrequency(List<string> tokens)
        {
            var dict = new Dictionary<string, double>();
            foreach (var token in tokens)
            {
                if (!dict.ContainsKey(token)) dict[token] = 0;
                dict[token] += 1;
            }
            return dict;
        }
    }
}
