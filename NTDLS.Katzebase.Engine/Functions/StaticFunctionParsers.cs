﻿using NTDLS.Katzebase.Client.Exceptions;
using NTDLS.Katzebase.Client.Types;
using NTDLS.Katzebase.Engine.Functions.Parameters;
using NTDLS.Katzebase.Engine.Query;
using NTDLS.Katzebase.Engine.Query.Tokenizers;
using System.Text;
using System.Text.RegularExpressions;

namespace NTDLS.Katzebase.Engine.Functions
{
    internal class StaticFunctionParsers
    {
        private static readonly char[] _mathChars = "+-/*!~^()=<>".ToCharArray();

        private class PreparseField
        {
            public string Text { get; set; } = string.Empty;
            public string Alias { get; set; } = string.Empty;
            public bool IsComplex { get; set; }
        }

        internal static FunctionParameterBase ParseProcedureParameters(QueryTokenizer query)
        {
            var preParsed = PreParseFunctionCall(query);
            if (preParsed != null)
            {
                return ParseFunctionCall(preParsed.Text, query.LiteralStrings);
            }

            return new FunctionParameterBase();
        }

        internal static FunctionParameterBaseCollection ParseGroupByFields(QueryTokenizer query)
        {
            var preParsed = PreParseGroupByFields(query);

            var result = new FunctionParameterBaseCollection();

            foreach (var field in preParsed)
            {
                if (field.IsComplex)
                {
                    var functionCall = ParseFunctionCall(field.Text, query.LiteralStrings);
                    functionCall.Alias = field.Alias;
                    result.Add(functionCall);
                }
                else
                {
                    var newField = new FunctionDocumentFieldParameter(field.Text)
                    {
                        Alias = field.Alias
                    };
                    result.Add(newField);
                }
            }

            return result;
        }

        internal static FunctionParameterBaseCollection ParseQueryFields(QueryTokenizer query)
        {
            var preParsed = PreParseQueryFields(query);

            var result = new FunctionParameterBaseCollection();

            foreach (var field in preParsed)
            {
                if (field.IsComplex)
                {
                    var functionCall = ParseFunctionCall(field.Text, query.LiteralStrings);
                    functionCall.Alias = field.Alias;
                    result.Add(functionCall);
                }
                else
                {
                    var newField = new FunctionDocumentFieldParameter(field.Text)
                    {
                        Alias = field.Alias
                    };
                    result.Add(newField);
                }
            }

            return result;
        }

        internal static List<NamedFunctionParameterBaseCollection> ParseInsertFields(QueryTokenizer query)
        {
            var result = new List<NamedFunctionParameterBaseCollection>();


            while (true)
            {
                if (query.IsNextCharacter('('))
                {
                    query.SkipNextCharacter();
                }
                else
                {
                    throw new KbParserException("Invalid query. Found '" + query.NextCharacter + "', expected: '('.");
                }

                var intermediateResult = new NamedFunctionParameterBaseCollection();

                while (true)
                {
                    var preParsed = PreParseInsertFields(query);
                    if (preParsed != null)
                    {
                        foreach (var field in preParsed)
                        {
                            if (field.Value.IsComplex)
                            {
                                var functionCall = ParseFunctionCall(field.Value.Text, query.LiteralStrings);
                                functionCall.Alias = field.Value.Alias;
                                intermediateResult.Add(field.Key, functionCall);
                            }
                            else
                            {
                                var newField = new FunctionConstantParameter(field.Value.Text)
                                {
                                    Alias = field.Value.Alias
                                };
                                intermediateResult.Add(field.Key, newField);
                            }
                        }
                    }

                    if (query.IsNextCharacter(','))
                    {
                        query.SkipNextCharacter();
                    }
                    else if (query.IsNextCharacter(')'))
                    {
                        break;
                    }
                    else
                    {
                        throw new KbParserException($"Invalid query. Found '{query.NextCharacter}', expected: insert expression.");
                    }
                }

                result.Add(intermediateResult);

                if (query.IsNextCharacter(')'))
                {
                    query.SkipNextCharacter();
                }

                if (query.IsNextCharacter(','))
                {
                    query.SkipNextCharacter();
                }
                else
                {
                    break;
                }
            }

            return result;
        }

        private static KbInsensitiveDictionary<PreparseField> PreParseInsertFields(QueryTokenizer query)
        {
            var preparseFields = new KbInsensitiveDictionary<PreparseField>();

            var updateFieldName = string.Empty;
            var param = new StringBuilder();
            var alias = string.Empty;

            int parenScope = 0;
            bool isComplex = false;

            while (true)
            {
                if (updateFieldName == string.Empty)
                {
                    updateFieldName = query.GetNextToken();

                    if (string.IsNullOrEmpty(updateFieldName))
                    {
                        throw new KbParserException($"Invalid query. Found [{updateFieldName}], expected: update field name.");
                    }

                    if (query.NextCharacter != '=')
                    {
                        throw new KbParserException($"Invalid query. Found [{query.NextCharacter}], expected: [=].");
                    }
                    query.SkipNextCharacter();
                }

                var token = query.PeekNextToken([',', '(', ')']);

                if (token != string.Empty && _mathChars.Contains(token[0]) && !(token[0] == '(' || token[0] == ')'))
                {
                    isComplex = true; //Found math token;
                }

                if (token == string.Empty && query.NextCharacter == '(')
                {
                    param.Append(query.NextCharacter);
                    query.SkipNextCharacter();
                    isComplex = true;
                    parenScope++;
                    continue;
                }
                else if (parenScope > 0 && token == string.Empty && query.NextCharacter == ')')
                {
                    param.Append(query.NextCharacter);
                    query.SkipNextCharacter();
                    parenScope--;
                    continue;
                }
                else if (parenScope == 0 && (token == string.Empty || query.NextCharacter == ','))
                {
                    if (parenScope != 0)
                    {
                        throw new KbParserException("Invalid query. Found end of field while still in scope.");
                    }

                    if (param.Length == 0)
                    {
                        throw new KbParserException("Unexpected empty token found at end of statement.");
                    }

                    if (param.Length > 0 && char.IsDigit(param[0]))
                    {
                        isComplex = true;
                    }

                    if (alias == null || alias == string.Empty)
                    {
                        if (isComplex)
                        {
                            alias = $"Expression{preparseFields.Count + 1}";
                        }
                        else
                        {
                            alias = PrefixedField.Parse(param.ToString()).Alias;
                        }
                    }

                    preparseFields.Add(updateFieldName.ToLowerInvariant(), new() { Text = param.ToString(), Alias = alias, IsComplex = isComplex });

                    if (query.IsEnd() || (parenScope == 0 && (query.NextCharacter == ',' || query.NextCharacter == ')')))
                    {
                        return preparseFields;
                    }
                    else
                    {
                        throw new KbParserException($"Unexpected token found at: {token}.");
                    }
                }
                else if (token == string.Empty && query.NextCharacter == ',')
                {
                    param.Append(query.NextCharacter);
                    query.SkipWhile(',');
                    continue;
                }
                else if (token.Equals("as", StringComparison.InvariantCultureIgnoreCase))
                {
                    throw new KbParserException($"Unexpected token found: {token}.");
                }
                else
                {
                    if (token == null || token == string.Empty)
                    {
                        throw new KbParserException("Unexpected empty token found.");
                    }

                    param.Append(query.GetNextToken());
                }
            }
        }


        internal static NamedFunctionParameterBaseCollection ParseUpdateFields(QueryTokenizer query)
        {
            var preParsed = PreParseUpdateFields(query);

            var result = new NamedFunctionParameterBaseCollection();

            foreach (var field in preParsed)
            {
                if (field.Value.IsComplex)
                {
                    var functionCall = ParseFunctionCall(field.Value.Text, query.LiteralStrings);
                    functionCall.Alias = field.Value.Alias;
                    result.Add(field.Key, functionCall);
                }
                else
                {
                    var newField = new FunctionConstantParameter(field.Value.Text)
                    {
                        Alias = field.Value.Alias
                    };
                    result.Add(field.Key, newField);
                }
            }

            return result;
        }

        private static bool IsNextNonIdentifier(string text, int startPos, char c)
        {
            return IsNextNonIdentifier(text, startPos, new char[] { c });
        }

        private static bool IsNextNonIdentifier(string text, int startPos, char[] c)
        {
            for (int i = startPos; i < text.Length; i++)
            {
                if (char.IsWhiteSpace(text[i]))
                {
                }
                else if (char.IsLetterOrDigit(text[i]))
                {
                }
                else if (text[i] == ':')
                {
                }
                else if (text[i] == '.')
                {
                }
                else if (c.Contains(text[i]))
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }

            return false;
        }

        private static FunctionExpression ParseMathExpression(string text, KbInsensitiveDictionary<string> literalValues)
        {
            var expression = new FunctionExpression();
            string param = string.Empty;

            int paramCount = 0;

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];

                if (c == '{')
                {
                    while (i < text.Length && text[i] != '}')
                    {
                        param += text[i++];
                    }
                    continue;
                }
                else if (char.IsLetter(c) && IsNextNonIdentifier(text, i, '('))
                {
                    int startPosition = i;
                    int parenScope = 0;

                    for (int endPosition = startPosition; endPosition < text.Length; endPosition++)
                    {
                        c = text[endPosition];

                        if (c == '(')
                        {
                            parenScope++;
                        }
                        else if (c == ')')
                        {
                            parenScope--;

                            if (parenScope == 0)
                            {
                                string subParamText = text.Substring(startPosition, endPosition - startPosition + 1);

                                string paramKey = $"{{p{paramCount++}}}";
                                var mathParamParams = (FunctionWithParams)ParseFunctionCall(subParamText, literalValues, paramKey);

                                expression.Parameters.Add(mathParamParams);

                                //Replace first occurrence.
                                var regex = new Regex(Regex.Escape(subParamText));
                                text = regex.Replace(text, paramKey, 1);

                                param = string.Empty; //We are starting over.
                                i = -1;
                                break;
                            }
                        }
                    }
                }
                else if (_mathChars.Contains(c) || char.IsDigit(c) || c == '.')
                {
                    param += c;
                }
                else if (c == '$') //Literal string placeholder.
                {
                    param += '$';

                    i++;
                    while (i < text.Length && text[i] != '$')
                    {
                        param += text[i];
                        i++;
                    }
                    param += '$';
                    continue;
                }
                else if (char.IsLetter(c))
                {
                    int startPosition = i;

                    for (int endPosition = startPosition; endPosition < text.Length + 1; endPosition++)
                    {
                        if (endPosition == text.Length || !(char.IsLetterOrDigit(text[endPosition]) || text[endPosition] == '.'))
                        {
                            //We either found the end of the string or found a non identifier character.
                            endPosition--;

                            string subParamText = text.Substring(startPosition, endPosition - startPosition + 1);

                            string paramKey = $"{{p{paramCount++}}}";
                            var mathParamParams = new FunctionDocumentFieldParameter(subParamText, paramKey);

                            expression.Parameters.Add(mathParamParams);

                            //Replace first occurrence.
                            var regex = new Regex(Regex.Escape(subParamText));
                            text = regex.Replace(text, paramKey, 1);

                            param = string.Empty; //We are starting over.
                            i = -1;
                            break;
                        }
                    }
                }
                else
                {
                    throw new KbParserException("Failed to parse mathematical expression.");
                }
            }

            expression.Value = text;

            return expression;
        }

        private static FunctionParameterBase ParseFunctionCall(string text, KbInsensitiveDictionary<string> literalValues, string expressionKey = "")
        {
            char firstChar = text[0];

            if (char.IsNumber(firstChar))
            {
                //Parse math expression.
                return ParseMathExpression(text, literalValues);
            }
            else if (char.IsLetter(firstChar) && IsNextNonIdentifier(text, 0, "+-/*!~^".ToCharArray()))
            {
                return ParseMathExpression(text, literalValues);
            }
            else if (char.IsLetter(firstChar) && IsNextNonIdentifier(text, 0, '('))
            {
                //Parse function call with one or more parameters.

                string param = string.Empty;
                int parenScope = 0;
                bool isComplex = false;
                bool parseMath = false;
                int parenIndex = text.IndexOf('(');

                FunctionWithParams results;

                if (expressionKey != string.Empty)
                {
                    results = new FunctionNamedWithParams(text.Substring(0, parenIndex))
                    {
                        ExpressionKey = expressionKey,
                    };
                }
                else
                {
                    results = new FunctionWithParams(text.Substring(0, parenIndex));
                }

                bool parenScopeFellToZero = false;

                //Parse parameters:
                for (int i = parenIndex; i < text.Length; i++)
                {
                    char c = text[i];

                    if (parenScopeFellToZero && _mathChars.Contains(c))
                    {
                        //We have finished parsing a full (...) scope for a function and now we are finding math. Reset and just parse math.
                        return ParseMathExpression(text, literalValues);
                    }

                    if (_mathChars.Contains(c) && !(c == '(' || c == ')'))
                    {
                        //The parameter contains math characters. '(' and ')' are used for function calls and do not count.
                        parseMath = true;
                    }

                    if (param == string.Empty && char.IsDigit(c))
                    {
                        //The first character of the parameter is a number.
                        parseMath = true;
                    }

                    if (c == '$') //Literal string placeholder.
                    {
                        param += '$';

                        i++;
                        while (i < text.Length && text[i] != '$')
                        {
                            c = text[i];
                            param += text[i];
                            i++;
                        }
                        param += '$';
                        continue;
                    }
                    else if (c == '(')
                    {
                        if (parenScope != 0)
                        {
                            isComplex = true;
                            param += c;
                        }
                        parenScope++;
                    }
                    else if (c == ')')
                    {
                        parenScope--;
                        if (parenScope != 0)
                        {
                            param += c;
                        }

                        if (parenScope == 0)
                        {
                            parenScopeFellToZero = true;
                        }
                    }
                    else if (c == ',')
                    {
                        if (parenScope != 1)
                        {
                            param += c;
                        }
                    }
                    else
                    {
                        param += c;
                    }

                    if (c == ',' && parenScope == 1 || i == text.Length - 1)
                    {
                        if (parseMath)
                        {
                            results.Parameters.Add(ParseMathExpression(param, literalValues));
                        }
                        else if (isComplex)
                        {
                            results.Parameters.Add(ParseFunctionCall(param, literalValues));
                        }
                        else if (param.StartsWith("$") && param.EndsWith("$"))
                        {
                            results.Parameters.Add(new FunctionConstantParameter(literalValues[param]));
                        }
                        else
                        {
                            if (param.Length > 0)
                            {
                                results.Parameters.Add(new FunctionDocumentFieldParameter(param));
                            }
                        }

                        parseMath = false;
                        isComplex = false;
                        param = string.Empty;
                    }
                }

                return results;
            }
            else
            {
                //Parse constant.
                return new FunctionConstantParameter(text);
            }
        }

        private static KbInsensitiveDictionary<PreparseField> PreParseUpdateFields(QueryTokenizer query)
        {
            var preparseFields = new KbInsensitiveDictionary<PreparseField>();

            while (true)
            {
                var updateFieldName = string.Empty;
                var param = new StringBuilder();
                var alias = string.Empty;

                int parenScope = 0;
                bool isComplex = false;

                while (true)
                {
                    if (updateFieldName == string.Empty)
                    {
                        updateFieldName = query.GetNextToken();

                        if (string.IsNullOrEmpty(updateFieldName))
                        {
                            throw new KbParserException($"Invalid query. Found [{updateFieldName}], expected: update field name.");
                        }

                        if (query.NextCharacter != '=')
                        {
                            throw new KbParserException($"Invalid query. Found [{query.NextCharacter}], expected: [=].");
                        }
                        query.SkipNextCharacter();
                    }

                    var token = query.PeekNextToken(new char[] { ',', '(', ')' });

                    if (token != string.Empty && _mathChars.Contains(token[0]) && !(token[0] == '(' || token[0] == ')'))
                    {
                        isComplex = true; //Found math token;
                    }

                    if (token == string.Empty && query.NextCharacter == '(')
                    {
                        param.Append(query.NextCharacter);
                        query.SkipNextCharacter();
                        isComplex = true;
                        parenScope++;
                        continue;
                    }
                    else if (token == string.Empty && query.NextCharacter == ')')
                    {
                        param.Append(query.NextCharacter);
                        query.SkipNextCharacter();
                        parenScope--;
                        continue;
                    }
                    else if (
                            token == string.Empty
                            && query.NextCharacter == ','
                            && parenScope == 0
                            || token.Equals("where", StringComparison.InvariantCultureIgnoreCase)
                            || token == string.Empty
                            && parenScope == 0 && query.IsEnd()
                        )
                    {
                        if (parenScope != 0)
                        {
                            throw new KbParserException("Invalid query. Found end of field while still in scope.");
                        }

                        if (param.Length == 0)
                        {
                            throw new KbParserException("Unexpected empty token found at end of statement.");
                        }

                        if (param.Length > 0 && char.IsDigit(param[0]))
                        {
                            isComplex = true;
                        }

                        if (alias == null || alias == string.Empty)
                        {
                            if (isComplex)
                            {
                                alias = $"Expression{preparseFields.Count + 1}";
                            }
                            else
                            {
                                alias = PrefixedField.Parse(param.ToString()).Alias;
                            }
                        }

                        preparseFields.Add(updateFieldName.ToLowerInvariant(), new PreparseField { Text = param.ToString(), Alias = alias, IsComplex = isComplex });

                        updateFieldName = string.Empty;

                        if (query.NextCharacter != ',')
                        {
                            return preparseFields;
                        }

                        //Done with this parameter.
                        query.SkipWhile(',');

                        /*
                        if (token.ToLowerInvariant() == "from")
                        {
                            return preparseFields;
                        }
                        else if (token.ToLowerInvariant() == "into")
                        {
                            return preparseFields;
                        }
                        */

                        isComplex = false;

                        break;
                    }
                    else if (token == string.Empty && query.NextCharacter == ',')
                    {
                        param.Append(query.NextCharacter);
                        query.SkipWhile(',');
                        continue;
                    }
                    /*
                    else if (token.ToLowerInvariant() == "as")
                    {
                        query.SkipNextToken();
                        alias = query.GetNextToken();
                        continue;
                    }
                    */
                    else
                    {
                        if (token == null || token == string.Empty)
                        {
                            throw new KbParserException("Unexpected empty token found.");
                        }

                        param.Append(query.GetNextToken());
                    }
                }
            }
        }

        private static List<PreparseField> PreParseQueryFields(QueryTokenizer query)
        {
            var preparseFields = new List<PreparseField>();

            while (true)
            {
                var param = new StringBuilder();
                var alias = string.Empty;

                int parenScope = 0;
                bool isComplex = false;

                while (true)
                {
                    var token = query.PeekNextToken(new char[] { ',', '(', ')' });

                    if (token != string.Empty && _mathChars.Contains(token[0]) && !(token[0] == '(' || token[0] == ')'))
                    {
                        isComplex = true; //Found math token;
                    }

                    if (token == string.Empty && query.NextCharacter == '(')
                    {
                        param.Append(query.NextCharacter);
                        query.SkipNextCharacter();
                        isComplex = true;
                        parenScope++;
                        continue;
                    }
                    else if (token == string.Empty && query.NextCharacter == ')')
                    {
                        param.Append(query.NextCharacter);
                        query.SkipNextCharacter();
                        parenScope--;
                        continue;
                    }
                    else if (token == string.Empty && query.NextCharacter == ',' && parenScope == 0
                        || token.Equals("from", StringComparison.InvariantCultureIgnoreCase) || token.ToLowerInvariant() == "into")
                    {
                        if (parenScope != 0)
                        {
                            throw new KbParserException("Invalid query. Found end of field while still in scope.");
                        }

                        if (param.Length == 0)
                        {
                            throw new KbParserException("Unexpected empty token found at end of statement.");
                        }

                        if (param.Length > 0)
                        {
                            var str = param.ToString();

                            //Is the parameter a number or a string?
                            if (char.IsDigit(str[0]) || (str.StartsWith('$') && str.EndsWith('$')))
                            {
                                isComplex = true;
                            }
                        }

                        if (alias == null || alias == string.Empty)
                        {
                            if (isComplex)
                            {
                                alias = $"Expression{preparseFields.Count + 1}";
                            }
                            else
                            {
                                alias = PrefixedField.Parse(param.ToString()).Alias;
                            }
                        }

                        preparseFields.Add(new PreparseField
                        {
                            Text = param.ToString(),
                            Alias = alias,
                            IsComplex = isComplex
                        });

                        //Done with this parameter.
                        query.SkipWhile(',');

                        if (token.Equals("from", StringComparison.InvariantCultureIgnoreCase))
                        {
                            return preparseFields;
                        }
                        else if (token.Equals("into", StringComparison.InvariantCultureIgnoreCase))
                        {
                            return preparseFields;
                        }

                        isComplex = false;

                        break;
                    }
                    else if (token == string.Empty && query.NextCharacter == ',')
                    {
                        param.Append(query.NextCharacter);
                        query.SkipWhile(',');
                        continue;
                    }
                    else if (token.Equals("as", StringComparison.InvariantCultureIgnoreCase))
                    {
                        query.SkipNextToken();
                        alias = query.GetNextToken();
                        continue;
                    }
                    else
                    {
                        if (token == null || token == string.Empty)
                        {
                            throw new KbParserException("Unexpected empty token found.");
                        }

                        param.Append(query.GetNextToken());
                    }
                }
            }
        }

        private static List<PreparseField> PreParseGroupByFields(QueryTokenizer query)
        {
            var preparseFields = new List<PreparseField>();

            while (true)
            {
                var param = new StringBuilder();
                var alias = string.Empty;

                int parenScope = 0;
                bool isComplex = false;

                while (true)
                {
                    var token = query.PeekNextToken(new char[] { ',', '(', ')' });

                    if (token != string.Empty && _mathChars.Contains(token[0]) && !(token[0] == '(' || token[0] == ')'))
                    {
                        isComplex = true; //Found math token;
                    }

                    if (token == string.Empty && query.NextCharacter == '(')
                    {
                        param.Append(query.NextCharacter);
                        query.SkipNextCharacter();
                        isComplex = true;
                        parenScope++;
                        continue;
                    }
                    else if (token == string.Empty && query.NextCharacter == ')')
                    {
                        param.Append(query.NextCharacter);
                        query.SkipNextCharacter();
                        parenScope--;
                        continue;
                    }
                    else if (
                            token == string.Empty
                            && query.NextCharacter == ','
                            && parenScope == 0
                            || token.Equals("order", StringComparison.InvariantCultureIgnoreCase)
                            || token == string.Empty && parenScope == 0 && query.IsEnd()
                        )
                    {
                        if (parenScope != 0)
                        {
                            throw new KbParserException("Invalid query. Found end of field while still in scope.");
                        }

                        if (param.Length == 0)
                        {
                            throw new KbParserException("Unexpected empty token found at end of statement.");
                        }

                        if (param.Length > 0 && char.IsDigit(param[0]))
                        {
                            isComplex = true;
                        }

                        if (alias == null || alias == string.Empty)
                        {
                            if (isComplex)
                            {
                                alias = $"Expression{preparseFields.Count + 1}";
                            }
                            else
                            {
                                alias = PrefixedField.Parse(param.ToString()).Alias;
                            }
                        }

                        preparseFields.Add(new PreparseField { Text = param.ToString(), Alias = alias, IsComplex = isComplex });

                        //Done with this parameter.
                        query.SkipWhile(',');

                        if (token.Equals("order", StringComparison.InvariantCultureIgnoreCase))
                        {
                            return preparseFields;
                        }
                        else if (token == string.Empty && parenScope == 0 && query.IsEnd())
                        {
                            return preparseFields;
                        }

                        isComplex = false;

                        break;
                    }
                    else if (token == string.Empty && query.NextCharacter == ',')
                    {
                        param.Append(query.NextCharacter);
                        query.SkipWhile(',');
                        continue;
                    }
                    else if (token.Equals("as", StringComparison.InvariantCultureIgnoreCase))
                    {
                        throw new KbParserException("Unexpected token [as] found.");
                    }
                    else
                    {
                        if (token == null || token == string.Empty)
                        {
                            throw new KbParserException("Unexpected empty token found.");
                        }

                        param.Append(query.GetNextToken());
                    }
                }
            }
        }

        private static PreparseField PreParseFunctionCall(QueryTokenizer query)
        {
            while (true)
            {
                var param = new StringBuilder();
                int parenScope = 0;

                var token = query.GetNextToken();
                if (token == null || token == string.Empty)
                {
                    throw new KbParserException("Found empty token, expected procedure name");
                }

                param.Append(token);

                while (true)
                {
                    token = query.PeekNextToken(new char[] { ',', '(', ')' });

                    if (query.NextCharacter == '(')
                    {
                        param.Append(query.NextCharacter);
                        query.SkipNextCharacter();
                        parenScope++;
                        continue;
                    }
                    else if (query.NextCharacter == ')')
                    {
                        param.Append(query.NextCharacter);
                        query.SkipNextCharacter();
                        parenScope--;
                        continue;
                    }
                    else if (parenScope == 0)
                    {
                        return new PreparseField { Text = param.ToString(), IsComplex = true };
                    }
                    else if (query.NextCharacter == ',')
                    {
                        param.Append(query.NextCharacter);
                        query.SkipWhile(',');
                        continue;
                    }
                    else
                    {
                        if (token == null || token == string.Empty)
                        {
                            throw new KbParserException("Unexpected empty token found.");
                        }

                        param.Append(query.GetNextToken());
                    }
                }
            }
        }
    }
}
