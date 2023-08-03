﻿using Katzebase.Engine.Query;
using Katzebase.PublicLibrary.Exceptions;
using Katzebase.PublicLibrary.Payloads;
using static Katzebase.Engine.Library.EngineConstants;
using static Katzebase.PublicLibrary.KbConstants;

namespace Katzebase.Engine.Indexes.Management
{
    /// <summary>
    /// Internal class methods for handling query requests related to indexes.
    /// </summary>
    internal class IndexQueryHandlers
    {
        private readonly Core core;

        public IndexQueryHandlers(Core core)
        {
            this.core = core;

            try
            {
            }
            catch (Exception ex)
            {
                core.Log.Write($"Failed to instanciate index query handler.", ex);
                throw;
            }
        }

        internal KbActionResponse ExecuteDrop(ulong processId, PreparedQuery preparedQuery)
        {
            try
            {
                using var transactionReference = core.Transactions.Acquire(processId);
                string schemaName = preparedQuery.Schemas.First().Name;

                if (preparedQuery.SubQueryType == SubQueryType.Index || preparedQuery.SubQueryType == SubQueryType.UniqueKey)
                {
                    core.Indexes.DropIndex(transactionReference.Transaction, schemaName, preparedQuery.Attribute<string>(PreparedQuery.QueryAttribute.IndexName));
                }
                else
                {
                    throw new KbNotImplementedException();
                }

                return transactionReference.CommitAndApplyMetricsThenReturnResults();
            }
            catch (Exception ex)
            {
                core.Log.Write($"Failed to execute index drop for process id {processId}.", ex);
                throw;
            }
        }

        internal KbActionResponse ExecuteAnalyze(ulong processId, PreparedQuery preparedQuery)
        {
            try
            {
                using var transactionReference = core.Transactions.Acquire(processId);
                string schemaName = preparedQuery.Schemas.First().Name;
                var analysis = core.Indexes.AnalyzeIndex(transactionReference.Transaction, schemaName, preparedQuery.Attribute<string>(PreparedQuery.QueryAttribute.IndexName));

                transactionReference.Transaction.AddMessage(analysis, KbMessageType.Verbose);
                return transactionReference.CommitAndApplyMetricsThenReturnResults();
            }
            catch (Exception ex)
            {
                core.Log.Write($"Failed to execute index rebuild for process id {processId}.", ex);
                throw;
            }
        }

        internal KbActionResponse ExecuteRebuild(ulong processId, PreparedQuery preparedQuery)
        {
            try
            {
                using var transactionReference = core.Transactions.Acquire(processId);
                string schemaName = preparedQuery.Schemas.First().Name;

                var indexName = preparedQuery.Attribute<string>(PreparedQuery.QueryAttribute.IndexName);
                var indexPartitions = preparedQuery.Attribute<uint>(PreparedQuery.QueryAttribute.PartitionCount, core.Settings.DefaultIndexPartitions);

                core.Indexes.RebuildIndex(transactionReference.Transaction, schemaName, indexName, indexPartitions);
                return transactionReference.CommitAndApplyMetricsThenReturnResults();
            }
            catch (Exception ex)
            {
                core.Log.Write($"Failed to execute index rebuild for process id {processId}.", ex);
                throw;
            }
        }

        internal KbActionResponse ExecuteCreate(ulong processId, PreparedQuery preparedQuery)
        {
            try
            {
                using var transactionReference = core.Transactions.Acquire(processId);

                if (preparedQuery.SubQueryType == SubQueryType.Index || preparedQuery.SubQueryType == SubQueryType.UniqueKey)
                {
                    var indexPartitions = preparedQuery.Attribute<uint>(PreparedQuery.QueryAttribute.PartitionCount, core.Settings.DefaultIndexPartitions);

                    var index = new KbIndex
                    {
                        Name = preparedQuery.Attribute<string>(PreparedQuery.QueryAttribute.IndexName),
                        IsUnique = preparedQuery.Attribute<bool>(PreparedQuery.QueryAttribute.IsUnique),
                        Partitions = indexPartitions
                    };

                    foreach (var field in preparedQuery.CreateFields)
                    {
                        index.Attributes.Add(new KbIndexAttribute() { Field = field.Field });
                    }

                    string schemaName = preparedQuery.Schemas.Single().Name;
                    core.Indexes.CreateIndex(transactionReference.Transaction, schemaName, index, out Guid indexId);
                }
                else
                {
                    throw new KbNotImplementedException();
                }

                return transactionReference.CommitAndApplyMetricsThenReturnResults();
            }
            catch (Exception ex)
            {
                core.Log.Write($"Failed to execute index create for process id {processId}.", ex);
                throw;
            }
        }
    }
}
