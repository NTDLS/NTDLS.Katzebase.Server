﻿using static Katzebase.Library.Constants;

namespace Katzebase.Library.Exceptions
{
    public class KbNotImplementedException : KbExceptionBase
    {
        public KbNotImplementedException()
        {
            Severity = LogSeverity.Warning;
        }

        public KbNotImplementedException(string message)
            : base(message)

        {
            Severity = LogSeverity.Warning;
        }
    }
}