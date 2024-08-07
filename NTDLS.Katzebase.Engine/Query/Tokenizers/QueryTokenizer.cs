﻿using NTDLS.Katzebase.Client;
using NTDLS.Katzebase.Client.Exceptions;
using NTDLS.Katzebase.Client.Types;
using System.Text.RegularExpressions;
using static NTDLS.Katzebase.Engine.Library.EngineConstants;

namespace NTDLS.Katzebase.Engine.Query.Tokenizers
{
    public class QueryTokenizer
    {
        static readonly char[] DefaultTokenDelimiters = new char[] { ',', '=' };

        private readonly string _text;
        private int _position = 0;
        private readonly int _startPosition = 0;

        public string Text => _text;
        public int Position => _position;
        public int Length => _text.Length;
        public int StartPosition => _startPosition;
        public KbInsensitiveDictionary<string> LiteralStrings { get; private set; }
        public List<string> Breadcrumbs { get; private set; } = new();
        public char? NextCharacter => _position < _text.Length ? _text[_position] : null;
        public bool IsEnd() => _position >= _text.Length;

        public QueryTokenizer(string text)
        {
            _text = text.Trim().TrimEnd(';').Trim();
            LiteralStrings = CleanQueryText(ref _text);
        }

        public QueryTokenizer(string text, int startPosition)
        {
            _text = text;
            _position = startPosition;
            _startPosition = startPosition;
            LiteralStrings = CleanQueryText(ref _text);
        }

        public void SwapFieldLiteral(ref string givenValue)
        {
            if (string.IsNullOrEmpty(givenValue) == false && LiteralStrings.TryGetValue(givenValue, out string? value))
            {
                givenValue = value;

                if (givenValue.StartsWith('\'') && givenValue.EndsWith('\''))
                {
                    givenValue = givenValue.Substring(1, givenValue.Length - 2);
                }
            }
        }

        public void SetPosition(int position)
        {
            _position = position;
            if (_position > _text.Length)
            {
                throw new KbParserException("Skip position is greater than query length.");
            }
        }

        public char CurrentChar()
        {
            if (_position >= Length)
            {
                return '\0';
            }
            return (_text.Substring(_position, 1)[0]);
        }

        public bool IsNextCharacter(char ch)
        {
            if (_position >= Length)
            {
                return false;
            }
            return (_text.Substring(_position, 1)[0] == ch);
        }

        public string Remainder()
        {
            return _text.Substring(_position).Trim();
        }

        public int GetNextTokenAsInt()
        {
            string token = GetNextToken();
            if (int.TryParse(token, out int value) == false)
            {
                throw new KbParserException("Invalid query. Found [" + token + "], expected numeric row limit.");
            }

            return value;
        }

        public string GetNextToken()
        {
            return GetNextToken(DefaultTokenDelimiters);
        }

        public bool IsNextTokenConsume(string[] tokens)
        {
            var token = GetNextToken().ToLowerInvariant();
            foreach (var given in tokens)
            {
                if (token.Equals(given, StringComparison.InvariantCultureIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        public bool IsNextToken(string[] tokens)
        {
            var token = PeekNextToken().ToLowerInvariant();
            foreach (var given in tokens)
            {
                if (token.Equals(given, StringComparison.InvariantCultureIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        public bool IsNextTokenStartOfQuery()
        {
            return IsNextTokenStartOfQuery(out var _);
        }

        public bool IsNextTokenStartOfQuery(out QueryType type)
        {
            var token = PeekNextToken().ToLowerInvariant();

            return Enum.TryParse(token, true, out type) //Enum parse.
                && Enum.IsDefined(typeof(QueryType), type) //Is enum value über lienient.
                && int.TryParse(token, out _) == false; //Is not number, because enum parsing is "too" flexible.
        }

        public bool IsNotNextToken(string[] tokens)
        {
            return !IsNextToken(tokens);
        }

        public bool IsNextTokenConsume(string token)
        {
            return GetNextToken().Equals(token, StringComparison.InvariantCultureIgnoreCase);
        }

        public bool IsNextToken(string token)
        {
            return PeekNextToken().Equals(token, StringComparison.InvariantCultureIgnoreCase);
        }

        public string PeekNextToken()
        {
            int originalPosition = _position;
            var result = GetNextToken(DefaultTokenDelimiters);
            _position = originalPosition;
            return result;
        }

        public void SkipNextToken()
        {
            GetNextToken(DefaultTokenDelimiters);
        }

        public void SkipToEnd()
        {
            _position = _text.Length;
        }

        public string PeekNextToken(char[] delimiters)
        {
            int originalPosition = _position;
            var result = GetNextToken(delimiters);
            _position = originalPosition;
            return result;
        }

        public void SkipNextToken(char[] delimiters)
        {
            GetNextToken(delimiters);
        }

        public string GetNextToken(char[] delimiters)
        {
            var token = string.Empty;

            if (_position == _text.Length)
            {
                Breadcrumbs.Add(string.Empty);
                return string.Empty;
            }

            for (; _position < _text.Length; _position++)
            {
                if (char.IsWhiteSpace(_text[_position]) || delimiters.Contains(_text[_position]) == true)
                {
                    break;
                }

                token += _text[_position];
            }

            SkipWhiteSpace();

            token = token.Trim();

            Breadcrumbs.Add(token);
            return token;
        }

        public void SkipDelimiters()
        {
            SkipDelimiters(DefaultTokenDelimiters);
        }

        public void SkipWhile(char[] chs)
        {
            while (_position < _text.Length && (chs.Contains(_text[_position]) || char.IsWhiteSpace(_text[_position])))
            {
                _position++;
            }
        }

        public void SkipWhile(char ch)
        {
            while (_position < _text.Length && (_text[_position] == ch || char.IsWhiteSpace(_text[_position])))
            {
                _position++;
            }
        }

        public void SkipNextCharacter()
        {
            _position++;

            while (_position < _text.Length && char.IsWhiteSpace(_text[_position]))
            {
                _position++;
            }
        }

        public void SkipWhiteSpace()
        {
            while (_position < _text.Length && char.IsWhiteSpace(_text[_position]))
            {
                _position++;
            }
        }

        public void SkipDelimiters(char delimiter)
        {
            SkipDelimiters([delimiter]);
        }

        public void SkipDelimiters(char[] delimiters)
        {
            while (_position < _text.Length && (char.IsWhiteSpace(_text[_position]) || delimiters.Contains(_text[_position]) == true))
            {
                _position++;
            }
        }

        /// <summary>
        /// Removes all unnecessary whitespace, newlines, comments and replaces literals with tokens to prepare query for parsing.
        /// </summary>
        /// <param name="query"></param>
        /// <param name="swapLiteralsBackIn"></param>
        /// <returns></returns>
        public static KbInsensitiveDictionary<string> CleanQueryText(ref string query, bool swapLiteralsBackIn = false)
        {
            query = KbTextUtility.RemoveComments(query);

            var literalStrings = SwapOutLiteralStrings(ref query);
            query = query.Trim();

            query = query.Replace("(", " ( ").Replace(")", " ) ");

            RemoveComments(ref query);
            if (swapLiteralsBackIn)
            {
                SwapInLiteralStrings(ref query, literalStrings);
            }
            TrimAllLines(ref query);
            RemoveEmptyLines(ref query);
            RemoveNewlines(ref query);
            RemoveDoubleWhitespace(ref query);
            query = query.Trim();
            return literalStrings;
        }

        /// <summary>
        /// Replaces literals with tokens to prepare query for parsing.
        /// </summary>
        /// <param name="query"></param>
        /// <returns></returns>
        public static KbInsensitiveDictionary<string> SwapOutLiteralStrings(ref string query)
        {
            var mappings = new KbInsensitiveDictionary<string>();

            var regex = new Regex("\"([^\"\\\\]*(\\\\.[^\"\\\\]*)*)\"|\\'([^\\'\\\\]*(\\\\.[^\\'\\\\]*)*)\\'");
            var results = regex.Matches(query);

            foreach (Match match in results)
            {
                string guid = $"${Guid.NewGuid()}$";

                mappings.Add(guid, match.ToString());

                query = query.Replace(match.ToString(), guid);
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

        public static void SwapInLiteralStrings(ref string query, KbInsensitiveDictionary<string> mappings)
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
