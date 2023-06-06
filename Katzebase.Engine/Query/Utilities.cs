﻿using Katzebase.PublicLibrary.Exceptions;
using System.Text.RegularExpressions;
using static Katzebase.Engine.KbLib.EngineConstants;

namespace Katzebase.Engine.Query
{
    public class Utilities
    {
        static char[] DefaultTokenDelimiters = new char[] { ',' };

        static public bool IsValidIdentifier(string text)
        {
            var regex = new Regex("^[a-zA-Z_][a-zA-Z0-9_]*$");
            var matches = regex.Matches(text);

            if (matches.Count == 1)
            {
                return (matches[0].Value == text);
            }

            return false;
        }

        static public bool IsValidIdentifier(string text, string ignoreCharacters)
        {
            foreach (char ignore in ignoreCharacters)
            {
                text = text.Replace(ignore.ToString(), "");
            }

            var regex = new Regex("^[a-zA-Z_][a-zA-Z0-9_]*$");
            var matches = regex.Matches(text);

            if (matches.Count == 1)
            {
                return (matches[0].Value == text);
            }

            return false;
        }

        static public LogicalQualifier ParseLogicalQualifier(string text)
        {
            switch (text)
            {
                case "=":
                    return LogicalQualifier.Equals;
                case "!=":
                    return LogicalQualifier.NotEquals;
                case ">":
                    return LogicalQualifier.GreaterThan;
                case "<":
                    return LogicalQualifier.LessThan;
                case ">=":
                    return LogicalQualifier.GreaterThanOrEqual;
                case "<=":
                    return LogicalQualifier.LessThanOrEqual;
                case "~":
                    return LogicalQualifier.Like;
                case "!~":
                    return LogicalQualifier.NotLike;
            }
            return LogicalQualifier.None;
        }

        public static void SkipDelimiters(string query, ref int position)
        {
            SkipDelimiters(query, DefaultTokenDelimiters, ref position);
        }

        public static void SkipWhiteSpace(string query, ref int position)
        {
            while (position < query.Length && char.IsWhiteSpace(query[position]))
            {
                position++;
            }
        }

        public static void SkipDelimiters(string query, char[] delimiters, ref int position)
        {
            while (position < query.Length && (char.IsWhiteSpace(query[position]) || delimiters.Contains(query[position]) == true))
            {
                position++;
            }
        }

        public static string GetNextToken(string query, ref int position)
        {
            return GetNextToken(query, DefaultTokenDelimiters, ref position);
        }

        public static string PeekNextToken(string query, int position)
        {
            int originalPosition = position;
            return GetNextToken(query, DefaultTokenDelimiters, ref originalPosition);
        }

        public static void SkipNextToken(string query, ref int position)
        {
            GetNextToken(query, DefaultTokenDelimiters, ref position);
        }

        public static string PeekNextToken(string query, char[] delimiters, int position)
        {
            int originalPosition = position;
            return GetNextToken(query, delimiters, ref originalPosition);
        }

        public static void SkipNextToken(string query, char[] delimiters, ref int position)
        {
            GetNextToken(query, delimiters, ref position);
        }

        public static string GetNextToken(string query, char[] delimiters, ref int position)
        {
            var token = string.Empty;

            if (position == query.Length)
            {
                return string.Empty;
            }

            for (; position < query.Length; position++)
            {
                if (char.IsWhiteSpace(query[position]) || delimiters.Contains(query[position]) == true)
                {
                    break;
                }

                token += query[position];
            }

            SkipDelimiters(query, ref position);

            return token.Trim().ToLowerInvariant();
        }

        public static string GetFirstClauseToken(string query, out int outPosition)
        {
            int position = 0;
            var result = GetNextClauseToken(query, ref position);
            outPosition = position;
            return result.ToLowerInvariant();
        }

        public static void RewindTo(string str, char c, ref int position)
        {
            while (position > 0 && str[position] != c)
            {
                position--;
            }
        }

        public static void RewindBefore(string str, char c, ref int position)
        {
            while (position > 0 && str[position] != c)
            {
                position--;
            }

            if (position == 0)
            {
                throw new KbParserException("Cannot rewind before position zero.");
            }

            position--;
        }

        public static void ForwardTo(string str, char c, ref int position)
        {
            while (position < str.Length && str[position] != c)
            {
                position++;
            }
        }

        /// <summary>
        /// Used for parsing WHERE clauses.
        /// </summary>
        /// <param name="query"></param>
        /// <param name="position"></param>
        /// <returns></returns>
        public static string GetNextClauseToken(string query, ref int position)
        {
            var token = string.Empty;

            if (position == query.Length)
            {
                return string.Empty;
            }

            if (new char[] { '(', ')' }.Contains(query[position]))
            {
                token += query[position];
                position++;
                SkipWhiteSpace(query, ref position);
                return token;
            }

            for (; position < query.Length; position++)
            {
                if (char.IsWhiteSpace(query[position]) || new char[] { '(', ')' }.Contains(query[position]))
                {
                    break;
                }

                token += query[position];
            }

            SkipWhiteSpace(query, ref position);
            SkipDelimiters(query, ref position);

            return token.Trim().ToLowerInvariant();
        }

        /// <summary>
        /// Removes all unnecessary whitespace, newlines, comments and replaces literals with tokens to prepare query for parsing.
        /// </summary>
        /// <param name="query"></param>
        /// <param name="swapLiteralsBackIn"></param>
        /// <returns></returns>
        public static Dictionary<string, string> CleanQueryText(ref string query, bool swapLiteralsBackIn = false)
        {
            var literalStrings = Utilities.SwapOutLiteralStrings(ref query);
            query = query.Trim().ToLowerInvariant();
            Utilities.RemoveComments(ref query);
            if (swapLiteralsBackIn)
            {
                Utilities.SwapInLiteralStrings(ref query, literalStrings);
            }
            Utilities.TrimAllLines(ref query);
            Utilities.RemoveEmptyLines(ref query);
            Utilities.RemoveNewlines(ref query);
            Utilities.RemoveDoubleWhitespace(ref query);
            query = query.Trim();
            return literalStrings;
        }

        /// <summary>
        /// Replaces literals with tokens to prepare query for parsing.
        /// </summary>
        /// <param name="query"></param>
        /// <returns></returns>
        public static Dictionary<string, string> SwapOutLiteralStrings(ref string query)
        {
            var mappings = new Dictionary<string, string>();

            var regex = new Regex("\"([^\"\\\\]*(\\\\.[^\"\\\\]*)*)\"|\\'([^\\'\\\\]*(\\\\.[^\\'\\\\]*)*)\\'");
            var results = regex.Matches(query);

            foreach (Match match in results)
            {
                string uuid = $"${Guid.NewGuid()}$";

                mappings.Add(uuid, match.ToString());

                query = query.Replace(match.ToString(), uuid);
            }

            return mappings;
        }

        public static void RemoveDoubleWhitespace(ref string query)
        {
            query = Regex.Replace(query, @"\s+", " ");
        }

        public static void RemoveNewlines(ref string query)
        {
            query = query.Replace("\r\n", " ");
        }

        public static void SwapInLiteralStrings(ref string query, Dictionary<string, string> mappings)
        {
            foreach (var mapping in mappings)
            {
                query = query.Replace(mapping.Key, mapping.Value);
            }
        }

        public static void RemoveComments(ref string query)
        {
            query = "\r\n" + query + "\r\n";

            var blockComments = @"/\*(.*?)\*/";
            //var lineComments = @"//(.*?)\r?\n";
            var lineComments = @"--(.*?)\r?\n";
            var strings = @"""((\\[^\n]|[^""\n])*)""";
            var verbatimStrings = @"@(""[^""]*"")+";

            query = Regex.Replace(query,
                blockComments + "|" + lineComments + "|" + strings + "|" + verbatimStrings,
                me =>
                {
                    if (me.Value.StartsWith("/*") || me.Value.StartsWith("--"))
                        return me.Value.StartsWith("--") ? Environment.NewLine : "";
                    // Keep the literal strings
                    return me.Value;
                },
                RegexOptions.Singleline);
        }

        public static void RemoveEmptyLines(ref string query)
        {
            query = Regex.Replace(query, @"^\s+$[\r\n]*", "", RegexOptions.Multiline);
        }

        public static void TrimAllLines(ref string query)
        {
            query = string.Join("\r\n", query.Split('\n').Select(o => o.Trim()));
        }
    }
}
