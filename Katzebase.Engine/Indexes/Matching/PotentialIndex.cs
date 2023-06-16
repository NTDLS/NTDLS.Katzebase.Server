﻿namespace Katzebase.Engine.Indexes.Matching
{
    public class PotentialIndex
    {
        public List<string> CoveredFields { get; private set; }
        public PhysicalIndex Index { get; set; }
        public bool Tried { get; set; }
        public string CoveredHash => string.Join(":", CoveredFields.OrderBy(o => o)).ToLowerInvariant();
        public Guid SourceSubsetUID { get; private set; }

        public PotentialIndex(PhysicalIndex index, List<string> coveredFields)
        {
            Index = index;
            CoveredFields = coveredFields;
        }
    }
}