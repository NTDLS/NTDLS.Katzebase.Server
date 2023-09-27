﻿using static NTDLS.Katzebase.KbConstants;

namespace NTDLS.Katzebase.Exceptions
{
    public class KbFunctionException : KbExceptionBase
    {
        public KbFunctionException()
        {
            Severity = KbLogSeverity.Warning;
        }

        public KbFunctionException(string message)
            : base($"Function exception: {message}.")

        {
            Severity = KbLogSeverity.Warning;
        }
    }
}