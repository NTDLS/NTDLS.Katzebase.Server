﻿namespace NTDLS.Katzebase.Engine.Query.Searchers
{
    public class DocumentLookupResult
    {
        public List<string?> Values { get; private set; } = new();

        public List<string?> AggregateFields { get; private set; } = new();

        public DocumentLookupResult(List<string?> values)
        {
            Values.AddRange(values);
        }

        public DocumentLookupResult(List<string?> values, List<string?> aggregateFields)
        {
            Values.AddRange(values);
            AggregateFields.AddRange(aggregateFields);
        }
    }
}
