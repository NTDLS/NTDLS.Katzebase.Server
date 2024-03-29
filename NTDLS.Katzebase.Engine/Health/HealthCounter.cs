﻿using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using static NTDLS.Katzebase.Engine.Library.EngineConstants;

namespace NTDLS.Katzebase.Engine.Health
{
    public class HealthCounter
    {
        [JsonConverter(typeof(StringEnumConverter))]
        public HealthCounterType Type { get; set; }
        public string Instance { get; set; } = string.Empty;
        public double Value { get; set; }
        public DateTime WaitDateTimeUtc { get; set; }
    }
}
