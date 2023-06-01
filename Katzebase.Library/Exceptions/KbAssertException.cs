﻿using static Katzebase.Library.Constants;

namespace Katzebase.Library.Exceptions
{
    public class KbAssertException : KbExceptionBase
    {
        public KbAssertException()
        {
            Severity = LogSeverity.Warning;
        }

        public KbAssertException(string message)
            : base(message)

        {
            Severity = LogSeverity.Exception;
        }
    }
}