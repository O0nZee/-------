using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace GraduationPlatform.Member2_ProjectIdeas
{
    public class SimilarityEngine
    {
        private static readonly HashSet<string> StopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // English stop words
            "a", "an", "the", "and", "or", "in", "on", "at", "to", "for", "with", "by", "from",
            "is", "are", "was", "were", "be", "been", "using", "used", "system", "project", "app", 
            "application", "platform", "based", "graduation", "study", "management",

            // Arabic stop words
            "في", "من", "على", "إلى", "عن", "مع", "هذا", "هذه", "ذلك", "تلك", "هو", "هي", "هم",
            "مشروع", "نظام", "تطبيق", "منصة", "تخرج", "إدارة", "دراسة", "باستخدام", "خلال", "بين"
        };

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
            List<string> titleOverlap, descOverlap, techOverlap;

            double titleScore = CalculateCosineSimilarity(titleA, titleB, out titleOverlap);
            double descScore = CalculateCosineSimilarity(descA, descB, out descOverlap);
            
            string techStringA = string.Join(" ", techA ?? new List<string>());
            string techStringB = string.Join(" ", techB ?? new List<string>());
            double techScore = CalculateCosineSimilarity(techStringA, techStringB, out techOverlap);

            overlappingTerms = titleOverlap.Union(descOverlap).Union(techOverlap).Distinct().ToList();

            // Weighted formula:
            // Title carries 45% weight, Description carries 35% weight, Tech stack carries 20% weight
            double weightedScore = (titleScore * 0.45) + (descScore * 0.35) + (techScore * 0.20);
            return Math.Round(Math.Min(1.0, weightedScore), 4);
        }

        public static List<string> TokenizeAndNormalize(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return new List<string>();

            // Normalize Arabic
            string normalized = input
                .Replace('أ', 'ا')
                .Replace('إ', 'ا')
                .Replace('آ', 'ا')
                .Replace('ة', 'ه')
                .Replace('ى', 'ي');

            // Strip punctuation and special characters
            normalized = Regex.Replace(normalized, @"[^\w\s]", " ");

            var words = normalized.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            var result = new List<string>();

            foreach (var rawWord in words)
            {
                string word = rawWord.Trim().ToLowerInvariant();
                if (word.Length >= 2 && !StopWords.Contains(word))
                {
                    result.Add(word);
                }
            }

            return result;
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
