﻿using Katzebase.Engine.KbLib;
using Katzebase.Engine.Query.Condition;
using Katzebase.PublicLibrary.Client.Management;
using Katzebase.PublicLibrary.Payloads;
using System.Runtime.Intrinsics.X86;
using System;

namespace Katzebase.Engine.Query
{
    public class QueryManager
    {
        private Core core;

        public QueryManager(Core core)
        {
            this.core = core;
        }

        public KbQueryResult ExplainQuery(ulong processId, string statement)
        {
            var preparedQuery = ParserEngine.ParseQuery(statement);
            return ExplainQuery(processId, preparedQuery);
        }

        public KbQueryResult ExplainQuery(ulong processId, PreparedQuery preparedQuery)
        {
            if (preparedQuery.QueryType == EngineConstants.QueryType.Select
                || preparedQuery.QueryType == EngineConstants.QueryType.Delete
                || preparedQuery.QueryType == EngineConstants.QueryType.Update)
            {
                return core.Documents.ExecuteExplain(processId, preparedQuery);
            }
            else if (preparedQuery.QueryType == EngineConstants.QueryType.Set)
            {
                return new KbQueryResult();
            }
            else
            {
                throw new NotImplementedException();
            }
        }

        public KbQueryResult ExecuteQuery(ulong processId, string statement)
        {
            var preparedQuery = ParserEngine.ParseQuery(statement);
            return ExecuteQuery(processId, preparedQuery);
        }

        public KbQueryResult ExecuteQuery(ulong processId, PreparedQuery preparedQuery)
        {
            if (preparedQuery.QueryType == EngineConstants.QueryType.Select)
            {
                return core.Documents.ExecuteSelect(processId, preparedQuery);
            }
            else if (preparedQuery.QueryType == EngineConstants.QueryType.Set)
            {
                //Reroute to non-query as appropriate:
                return KbQueryResult.FromActionResponse(ExecuteNonQuery(processId, preparedQuery));
            }
            else if (preparedQuery.QueryType == EngineConstants.QueryType.Delete)
            {
                //Reroute to non-query as appropriate:
                return KbQueryResult.FromActionResponse(ExecuteNonQuery(processId, preparedQuery));
            }
            else
            {
                throw new NotImplementedException();
            }
        }

        public KbActionResponse ExecuteNonQuery(ulong processId, string statement)
        {
            var preparedQuery = ParserEngine.ParseQuery(statement);
            return ExecuteNonQuery(processId, preparedQuery);
        }

        public KbActionResponse ExecuteNonQuery(ulong processId, PreparedQuery preparedQuery)
        {
            if (preparedQuery.QueryType == EngineConstants.QueryType.Delete)
            {
                return core.Documents.ExecuteDelete(processId, preparedQuery);
            }
            else if (preparedQuery.QueryType == EngineConstants.QueryType.Set)
            {
                return core.Sessions.ExecuteSetVariable(processId, preparedQuery);
            }
            else
            {
                throw new NotImplementedException();
            }
        }

    }
}
