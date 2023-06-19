﻿using Katzebase.Engine.Atomicity;

namespace Katzebase.Engine.Atomicity.Management
{
    public class TransactiontAPIHandlers
    {
        private readonly Core core;

        public TransactiontAPIHandlers(Core core)
        {
            this.core = core;
        }

        public void Begin(ulong processId)
        {
            core.Transactions.Begin(processId, true);
        }

        public void Commit(ulong processId)
        {
            core.Transactions.Commit(processId);
        }

        public void Rollback(ulong processId)
        {
            core.Transactions.Rollback(processId);
        }
    }
}
