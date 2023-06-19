﻿using Katzebase.Engine.Atomicity;
using Katzebase.Engine.Documents;
using Katzebase.Engine.Indexes;
using Katzebase.Engine.Trace;
using Katzebase.PublicLibrary;
using Katzebase.PublicLibrary.Exceptions;
using static Katzebase.Engine.KbLib.EngineConstants;
using static Katzebase.Engine.Schemas.PhysicalSchema;
using static Katzebase.Engine.Trace.PerformanceTrace;

namespace Katzebase.Engine.Schemas.Management
{
    public class SchemaManager
    {
        private Core core;
        private string rootCatalogFile;
        private PhysicalSchema? rootPhysicalSchema = null;
        internal SchemaQueryHandlers QueryHandlers { get; set; }
        public SchemaAPIHandlers APIHandlers { get; set; }

        public PhysicalSchema RootPhysicalSchema
        {
            get
            {
                rootPhysicalSchema ??= new PhysicalSchema()
                {
                    Id = RootSchemaGUID,
                    DiskPath = core.Settings.DataRootPath,
                    VirtualPath = string.Empty,
                    //Exists = true,
                    Name = string.Empty,
                };
                return rootPhysicalSchema;
            }
        }

        public SchemaManager(Core core)
        {
            this.core = core;
            QueryHandlers = new SchemaQueryHandlers(core);
            APIHandlers = new SchemaAPIHandlers(core);

            rootCatalogFile = Path.Combine(core.Settings.DataRootPath, SchemaCatalogFile);

            //If the catalog doesnt exist, create a new empty one.
            if (File.Exists(rootCatalogFile) == false)
            {
                Directory.CreateDirectory(core.Settings.DataRootPath);

                core.IO.PutJsonNonTracked(Path.Combine(core.Settings.DataRootPath, SchemaCatalogFile), new PhysicalSchemaCatalog());
                core.IO.PutJsonNonTracked(Path.Combine(core.Settings.DataRootPath, DocumentPageCatalogFile), new PhysicalDocumentPageCatalog());
                core.IO.PutJsonNonTracked(Path.Combine(core.Settings.DataRootPath, IndexCatalogFile), new PhysicalIndexCatalog());
            }
        }




        #region Core get/put/lock methods.

        public void CreateSingleSchema(ulong processId, string schemaName)
        {
            try
            {
                using (var transaction = core.Transactions.Acquire(processId))
                {
                    Guid newSchemaId = Guid.NewGuid();

                    var physicalSchema = AcquireVirtual(transaction, schemaName, LockOperation.Write);
                    if (physicalSchema.Exists)
                    {
                        transaction.Commit();
                        //The schema already exists.
                        return;
                    }

                    var parentPhysicalSchema = AcquireParent(transaction, physicalSchema, LockOperation.Write);

                    if (parentPhysicalSchema.DiskPath == null || physicalSchema.DiskPath == null)
                    {
                        throw new KbNullException($"Value should not be null {nameof(physicalSchema.DiskPath)}.");
                    }

                    string parentSchemaCatalogFile = Path.Combine(parentPhysicalSchema.DiskPath, SchemaCatalogFile);
                    PhysicalSchemaCatalog? parentCatalog = core.IO.GetJson<PhysicalSchemaCatalog>(transaction, parentSchemaCatalogFile, LockOperation.Write);

                    string filePath = string.Empty;

                    core.IO.CreateDirectory(transaction, physicalSchema.DiskPath);

                    //Create default schema catalog file.
                    filePath = Path.Combine(physicalSchema.DiskPath, SchemaCatalogFile);
                    if (core.IO.FileExists(transaction, filePath, LockOperation.Write) == false)
                    {
                        core.IO.PutJson(transaction, filePath, new PhysicalSchemaCatalog());
                    }

                    //Create default document page catalog file.
                    filePath = Path.Combine(physicalSchema.DiskPath, DocumentPageCatalogFile);
                    if (core.IO.FileExists(transaction, filePath, LockOperation.Write) == false)
                    {
                        core.IO.PutJson(transaction, filePath, new PhysicalDocumentPageCatalog());
                    }

                    //Create default index catalog file.
                    filePath = Path.Combine(physicalSchema.DiskPath, IndexCatalogFile);
                    if (core.IO.FileExists(transaction, filePath, LockOperation.Write) == false)
                    {
                        core.IO.PutJson(transaction, filePath, new PhysicalIndexCatalog());
                    }

                    if (parentCatalog.ContainsName(schemaName) == false)
                    {
                        parentCatalog.Add(new PhysicalSchema
                        {
                            Id = newSchemaId,
                            Name = physicalSchema.Name
                        });

                        core.IO.PutJson(transaction, parentSchemaCatalogFile, parentCatalog);
                    }

                    transaction.Commit();
                }
            }
            catch (Exception ex)
            {
                core.Log.Write($"Failed to create single schema for process {processId}.", ex);
                throw;
            }
        }

        internal List<PhysicalSchema> AcquireChildren(Transaction transaction, PhysicalSchema schema, LockOperation intendedOperation)
        {
            var schemas = new List<PhysicalSchema>();

            string schemaCatalogDiskPath = Path.Combine(schema.DiskPath, SchemaCatalogFile);

            if (core.IO.FileExists(transaction, schemaCatalogDiskPath, intendedOperation))
            {
                var schemaCatalog = core.IO.GetJson<PhysicalSchemaCatalog>(transaction, schemaCatalogDiskPath, intendedOperation);

                foreach (var catalogItem in schemaCatalog.Collection)
                {
                    schemas.Add(new PhysicalSchema()
                    {
                        DiskPath = schema.DiskPath + "\\" + catalogItem.Name,
                        Id = catalogItem.Id,
                        Name = catalogItem.Name,
                        VirtualPath = schema.VirtualPath + ":" + catalogItem.Name
                    });
                }
            }

            return schemas;
        }

        internal PhysicalSchema AcquireParent(Transaction transaction, PhysicalSchema child, LockOperation intendedOperation)
        {
            try
            {
                if (child == RootPhysicalSchema)
                {
                    throw new KbGenericException("The root schema does not have a parent.");
                }

                if (child.VirtualPath == null)
                {
                    throw new KbNullException($"Value should not be null {nameof(child.VirtualPath)}.");
                }

                var segments = child.VirtualPath.Split(':').ToList();
                segments.RemoveAt(segments.Count - 1);
                string parentNs = string.Join(":", segments);
                return Acquire(transaction, parentNs, intendedOperation);
            }
            catch (Exception ex)
            {
                core.Log.Write("Failed to get parent schema.", ex);
                throw;
            }
        }

        /// <summary>
        /// Opens a schema for a desired access. Takes a virtual schema path (schema:schema2:scheams3) and converts to to a physical location
        /// </summary>
        internal PhysicalSchema Acquire(Transaction transaction, string schemaName, LockOperation intendedOperation)
        {
            PerformanceTraceDurationTracker? ptLockSchema = null;

            try
            {
                ptLockSchema = transaction.PT?.CreateDurationTracker<PhysicalSchema>(PerformanceTraceCumulativeMetricType.Lock);
                schemaName = schemaName.Trim(new char[] { ':' }).Trim();

                if (schemaName == string.Empty)
                {
                    return RootPhysicalSchema;
                }
                else
                {
                    var segments = schemaName.Split(':');
                    var parentSchemaame = segments[segments.Count() - 1];

                    var schemaDiskPath = Path.Combine(core.Settings.DataRootPath, string.Join("\\", segments));
                    var parentSchemaDiskPath = Directory.GetParent(schemaDiskPath)?.FullName;
                    Utility.EnsureNotNull(parentSchemaDiskPath);

                    var parentCatalogDiskPath = Path.Combine(parentSchemaDiskPath, SchemaCatalogFile);

                    if (core.IO.FileExists(transaction, parentCatalogDiskPath, intendedOperation) == false)
                    {
                        throw new KbObjectNotFoundException($"The schema [{schemaName}] does not exist.");
                    }

                    var parentCatalog = core.IO.GetJson<PhysicalSchemaCatalog>(transaction,
                        Path.Combine(parentSchemaDiskPath, SchemaCatalogFile), intendedOperation);

                    var physicalSchema = parentCatalog.GetByName(parentSchemaame);
                    if (physicalSchema != null)
                    {
                        physicalSchema.Name = parentSchemaame;
                        physicalSchema.DiskPath = schemaDiskPath;
                        physicalSchema.VirtualPath = schemaName;
                    }
                    else
                    {
                        throw new KbObjectNotFoundException(schemaName);
                    }

                    transaction.LockDirectory(intendedOperation, physicalSchema.DiskPath);

                    return physicalSchema;
                }
            }
            catch (Exception ex)
            {
                core.Log.Write("Failed to translate virtual path to schema.", ex);
                throw;
            }
            finally
            {
                ptLockSchema?.StopAndAccumulate();
            }
        }

        /// <summary>
        /// Opens a schema for a desired access even if it does not exist. Takes a virtual schema path (schema:schema2:scheams3) and converts to to a physical location
        /// </summary>
        internal VirtualSchema AcquireVirtual(Transaction transaction, string schemaName, LockOperation intendedOperation)
        {
            PerformanceTraceDurationTracker? ptLockSchema = null;

            try
            {
                ptLockSchema = transaction.PT?.CreateDurationTracker<PhysicalSchema>(PerformanceTraceCumulativeMetricType.Lock);
                schemaName = schemaName.Trim(new char[] { ':' }).Trim();

                if (schemaName == string.Empty)
                {
                    return RootPhysicalSchema.ToVirtual();
                }
                else
                {
                    var segments = schemaName.Split(':');
                    var parentSchemaName = segments[segments.Count() - 1];

                    var schemaDiskPath = Path.Combine(core.Settings.DataRootPath, string.Join("\\", segments));
                    var parentSchemaDiskPath = Directory.GetParent(schemaDiskPath)?.FullName;
                    Utility.EnsureNotNull(parentSchemaDiskPath);

                    var parentCatalogDiskPath = Path.Combine(parentSchemaDiskPath, SchemaCatalogFile);

                    if (core.IO.FileExists(transaction, parentCatalogDiskPath, intendedOperation) == false)
                    {
                        throw new KbObjectNotFoundException($"The schema [{schemaName}] does not exist.");
                    }

                    var parentCatalog = core.IO.GetJson<PhysicalSchemaCatalog>(transaction,
                        Path.Combine(parentSchemaDiskPath, SchemaCatalogFile), intendedOperation);

                    var virtualSchema = parentCatalog.GetByName(parentSchemaName)?.ToVirtual();
                    if (virtualSchema != null)
                    {
                        virtualSchema.Name = parentSchemaName;
                        virtualSchema.DiskPath = schemaDiskPath;
                        virtualSchema.VirtualPath = schemaName;
                        virtualSchema.Exists = true;
                    }
                    else
                    {
                        virtualSchema = new VirtualSchema()
                        {
                            Name = parentSchemaName,
                            DiskPath = core.Settings.DataRootPath + "\\" + schemaName.Replace(':', '\\'),
                            VirtualPath = schemaName,
                            Exists = false
                        };
                    }

                    transaction.LockDirectory(intendedOperation, virtualSchema.DiskPath);

                    return virtualSchema;
                }
            }
            catch (Exception ex)
            {
                core.Log.Write("Failed to translate virtual path to schema.", ex);
                throw;
            }
            finally
            {
                ptLockSchema?.StopAndAccumulate();
            }
        }

        #endregion
    }
}