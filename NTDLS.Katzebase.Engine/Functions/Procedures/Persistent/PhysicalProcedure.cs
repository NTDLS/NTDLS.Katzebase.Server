﻿using NTDLS.Katzebase.Client.Exceptions;
using NTDLS.Katzebase.Engine.Functions.Parameters;

namespace NTDLS.Katzebase.Engine.Functions.Procedures.Persistent
{
    [Serializable]
    public class PhysicalProcedure
    {
        public List<PhysicalProcedureParameter> Parameters { get; set; } = new();
        public string Name { get; set; } = string.Empty;
        public Guid Id { get; set; }
        public DateTime Created { get; set; }
        public DateTime Modfied { get; set; }
        public List<string> Batches { get; set; } = new List<string>();

        public PhysicalProcedure Clone()
        {
            return new PhysicalProcedure
            {
                Id = Id,
                Name = Name,
                Created = Created,
                Modfied = Modfied
            };
        }

        internal ProcedureParameterValueCollection ApplyParameters(List<FunctionParameterBase> values)
        {
            int requiredParameterCount = Parameters.Where(o => o.Type.ToString().ToLower().Contains("optional") == false).Count();

            if (Parameters.Count < requiredParameterCount)
            {
                if (Parameters.Count > 0 && Parameters[0].Type == KbProcedureParameterType.Infinite_String)
                {
                    //The first parameter is infinite, we dont even check anything else.
                }
                else
                {
                    throw new KbFunctionException($"Incorrect number of parameter passed to {Name}.");
                }
            }

            var result = new ProcedureParameterValueCollection();

            if (Parameters.Count > 0 && Parameters[0].Type == KbProcedureParameterType.Infinite_String)
            {
                for (int i = 0; i < Parameters.Count; i++)
                {
                    if (values[i] is FunctionExpression)
                    {
                        var expression = (FunctionExpression)values[i];
                        result.Values.Add(new ProcedureParameterValue(Parameters[0].ToProcedureParameterPrototype(), expression.Value));
                    }
                    else
                    {
                        throw new KbNotImplementedException($"Parameter type [{values[i].GetType()}] is not implemented.");
                    }
                }
            }
            else
            {
                for (int i = 0; i < Parameters.Count; i++)
                {
                    if (i >= values.Count)
                    {
                        result.Values.Add(new ProcedureParameterValue(Parameters[i].ToProcedureParameterPrototype()));
                    }
                    else
                    {
                        if (values[i] is FunctionExpression)
                        {
                            var expression = (FunctionExpression)values[i];
                            result.Values.Add(new ProcedureParameterValue(Parameters[i].ToProcedureParameterPrototype(), expression.Value));
                        }
                        else if (values[i] is FunctionConstantParameter)
                        {
                            var expression = (FunctionConstantParameter)values[i];
                            result.Values.Add(new ProcedureParameterValue(Parameters[i].ToProcedureParameterPrototype(), expression.RawValue));
                        }
                        else
                        {
                            throw new KbNotImplementedException($"Parameter type [{values[i].GetType()}] is not implemented.");
                        }
                    }
                }
            }

            return result;
        }
    }
}
