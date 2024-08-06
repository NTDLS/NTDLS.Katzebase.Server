﻿using NTDLS.Katzebase.Engine.Library;

namespace NTDLS.Katzebase.Engine.Sessions
{
    public class SessionState
    {
        public enum KbConnectionSetting
        {
            TraceWaitTimes,
            ExplainQuery
        }

        /// <summary>
        /// The query currently associated with the session.
        /// </summary>
        public string QueryText { get; set; } = string.Empty;

        /// <summary>
        /// Settings associated with the connection.
        /// </summary>
        public List<KbNameValuePair<KbConnectionSetting, double>> Variables { get; private set; } = new();

        /// <summary>
        /// The UTC date/time that the session was created.
        /// </summary>
        public DateTime LoginTime { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// The last UTC date/time that the connection was interacted with.
        /// </summary>
        public DateTime LastCheckInTime { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// ProcessId is produced by the server.
        /// </summary>
        public ulong ProcessId { get; private set; }

        /// <summary>
        /// SessionId is produced by the client.
        /// </summary>
        public Guid ConnectionId { get; set; }

        /// <summary>
        /// A user supplied client name to assist in identifying connection sources.
        /// </summary>
        public string? ClientName { get; set; }

        public KbNameValuePair<KbConnectionSetting, double> UpsertConnectionSetting(KbConnectionSetting name, double value)
        {
            var result = Variables.FirstOrDefault(o => o.Name == name);
            if (result != null)
            {
                result.Value = value;
            }
            else
            {
                result = new(name, value);
                Variables.Add(result);
            }
            return result;
        }

        /// <summary>
        /// Shortcut to determine if a value is set to 1 (boolean true).
        /// </summary>
        /// <param name="name"></param>
        /// <returns></returns>
        public bool IsConnectionSettingSet(KbConnectionSetting name)
        {
            var result = Variables.FirstOrDefault(o => o.Name == name);
            return result?.Value == 1;
        }

        public bool IsConnectionSettingPresent(KbConnectionSetting name)
        {
            var result = Variables.FirstOrDefault(o => o.Name == name);
            return result != null;
        }

        public double? GetConnectionSetting(KbConnectionSetting name)
        {
            var result = Variables.FirstOrDefault(o => o.Name == name);
            if (result != null)
            {
                return result.Value;
            }
            return null;
        }

        public SessionState(ulong processId, Guid connectionId)
        {
            ProcessId = processId;
            ConnectionId = connectionId;
        }

        public void SetCurrentQuery(string statement)
            => QueryText = statement;

        public void ClearCurrentQuery()
            => QueryText = string.Empty;
    }
}
