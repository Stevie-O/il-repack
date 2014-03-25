using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Mono.Cecil;

namespace ILRepacking
{
    /// <summary>
    /// Used to pass around the reasons for exposing a type that would otherwise be internalized
    /// </summary>
    internal class InternalizeExceptionReason : IFormattable
    {
        string _format;
        object[] _args;
        protected InternalizeExceptionReason(string message) { _format = message; _args = null; }
        protected InternalizeExceptionReason(string format, params object[] args)
        {
            _format = format;
            _args = args ?? new object[0];
        }

        public static readonly InternalizeExceptionReason PrimaryAssembly = new InternalizeExceptionReason("Type belongs to primary assembly");
        public static readonly InternalizeExceptionReason InternalizeDisabled = new InternalizeExceptionReason("/internalize was not specified");

        public static InternalizeExceptionReason InternalizeRule(InternalizeManager.InternalizeRule rule)
        {
            return new InternalizeExceptionReason("Internalize rule '{0}'", rule.TypeNameRegex);
        }

        public static InternalizeExceptionReason FieldType(FieldDefinition field)
        {
            return new InternalizeExceptionReason("Public field '{0}' in exposed type '{1}'", field.Name, field.DeclaringType.FullName);
        }

        public static InternalizeExceptionReason EventType(EventDefinition evt)
        {
            return new InternalizeExceptionReason("Public event '{0}' in exposed type '{1}'", evt.Name, evt.DeclaringType.FullName);
        }

        public static InternalizeExceptionReason PropertyType(PropertyDefinition prop)
        {
            return new InternalizeExceptionReason("Public property '{0}' in exposed type '{1}'", prop.Name, prop.DeclaringType.FullName);
        }

        public static InternalizeExceptionReason MethodReturnValue(MethodDefinition meth)
        {
            return new InternalizeExceptionReason("Return value of public method '{0}' in exposed type '{1}'", meth.ToString(), meth.DeclaringType.FullName);
        }

        public static InternalizeExceptionReason MethodParameter(MethodDefinition meth, ParameterDefinition parm)
        {
            return new InternalizeExceptionReason("Argument '{2}' of public method '{0}' in exposed type '{1}'", meth.ToString(), meth.DeclaringType.FullName, parm.Name);
        }

        public static InternalizeExceptionReason BaseClass(TypeDefinition childClass)
        {
            return new InternalizeExceptionReason("Base class of exposed type '{0}'", childClass.FullName);
        }

        public override string ToString()
        {
            if (_args == null) return _format;
            return ToString(null, null);
        }

        public string ToString(string format, IFormatProvider formatProvider)
        {
            if (_args == null) return _format;
            return string.Format(formatProvider, _format, _args);
        }
    }
}
