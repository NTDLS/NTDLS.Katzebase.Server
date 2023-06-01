﻿using static Katzebase.Library.Constants;

namespace Katzebase.Library.Exceptions
{
    public class KbParserException : KbExceptionBase
    {
        public KbParserException()
        {
            Severity = LogSeverity.Warning;
        }

        public KbParserException(string message)
            : base(message)

        {
            Severity = LogSeverity.Warning;
        }
    }
}