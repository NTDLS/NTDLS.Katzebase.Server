﻿using Katzebase.Engine.Library;
using Katzebase.Engine.Method.ParsedMethodParameter;
using Katzebase.Engine.Query.Constraints;
using static Katzebase.Engine.Library.EngineConstants;

namespace Katzebase.Engine.Query
{
    internal class PreparedQuery
    {
        internal enum QueryAttribute
        {
            IndexName,
            IsUnique,
            ProcessId,
            SpecificSchemaPrefix
        }

        public bool DynamicallyBuildSelectList { get; set; } = false;

        public Dictionary<QueryAttribute, object> Attributes { get; set; } = new();
        public List<QuerySchema> Schemas { get; set; } = new();
        public int RowLimit { get; set; }
        public QueryType QueryType { get; set; }
        public SubQueryType SubQueryType { get; set; }
        public Conditions Conditions { get; set; } = new();
        //public PrefixedFields SelectFields { get; set; } = new();

        public PrefixedFields CreateFields { get; set; } = new();

        public GenericParsedMethodParameters SelectFields = new();

        public PrefixedFields GroupFields { get; set; } = new();
        public SortFields SortFields { get; set; } = new();

        /// <summary>
        /// List of key/values used for insert statements.
        /// </summary>
        public List<UpsertKeyValues> UpsertValues { get; set; } = new();

        public List<KbNameValuePair<string, string>> VariableValues { get; set; } = new();

        public T Attribute<T>(QueryAttribute attribute)
        {
            return (T)Attributes[attribute];
        }

        public void AddAttribute(QueryAttribute key, object value)
        {
            if (Attributes.ContainsKey(key))
            {
                Attributes[key] = value;
            }
            else
            {
                Attributes.Add(key, value);
            }
        }
    }
}
