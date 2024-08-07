﻿using NTDLS.Katzebase.Client.Payloads.RoundTrip;
using NTDLS.Katzebase.Engine.Query;
using NTDLS.ReliableMessaging;

namespace NTDLS.Katzebase.Engine.Interactions.APIHandlers
{
    /// <summary>
    /// Public class methods for handling API requests related to queries.
    /// </summary>
    public class QueryAPIHandlers : IRmMessageHandler
    {
        private readonly EngineCore _core;

        public QueryAPIHandlers(EngineCore core)
        {
            _core = core;

            try
            {
            }
            catch (Exception ex)
            {
                Management.LogManager.Error($"Failed to instantiate query API handlers.", ex);
                throw;
            }
        }

        public KbQueryQueryExplainQueriesReply ExecuteStatementExplains(RmContext context, KbQueryQueryExplainQueries param)
        {
            var session = _core.Sessions.UpsertConnectionId(context.ConnectionId);
#if DEBUG
            Thread.CurrentThread.Name = $"KbAPI:{session.ProcessId}:{param.GetType().Name}";
            Management.LogManager.Debug(Thread.CurrentThread.Name);
#endif

            var results = new KbQueryQueryExplainQueriesReply();

            foreach (var statement in param.Statements)
            {
                session.SetCurrentQuery(statement);

                foreach (var preparedQuery in StaticQueryParser.PrepareBatch(statement))
                {
                    var intermediateResult = _core.Query.ExplainQuery(session, preparedQuery);

                    results.Add(intermediateResult);
                }
            }

            session.ClearCurrentQuery();

            return results;
        }

        public KbQueryQueryExplainQueryReply ExecuteStatementExplain(RmContext context, KbQueryQueryExplainQuery param)
        {
            var session = _core.Sessions.UpsertConnectionId(context.ConnectionId);
#if DEBUG
            Thread.CurrentThread.Name = $"KbAPI:{session.ProcessId}:{param.GetType().Name}";
            Management.LogManager.Debug(Thread.CurrentThread.Name);
#endif

            var results = new KbQueryQueryExplainQueryReply();

            session.SetCurrentQuery(param.Statement);

            foreach (var preparedQuery in StaticQueryParser.PrepareBatch(param.Statement))
            {
                var intermediateResult = _core.Query.ExplainQuery(session, preparedQuery);

                results.Add(intermediateResult);
            }

            session.ClearCurrentQuery();

            return results;
        }

        public KbQueryProcedureExecuteReply ExecuteStatementProcedure(RmContext context, KbQueryProcedureExecute param)
        {
            var session = _core.Sessions.UpsertConnectionId(context.ConnectionId);
#if DEBUG
            Thread.CurrentThread.Name = $"KbAPI:{session.ProcessId}:{param.GetType().Name}";
            Management.LogManager.Debug(Thread.CurrentThread.Name);
#endif

            session.SetCurrentQuery(param.Procedure.ProcedureName);
            var result = (KbQueryProcedureExecuteReply)_core.Query.ExecuteProcedure(session, param.Procedure);
            session.ClearCurrentQuery();

            return result;
        }

        public KbQueryQueryExecuteQueryReply ExecuteStatementQuery(RmContext context, KbQueryQueryExecuteQuery param)
        {
            var session = _core.Sessions.UpsertConnectionId(context.ConnectionId);
#if DEBUG
            Thread.CurrentThread.Name = $"KbAPI:{session.ProcessId}:{param.GetType().Name}";
            Management.LogManager.Debug(Thread.CurrentThread.Name);
#endif
            session.SetCurrentQuery(param.Statement);

            var results = new KbQueryQueryExecuteQueryReply();
            foreach (var preparedQuery in StaticQueryParser.PrepareBatch(param.Statement))
            {
                results.Add(_core.Query.ExecuteQuery(session, preparedQuery));
            }

            session.ClearCurrentQuery();

            return results;
        }

        public KbQueryQueryExecuteQueriesReply ExecuteStatementQueries(RmContext context, KbQueryQueryExecuteQueries param)
        {
            var session = _core.Sessions.UpsertConnectionId(context.ConnectionId);
#if DEBUG
            Thread.CurrentThread.Name = $"KbAPI:{session.ProcessId}:{param.GetType().Name}";
            Management.LogManager.Debug(Thread.CurrentThread.Name);
#endif
            var results = new KbQueryQueryExecuteQueriesReply();

            foreach (var statement in param.Statements)
            {
                session.SetCurrentQuery(statement);

                foreach (var preparedQuery in StaticQueryParser.PrepareBatch(statement))
                {
                    var intermediateResult = _core.Query.ExecuteQuery(session, preparedQuery);

                    results.Add(intermediateResult);
                }
            }

            session.ClearCurrentQuery();

            return results;
        }

        public KbQueryQueryExecuteNonQueryReply ExecuteStatementNonQuery(RmContext context, KbQueryQueryExecuteNonQuery param)
        {
            var session = _core.Sessions.UpsertConnectionId(context.ConnectionId);
#if DEBUG
            Thread.CurrentThread.Name = $"KbAPI:{session.ProcessId}:{param.GetType().Name}";
            Management.LogManager.Debug(Thread.CurrentThread.Name);
#endif
            session.SetCurrentQuery(param.Statement);

            var results = new KbQueryQueryExecuteNonQueryReply();
            foreach (var preparedQuery in StaticQueryParser.PrepareBatch(param.Statement))
            {
                results.Add(_core.Query.ExecuteNonQuery(session, preparedQuery));
            }

            session.ClearCurrentQuery();

            return results;
        }
    }
}
