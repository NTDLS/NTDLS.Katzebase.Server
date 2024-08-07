﻿using NTDLS.Helpers;
using NTDLS.Katzebase.Client.Exceptions;
using NTDLS.Katzebase.Client.Payloads;
using NTDLS.Katzebase.Client.Types;
using NTDLS.Katzebase.Engine.Atomicity;
using NTDLS.Katzebase.Engine.Documents;
using NTDLS.Katzebase.Engine.Indexes;
using NTDLS.Katzebase.Engine.Indexes.Matching;
using NTDLS.Katzebase.Engine.Interactions.APIHandlers;
using NTDLS.Katzebase.Engine.Interactions.QueryHandlers;
using NTDLS.Katzebase.Engine.Query.Constraints;
using NTDLS.Katzebase.Engine.Schemas;
using NTDLS.Katzebase.Engine.Threading.PoolingParameters;
using System.Collections.Generic;
using System.Text;
using static NTDLS.Katzebase.Engine.Indexes.Matching.IndexConstants;
using static NTDLS.Katzebase.Engine.Library.EngineConstants;
using static NTDLS.Katzebase.Engine.Threading.PoolingParameters.MatchConditionValuesDocumentsOperation;
using static NTDLS.Katzebase.Engine.Trace.PerformanceTrace;

namespace NTDLS.Katzebase.Engine.Interactions.Management
{
    /// <summary>
    /// Public core class methods for locking, reading, writing and managing tasks related to indexes.
    /// </summary>
    public class IndexManager
    {
        private readonly EngineCore _core;
        internal IndexQueryHandlers QueryHandlers { get; private set; }
        public IndexAPIHandlers APIHandlers { get; private set; }

        public IndexManager(EngineCore core)
        {
            _core = core;
            try
            {
                QueryHandlers = new IndexQueryHandlers(core);
                APIHandlers = new IndexAPIHandlers(core);
            }
            catch (Exception ex)
            {
                LogManager.Error("Failed to instantiate index manager.", ex);
                throw;
            }
        }

        internal void CreateIndex(Transaction transaction, string schemaName, KbIndex index, out Guid newId)
        {
            try
            {
                var physicalIndex = PhysicalIndex.FromClientPayload(index);

                physicalIndex.Id = Guid.NewGuid();
                physicalIndex.Created = DateTime.UtcNow;
                physicalIndex.Modified = DateTime.UtcNow;

                if (physicalIndex.Partitions <= 0)
                {
                    physicalIndex.Partitions = _core.Settings.DefaultIndexPartitions;
                }

                var physicalSchema = _core.Schemas.Acquire(transaction, schemaName, LockOperation.Write);
                var indexCatalog = AcquireIndexCatalog(transaction, physicalSchema, LockOperation.Write);

                if (indexCatalog.GetByName(index.Name) != null)
                {
                    throw new KbObjectAlreadyExistsException(index.Name);
                }

                indexCatalog.Add(physicalIndex);

                if (indexCatalog.DiskPath == null || physicalSchema.DiskPath == null)
                {
                    throw new KbNullException($"Value should not be null {nameof(physicalSchema.DiskPath)}.");
                }

                _core.IO.PutJson(transaction, indexCatalog.DiskPath, indexCatalog);

                RebuildIndex(transaction, physicalSchema, physicalIndex);

                newId = physicalIndex.Id;
            }
            catch (Exception ex)
            {
                LogManager.Error($"Failed to create index for process id {transaction.ProcessId}.", ex);
                throw;
            }
        }

        internal string AnalyzeIndex(Transaction transaction, string schemaName, string indexName)
        {
            try
            {
                var physicalSchema = _core.Schemas.Acquire(transaction, schemaName, LockOperation.Read);
                var indexCatalog = AcquireIndexCatalog(transaction, physicalSchema, LockOperation.Read);
                if (indexCatalog.DiskPath == null || physicalSchema.DiskPath == null)
                {
                    throw new KbNullException($"Value should not be null {nameof(physicalSchema.DiskPath)}.");
                }

                var physicalIndex = indexCatalog.GetByName(indexName) ?? throw new KbObjectNotFoundException(indexName);

                var physicalIndexPageMap = new Dictionary<uint, PhysicalIndexPages>();
                var physicalIndexPageMapDistilledLeaves = new List<List<PhysicalIndexLeaf>>();

                double diskSize = 0;
                double decompressedSiskSize = 0;

                int rootNodes = 0;

                for (uint indexPartition = 0; indexPartition < physicalIndex.Partitions; indexPartition++)
                {
                    string pageDiskPath = physicalIndex.GetPartitionPagesFileName(physicalSchema, indexPartition);
                    physicalIndexPageMap[indexPartition] = _core.IO.GetPBuf<PhysicalIndexPages>(transaction, pageDiskPath, LockOperation.Read);
                    diskSize += _core.IO.GetDecompressedSizeTracked(pageDiskPath);
                    decompressedSiskSize += new FileInfo(pageDiskPath).Length;
                    physicalIndexPageMapDistilledLeaves.Add(DistillIndexBaseNodes(physicalIndexPageMap[indexPartition].Root));
                    rootNodes += physicalIndexPageMap[indexPartition].Root.Children.Count;

                }

                var combinedNodes = physicalIndexPageMapDistilledLeaves.SelectMany(o => o);

                int minDocumentsPerNode = combinedNodes.Min(o => o.Documents?.Count ?? 0);
                int maxDocumentsPerNode = combinedNodes.Max(o => o.Documents?.Count) ?? 0;
                double avgDocumentsPerNode = combinedNodes.Average(o => o.Documents?.Count) ?? 0;
                int documentCount = combinedNodes.Sum(o => o.Documents?.Count ?? 0);
                double selectivityScore = 100.0;

                if (documentCount > 0)
                {
                    selectivityScore = 100.0 - avgDocumentsPerNode / documentCount * 100.0;
                }

                var builder = new StringBuilder();
                builder.AppendLine("Index Analysis {");
                builder.AppendLine($"    Schema            : {physicalSchema.Name}");
                builder.AppendLine($"    Name              : {physicalIndex.Name}");
                builder.AppendLine($"    Partitions        : {physicalIndex.Partitions}");
                builder.AppendLine($"    Id                : {physicalIndex.Id}");
                builder.AppendLine($"    Unique            : {physicalIndex.IsUnique}");
                builder.AppendLine($"    Created           : {physicalIndex.Created}");
                builder.AppendLine($"    Modified          : {physicalIndex.Modified}");
                builder.AppendLine($"    Disk Path         : {physicalIndex.GetPartitionPagesPath(physicalSchema)}");
                builder.AppendLine($"    Pages Size        : {diskSize / 1024.0:N2}k");
                builder.AppendLine($"    Disk Size         : {decompressedSiskSize / 1024.0:N2}k");
                builder.AppendLine($"    Compression Ratio : {decompressedSiskSize / diskSize * 100.0:N2}");
                builder.AppendLine($"    Node Count        : {combinedNodes.Sum(o => o.Documents?.Count ?? 0):N0}");
                builder.AppendLine($"    Root Node Count   : {rootNodes:N0}");
                builder.AppendLine($"    Distinct Nodes    : {combinedNodes.Count():N0}");
                builder.AppendLine($"    Max. Node Depth   : {physicalIndex.Attributes.Count:N0}");
                builder.AppendLine($"    Min. Node Density : {minDocumentsPerNode:N0}");
                builder.AppendLine($"    Max. Node Density : {maxDocumentsPerNode:N0}" + (maxDocumentsPerNode == 1 ? " (unique)" : ""));
                builder.AppendLine($"    Avg. Node Density : {avgDocumentsPerNode:N2}");
                builder.AppendLine($"    Document Count    : {documentCount:N0}");
                builder.AppendLine($"    Selectivity Score : {selectivityScore:N4}%");

                builder.AppendLine("    Attributes {");
                foreach (var attrib in physicalIndex.Attributes)
                {
                    builder.AppendLine($"        {attrib.Field}");
                }
                builder.AppendLine("    }");
                builder.AppendLine("}");

                return builder.ToString();
            }
            catch (Exception ex)
            {
                LogManager.Error($"Failed to analyze index for process id {transaction.ProcessId}.", ex);
                throw;
            }
        }

        internal void RebuildIndex(Transaction transaction, string schemaName, string indexName, uint newPartitionCount = 0)
        {
            try
            {
                var physicalSchema = _core.Schemas.Acquire(transaction, schemaName, LockOperation.Write);
                var indexCatalog = AcquireIndexCatalog(transaction, physicalSchema, LockOperation.Write);
                if (indexCatalog.DiskPath == null || physicalSchema.DiskPath == null)
                {
                    throw new KbNullException($"Value should not be null {nameof(physicalSchema.DiskPath)}.");
                }

                var physicalIndex = indexCatalog.GetByName(indexName) ?? throw new KbObjectNotFoundException(indexName);

                if (newPartitionCount != 0)
                {
                    physicalIndex.Partitions = newPartitionCount;
                }

                RebuildIndex(transaction, physicalSchema, physicalIndex);

                physicalIndex.Modified = DateTime.UtcNow;

                _core.IO.PutJson(transaction, indexCatalog.DiskPath, indexCatalog);
            }
            catch (Exception ex)
            {
                LogManager.Error($"Failed to rebuild index for process id {transaction.ProcessId}.", ex);
                throw;
            }
        }

        internal void DropIndex(Transaction transaction, string schemaName, string indexName)
        {
            try
            {
                var physicalSchema = _core.Schemas.Acquire(transaction, schemaName, LockOperation.Write);
                var indexCatalog = AcquireIndexCatalog(transaction, physicalSchema, LockOperation.Write);
                if (indexCatalog.DiskPath == null || physicalSchema.DiskPath == null)
                {
                    throw new KbNullException($"Value should not be null {nameof(physicalSchema.DiskPath)}.");
                }

                var physicalIndex = indexCatalog.GetByName(indexName);
                if (physicalIndex != null)
                {
                    indexCatalog.Remove(physicalIndex);

                    if (Path.Exists(physicalIndex.GetPartitionPagesPath(physicalSchema)))
                    {
                        _core.IO.DeletePath(transaction, physicalIndex.GetPartitionPagesPath(physicalSchema));
                    }

                    _core.IO.PutJson(transaction, indexCatalog.DiskPath, indexCatalog);
                }
            }
            catch (Exception ex)
            {
                LogManager.Error($"Failed to drop index for process id {transaction.ProcessId}.", ex);
                throw;
            }
        }

        internal Dictionary<uint, DocumentPointer> MatchConditionValuesDocuments(Transaction transaction, PhysicalSchema physicalSchema,
            IndexSelection indexSelection, SubCondition givenSubCondition, KbInsensitiveDictionary<string> conditionValues)
        {
            var firstCondition = givenSubCondition.Conditions.First();

            if (firstCondition.LogicalQualifier == LogicalQualifier.Equals)
            {
                //Yay, we have an "equals" condition so we can eliminate all but one partition.
                uint indexPartition = indexSelection.PhysicalIndex.ComputePartition(conditionValues.First().Value);
                string pageDiskPath = indexSelection.PhysicalIndex.GetPartitionPagesFileName(physicalSchema, indexPartition);
                var physicalIndexPages = _core.IO.GetPBuf<PhysicalIndexPages>(transaction, pageDiskPath, LockOperation.Read);
                return MatchDocuments(transaction, physicalIndexPages, indexSelection, givenSubCondition, conditionValues);
            }
            else
            {
                //Unfortunately, we can't easily eliminate index partitions. Lets gram some threads and scan all of the partitions.

                var queue = _core.ThreadPool.Generic.CreateChildQueue<MatchConditionValuesDocumentsInstance>();
                var operation = new MatchConditionValuesDocumentsOperation(
                    transaction, indexSelection.PhysicalIndex, physicalSchema, indexSelection, givenSubCondition, conditionValues);

                for (int indexPartition = 0; indexPartition < indexSelection.PhysicalIndex.Partitions; indexPartition++)
                {
                    if (queue.ExceptionOccurred())
                    {
                        break;
                    }

                    var instance = new MatchConditionValuesDocumentsInstance(operation, indexPartition);

                    var ptThreadQueue = transaction.PT?.CreateDurationTracker(PerformanceTraceCumulativeMetricType.ThreadQueue);
                    queue.Enqueue(instance, MatchConditionValuesDocumentsThreadWorker);
                    ptThreadQueue?.StopAndAccumulate();
                }

                var ptThreadCompletion = transaction.PT?.CreateDurationTracker(PerformanceTraceCumulativeMetricType.ThreadCompletion);
                queue.WaitForCompletion();
                ptThreadCompletion?.StopAndAccumulate();

                return operation.Results;
            }
        }

        private void MatchConditionValuesDocumentsThreadWorker(MatchConditionValuesDocumentsInstance instance)
        {
            try
            {
                instance.Operation.Transaction.EnsureActive();

                var ptThreadDeQueue = instance.Operation.Transaction.PT?.CreateDurationTracker(PerformanceTraceCumulativeMetricType.ThreadDeQueue);

                string pageDiskPath = instance.Operation.PhysicalIndex.GetPartitionPagesFileName(
                    instance.Operation.PhysicalSchema, (uint)instance.IndexPartition);

                var physicalIndexPages = _core.IO.GetPBuf<PhysicalIndexPages>(instance.Operation.Transaction, pageDiskPath, LockOperation.Write);

                var results = MatchDocuments(instance.Operation.Transaction, physicalIndexPages,
                    instance.Operation.IndexSelection, instance.Operation.GivenSubCondition, instance.Operation.ConditionValues);
                if (results.Count != 0)
                {
                    lock (instance.Operation.Results)
                    {
                        foreach (var kvp in results)
                        {
                            instance.Operation.Results.Add(kvp.Key, kvp.Value);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogManager.Error($"Failed to delete from index by thread.", ex);
                throw;
            }
        }

        private Dictionary<uint, DocumentPointer> MatchDocuments(Transaction transaction, PhysicalIndexPages physicalIndexPages,
            IndexSelection indexSelection, SubCondition givenSubCondition, KbInsensitiveDictionary<string> conditionValues)
        {
            try
            {
                List<PhysicalIndexLeaf> workingPhysicalIndexLeaves = [physicalIndexPages.Root];
                bool foundAnything = false;

                foreach (var attribute in indexSelection.PhysicalIndex.Attributes)
                {
                    var conditionField = givenSubCondition.Conditions
                        .FirstOrDefault(o => o.Left.Value?.Equals(attribute.Field.EnsureNotNull(), StringComparison.InvariantCultureIgnoreCase) == true);

                    if (conditionField == null)
                    {
                        //This happens when there is no condition on this index, this will be a partial match.
                        break;
                    }

                    if (conditionField.LogicalConnector == LogicalConnector.Or)
                    {
                        //TODO: Indexing only supports AND connectors, that's a performance problem.
                        break;
                    }

                    IEnumerable<PhysicalIndexLeaf>? foundLeaves = null;

                    var conditionValue = conditionValues[attribute.Field.EnsureNotNull().ToLowerInvariant()];

                    var ptIndexSeek = transaction.PT?.CreateDurationTracker(PerformanceTraceCumulativeMetricType.IndexSearch);

                    if (conditionField.LogicalQualifier == LogicalQualifier.Equals)
                        foundLeaves = workingPhysicalIndexLeaves.SelectMany(o => o.Children.Where(w => Condition.IsMatchEqual(transaction, w.Key, conditionValue) == true).Select(s => s.Value));
                    else if (conditionField.LogicalQualifier == LogicalQualifier.NotEquals)
                        foundLeaves = workingPhysicalIndexLeaves.SelectMany(o => o.Children.Where(w => Condition.IsMatchEqual(transaction, w.Key, conditionValue) == false).Select(s => s.Value));
                    else if (conditionField.LogicalQualifier == LogicalQualifier.GreaterThan)
                        foundLeaves = workingPhysicalIndexLeaves.SelectMany(o => o.Children.Where(w => Condition.IsMatchGreater(transaction, w.Key, conditionValue) == true).Select(s => s.Value));
                    else if (conditionField.LogicalQualifier == LogicalQualifier.GreaterThanOrEqual)
                        foundLeaves = workingPhysicalIndexLeaves.SelectMany(o => o.Children.Where(w => Condition.IsMatchGreaterOrEqual(transaction, w.Key, conditionValue) == true).Select(s => s.Value));
                    else if (conditionField.LogicalQualifier == LogicalQualifier.LessThan)
                        foundLeaves = workingPhysicalIndexLeaves.SelectMany(o => o.Children.Where(w => Condition.IsMatchLesser(transaction, w.Key, conditionValue) == true).Select(s => s.Value));
                    else if (conditionField.LogicalQualifier == LogicalQualifier.LessThanOrEqual)
                        foundLeaves = workingPhysicalIndexLeaves.SelectMany(o => o.Children.Where(w => Condition.IsMatchLesserOrEqual(transaction, w.Key, conditionValue) == true).Select(s => s.Value));
                    else if (conditionField.LogicalQualifier == LogicalQualifier.Like)
                        foundLeaves = workingPhysicalIndexLeaves.SelectMany(o => o.Children.Where(w => Condition.IsMatchLike(transaction, w.Key, conditionValue) == true).Select(s => s.Value));
                    else if (conditionField.LogicalQualifier == LogicalQualifier.NotLike)
                        foundLeaves = workingPhysicalIndexLeaves.SelectMany(o => o.Children.Where(w => Condition.IsMatchLike(transaction, w.Key, conditionValue) == false).Select(s => s.Value));
                    else if (conditionField.LogicalQualifier == LogicalQualifier.Between)
                        foundLeaves = workingPhysicalIndexLeaves.SelectMany(o => o.Children.Where(w => Condition.IsMatchBetween(transaction, w.Key, conditionField.Right.Value) == true).Select(s => s.Value));
                    else if (conditionField.LogicalQualifier == LogicalQualifier.NotBetween)
                        foundLeaves = workingPhysicalIndexLeaves.SelectMany(o => o.Children.Where(w => Condition.IsMatchBetween(transaction, w.Key, conditionField.Right.Value) == false).Select(s => s.Value));
                    else throw new KbNotImplementedException($"Logical qualifier has not been implemented for indexing: {conditionField.LogicalQualifier}");

                    ptIndexSeek?.StopAndAccumulate();

                    if (foundLeaves.EnsureNotNull().FirstOrDefault()?.Documents?.Count > 0) //We found documents, we are at the base of the index.
                    {
                        return foundLeaves.SelectMany(o => o.Documents ?? new()).ToDictionary(o => o.DocumentId, o => new DocumentPointer(o.PageNumber, o.DocumentId));
                    }

                    //Drop down to the next leaf in the virtual tree we are building.
                    workingPhysicalIndexLeaves = new List<PhysicalIndexLeaf>(foundLeaves);

                    if (foundAnything == false)
                    {
                        foundAnything = foundLeaves.Any();
                    }
                }

                if (foundAnything == false)
                {
                    return new Dictionary<uint, DocumentPointer>();
                }

                //This is an index scan.
                var ptIndexDistillation = transaction.PT?.CreateDurationTracker(PerformanceTraceCumulativeMetricType.IndexDistillation);
                //If we got here then we didn't get a full match and will need to add all of the child-leaf document IDs for later elimination.
                var resultingDocuments = DistillIndexLeaves(workingPhysicalIndexLeaves);
                ptIndexDistillation?.StopAndAccumulate();

                return resultingDocuments;
            }
            catch (Exception ex)
            {
                LogManager.Error($"Failed to match index documents for process id {transaction.ProcessId}.", ex);
                throw;
            }
        }

        internal Dictionary<uint, DocumentPointer> MatchWorkingSchemaDocuments(Transaction transaction,
                    PhysicalSchema physicalSchema, IndexSelection indexSelection, SubCondition givenSubCondition, string workingSchemaPrefix)
        {
            Condition? firstConditionLeft = null;
            Condition? firstConditionRight = null;

            foreach (var condition in givenSubCondition.Conditions)
            {
                if (indexSelection.CoveredFields.Any(o => o.Key == condition.Left.Key))
                {
                    firstConditionLeft = condition;
                }
                if (indexSelection.CoveredFields.Any(o => o.Key == condition.Right.Key))
                {
                    firstConditionRight = condition;
                }
            }

            if (firstConditionLeft?.LogicalQualifier == LogicalQualifier.Equals)
            {
                //Index seek.

                //Yay, we have an "equals" condition so we can eliminate all but one partition.

                //Yes, we matched the index on the left field, so the indexed value is on the right.
                var firstValue = firstConditionLeft?.Right.Value.EnsureNotNull();

                uint indexPartition = indexSelection.PhysicalIndex.ComputePartition(firstValue);
                string pageDiskPath = indexSelection.PhysicalIndex.GetPartitionPagesFileName(physicalSchema, indexPartition);
                var physicalIndexPages = _core.IO.GetPBuf<PhysicalIndexPages>(transaction, pageDiskPath, LockOperation.Read);
                return MatchDocuments(transaction, physicalIndexPages, indexSelection, givenSubCondition, workingSchemaPrefix);
            }
            else if (firstConditionRight?.LogicalQualifier == LogicalQualifier.Equals)
            {
                //Index seek.

                //Yay, we have an "equals" condition so we can eliminate all but one partition.

                //Yes, we matched the index on the right field, so the indexed value is on the left.
                var firstValue = firstConditionRight.Left.Value.EnsureNotNull();

                uint indexPartition = indexSelection.PhysicalIndex.ComputePartition(firstValue);
                string pageDiskPath = indexSelection.PhysicalIndex.GetPartitionPagesFileName(physicalSchema, indexPartition);
                var physicalIndexPages = _core.IO.GetPBuf<PhysicalIndexPages>(transaction, pageDiskPath, LockOperation.Read);
                return MatchDocuments(transaction, physicalIndexPages, indexSelection, givenSubCondition, workingSchemaPrefix);
            }
            else
            {
                //Index scan.

                //Unfortunately, we can't easily eliminate index partitions. Lets fire up some threads and scan all of the partitions.

                var queue = _core.ThreadPool.Generic.CreateChildQueue<MatchWorkingSchemaDocumentsOperation.MatchWorkingSchemaDocumentsInstance>();
                var operation = new MatchWorkingSchemaDocumentsOperation(
                    transaction, indexSelection.PhysicalIndex, physicalSchema, indexSelection, givenSubCondition, workingSchemaPrefix);

                for (int indexPartition = 0; indexPartition < indexSelection.PhysicalIndex.Partitions; indexPartition++)
                {
                    if (queue.ExceptionOccurred())
                    {
                        break;
                    }

                    var instance = new MatchWorkingSchemaDocumentsOperation.MatchWorkingSchemaDocumentsInstance(operation, indexPartition);

                    var ptThreadQueue = transaction.PT?.CreateDurationTracker(PerformanceTraceCumulativeMetricType.ThreadQueue);
                    queue.Enqueue(instance, MatchWorkingSchemaDocumentsThreadWorker);
                    ptThreadQueue?.StopAndAccumulate();
                }

                var ptThreadCompletion = transaction.PT?.CreateDurationTracker(PerformanceTraceCumulativeMetricType.ThreadCompletion);
                queue.WaitForCompletion();
                ptThreadCompletion?.StopAndAccumulate();

                return operation.Results;
            }
        }

        private void MatchWorkingSchemaDocumentsThreadWorker(MatchWorkingSchemaDocumentsOperation.MatchWorkingSchemaDocumentsInstance instance)
        {
            try
            {
                instance.Operation.Transaction.EnsureActive();

                string pageDiskPath = instance.Operation.PhysicalIndex.GetPartitionPagesFileName(
                    instance.Operation.PhysicalSchema, (uint)instance.IndexPartition);
                var physicalIndexPages = _core.IO.GetPBuf<PhysicalIndexPages>(instance.Operation.Transaction, pageDiskPath, LockOperation.Write);

                var results = MatchDocuments(instance.Operation.Transaction, physicalIndexPages,
                    instance.Operation.IndexSelection, instance.Operation.GivenSubCondition, instance.Operation.WorkingSchemaPrefix);

                if (results.Count != 0)
                {
                    lock (instance.Operation.Results)
                    {
                        foreach (var kvp in results)
                        {
                            instance.Operation.Results.Add(kvp.Key, kvp.Value);
                        }
                    }
                }

            }
            catch (Exception ex)
            {
                LogManager.Error($"Failed to delete from index by thread.", ex);
                throw;
            }
        }

        /// <summary>
        /// Finds document IDs given a set of conditions.
        /// </summary>
        private Dictionary<uint, DocumentPointer> MatchDocuments(Transaction transaction,
                    PhysicalIndexPages physicalIndexPages, IndexSelection indexSelection,
                    SubCondition givenSubCondition, string workingSchemaPrefix)
        {
            try
            {
                List<PhysicalIndexLeaf> workingPhysicalIndexLeaves = [physicalIndexPages.Root];

                bool foundAnything = false;

                foreach (var attribute in indexSelection.PhysicalIndex.Attributes)
                {
                    var conditionField = givenSubCondition.Conditions
                        .FirstOrDefault(o => o.Left.Prefix == workingSchemaPrefix
                        && o.Left.Value?.Equals(attribute.Field.EnsureNotNull(), StringComparison.InvariantCultureIgnoreCase) == true);

                    if (conditionField == null)
                    {
                        //This happens when there is no condition on this index, this will be a partial match.
                        break;
                    }

                    if (conditionField.LogicalConnector == LogicalConnector.Or)
                    {
                        //TODO: Indexing only supports AND connectors, that's a performance problem.
                        break;
                    }

                    IEnumerable<PhysicalIndexLeaf>? foundLeaves = null;

                    var ptIndexSeek = transaction.PT?.CreateDurationTracker(PerformanceTraceCumulativeMetricType.IndexSearch);

                    if (conditionField.LogicalQualifier == LogicalQualifier.Equals)
                        foundLeaves = workingPhysicalIndexLeaves.SelectMany(o => o.Children.Where(w => Condition.IsMatchEqual(transaction, w.Key, conditionField.Right.Value) == true).Select(s => s.Value));
                    else if (conditionField.LogicalQualifier == LogicalQualifier.NotEquals)
                        foundLeaves = workingPhysicalIndexLeaves.SelectMany(o => o.Children.Where(w => Condition.IsMatchEqual(transaction, w.Key, conditionField.Right.Value) == false).Select(s => s.Value));
                    else if (conditionField.LogicalQualifier == LogicalQualifier.GreaterThan)
                        foundLeaves = workingPhysicalIndexLeaves.SelectMany(o => o.Children.Where(w => Condition.IsMatchGreater(transaction, w.Key, conditionField.Right.Value) == true).Select(s => s.Value));
                    else if (conditionField.LogicalQualifier == LogicalQualifier.GreaterThanOrEqual)
                        foundLeaves = workingPhysicalIndexLeaves.SelectMany(o => o.Children.Where(w => Condition.IsMatchGreaterOrEqual(transaction, w.Key, conditionField.Right.Value) == true).Select(s => s.Value));
                    else if (conditionField.LogicalQualifier == LogicalQualifier.LessThan)
                        foundLeaves = workingPhysicalIndexLeaves.SelectMany(o => o.Children.Where(w => Condition.IsMatchLesser(transaction, w.Key, conditionField.Right.Value) == true).Select(s => s.Value));
                    else if (conditionField.LogicalQualifier == LogicalQualifier.LessThanOrEqual)
                        foundLeaves = workingPhysicalIndexLeaves.SelectMany(o => o.Children.Where(w => Condition.IsMatchLesserOrEqual(transaction, w.Key, conditionField.Right.Value) == true).Select(s => s.Value));
                    else if (conditionField.LogicalQualifier == LogicalQualifier.Like)
                        foundLeaves = workingPhysicalIndexLeaves.SelectMany(o => o.Children.Where(w => Condition.IsMatchLike(transaction, w.Key, conditionField.Right.Value) == true).Select(s => s.Value));
                    else if (conditionField.LogicalQualifier == LogicalQualifier.NotLike)
                        foundLeaves = workingPhysicalIndexLeaves.SelectMany(o => o.Children.Where(w => Condition.IsMatchLike(transaction, w.Key, conditionField.Right.Value) == false).Select(s => s.Value));
                    else if (conditionField.LogicalQualifier == LogicalQualifier.Between)
                        foundLeaves = workingPhysicalIndexLeaves.SelectMany(o => o.Children.Where(w => Condition.IsMatchBetween(transaction, w.Key, conditionField.Right.Value) == true).Select(s => s.Value));
                    else if (conditionField.LogicalQualifier == LogicalQualifier.NotBetween)
                        foundLeaves = workingPhysicalIndexLeaves.SelectMany(o => o.Children.Where(w => Condition.IsMatchBetween(transaction, w.Key, conditionField.Right.Value) == false).Select(s => s.Value));
                    else throw new KbNotImplementedException($"Logical qualifier has not been implemented for indexing: {conditionField.LogicalQualifier}");

                    ptIndexSeek?.StopAndAccumulate();

                    if (foundLeaves.EnsureNotNull().FirstOrDefault()?.Documents?.Count > 0) //We found documents, we are at the base of the index.
                    {
                        return foundLeaves.SelectMany(o => o.Documents ?? new()).ToDictionary(o => o.DocumentId, o => new DocumentPointer(o.PageNumber, o.DocumentId));
                    }

                    //Drop down to the next leaf in the virtual tree we are building.
                    workingPhysicalIndexLeaves = new List<PhysicalIndexLeaf>(foundLeaves);

                    if (foundAnything == false)
                    {
                        foundAnything = foundLeaves.Any();
                    }
                }

                if (foundAnything == false)
                {
                    return new Dictionary<uint, DocumentPointer>();
                }

                workingPhysicalIndexLeaves.EnsureNotNull();

                //This is an index scan.
                var ptIndexDistillation = transaction.PT?.CreateDurationTracker(PerformanceTraceCumulativeMetricType.IndexDistillation);
                //If we got here then we didn't get a full match and will need to add all of the child-leaf document IDs for later elimination.
                var resultingDocuments = DistillIndexLeaves(workingPhysicalIndexLeaves);
                ptIndexDistillation?.StopAndAccumulate();

                return resultingDocuments;
            }
            catch (Exception ex)
            {
                LogManager.Error($"Failed to match index documents for process id {transaction.ProcessId}.", ex);
                throw;
            }
        }

        private List<PhysicalIndexLeaf> DistillIndexBaseNodes(List<PhysicalIndexLeaf> physicalIndexLeaves)
        {
            var result = new List<PhysicalIndexLeaf>();

            foreach (var leaf in physicalIndexLeaves)
            {
                result.AddRange(DistillIndexBaseNodes(leaf));
            }

            return result;
        }

        /// <summary>
        /// Traverse to the bottom of the index tree (from whatever starting point is passed in) and return
        /// a list of all nodes containing documents.
        /// </summary>
        /// <param name="indexEntires"></param>
        /// <returns></returns>
        private List<PhysicalIndexLeaf> DistillIndexBaseNodes(PhysicalIndexLeaf physicalIndexLeaf)
        {
            try
            {
                var result = new List<PhysicalIndexLeaf>();

                void DistillIndexBaseNodesRecursive(PhysicalIndexLeaf physicalIndexLeaf)
                {
                    foreach (var child in physicalIndexLeaf.Children)
                    {
                        DistillIndexBaseNodesRecursive(child.Value);
                    }

                    if (physicalIndexLeaf?.Documents?.Count > 0)
                    {
                        result.Add(physicalIndexLeaf);
                    }
                }

                if (physicalIndexLeaf?.Documents?.Count > 0)
                {
                    result.Add(physicalIndexLeaf);
                }
                else if (physicalIndexLeaf?.Children != null)
                {
                    foreach (var child in physicalIndexLeaf.Children)
                    {
                        DistillIndexBaseNodesRecursive(child.Value);
                    }
                }

                return result;
            }
            catch (Exception ex)
            {
                LogManager.Error($"Failed to distill index base nodes.", ex);
                throw;
            }
        }

        private Dictionary<uint, DocumentPointer> DistillIndexLeaves(List<PhysicalIndexLeaf> physicalIndexLeaves)
        {
            var result = new List<DocumentPointer>();

            foreach (var leaf in physicalIndexLeaves)
            {
                result.AddRange(DistillIndexLeaves(leaf));
            }

            return result.ToDictionary(o => o.DocumentId, o => o);
        }

        /// <summary>
        /// Traverse to the bottom of the index tree (from whatever starting point is passed in) and return a list of all documentIds.
        /// </summary>
        /// <param name="indexEntires"></param>
        /// <returns></returns>
        private List<DocumentPointer> DistillIndexLeaves(PhysicalIndexLeaf physicalIndexLeaf)
        {
            try
            {
                var result = new List<DocumentPointer>();

                void DistillIndexLeavesRecursive(PhysicalIndexLeaf physicalIndexLeaf)
                {
                    foreach (var child in physicalIndexLeaf.Children)
                    {
                        DistillIndexLeavesRecursive(child.Value);
                    }

                    if (physicalIndexLeaf?.Documents?.Count > 0)
                    {
                        result.AddRange(physicalIndexLeaf.Documents.Select(o => new DocumentPointer(o.PageNumber, o.DocumentId)));
                    }
                }

                if (physicalIndexLeaf?.Documents?.Count > 0)
                {
                    result.AddRange(physicalIndexLeaf.Documents.Select(o => new DocumentPointer(o.PageNumber, o.DocumentId)));
                }
                else if (physicalIndexLeaf?.Children != null)
                {
                    foreach (var child in physicalIndexLeaf.Children)
                    {
                        DistillIndexLeavesRecursive(child.Value);
                    }
                }

                return result;
            }
            catch (Exception ex)
            {
                LogManager.Error($"Failed to distill index leaves.", ex);
                throw;
            }
        }

        internal PhysicalIndexCatalog AcquireIndexCatalog(Transaction transaction, string schemaName, LockOperation intendedOperation)
        {
            try
            {
                var physicalSchema = _core.Schemas.Acquire(transaction, schemaName, intendedOperation);
                return AcquireIndexCatalog(transaction, physicalSchema, intendedOperation);
            }
            catch (Exception ex)
            {
                LogManager.Error($"Failed to acquire index catalog for process id {transaction.ProcessId}.", ex);
                throw;
            }
        }

        internal PhysicalIndexCatalog AcquireIndexCatalog(Transaction transaction,
            PhysicalSchema physicalSchema, LockOperation intendedOperation)
        {
            try
            {
                var indexCatalog = _core.IO.GetJson<PhysicalIndexCatalog>(
                    transaction, physicalSchema.IndexCatalogFilePath(), intendedOperation);
                indexCatalog.DiskPath = physicalSchema.IndexCatalogFilePath();
                return indexCatalog;
            }
            catch (Exception ex)
            {
                LogManager.Error($"Failed to acquire index catalog for process id {transaction.ProcessId}.", ex);
                throw;
            }
        }

        private List<string> GetIndexSearchTokens(Transaction transaction, PhysicalIndex physicalIndex, PhysicalDocument document)
        {
            try
            {
                var result = new List<string>();

                foreach (var indexAttribute in physicalIndex.Attributes)
                {
                    if (document.Elements.TryGetValue(indexAttribute.Field.EnsureNotNull(), out string? documentValue))
                    {
                        if (documentValue != null) //TODO: How do we handle indexed NULL values?
                        {
                            result.Add(documentValue.ToLowerInvariant());
                        }
                    }
                }

                return result;
            }
            catch (Exception ex)
            {
                LogManager.Error($"Failed to get index search tokens for process {transaction.ProcessId}.", ex);
                throw;
            }
        }

        /// <summary>
        /// Finds the appropriate index page for a set of key values in the given index page catalog.
        /// </summary>
        /// <param name="transaction"></param>
        /// <param name="physicalIndex"></param>
        /// <param name="searchTokens"></param>
        /// <param name="indexPageCatalog"></param>
        /// <returns>A reference to a node in the suppliedIndexPageCatalog</returns>
        private IndexScanResult LocateExtentInGivenIndexPageCatalog(
            Transaction transaction, List<string> searchTokens, PhysicalIndexPages rootPhysicalIndexPages)
        {
            try
            {
                var physicalIndexPages = rootPhysicalIndexPages;

                var result = new IndexScanResult()
                {
                    Leaf = physicalIndexPages.Root,
                    MatchType = IndexMatchType.None
                };

                if (physicalIndexPages.Root.Children == null || physicalIndexPages.Root.Children.Count == 0)
                {
                    return result; //The index is empty.
                }

                foreach (var token in searchTokens)
                {
                    if (result.Leaf.Children == null)
                    {
                        break;
                    }

                    if (result.Leaf.Children.TryGetValue(token, out PhysicalIndexLeaf? value))
                    {
                        result.ExtentLevel++;
                        result.Leaf = value; //Move one level lower in the extent tree.
                    }
                    else
                    {
                        break;
                    }
                }

                if (result.ExtentLevel > 0)
                {
                    result.MatchType = result.ExtentLevel == searchTokens.Count ? IndexMatchType.Full : IndexMatchType.Partial;
                }

                return result;
            }
            catch (Exception ex)
            {
                LogManager.Error($"Failed to locate index extent for process {transaction.ProcessId}.", ex);
                throw;
            }
        }

        /// <summary>
        /// Updates an index entry for a single document into each index in the schema.
        /// </summary>
        /// <param name="transaction"></param>
        /// <param name="schema"></param>
        /// <param name="document"></param>
        internal void UpdateDocumentsIntoIndexes(Transaction transaction, PhysicalSchema physicalSchema,
            Dictionary<DocumentPointer, PhysicalDocument> documents, IEnumerable<string>? listOfModifiedFields)
        {
            try
            {
                var indexCatalog = AcquireIndexCatalog(transaction, physicalSchema, LockOperation.Read);

                foreach (var physicalIndex in indexCatalog.Collection)
                {
                    if (listOfModifiedFields == null || physicalIndex.Attributes.Any(o => listOfModifiedFields.Contains(o.Field)))
                    {
                        RemoveDocumentsFromIndex(transaction, physicalSchema, physicalIndex, documents.Select(o => o.Key));
                        InsertDocumentsIntoIndex(transaction, physicalSchema, physicalIndex, documents);
                    }
                }
            }
            catch (Exception ex)
            {
                LogManager.Error($"Failed to update document into indexes for process id {transaction.ProcessId}.", ex);
                throw;
            }
        }

        /// <summary>
        /// Inserts an index entry for a single document into each index in the schema.
        /// </summary>
        /// <param name="transaction"></param>
        /// <param name="schema"></param>
        /// <param name="document"></param>
        internal void InsertDocumentIntoIndexes(Transaction transaction,
            PhysicalSchema physicalSchema, PhysicalDocument physicalDocument, DocumentPointer documentPointer)
        {
            try
            {
                var indexCatalog = AcquireIndexCatalog(transaction, physicalSchema, LockOperation.Read);

                //Loop though each index in the schema.
                foreach (var physicalIndex in indexCatalog.Collection)
                {
                    InsertDocumentIntoIndex(transaction, physicalSchema, physicalIndex, physicalDocument, documentPointer);
                }
            }
            catch (Exception ex)
            {
                LogManager.Error($"Failed to insert document into indexes for process id {transaction.ProcessId}.", ex);
                throw;
            }
        }

        /// <summary>
        /// Inserts an index entry for a single document into each index in the schema.
        /// </summary>
        /// <param name="transaction"></param>
        /// <param name="schema"></param>
        /// <param name="document"></param>
        internal void InsertDocumentsIntoIndex(Transaction transaction,
            PhysicalSchema physicalSchema, PhysicalIndex physicalIndex, Dictionary<DocumentPointer, PhysicalDocument> documents)
        {
            try
            {
                foreach (var document in documents)
                {
                    InsertDocumentIntoIndex(transaction, physicalSchema, physicalIndex, document.Value, document.Key);
                }
            }
            catch (Exception ex)
            {
                LogManager.Error($"Failed to insert document into indexes for process id {transaction.ProcessId}.", ex);
                throw;
            }
        }


        /// <summary>
        /// Inserts an index entry for a single document into each index in the schema.
        /// </summary>
        /// <param name="transaction"></param>
        /// <param name="schema"></param>
        /// <param name="document"></param>
        internal void InsertDocumentsIntoIndexes(Transaction transaction,
            PhysicalSchema physicalSchema, Dictionary<DocumentPointer, PhysicalDocument> documents)
        {
            try
            {
                var indexCatalog = AcquireIndexCatalog(transaction, physicalSchema, LockOperation.Read);

                foreach (var document in documents)
                {
                    //Loop though each index in the schema.
                    foreach (var physicalIndex in indexCatalog.Collection)
                    {
                        InsertDocumentIntoIndex(transaction, physicalSchema, physicalIndex, document.Value, document.Key);
                    }
                }
            }
            catch (Exception ex)
            {
                LogManager.Error($"Failed to insert document into indexes for process id {transaction.ProcessId}.", ex);
                throw;
            }
        }

        /// <summary>
        /// Inserts an index entry for a single document into a single index using the file name from the index object.
        /// </summary>
        private void InsertDocumentIntoIndex(Transaction transaction,
            PhysicalSchema physicalSchema, PhysicalIndex physicalIndex, PhysicalDocument document, DocumentPointer documentPointer)
        {
            try
            {
                var documentField = physicalIndex.Attributes[0].Field;
                document.Elements.TryGetValue(documentField.EnsureNotNull(), out string? value);

                uint indexPartition = physicalIndex.ComputePartition(value);

                string pageDiskPath = physicalIndex.GetPartitionPagesFileName(physicalSchema, indexPartition);
                var physicalIndexPages = _core.IO.GetPBuf<PhysicalIndexPages>(transaction, pageDiskPath, LockOperation.Write);

                InsertDocumentIntoIndexPages(transaction, physicalIndex, physicalIndexPages, document, documentPointer);

                _core.IO.PutPBuf(transaction, pageDiskPath, physicalIndexPages);
            }
            catch (Exception ex)
            {
                LogManager.Error($"Failed to insert document into index for process id {transaction.ProcessId}.", ex);
                throw;
            }
        }


        /// <summary>
        /// Inserts an index entry for a single document into a single index using a long lived index page catalog.
        /// </summary>
        private void InsertDocumentIntoIndexPages(Transaction transaction, PhysicalIndex physicalIndex, PhysicalIndexPages physicalIndexPages, PhysicalDocument document, DocumentPointer documentPointer)
        {
            try
            {
                var searchTokens = GetIndexSearchTokens(transaction, physicalIndex, document);

                var indexScanResult = LocateExtentInGivenIndexPageCatalog(transaction, searchTokens, physicalIndexPages);

                //If we found a full match for all supplied key values - add the document to the leaf collection.
                if (indexScanResult.MatchType == IndexMatchType.Full)
                {
                    indexScanResult.Leaf.EnsureNotNull().Documents ??= new();

                    if (physicalIndex.IsUnique && indexScanResult.Leaf.Documents.EnsureNotNull().Count > 1)
                    {
                        string exceptionText = $"Duplicate key violation occurred for index [[{physicalIndex.Name}]. Values: {{{string.Join(",", searchTokens)}}}";
                        throw new KbDuplicateKeyViolationException(exceptionText);
                    }
                }
                else
                {
                    //If we didn't find a full match for all supplied key values, then create the tree and add the document to the
                    //  lowest leaf. Note that we are going to start creating the leaf level at the findResult.ExtentLevel. This is
                    //  because we may have a partial match and don't need to create the full tree.

                    for (int i = indexScanResult.ExtentLevel; i < searchTokens.Count; i++)
                    {
                        indexScanResult.Leaf = indexScanResult.Leaf.EnsureNotNull().AddNewLeaf(searchTokens[i]);
                    }


                    indexScanResult.Leaf.EnsureNotNull().Documents ??= new();
                }

                //Add the document to the lowest index extent.
                indexScanResult.Leaf.Documents.EnsureNotNull().Add(
                    new PhysicalIndexEntry(documentPointer.DocumentId, documentPointer.PageNumber));
            }
            catch (Exception ex)
            {
                LogManager.Error($"Failed to insert document into index for process id {transaction.ProcessId}.", ex);
                throw;
            }
        }

        private void RebuildIndexThreadWorker(RebuildIndexOperation.RebuildIndexInstance instance)
        {
            try
            {
                instance.Operation.Transaction.EnsureActive();

                if (instance.Operation.PhysicalSchema.DiskPath == null)
                {
                    throw new KbNullException($"Value should not be null {nameof(instance.Operation.PhysicalSchema.DiskPath)}.");
                }

                var physicalDocument = _core.Documents.AcquireDocument(
                    instance.Operation.Transaction, instance.Operation.PhysicalSchema, instance.DocumentPointer, LockOperation.Read);

                try
                {
                    var documentField = instance.Operation.PhysicalIndex.Attributes[0].Field;
                    physicalDocument.Elements.TryGetValue(documentField.EnsureNotNull(), out string? value);

                    uint indexPartition = instance.Operation.PhysicalIndex.ComputePartition(value);

                    lock (instance.Operation.SyncObjects[indexPartition])
                    {
                        string pageDiskPath = instance.Operation.PhysicalIndex.GetPartitionPagesFileName(
                            instance.Operation.PhysicalSchema, indexPartition);

                        var physicalIndexPages = _core.IO.GetPBuf<PhysicalIndexPages>(
                            instance.Operation.Transaction, pageDiskPath, LockOperation.Write);

                        InsertDocumentIntoIndexPages(instance.Operation.Transaction,
                            instance.Operation.PhysicalIndex, physicalIndexPages, physicalDocument, instance.DocumentPointer);
                    }
                }
                catch (Exception ex)
                {
                    LogManager.Error($"Failed to insert document into index for process id {instance.Operation.Transaction.ProcessId}.", ex);
                    throw;
                }
            }
            catch (Exception ex)
            {
                LogManager.Error($"Failed to rebuild index by thread.", ex);
                throw;
            }
        }


        /// <summary>
        /// Inserts all documents in a schema into a single index in the schema. Locks the index page catalog for write.
        /// </summary>
        /// <param name="transaction"></param>
        /// <param name="physicalSchema"></param>
        /// <param name="physicalIndex"></param>
        private void RebuildIndex(Transaction transaction, PhysicalSchema physicalSchema, PhysicalIndex physicalIndex)
        {
            try
            {
                var documentPointers = _core.Documents.AcquireDocumentPointers(transaction, physicalSchema, LockOperation.Read).ToList();

                //Clear out the existing index pages.
                if (Path.Exists(physicalIndex.GetPartitionPagesPath(physicalSchema)))
                {
                    _core.IO.DeletePath(transaction, physicalIndex.GetPartitionPagesPath(physicalSchema));
                }
                _core.IO.CreateDirectory(transaction, physicalIndex.GetPartitionPagesPath(physicalSchema));

                var physicalIndexPageMap = new Dictionary<uint, PhysicalIndexPages>();
                for (uint indexPartition = 0; indexPartition < physicalIndex.Partitions; indexPartition++)
                {
                    var physicalIndexPages = new PhysicalIndexPages();
                    physicalIndexPageMap.Add(indexPartition, physicalIndexPages);
                    _core.IO.PutPBuf(transaction, physicalIndex.GetPartitionPagesFileName
                        (physicalSchema, indexPartition), physicalIndexPages);
                }

                var queue = _core.ThreadPool.Generic.CreateChildQueue<RebuildIndexOperation.RebuildIndexInstance>();

                var operation = new RebuildIndexOperation(
                    transaction, physicalSchema, physicalIndexPageMap, physicalIndex, physicalIndex.Partitions);

                foreach (var documentPointer in documentPointers)
                {
                    if (queue.ExceptionOccurred())
                    {
                        break;
                    }

                    var instance = new RebuildIndexOperation.RebuildIndexInstance(operation, documentPointer);

                    var ptThreadQueue = transaction.PT?.CreateDurationTracker(PerformanceTraceCumulativeMetricType.ThreadQueue);
                    queue.Enqueue(instance, RebuildIndexThreadWorker);
                    ptThreadQueue?.StopAndAccumulate();
                }

                var ptThreadCompletion = transaction.PT?.CreateDurationTracker(PerformanceTraceCumulativeMetricType.ThreadCompletion);
                queue.WaitForCompletion();
                ptThreadCompletion?.StopAndAccumulate();

                for (uint indexPartition = 0; indexPartition < physicalIndex.Partitions; indexPartition++)
                {
                    _core.IO.PutPBuf(transaction, physicalIndex.GetPartitionPagesFileName(physicalSchema, indexPartition), physicalIndexPageMap[indexPartition]);
                }
            }
            catch (Exception ex)
            {
                LogManager.Error($"Failed to rebuild index for process id {transaction.ProcessId}.", ex);
                throw;
            }
        }

        /// <summary>
        /// Removes a collection of document from all of the indexes on the schema.
        /// </summary>
        /// <param name="transaction"></param>
        /// <param name="physicalSchema"></param>
        /// <param name="documentPointer"></param>
        internal void RemoveDocumentsFromIndexes(Transaction transaction,
            PhysicalSchema physicalSchema, IEnumerable<DocumentPointer> documentPointers)
        {
            try
            {
                var indexCatalog = AcquireIndexCatalog(transaction, physicalSchema, LockOperation.Read);

                //Loop though each index in the schema.
                foreach (var physicalIndex in indexCatalog.Collection)
                {
                    RemoveDocumentsFromIndex(transaction, physicalSchema, physicalIndex, documentPointers);
                }
            }
            catch (Exception ex)
            {
                LogManager.Error($"Failed to delete document from indexes for process id {transaction.ProcessId}.", ex);
                throw;
            }
        }

        private long RemoveDocumentsFromLeaves(PhysicalIndexLeaf leaf, IEnumerable<DocumentPointer> documentPointers)
        {
            return RemoveDocumentsFromLeaves(leaf, documentPointers, documentPointers.Count());
        }

        private long RemoveDocumentsFromLeaves(PhysicalIndexLeaf leaf, IEnumerable<DocumentPointer> documentPointers, long maxCount)
        {
            long totalDeletes = 0;

            try
            {
                if (leaf.Documents?.Count > 0)
                {
                    foreach (var documentPointer in documentPointers)
                    {
                        totalDeletes += leaf?.Documents.RemoveAll(o => o.PageNumber == documentPointer.PageNumber
                                                                    && o.DocumentId == documentPointer.DocumentId) ?? 0;
                        if (totalDeletes == maxCount)
                        {
                            break;
                        }

                    }
                    return totalDeletes;
                }

                foreach (var child in leaf.Children)
                {
                    totalDeletes += RemoveDocumentsFromLeaves(child.Value, documentPointers); //Dig to the bottom of each branch using recursion.
                    if (totalDeletes == maxCount)
                    {
                        break;
                    }
                }

                return totalDeletes;
            }
            catch (Exception ex)
            {
                LogManager.Error($"Failed to remove documents from index leaves.", ex);
                throw;
            }
        }



        /// <summary>
        /// Removes a collection of documents from an index. Locks the index page catalog for write.
        /// </summary>
        private void RemoveDocumentsFromIndex(Transaction transaction, PhysicalSchema physicalSchema,
            PhysicalIndex physicalIndex, IEnumerable<DocumentPointer> documentPointers)
        {
            try
            {
                bool useMultiThreadedIndexDeletion = true;

                //TODO: We need to determine how large this job is going to be and use threads when we have huge indexes.
                if (useMultiThreadedIndexDeletion == false)
                {
                    for (uint indexPartition = 0; indexPartition < physicalIndex.Partitions; indexPartition++)
                    {
                        string pageDiskPath = physicalIndex.GetPartitionPagesFileName(physicalSchema, indexPartition);
                        var physicalIndexPages = _core.IO.GetPBuf<PhysicalIndexPages>(transaction, pageDiskPath, LockOperation.Write);

                        if (RemoveDocumentsFromLeaves(physicalIndexPages.Root, documentPointers) > 0)
                        {
                            _core.IO.PutPBuf(transaction, pageDiskPath, physicalIndexPages);
                        }
                    }
                }
                else
                {
                    var queue = _core.ThreadPool.Generic.CreateChildQueue<RemoveDocumentsFromIndexThreadInstance>();
                    var operation = new RemoveDocumentsFromIndexThreadOperation(transaction, physicalIndex, physicalSchema, documentPointers);

                    for (int indexPartition = 0; indexPartition < physicalIndex.Partitions; indexPartition++)
                    {
                        if (queue.ExceptionOccurred())
                        {
                            break;
                        }

                        var instance = new RemoveDocumentsFromIndexThreadInstance(operation, indexPartition);

                        var ptThreadQueue = transaction.PT?.CreateDurationTracker(PerformanceTraceCumulativeMetricType.ThreadQueue);
                        queue.Enqueue(instance, RemoveDocumentsFromIndexThreadWorker);
                        ptThreadQueue?.StopAndAccumulate();
                    }

                    var ptThreadCompletion = transaction.PT?.CreateDurationTracker(PerformanceTraceCumulativeMetricType.ThreadCompletion);
                    queue.WaitForCompletion();
                    ptThreadCompletion?.StopAndAccumulate();
                }
            }
            catch (Exception ex)
            {
                LogManager.Error($"Failed to remove documents from index for process id {transaction.ProcessId}.", ex);
                throw;
            }
        }

        /// <summary>
        /// Thread parameters for a lookup operations. Used by a single thread.
        /// </summary>
        private class RemoveDocumentsFromIndexThreadInstance
        {
            public RemoveDocumentsFromIndexThreadOperation Operation { get; set; }
            public int IndexPartition { get; set; }

            public RemoveDocumentsFromIndexThreadInstance(RemoveDocumentsFromIndexThreadOperation operation, int indexPartition)
            {
                Operation = operation;
                IndexPartition = indexPartition;
            }
        }

        /// <summary>
        /// Thread parameters for a lookup operations. Shared across all threads in a single lookup operation.
        /// </summary>
        private class RemoveDocumentsFromIndexThreadOperation
        {
            public Transaction Transaction { get; set; }
            public PhysicalIndex PhysicalIndex { get; set; }
            public PhysicalSchema PhysicalSchema { get; set; }
            public IEnumerable<DocumentPointer> DocumentPointers { get; set; }

            public RemoveDocumentsFromIndexThreadOperation(Transaction transaction,
                PhysicalIndex physicalIndex, PhysicalSchema physicalSchema, IEnumerable<DocumentPointer> documentPointers)
            {
                Transaction = transaction;
                PhysicalIndex = physicalIndex;
                PhysicalSchema = physicalSchema;
                DocumentPointers = documentPointers;
            }
        }

        private void RemoveDocumentsFromIndexThreadWorker(RemoveDocumentsFromIndexThreadInstance instance)
        {
            try
            {
                instance.Operation.Transaction.EnsureActive();

                string pageDiskPath = instance.Operation.PhysicalIndex.GetPartitionPagesFileName(
                    instance.Operation.PhysicalSchema, (uint)instance.IndexPartition);

                var physicalIndexPages = _core.IO.GetPBuf<PhysicalIndexPages>(instance.Operation.Transaction, pageDiskPath, LockOperation.Write);

                if (RemoveDocumentsFromLeaves(physicalIndexPages.Root, instance.Operation.DocumentPointers) > 0)
                {
                    _core.IO.PutPBuf(instance.Operation.Transaction, pageDiskPath, physicalIndexPages);
                }
            }
            catch (Exception ex)
            {
                LogManager.Error($"Failed to delete from index by thread.", ex);
                throw;
            }
        }
    }
}
