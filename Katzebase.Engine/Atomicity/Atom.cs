﻿using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using static Katzebase.Engine.KbLib.EngineConstants;

namespace Katzebase.Engine.Atomicity
{
    /// <summary>
    /// The atom is a unit of reversable work.
    /// </summary>
    public class Atom
    {
        [JsonConverter(typeof(StringEnumConverter))]
        public ActionType Action { get; set; }
        public string OriginalPath { get; set; }
        public string Key { get; set; }
        public string? BackupPath { get; set; }
        public int Sequence { get; set; } = 0;

        public Atom(ActionType action, string originalPath)
        {
            Action = action;
            OriginalPath = originalPath;
            Key = OriginalPath.ToLower();
        }
    }
}