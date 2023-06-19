﻿using Katzebase.Engine.Documents;
using Katzebase.Engine.Indexes;
using Katzebase.Engine.Query.Constraints;
using Katzebase.Engine.Query.Searchers.MultiSchema;
using Katzebase.Engine.Query.Sorting;
using Katzebase.Engine.Schemas;
using Katzebase.Engine.Threading;
using Katzebase.Engine.Transactions;
using Katzebase.PublicLibrary;
using Katzebase.PublicLibrary.Exceptions;
using Newtonsoft.Json.Linq;
using static Katzebase.Engine.KbLib.EngineConstants;
using static Katzebase.Engine.Trace.PerformanceTrace;
using static Katzebase.PublicLibrary.Constants;

namespace Katzebase.Engine.Query.Searchers.SingleSchema
{
    internal static class SSQStaticMethods
    {
        /// <summary>
        /// Gets all documents by a subset of conditions.
        /// </summary>
        internal static SSQDocumentLookupResults GetDocumentsByConditions(
            Core core, Transaction transaction, QuerySchema querySchema, PreparedQuery query, bool justReturnDocumentPointers)
        {
            var physicalSchema = core.Schemas.Acquire(transaction, querySchema.Name, LockOperation.Read);

            //Lock the document catalog:
            var documentPointers = core.Documents.AcquireDocumentPointers(transaction, physicalSchema, LockOperation.Read).ToList();

            ConditionLookupOptimization? lookupOptimization = null;

            //If we dont have any conditions then we just need to return all rows from the schema.
            if (query.Conditions.Subsets.Count > 0)
            {
                lookupOptimization = ConditionLookupOptimization.Build(core, transaction, physicalSchema, query.Conditions, querySchema.Prefix);

                if (lookupOptimization.CanApplyIndexing())
                {
                    //We are going to create a limited document catalog from the indexes. So kill the reference and create an empty list.
                    documentPointers = new List<DocumentPointer>();

                    //All condition subsets have a selected index. Start building a list of possible document IDs.
                    foreach (var subset in lookupOptimization.Conditions.NonRootSubsets)
                    {
                        Utility.EnsureNotNull(subset.IndexSelection);

                        var physicalIndexPages = core.IO.GetPBuf<PhysicalIndexPages>(transaction, subset.IndexSelection.Index.DiskPath, LockOperation.Read);
                        var indexMatchedDocuments = core.Indexes.MatchDocuments(transaction, physicalIndexPages, subset.IndexSelection, subset, querySchema.Prefix);

                        documentPointers.AddRange(indexMatchedDocuments.Select(o => o.Value));
                    }
                }
                else
                {
                    #region Why no indexing? Find out here!
                    //   * One or more of the conditon subsets lacks an index.
                    //   *
                    //   *   Since indexing requires that we can ensure document elimination we will have
                    //   *      to ensure that we have a covering index on EACH-and-EVERY conditon group.
                    //   *
                    //   *   Then we can search the indexes for each condition group to obtain a list of all possible
                    //   *       document IDs, then use those document IDs to early eliminate documents from the main lookup loop.
                    //   *
                    //   *   If any one conditon group does not have an index, then no indexing will be used at all since all
                    //   *      documents will need to be scaned anyway. To prevent unindexed scans, reduce the number of
                    //   *      condition groups (nested in parentheses).
                    //   *
                    //   * ConditionLookupOptimization:BuildFullVirtualExpression() Will tell you why we cant use an index.
                    //   * var explanationOfIndexability = lookupOptimization.BuildFullVirtualExpression();
                    //*
                    #endregion
                }
            }

            var ptThreadCreation = transaction.PT?.CreateDurationTracker(PerformanceTraceCumulativeMetricType.ThreadCreation);
            var threadParam = new LookupThreadParam(core, transaction, physicalSchema, query, lookupOptimization, querySchema, justReturnDocumentPointers);
            int threadCount = ThreadPoolHelper.CalculateThreadCount(core.Sessions.ByProcessId(transaction.ProcessId), documentPointers.Count);
            transaction.PT?.AddDescreteMetric(PerformanceTraceDescreteMetricType.ThreadCount, threadCount);
            var threadPool = ThreadPoolQueue<DocumentPointer, LookupThreadParam>.CreateAndStart(GetDocumentsByConditionsThreadProc, threadParam, threadCount);
            ptThreadCreation?.StopAndAccumulate();

            foreach (var documentPointer in documentPointers)
            {
                if ((query.RowLimit != 0 && query.SortFields.Any() == false) && threadParam.Results.Collection.Count >= query.RowLimit)
                {
                    break;
                }

                if (threadPool.HasException || threadPool.ContinueToProcessQueue == false)
                {
                    break;
                }

                threadPool.EnqueueWorkItem(documentPointer);
            }

            var ptThreadCompletion = transaction.PT?.CreateDurationTracker(PerformanceTraceCumulativeMetricType.ThreadCompletion);
            threadPool.WaitForCompletion();
            ptThreadCompletion?.StopAndAccumulate();

            //Get a list of all the fields we need to sory by.
            if (query.SortFields.Any())
            {
                var sortingColumns = new List<(int fieldIndex, KbSortDirection sortDirection)>();
                foreach (var sortField in query.SortFields.OfType<SortField>())
                {
                    var field = query.SelectFields.Where(o => o.Key == sortField.Key).FirstOrDefault();
                    Utility.EnsureNotNull(field);
                    sortingColumns.Add(new(field.Ordinal, sortField.SortDirection));
                }

                //Sort the results:
                var ptSorting = transaction.PT?.CreateDurationTracker(PerformanceTraceCumulativeMetricType.Sorting);
                threadParam.Results.Collection = threadParam.Results.Collection.OrderBy(row => row.Values, new ResultValueComparer(sortingColumns)).ToList();
                ptSorting?.StopAndAccumulate();
            }

            //Enforce row limits.
            if (query.RowLimit > 0)
            {
                threadParam.Results.Collection = threadParam.Results.Collection.Take(query.RowLimit).ToList();
            }

            return threadParam.Results;
        }

        #region Threading.

        private class LookupThreadParam
        {
            public bool JustReturnDocumentPointers { get; set; }
            public SSQDocumentLookupResults Results = new();
            public PhysicalSchema PhysicalSchema { get; private set; }
            public Core Core { get; private set; }
            public Transaction Transaction { get; private set; }
            public PreparedQuery Query { get; private set; }
            public ConditionLookupOptimization? LookupOptimization { get; private set; }
            public QuerySchema QuerySchema { get; set; }

            public LookupThreadParam(Core core, Transaction transaction, PhysicalSchema physicalSchema, PreparedQuery query,
                ConditionLookupOptimization? lookupOptimization, QuerySchema querySchema, bool justReturnDocumentPointers)
            {
                JustReturnDocumentPointers = justReturnDocumentPointers;
                Core = core;
                Transaction = transaction;
                PhysicalSchema = physicalSchema;
                Query = query;
                LookupOptimization = lookupOptimization;
                QuerySchema = querySchema;
            }
        }

        #endregion

        private static void GetDocumentsByConditionsThreadProc(ThreadPoolQueue<DocumentPointer, LookupThreadParam> pool, LookupThreadParam? param)
        {
            Utility.EnsureNotNull(param);

            NCalc.Expression? expression = null;

            if (param.LookupOptimization != null)
            {
                expression = new NCalc.Expression(param.LookupOptimization.Conditions.HighLevelExpressionTree);
            }

            while (pool.ContinueToProcessQueue)
            {
                var documentPointer = pool.DequeueWorkItem();
                if (documentPointer == null)
                {
                    continue;
                }

                var physicalDocument = param.Core.Documents.AcquireDocument(param.Transaction, param.PhysicalSchema, documentPointer.DocumentId, LockOperation.Read);
                var jContent = JObject.Parse(physicalDocument.Content);

                if (expression != null && param.LookupOptimization != null)
                {
                    SetExpressionParameters(ref expression, param.LookupOptimization.Conditions, jContent);
                }

                var ptEvaluate = param.Transaction.PT?.CreateDurationTracker(PerformanceTraceCumulativeMetricType.Evaluate);
                bool evaluation = (expression == null) || (bool)expression.Evaluate();
                ptEvaluate?.StopAndAccumulate();

                if (evaluation)
                {
                    if (param.JustReturnDocumentPointers)
                    {
                        lock (param.Results)
                        {
                            param.Results.DocumentPointers.Add(documentPointer);
                        }
                    }
                    else
                    {
                        var result = new SSQDocumentLookupResult(physicalDocument.Id);
                        if (param.Query.DynamicallyBuildSelectList)
                        {
                            var dynamicFields = new List<PrefixedField>();
                            foreach (var jToken in jContent)
                            {
                                dynamicFields.Add(new PrefixedField(param.QuerySchema.Prefix, jToken.Key));
                            }

                            lock (param.Query.SelectFields)
                            {
                                foreach (var dynamicField in dynamicFields)
                                {
                                    if (param.Query.SelectFields.Any(o => o.Key == dynamicField.Key) == false)
                                    {
                                        param.Query.SelectFields.Add(dynamicField);
                                    }
                                }
                            }
                        }

                        foreach (var field in param.Query.SelectFields)
                        {
                            jContent.TryGetValue(field.Field, StringComparison.CurrentCultureIgnoreCase, out JToken? jToken);
                            result.Values.Add(jToken?.ToString() ?? string.Empty);
                        }

                        lock (param.Results)
                        {
                            param.Results.Add(result);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Gets the json content values for the specified conditions.
        /// </summary>
        /// <param name="expression"></param>
        /// <param name="conditions"></param>
        /// <param name="jContent"></param>
        private static void SetExpressionParameters(ref NCalc.Expression expression, Conditions conditions, JObject jContent)
        {
            //If we have subsets, then we need to satisify those in order to complete the equation.
            foreach (var subsetKey in conditions.Root.SubsetKeys)
            {
                var subExpression = conditions.SubsetByKey(subsetKey);
                SetExpressionParametersRecursive(ref expression, conditions, subExpression, jContent);
            }
        }

        private static void SetExpressionParametersRecursive(ref NCalc.Expression expression, Conditions conditions, ConditionSubset conditionSubset, JObject jContent)
        {
            //If we have subsets, then we need to satisify those in order to complete the equation.
            foreach (var subsetKey in conditionSubset.SubsetKeys)
            {
                var subExpression = conditions.SubsetByKey(subsetKey);
                SetExpressionParametersRecursive(ref expression, conditions, subExpression, jContent);
            }

            foreach (var condition in conditionSubset.Conditions)
            {
                Utility.EnsureNotNull(condition.Left.Value);

                //Get the value of the condition:
                if (!jContent.TryGetValue(condition.Left.Value, StringComparison.CurrentCultureIgnoreCase, out JToken? jToken))
                {
                    throw new KbParserException($"Field not found in document [{condition.Left.Value}].");
                }

                expression.Parameters[condition.ConditionKey] = condition.IsMatch(jToken.ToString().ToLower());
            }
        }
    }
}