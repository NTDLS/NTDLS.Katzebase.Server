﻿using static NTDLS.Katzebase.KbConstants;

namespace NTDLS.Katzebase.Engine.Query
{
    public class SortFields : PrefixedFields
    {
        public KbSortDirection SortDirection { get; private set; }

        public SortField Add(string key, KbSortDirection sortDirection)
        {
            string prefix = string.Empty;
            string field = key;

            if (key.Contains('.'))
            {
                var parts = key.Split('.');
                prefix = parts[0];
                field = parts[1];
            }

            var newField = new SortField(prefix, field)
            {
                Ordinal = Count,
                SortDirection = sortDirection
            };
            Add(newField);

            return newField;
        }
    }
}
