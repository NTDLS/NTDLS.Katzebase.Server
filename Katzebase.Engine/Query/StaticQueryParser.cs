﻿using Katzebase.Engine.Query.Constraints;
using Katzebase.Engine.Query.Function;
using Katzebase.Engine.Query.Tokenizers;
using Katzebase.PublicLibrary.Exceptions;
using static Katzebase.Engine.Library.EngineConstants;
using static Katzebase.PublicLibrary.KbConstants;

namespace Katzebase.Engine.Query
{
    internal class StaticQueryParser
    {
        static public PreparedQuery PrepareQuery(string queryText)
        {
            PreparedQuery result = new PreparedQuery();

            var query = new QueryTokenizer(queryText);

            string token = query.GetNextToken().ToLower();

            if (Enum.TryParse(token, true, out QueryType queryType) == false || Enum.IsDefined(typeof(QueryType), queryType) == false)
            {
                string acceptibleValues = string.Join("', '", ((QueryType[])Enum.GetValues(typeof(QueryType))).Where(o => o != QueryType.None));
                throw new KbParserException($"Invalid query. Found '{token}', expected: '{acceptibleValues}'.");
            }

            result.QueryType = queryType;

            //Parser insanity. Keep these region tags at 100 characters! :D

            #region Exec -----------------------------------------------------------------------------------------------
            if (queryType == QueryType.Exec)
            {
                result.ProcedureCall = StaticFunctionParsers.ParseProcedureParameters(query);

                if (query.IsEnd() == false)
                {
                    throw new KbParserException("Invalid query. Found '" + query.PeekNextToken() + "', expected: end of statement.");
                }
            }
            #endregion

            #region Begin ----------------------------------------------------------------------------------------------
            else if (queryType == QueryType.Begin)
            {
                if (query.IsNextToken(new string[] { "transaction" }) == false)
                {
                    throw new KbParserException("Invalid query. Found '" + token + "', expected: 'transaction'.");
                }

                token = query.GetNextToken();
                if (Enum.TryParse<SubQueryType>(token, true, out SubQueryType subQueryType) == false)
                {
                    throw new KbParserException("Invalid query. Found '" + token + "', expected: 'transaction'.");
                }
                result.SubQueryType = subQueryType;

                if (query.IsEnd() == false)
                {
                    throw new KbParserException("Invalid query. Found '" + query.PeekNextToken() + "', expected: end of statement.");
                }
            }
            #endregion

            #region Commit ---------------------------------------------------------------------------------------------
            else if (queryType == QueryType.Commit)
            {
                if (query.IsNextToken(new string[] { "transaction" }) == false)
                {
                    throw new KbParserException("Invalid query. Found '" + token + "', expected: 'transaction'.");
                }

                token = query.GetNextToken();
                if (Enum.TryParse<SubQueryType>(token, true, out SubQueryType subQueryType) == false)
                {
                    throw new KbParserException("Invalid query. Found '" + token + "', expected: 'transaction.");
                }
                result.SubQueryType = subQueryType;

                if (query.IsEnd() == false)
                {
                    throw new KbParserException("Invalid query. Found '" + query.PeekNextToken() + "', expected: end of statement.");
                }
            }
            #endregion

            #region Rollback -------------------------------------------------------------------------------------------
            else if (queryType == QueryType.Rollback)
            {
                if (query.IsNextToken(new string[] { "transaction" }) == false)
                {
                    throw new KbParserException("Invalid query. Found '" + token + "', expected: 'transaction'.");
                }

                token = query.GetNextToken();
                if (Enum.TryParse<SubQueryType>(token, true, out SubQueryType subQueryType) == false)
                {
                    throw new KbParserException("Invalid query. Found '" + token + "', expected: 'transaction'.");
                }
                result.SubQueryType = subQueryType;

                if (query.IsEnd() == false)
                {
                    throw new KbParserException("Invalid query. Found '" + query.PeekNextToken() + "', expected: end of statement.");
                }
            }
            #endregion

            #region Create ---------------------------------------------------------------------------------------------
            else if (queryType == QueryType.Create)
            {
                if (query.IsNextToken(new string[] { "schema", "index", "uniquekey" }) == false)
                {
                    throw new KbParserException("Invalid query. Found '" + token + "', expected 'schema', 'index' or 'uniquekey'.");
                }

                token = query.GetNextToken();
                if (Enum.TryParse(token, true, out SubQueryType subQueryType) == false)
                {
                    throw new KbParserException("Invalid query. Found '" + token + "', expected: 'schema', 'index' or 'uniquekey'.");
                }
                result.SubQueryType = subQueryType;

                result.AddAttribute(PreparedQuery.QueryAttribute.IsUnique, (subQueryType == SubQueryType.UniqueKey));

                token = query.GetNextToken();
                if (token == string.Empty)
                {
                    throw new KbParserException("Invalid query. Found '" + token + "', expected: object name.");
                }

                if (subQueryType == SubQueryType.Schema)
                {
                    result.Schemas.Add(new QuerySchema(token));
                }
                else if (subQueryType == SubQueryType.Index || subQueryType == SubQueryType.UniqueKey)
                {
                    result.AddAttribute(PreparedQuery.QueryAttribute.IndexName, token);

                    if (query.NextCharacter != '(')
                    {
                        throw new KbParserException("Invalid query. Found '" + query.NextCharacter + "', expected: ','.");
                    }
                    query.SkipDelimiters('(');

                    while (true) //Get fields
                    {
                        token = query.GetNextToken().ToLower();
                        if (token == string.Empty)
                        {
                            throw new KbParserException("Invalid query. Found '" + query.PeekNextToken() + "', expected: ',' or ')'.");
                        }

                        result.CreateFields.Add(token);

                        if (query.NextCharacter == ',')
                        {
                            query.SkipDelimiters(',');
                        }
                        if (query.NextCharacter == ')')
                        {
                            query.SkipDelimiters(')');
                            break;
                        }
                    }

                    token = query.GetNextToken().ToLower();
                    if (token != "on")
                    {
                        throw new KbParserException("Invalid query. Found '" + token + "', expected: 'on'.");
                    }

                    token = query.GetNextToken();
                    if (token == string.Empty)
                    {
                        throw new KbParserException("Invalid query. Found '" + token + "', expected: schema name.");
                    }

                    result.Schemas.Add(new QuerySchema(token));
                }
                else
                {
                    throw new KbNotImplementedException();
                }

                if (query.IsEnd() == false)
                {
                    throw new KbParserException("Invalid query. Found '" + query.PeekNextToken() + "', expected: end of statement.");
                }
            }
            #endregion

            #region Drop -----------------------------------------------------------------------------------------------
            else if (queryType == QueryType.Drop)
            {
                if (query.IsNextToken(new string[] { "schema", "index", "uniquekey" }) == false)
                {
                    throw new KbParserException("Invalid query. Found '" + token + "', expected: 'schema', 'index' or 'uniquekey'.");
                }

                token = query.GetNextToken();
                if (Enum.TryParse(token, true, out SubQueryType subQueryType) == false)
                {
                    throw new KbParserException("Invalid query. Found '" + token + "', expected: 'index' or 'uniquekey'.");
                }
                result.SubQueryType = subQueryType;

                if (subQueryType == SubQueryType.Index || subQueryType == SubQueryType.UniqueKey)
                {
                    token = query.GetNextToken();
                    if (token == string.Empty)
                    {
                        throw new KbParserException("Invalid query. Found '" + token + "', expected: object name.");
                    }
                    result.AddAttribute(PreparedQuery.QueryAttribute.IndexName, token);

                    token = query.GetNextToken().ToLower();
                    if (token != "on")
                    {
                        throw new KbParserException("Invalid query. Found '" + token + "', expected: 'on'.");
                    }
                }

                token = query.GetNextToken();
                if (token == string.Empty)
                {
                    throw new KbParserException("Invalid query. Found '" + token + "', expected: schema name.");
                }

                result.Schemas.Add(new QuerySchema(token));

                if (query.IsEnd() == false)
                {
                    throw new KbParserException("Invalid query. Found '" + query.PeekNextToken() + "', expected: end of statement.");
                }
            }
            #endregion

            #region Rebuild --------------------------------------------------------------------------------------------
            else if (queryType == QueryType.Rebuild)
            {
                if (query.IsNextToken(new string[] { "index", "uniquekey" }) == false)
                {
                    throw new KbParserException("Invalid query. Found '" + token + "', expected: 'index' or 'uniquekey'.");
                }

                token = query.GetNextToken();
                if (Enum.TryParse<SubQueryType>(token, true, out SubQueryType subQueryType) == false)
                {
                    throw new KbParserException("Invalid query. Found '" + token + "', expected: 'index' or 'uniquekey'.");
                }
                result.SubQueryType = subQueryType;

                token = query.GetNextToken();
                if (token == string.Empty)
                {
                    throw new KbParserException("Invalid query. Found '" + token + "', expected: index name.");
                }
                result.AddAttribute(PreparedQuery.QueryAttribute.IndexName, token);

                token = query.GetNextToken().ToLower();
                if (token != "on")
                {
                    throw new KbParserException("Invalid query. Found '" + token + "', expected: 'on'.");
                }

                token = query.GetNextToken();
                if (token == string.Empty)
                {
                    throw new KbParserException("Invalid query. Found '" + token + "', expected: schema name.");
                }

                result.Schemas.Add(new QuerySchema(token));

                if (query.IsEnd() == false)
                {
                    throw new KbParserException("Invalid query. Found '" + query.PeekNextToken() + "', expected: end of statement.");
                }
            }
            #endregion

            #region Update ---------------------------------------------------------------------------------------------
            else if (queryType == QueryType.Update)
            {
                /*
                token = query.GetNextToken();
                if (token.ToLower() == "top")
                {
                    token = query.GetNextToken();
                    int rowLimit = 0;

                    if (Int32.TryParse(token, out rowLimit) == false)
                    {
                        throw new KbParserException("Invalid query. Found '" + token + "', expected: numeric row limit.");
                    }

                    result.RowLimit = rowLimit;

                    //Get schema name:
                    token = query.GetNextToken();
                }

                if (token == string.Empty || Utilities.IsValidIdentifier(token, "/\\") == false)
                {
                    throw new KbParserException("Invalid query. Found '" + token + "', expected: schema name.");
                }
                result.Schema = token;

                token = query.GetNextToken();
                if (token.ToLower() != "set")
                {
                    throw new KbParserException("Invalid query. Found '" + token + "', expected: 'set'.");
                }

                result.UpsertKeyValuePairs = ParseUpsertKeyValues(query, ref position);

                token = query.GetNextToken();
                if (token != string.Empty && token.ToLower() != "where")
                {
                    throw new KbParserException("Invalid query. Found '" + token + "', expected: 'where' or end of statement.");
                }

                if (token.ToLower() == "where")
                {
                    string conditionText = query.Substring(position).Trim();
                    if (conditionText == string.Empty)
                    {
                        throw new KbParserException("Invalid query. Found '" + token + "', expected: list of conditions.");
                    }

                    result.Conditions = ParseConditions(conditionText, literalStrings);
                }
                else if (token != string.Empty)
                {
                    throw new KbParserException("Invalid query. Found '" + token + "', expected: end of statement.");
                }
                */
            }
            #endregion

            #region Sample ---------------------------------------------------------------------------------------------
            else if (queryType == QueryType.Sample)
            {
                if (query.IsNextToken("documents") == false)
                {
                    throw new KbParserException("Invalid query. Found '" + token + "', expected: 'documents'.");
                }

                token = query.GetNextToken();
                if (Enum.TryParse<SubQueryType>(token, true, out SubQueryType subQueryType) == false)
                {
                    throw new KbParserException("Invalid query. Found '" + token + "', expected: 'documents'.");
                }
                result.SubQueryType = subQueryType;

                token = query.GetNextToken();
                if (token == string.Empty)
                {
                    throw new KbParserException("Invalid query. Found '" + token + "', expected: schema name.");
                }

                result.Schemas.Add(new QuerySchema(token));

                token = query.GetNextToken();
                if (int.TryParse(token, out int topCount) == false)
                {
                    throw new KbParserException("Invalid query. Found '" + token + "', expected: numeric top count.");
                }
                result.RowLimit = topCount;

                if (query.IsEnd() == false)
                {
                    throw new KbParserException("Invalid query. Found '" + query.PeekNextToken() + "', expected: end of statement.");
                }
            }
            #endregion

            #region List -----------------------------------------------------------------------------------------------
            else if (queryType == QueryType.List)
            {
                if (query.IsNextToken(new string[] { "documents", "schemas" }) == false)
                {
                    throw new KbParserException("Invalid query. Found '" + token + "', expected: 'documents' or 'schemas'.");
                }

                token = query.GetNextToken();
                if (Enum.TryParse<SubQueryType>(token, true, out SubQueryType subQueryType) == false)
                {
                    throw new KbParserException("Invalid query. Found '" + token + "', expected: 'documents' or 'schemas'.");
                }
                result.SubQueryType = subQueryType;

                token = query.GetNextToken();
                if (token == string.Empty)
                {
                    throw new KbParserException("Invalid query. Found '" + token + "', expected: schema name.");
                }

                result.Schemas.Add(new QuerySchema(token));

                token = query.GetNextToken();
                if (token != string.Empty)
                {
                    if (int.TryParse(token, out int topCount) == false)
                    {
                        throw new KbParserException("Invalid query. Found '" + token + "', expected: numeric top count.");
                    }
                    result.RowLimit = topCount;
                }

                if (query.IsEnd() == false)
                {
                    throw new KbParserException("Invalid query. Found '" + query.PeekNextToken() + "', expected: end of statement.");
                }
            }
            #endregion

            #region Select ---------------------------------------------------------------------------------------------
            else if (queryType == QueryType.Select)
            {
                if (query.IsNextToken("top"))
                {
                    query.SkipNextToken();
                    result.RowLimit = query.GetNextTokenAsInt();
                }

                if (query.IsNextToken("*"))
                {
                    query.SkipNextToken();
                    result.DynamicallyBuildSelectList = true;
                }
                else
                {
                    result.SelectFields = StaticFunctionParsers.ParseQueryFields(query);
                    result.SelectFields.RefillStringLiterals(query.LiteralStrings);
                }

                if (query.IsNextToken("into"))
                {
                    query.SkipNextToken();
                    var selectIntoSchema = query.GetNextToken();
                    result.AddAttribute(PreparedQuery.QueryAttribute.TargetSchema, selectIntoSchema);

                    result.QueryType = QueryType.SelectInto;

                }

                if (query.IsNextToken("from"))
                {
                    query.SkipNextToken();
                }
                else
                {
                    throw new KbParserException("Invalid query. Found '" + query.PeekNextToken() + "', expected: 'from'.");
                }

                string sourceSchema = query.GetNextToken();
                string schemaAlias = string.Empty;
                if (sourceSchema == string.Empty || TokenHelpers.IsValidIdentifier(sourceSchema, "#:") == false)
                {
                    throw new KbParserException("Invalid query. Found '" + sourceSchema + "', expected: schema name.");
                }

                if (query.IsNextToken("as"))
                {
                    query.SkipNextToken();
                    schemaAlias = query.GetNextToken();
                }

                result.Schemas.Add(new QuerySchema(sourceSchema.ToLower(), schemaAlias.ToLower()));

                while (query.IsNextToken("inner"))
                {
                    query.SkipNextToken();
                    if (query.IsNextToken("join") == false)
                    {
                        throw new KbParserException("Invalid query. Found '" + query.GetNextToken() + "', expected: 'join'.");
                    }
                    query.SkipNextToken();

                    string subSchemaSchema = query.GetNextToken();
                    string subSchemaAlias = string.Empty;
                    if (subSchemaSchema == string.Empty || TokenHelpers.IsValidIdentifier(subSchemaSchema, ":") == false)
                    {
                        throw new KbParserException("Invalid query. Found '" + token + "', expected: schema name.");
                    }

                    if (query.IsNextToken("as"))
                    {
                        query.SkipNextToken();
                        subSchemaAlias = query.GetNextToken();
                    }
                    else
                    {
                        throw new KbParserException("Invalid query. Found '" + query.GetNextToken() + "', expected: 'as' (schema alias).");
                    }

                    token = query.GetNextToken();
                    if (token.ToLower() != "on")
                    {
                        throw new KbParserException("Invalid query. Found '" + token + "', expected 'on'.");
                    }

                    int joinConditionsStartPosition = query.Position;

                    while (true)
                    {
                        if (query.IsNextToken(new string[] { "where", "inner", "" }))
                        {
                            break;
                        }

                        if (query.IsNextToken(new string[] { "and", "or" }))
                        {
                            query.SkipNextToken();
                        }

                        var joinLeftCondition = query.GetNextToken();
                        if (joinLeftCondition == string.Empty || TokenHelpers.IsValidIdentifier(joinLeftCondition, ".") == false)
                        {
                            throw new KbParserException("Invalid query. Found '" + joinLeftCondition + "', expected: left side of join expression.");
                        }

                        int logicalQualifierPos = query.Position;

                        token = ConditionTokenizer.GetNextToken(query.Text, ref logicalQualifierPos);
                        if (ConditionTokenizer.ParseLogicalQualifier(token) == LogicalQualifier.None)
                        {
                            throw new KbParserException("Invalid query. Found '" + token + "], logical qualifier.");
                        }

                        query.SetPosition(logicalQualifierPos);

                        var joinRightCondition = query.GetNextToken();
                        if (joinRightCondition == string.Empty || TokenHelpers.IsValidIdentifier(joinRightCondition, ".") == false)
                        {
                            throw new KbParserException("Invalid query. Found '" + joinRightCondition + "', expected: right side of join expression.");
                        }
                    }

                    var joinConditionsText = query.Text.Substring(joinConditionsStartPosition, query.Position - joinConditionsStartPosition).Trim();
                    var joinConditions = Conditions.Create(joinConditionsText, query.LiteralStrings, subSchemaAlias);

                    result.Schemas.Add(new QuerySchema(subSchemaSchema.ToLower(), subSchemaAlias.ToLower(), joinConditions));
                }

                if (query.IsNextToken("where"))
                {
                    query.SkipNextToken();

                    var conditionTokenizer = new ConditionTokenizer(query.Text, query.Position);

                    while (true)
                    {
                        int previousTokenPosition = conditionTokenizer.Position;
                        var conditonToken = conditionTokenizer.GetNextToken();

                        if (((new string[] { "order", "group", "" }).Contains(conditonToken) && conditionTokenizer.IsNextToken("by"))
                            || conditonToken == string.Empty)
                        {
                            //Set both the conditition and query position to the begining of the "ORDER BY" or "GROUP BY".
                            conditionTokenizer.SetPosition(previousTokenPosition);
                            query.SetPosition(previousTokenPosition);
                            break;
                        }
                    }

                    string conditionText = query.Text.Substring(conditionTokenizer.StartPosition, conditionTokenizer.Position - conditionTokenizer.StartPosition).Trim();
                    if (conditionText == string.Empty)
                    {
                        throw new KbParserException("Invalid query. Found '" + token + "', expected: list of conditions.");
                    }

                    result.Conditions = Conditions.Create(conditionText, query.LiteralStrings);
                }

                if (query.IsNextToken("group"))
                {
                    query.SkipNextToken();

                    if (query.IsNextToken("by") == false)
                    {
                        throw new KbParserException("Invalid query. Found '" + query.GetNextToken() + "', expected: 'by'.");
                    }
                    query.SkipNextToken();

                    result.GroupFields = StaticFunctionParsers.ParseGroupByFields(query);

                    /*

                    while (true)
                    {
                        int previousTokenPosition = query.Position;
                        var fieldToken = query.GetNextToken();

                        if (result.SortFields.Count > 0)
                        {
                            if (query.NextCharacter == ',')
                            {
                                query.SkipDelimiters();
                                fieldToken = query.GetNextToken();
                            }
                            else if (!(query.Position < query.Length || query.IsNextToken("order") == false)) //We should have consumed the entire GROUP BY at this point.
                            {
                                throw new KbParserException("Invalid query. Found '" + fieldToken + "', expected: ','.");
                            }
                        }

                        if (((new string[] { "order", "" }).Contains(fieldToken) && query.IsNextToken("by")) || fieldToken == string.Empty)
                        {
                            //Set query position to the begining of the "ORDER BY"..
                            query.SetPosition(previousTokenPosition);
                            break;
                        }

                        result.GroupFields.Add(fieldToken);

                        if (query.NextCharacter == ',')
                        {
                            query.SkipDelimiters();
                        }
                    }
                    */
                }

                if (query.IsNextToken("order"))
                {
                    query.SkipNextToken();

                    if (query.IsNextToken("by") == false)
                    {
                        throw new KbParserException("Invalid query. Found '" + query.GetNextToken() + "', expected: 'by'.");
                    }
                    query.SkipNextToken();

                    var fields = new List<string>();

                    while (true)
                    {
                        int previousTokenPosition = query.Position;
                        var fieldToken = query.GetNextToken();

                        if (result.SortFields.Count > 0)
                        {
                            if (query.NextCharacter == ',')
                            {
                                query.SkipDelimiters();
                                fieldToken = query.GetNextToken();
                            }
                            else if (query.Position < query.Length) //We should have consumed the entire query at this point.
                            {
                                throw new KbParserException("Invalid query. Found '" + fieldToken + "', expected: ','.");
                            }
                        }

                        if (fieldToken == string.Empty)
                        {
                            if (query.Position < query.Length)
                            {
                                throw new KbParserException("Invalid query. Found '" + query.Remainder() + "', expected: end of statement.");
                            }

                            query.SetPosition(previousTokenPosition);
                            break;
                        }

                        var sortDirection = KbSortDirection.Ascending;
                        if (query.IsNextToken(new string[] { "asc", "desc" }))
                        {
                            if (query.IsNextTokenConsume("desc"))
                            {
                                sortDirection = KbSortDirection.Descending;
                            }
                        }

                        result.SortFields.Add(fieldToken, sortDirection);
                    }
                }
            }
            #endregion

            #region Delete ---------------------------------------------------------------------------------------------
            else if (queryType == QueryType.Delete)
            {
                token = query.GetNextToken().ToLower();
                if (token != "from")
                {
                    result.Attributes.Add(PreparedQuery.QueryAttribute.SpecificSchemaPrefix, token);

                    if (query.IsNextTokenConsume("from") == false)
                    {
                        throw new KbParserException("Invalid query. Found '" + query.Breadcrumbs.Last() + "', expected: 'from'.");
                    }
                }

                string sourceSchema = query.GetNextToken();
                string schemaAlias = string.Empty;
                if (sourceSchema == string.Empty || TokenHelpers.IsValidIdentifier(sourceSchema, ":") == false)
                {
                    throw new KbParserException("Invalid query. Found '" + token + "', expected: schema name.");
                }

                if (query.IsNextToken("as"))
                {
                    query.SkipNextToken();
                    schemaAlias = query.GetNextToken();
                }

                result.Schemas.Add(new QuerySchema(sourceSchema.ToLower(), schemaAlias.ToLower()));

                while (query.IsNextToken("inner"))
                {
                    query.SkipNextToken();
                    if (query.IsNextToken("join") == false)
                    {
                        throw new KbParserException("Invalid query. Found '" + query.GetNextToken() + "', expected: join.");
                    }
                    query.SkipNextToken();

                    string subSchemaSchema = query.GetNextToken();
                    string subSchemaAlias = string.Empty;
                    if (subSchemaSchema == string.Empty || TokenHelpers.IsValidIdentifier(subSchemaSchema, ":") == false)
                    {
                        throw new KbParserException("Invalid query. Found '" + token + "', expected: schema name.");
                    }

                    if (query.IsNextToken("as"))
                    {
                        query.SkipNextToken();
                        subSchemaAlias = query.GetNextToken();
                    }
                    else
                    {
                        throw new KbParserException("Invalid query. Found '" + query.GetNextToken() + "', expected: 'as' (schema alias).");
                    }

                    token = query.GetNextToken();
                    if (token.ToLower() != "on")
                    {
                        throw new KbParserException("Invalid query. Found '" + token + "', expected: 'on'.");
                    }

                    int joinConditionsStartPosition = query.Position;

                    while (true)
                    {
                        if (query.IsNextToken(new string[] { "where", "inner", "" }))
                        {
                            break;
                        }

                        if (query.IsNextToken(new string[] { "and", "or" }))
                        {
                            query.SkipNextToken();
                        }

                        var joinLeftCondition = query.GetNextToken();
                        if (joinLeftCondition == string.Empty || TokenHelpers.IsValidIdentifier(joinLeftCondition, ".") == false)
                        {
                            throw new KbParserException("Invalid query. Found '" + joinLeftCondition + "', expected: left side of join expression.");
                        }

                        int logicalQualifierPos = query.Position;

                        token = ConditionTokenizer.GetNextToken(query.Text, ref logicalQualifierPos);
                        if (ConditionTokenizer.ParseLogicalQualifier(token) == LogicalQualifier.None)
                        {
                            throw new KbParserException("Invalid query. Found '" + token + "], logical qualifier.");
                        }

                        query.SetPosition(logicalQualifierPos);

                        var joinRightCondition = query.GetNextToken();
                        if (joinRightCondition == string.Empty || TokenHelpers.IsValidIdentifier(joinRightCondition, ".") == false)
                        {
                            throw new KbParserException("Invalid query. Found '" + joinRightCondition + "', expected: right side of join expression.");
                        }
                    }

                    var joinConditionsText = query.Text.Substring(joinConditionsStartPosition, query.Position - joinConditionsStartPosition).Trim();
                    var joinConditions = Conditions.Create(joinConditionsText, query.LiteralStrings, subSchemaAlias);

                    result.Schemas.Add(new QuerySchema(subSchemaSchema.ToLower(), subSchemaAlias.ToLower(), joinConditions));
                }

                token = query.GetNextToken();
                if (token != string.Empty && token.ToLower() != "where")
                {
                    throw new KbParserException("Invalid query. Found '" + token + "', expected: 'where' or end of statement.");
                }

                if (token.ToLower() == "where")
                {
                    var conditionTokenizer = new ConditionTokenizer(query.Text, query.Position);

                    while (true)
                    {
                        int previousTokenPosition = conditionTokenizer.Position;
                        var conditonToken = conditionTokenizer.GetNextToken();

                        if ((new string[] { "order", "group", "" }).Contains(conditonToken) && conditionTokenizer.IsNextToken("by"))
                        {
                            throw new KbParserException("Invalid query. Found '" + conditonToken + "', expected: end of statement.");
                        }
                        else if (conditonToken == string.Empty)
                        {
                            //Set both the conditition and query position to the begining of the "ORDER BY" or "GROUP BY".
                            conditionTokenizer.SetPosition(previousTokenPosition);
                            query.SetPosition(previousTokenPosition);
                            break;
                        }
                    }

                    string conditionText = query.Text.Substring(conditionTokenizer.StartPosition, conditionTokenizer.Position - conditionTokenizer.StartPosition).Trim();
                    if (conditionText == string.Empty)
                    {
                        throw new KbParserException("Invalid query. Found '" + token + "', expected: list of conditions.");
                    }

                    result.Conditions = Conditions.Create(conditionText, query.LiteralStrings);
                }

                if (query.IsEnd() == false)
                {
                    throw new KbParserException("Invalid query. Found '" + query.PeekNextToken() + "', expected: end of statement.");
                }
            }
            #endregion

            #region Kill -----------------------------------------------------------------------------------------------
            else if (queryType == QueryType.Kill)
            {
                string referencedProcessId = query.Remainder();
                try
                {
                    result.AddAttribute(PreparedQuery.QueryAttribute.ProcessId, ulong.Parse(referencedProcessId));
                }
                catch
                {
                    throw new KbParserException("Invalid query. Found '" + referencedProcessId + "', expected: numeric process id.");
                }
            }
            #endregion

            #region Set ------------------------------------------------------------------------------------------------
            else if (queryType == QueryType.Set)
            {
                //Variable 
                string variableName = query.GetNextToken();
                string variableValue = query.Remainder();
                result.VariableValues.Add(new(variableName, variableValue));
            }
            #endregion

            #region Insert ---------------------------------------------------------------------------------------------
            else if (queryType == QueryType.Insert)
            {
                if (query.IsNextTokenConsume("into") == false)
                {
                    throw new KbParserException("Invalid query. Found '" + query.Breadcrumbs.Last() + "', expected: 'into'.");
                }

                token = query.GetNextToken();
                if (token == string.Empty)
                {
                    throw new KbParserException("Invalid query. Found '" + query.Breadcrumbs.Last() + "', expected: schema name.");
                }
                result.Schemas.Add(new QuerySchema(token));

                if (query.NextCharacter != '(')
                {
                    throw new KbParserException("Invalid query. Found '" + query.NextCharacter + "', expected: '('.");
                }
                query.SkipWhile('(');


                var upsertKeyValues = new UpsertKeyValues();

                while (query.NextCharacter != ')')
                {
                    string fieldName = query.GetNextToken();
                    if (fieldName == string.Empty)
                    {
                        throw new KbParserException("Invalid query. Found '" + fieldName + "', expected: field name.");
                    }

                    if (query.IsNextTokenConsume("=") == false)
                    {
                        throw new KbParserException("Invalid query. Found '" + query.Breadcrumbs.Last() + "', expected: '='.");
                    }

                    string fieldValue = query.GetNextToken();
                    if (fieldName == string.Empty)
                    {
                        throw new KbParserException("Invalid query. Found '" + fieldValue + "', expected: field value.");
                    }

                    upsertKeyValues.Add(new UpsertKeyValue(PrefixedField.Parse(fieldName), new SmartValue(fieldValue)));

                    if (query.NextCharacter == ',')
                    {
                        query.SkipWhile(',');
                    }
                    else if (query.NextCharacter != ')')
                    {
                        throw new KbParserException("Invalid query. Found '" + query.NextCharacter + "', expected: ',' or ')'.");
                    }
                    else if (query.NextCharacter == ')')
                    {
                        query.SkipWhile(')');

                        if (query.NextCharacter == ',') //This is the next set of values.
                        {
                            result.UpsertValues.Add(upsertKeyValues);
                            upsertKeyValues = new UpsertKeyValues();
                            query.SkipWhile(',');

                            if (query.NextCharacter != '(')
                            {
                                throw new KbParserException("Invalid query. Found '" + query.NextCharacter + "', expected: '('.");
                            }
                            query.SkipWhile('(');
                        }
                        else
                        {
                            break;
                        }
                    }
                }

                result.UpsertValues.Add(upsertKeyValues);

                if (query.IsEnd() == false)
                {
                    throw new KbParserException("Invalid query. Found '" + query.PeekNextToken() + "', expected: end of statement.");
                }
            }
            #endregion

            #region Cleanup and Validation.

            if (result.UpsertValues != null) //Fill in upsert string literals.
            {
                foreach (var upsertRow in result.UpsertValues)
                {
                    foreach (var kvp in upsertRow)
                    {
                        if (query.LiteralStrings.ContainsKey(kvp.Value.ToString()))
                        {
                            kvp.Value.Value = query.LiteralStrings[kvp.Value.Value ?? ""];
                        }
                    }
                }
            }

            foreach (var field in result.GroupFields)
            {
                //if (result.Schemas.Any(o => o.Prefix == field.Prefix) == false)
                //{
                //    throw new KbParserException($"The group-by schema alias [{field.Prefix}] for [{field.Field}] was not found in the query.");
                //}
            }

            foreach (var field in result.SelectFields) //Top level fields.
            {
                if (query.LiteralStrings.ContainsKey(field.Alias))
                {
                    field.Alias = query.LiteralStrings[field.Alias].Substring(1, query.LiteralStrings[field.Alias].Length - 2);
                }
            }

            foreach (var field in result.SelectFields.AllDocumentFields()) //Document related fields.
            {
                if (result.Schemas.Any(o => o.Prefix == field.Prefix) == false)
                {
                    throw new KbParserException($"The select schema alias [{field.Prefix}] for [{field.Field}] was not found in the query.");
                }
            }

            foreach (var field in result.SortFields)
            {
                if (query.LiteralStrings.ContainsKey(field.Alias))
                {
                    field.Alias = query.LiteralStrings[field.Alias].Substring(1, query.LiteralStrings[field.Alias].Length - 2);
                    field.Field = field.Alias;
                }

                if (result.SelectFields.Any(o => o.Alias == field.Alias) == false)
                {
                    throw new KbParserException($"The order-by field [{field.Field}] was not found in the query.");
                }

                //if (result.SelectFields.Where(o => o.Key == field.Key).Any() == false && result.DynamicallyBuildSelectList == false)
                //{
                //    throw new KbParserException($"The sort-by schema alias [{field.Prefix}] for [{field.Field}] was not found in the query.");
                //}
            }

            foreach (var field in result.Conditions.AllFields)
            {
                if (result.Schemas.Any(o => o.Prefix == field.Prefix) == false)
                {
                    throw new KbParserException($"The condition schema alias [{field.Prefix}] for [{field.Field}] was not found in the query.");
                }
            }

            if (result.QueryType == QueryType.Select)
            {
                if (result.DynamicallyBuildSelectList == false && result.SelectFields.Count == 0)
                {
                    throw new KbParserException("No fields were selected.");
                }

                if (result.Schemas.Count == 0)
                {
                    throw new KbParserException("No schemas were selected.");
                }

                if (result.DynamicallyBuildSelectList == true && result.SelectFields.Count > 0)
                {
                    throw new KbParserException("Queries with dynamic field-sets cannot also contain explicit fields.");
                }
            }

            #endregion

            return result;
        }

        /*
        private static UpsertKeyValues ParseUpsertKeyValues(string conditionsText, ref int position)
        {
            UpsertKeyValues keyValuePairs = new UpsertKeyValues();
            int beforeTokenPosition;

            while (true)
            {
                string token;
                beforeTokenPosition = position;
                if ((token = Utilities.GetNextToken(conditionsText, ref position)) == string.Empty)
                {
                    if (keyValuePairs.Collection.Count > 0)
                    {
                        break; //Completed successfully.
                    }
                    throw new KbParserException("Invalid query. Unexpexted end of query found.");
                }

                if (token.ToLower() == "where")
                {
                    position = beforeTokenPosition;
                    break; //Completed successfully.
                }

                var keyValue = new UpsertKeyValue();

                if (token == string.Empty || Utilities.IsValidIdentifier(token) == false)
                {
                    throw new KbParserException("Invalid query. Found '" + token + "', expected: identifier name.");
                }
                keyValue.Key = token;

                token = Utilities.GetNextToken(conditionsText, ref position);
                if (token != "=")
                {
                    throw new KbParserException("Invalid query. Found '" + token + "', expected: '='.");
                }

                if ((token = Utilities.GetNextToken(conditionsText, ref position)) == string.Empty)
                {
                    throw new KbParserException("Invalid query. Found '" + token + "', expected: condition value.");
                }
                keyValue.Value.Value = token;

                keyValuePairs.Collection.Add(keyValue);
            }

            return keyValuePairs;
        }
        */
    }
}
