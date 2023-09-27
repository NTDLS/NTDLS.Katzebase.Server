﻿using NTDLS.Katzebase.Engine.Query;
using NTDLS.Katzebase.Exceptions;
using NTDLS.Katzebase.Payloads;
using static NTDLS.Katzebase.Engine.Library.EngineConstants;

namespace NTDLS.Katzebase.Engine.Interactions.QueryHandlers
{
    /// <summary>
    /// Internal class methods for handling query requests related to configuration.
    /// </summary>
    internal class EnvironmentQueryHandlers
    {
        private readonly Core _core;

        public EnvironmentQueryHandlers(Core core)
        {
            _core = core;

            try
            {
            }
            catch (Exception ex)
            {
                core.Log.Write($"Failed to instanciate environment query handler.", ex);
                throw;
            }
        }

        internal KbActionResponse ExecuteAlter(ulong processId, PreparedQuery preparedQuery)
        {
            try
            {
                using var transactionReference = _core.Transactions.Acquire(processId);

                if (preparedQuery.SubQueryType == SubQueryType.Configuration)
                {
                    _core.Environment.Alter(transactionReference.Transaction, preparedQuery.Attributes);
                }
                else
                {
                    throw new KbNotImplementedException();
                }

                return transactionReference.CommitAndApplyMetricsThenReturnResults();
            }
            catch (Exception ex)
            {
                _core.Log.Write($"Failed to execute environment alter for process id {processId}.", ex);
                throw;
            }
        }
    }
}