﻿using NTDLS.Katzebase.Client.Exceptions;
using NTDLS.Katzebase.Engine.Atomicity;
using NTDLS.Katzebase.Engine.Indexes.Matching;
using NTDLS.Katzebase.Engine.Schemas;
using System.Text;
using static NTDLS.Katzebase.Engine.Library.EngineConstants;

namespace NTDLS.Katzebase.Engine.Query.Constraints
{
    internal class ConditionOptimization
    {
        /// <summary>
        /// A list of the indexes that have been selected by the optimizer for the specified conditions.
        /// </summary>
        public List<IndexSelection> IndexSelection { get; private set; } = new();

        /// <summary>
        /// A clone of the conditions that this set of index selections was built for.
        /// Also contains the indexes associated with each SubCondition of conditions.
        /// </summary>
        public Conditions Conditions { get; private set; }

        private readonly Transaction _transaction;

        public ConditionOptimization(Transaction transaction, Conditions conditions)
        {
            _transaction = transaction;
            Conditions = conditions.Clone();
        }

        #region Builder.

        /// <summary>
        /// Takes a nested set of conditions and returns a selection of indexes as well as a clone of the conditions with associated indexes.
        /// </summary>
        /// <returns>A selection of indexes as well as a clone of the conditions with associated indexes</returns>
        public static ConditionOptimization Build(EngineCore core,
            Transaction transaction, PhysicalSchema physicalSchema, Conditions conditions, string workingSchemaPrefix)
        {
            try
            {
                /* This still has condition values in it, that wont work. *Face palm*
                var cacheItem = core.LookupOptimizationCache.Get(conditions.Hash) as MSQConditionLookupOptimization;
                if (cacheItem != null)
                {
                    return cacheItem;
                }
                */

                var indexCatalog = core.Indexes.AcquireIndexCatalog(transaction, physicalSchema, LockOperation.Read);

                var optimization = new ConditionOptimization(transaction, conditions);

                foreach (var subCondition in conditions.SubConditions)
                {
                    if (subCondition.Conditions.Any(o => o.Left.Prefix != workingSchemaPrefix))
                    {
                        if (subCondition.Conditions.Any(o => o.LogicalConnector != LogicalConnector.And) == false)
                        {
                            //We can't yet figure out how to eliminate documents if the conditions are for more
                            //..    than one schema and all of the logical connectors are not AND. This can be done however.
                            //  We just generally have a lot of optimization trouble with ORs.
                            continue;
                        }
                    }

                    var potentialIndexes = new List<PotentialIndex>();

                    //Loop though each index in the schema.
                    foreach (var physicalIndex in indexCatalog.Collection)
                    {
                        var handledKeyNames = new List<PrefixedField>();

                        for (int i = 0; i < physicalIndex.Attributes.Count; i++)
                        {
                            if (physicalIndex.Attributes == null || physicalIndex.Attributes[i] == null)
                            {
                                throw new KbNullException($"Value should not be null {nameof(physicalIndex.Attributes)}.");
                            }

                            var keyName = physicalIndex.Attributes[i].Field?.ToLowerInvariant();
                            if (keyName == null)
                            {
                                throw new KbNullException($"Value should not be null {nameof(keyName)}.");
                            }

                            var matchedNonConvertedConditions =
                                subCondition.Conditions.Where(o => o.CoveredByIndex == false
                                    && o.Left.Value == keyName && o.Left.Prefix == workingSchemaPrefix);

                            foreach (var matchedCondition in matchedNonConvertedConditions)
                            {
                                handledKeyNames.Add(PrefixedField.Parse(matchedCondition.Left.Key));
                            }

                            if (matchedNonConvertedConditions.Any() == false)
                            {
                                break;
                            }
                        }

                        if (handledKeyNames.Count > 0)
                        {
                            var potentialIndex = new PotentialIndex(physicalIndex, handledKeyNames);
                            potentialIndexes.Add(potentialIndex);
                        }
                    }

                    //Grab the index that matches the most of our supplied keys but also has the least attributes.
                    var firstIndex = (from o in potentialIndexes where o.Tried == false select o)
                        .OrderByDescending(s => s.CoveredFields.Count)
                        .ThenBy(t => t.Index.Attributes.Count).FirstOrDefault();

                    if (firstIndex != null)
                    {
                        var handledKeys = GetConvertedConditions(subCondition.Conditions, firstIndex.CoveredFields);

                        //Where the left value is in the covered fields:

                        //var handledKeys = (from o in SubCondition.Conditions where firstIndex.CoveredFields.Contains(o.Left.Value ?? string.Empty) select o).ToList();
                        foreach (var handledKey in handledKeys)
                        {
                            handledKey.CoveredByIndex = true;
                        }

                        firstIndex.SetTried();

                        var indexSelection = new IndexSelection(firstIndex.Index, firstIndex.CoveredFields);

                        optimization.IndexSelection.Add(indexSelection);

                        //Mark which condition this index selection satisfies.
                        var sourceSubCondition = optimization.Conditions.SubConditionFromKey(subCondition.Key);
                        sourceSubCondition.IndexSelection = indexSelection;

                        foreach (var condition in sourceSubCondition.Conditions)
                        {
                            if (indexSelection.CoveredFields.Any(o => o.Key == condition.Left.Key))
                            {
                                condition.CoveredByIndex = true;
                            }
                        }
                    }
                }

                //core.LookupOptimizationCache.Add(conditions.Hash, lookupOptimization, DateTime.Now.AddMinutes(10));

                //When we get here, we have one index that seems to want to cover multiple tables - no cool man. Not cool.

                //foreach (var index in optimization.IndexSelection)
                //{
                //    Interactions.Management.LogManager.Information($"{physicalSchema.Name}->{index.PhysicalIndex.Name}.");
                //}

                return optimization;
            }
            catch (Exception ex)
            {
                Interactions.Management.LogManager.Error($"Failed to select indexes for process {transaction.ProcessId}.", ex);
                throw;
            }
        }

        #endregion

        public static List<Condition> GetConvertedConditions(
            List<Condition> conditions, List<PrefixedField> coveredFields)
        {
            var result = new List<Condition>();

            foreach (var coveredField in coveredFields)
            {
                foreach (var condition in conditions)
                {
                    if (condition.Left.Key == coveredField.Key)
                    {
                        result.Add(condition);
                    }
                }
            }

            return result;
        }

        public static bool CanApplyIndexing(SubCondition subCondition)
        {
            //Currently we can only use a index match if all of the conditions in a group are "AND"s, so if
            //  we have an "OR" or none of the conditions are not covered by an index then we cant optimize.
            if (subCondition.Conditions.Any(o => o.LogicalConnector == LogicalConnector.Or)
                || subCondition.Conditions.Any(o => o.CoveredByIndex == true) == false)
            {
                return false;
            }
            return true;
        }

        private bool? _canApplyIndexingResultCached = null;

        public bool CanApplyIndexing()
        {
            if (_canApplyIndexingResultCached != null)
            {
                return (bool)_canApplyIndexingResultCached;
            }

            if (Conditions.NonRootSubConditions.Any(o => o.IndexSelection == null) == false)
            {
                //All condition SubConditions have a selected index.
                foreach (var subCondition in Conditions.NonRootSubConditions)
                {
                    if (CanApplyIndexing(subCondition) == false)
                    {
                        _canApplyIndexingResultCached = false;
                        return false;
                    }
                }

                _canApplyIndexingResultCached = true;
                return true;
            }
            _canApplyIndexingResultCached = false;
            return false;
        }

        #region Optimization explanation.

        private static string Pad(int indentation) => "".PadLeft(indentation * 2, ' ');

        /// <summary>
        /// This function makes a (somewhat) user readable expression tree, used for debugging and explanations.
        /// It includes indexes where they can be applied.
        /// It also demonstrates how we process the recursive condition logic.
        /// </summary>
        public string ExplainOptimization(int indentation = 0)
        {
            var result = new StringBuilder();

            if (Conditions.Root.ExpressionKeys.Count > 0)
            {
                //The root condition is just a pointer to a child condition, so get the "root" child condition.
                var rootCondition = Conditions.SubConditionFromKey(Conditions.Root.Key);
                ExplainSubCondition(ref result, rootCondition, indentation);
            }

            return result.ToString();
        }

        /// <summary>
        /// This function makes a (somewhat) user readable expression tree, used for debugging and explanations.
        /// It includes indexes where they can be applied.
        /// It also demonstrates how we process the recursive condition logic.
        /// Called by parent ExplainOptimization()
        /// </summary>
        private void ExplainSubCondition(ref StringBuilder result, SubCondition givenSubCondition, int indentation)
        {
            foreach (var subConditionKey in givenSubCondition.ExpressionKeys)
            {
                var subCondition = Conditions.SubConditionFromKey(subConditionKey);

                var indexName = subCondition.IndexSelection?.PhysicalIndex?.Name;

                result.AppendLine(Pad(indentation + 1)
                    + $"{Conditions.FriendlyPlaceholder(subCondition.Key)} is ({Conditions.FriendlyPlaceholder(subCondition.Expression)})"
                    + (CanApplyIndexing(subCondition) && indexName != null ? $" [{indexName}]" : ""));

                result.AppendLine(Pad(indentation + 1) + "(");

                if (subCondition.Conditions.Count > 0)
                {
                    foreach (var condition in subCondition.Conditions)
                    {
                        result.AppendLine(Pad(indentation + 2)
                            + $"{Conditions.FriendlyPlaceholder(condition.ConditionKey)} is ({condition.Left} {condition.LogicalQualifier} {condition.Right})");
                    }
                }

                if (subCondition.ExpressionKeys.Count > 0)
                {
                    result.AppendLine(Pad(indentation + 2) + "(");
                    ExplainSubCondition(ref result, subCondition, indentation + 2);
                    result.AppendLine(Pad(indentation + 2) + ")");
                }

                result.AppendLine(Pad(indentation + 1) + ")");
            }
        }

        #endregion
    }
}
