using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Mono.Cecil;

namespace ILRepacking
{
    class InternalizeManager
    {
        InternalizeRule[] rules;
        HashSet<TypeDefinition> publicTypes;
        HashSet<AssemblyDefinition> assemblies;
        ILRepack owner;

        /// <summary>
        /// Do not use. This class is only visible to make MemberInternalizeManager work.
        /// </summary>
        internal class InternalizeRule
        {
            /// <summary>
            /// If true, DO internalize the given item (as opposed to leaving it public)
            /// </summary>
            public bool ForceInternal { get; set; }
            /// <summary>
            /// Gets/sets the regular expression for matching the type name
            /// </summary>
            public Regex TypeNameRegex { get; set; }
            /// <summary>
            /// Gets/sets the regular expression for matching member names (may be null)
            /// </summary>
            public Regex MemberNameRegex { get; set; }

            /// <summary>
            /// Parse a line into an InternalizeRule
            /// </summary>
            /// <param name="line"></param>
            /// <returns></returns>
            /// <remarks>
            /// Format is
            /// (invert flag)(type name regex)[::(member name regex)]
            /// </remarks>
            public static InternalizeRule Parse(string line)
            {
                if (string.IsNullOrEmpty(line)) return null;
                InternalizeRule ir = new InternalizeRule();
                if (line[0] == '!')
                {
                    ir.ForceInternal = true;
                    line = line.Substring(1);
                }
                int method_idx = line.IndexOf("::");
                if (method_idx < 0)
                {
                    ir.TypeNameRegex = new Regex(line);
                }
                else
                {
                    ir.TypeNameRegex = new Regex(line.Substring(0, method_idx));
                    ir.MemberNameRegex = new Regex(line.Substring(method_idx + 2));
                }
                return ir;
            }
        }

        /// <summary>
        /// Constructs a new InternalizeManager
        /// </summary>
        /// <param name="excludeLines">Lines from exclude file</param>
        /// <param name="owner">Used to emit log messages</param>
        public InternalizeManager(string[] excludeLines, ILRepack owner)
        {
            if (excludeLines == null)
                throw new ArgumentNullException("excludeLines");
            this.owner = owner;

            List<InternalizeRule> rules = new List<InternalizeRule>(excludeLines.Length);
            foreach (string excludeLine in excludeLines)
                rules.Add(InternalizeRule.Parse(excludeLine));
            this.rules = rules.ToArray();
            this.publicTypes = new HashSet<TypeDefinition>(new TypeDefinitionComparer());
            this.assemblies = new HashSet<AssemblyDefinition>(new AssemblyDefinitionComparer());
        }

        public class MemberInternalizeManager
        {
            InternalizeRule[] rules;

            public MemberInternalizeManager(InternalizeRule[] rules)
            {
                this.rules = rules;
            }

            public bool ShouldInternalize(IMemberDefinition member)
            {
                foreach (InternalizeRule r in rules)
                {
                    if (r.MemberNameRegex.IsMatch(member.Name))
                        return r.ForceInternal;
                }
                return false;
            }
        }

        sealed class TypeDefinitionComparer : IEqualityComparer<TypeDefinition>
        {
            public bool Equals(TypeDefinition x, TypeDefinition y)
            {
                if ((object)x == (object)y) return true;
                if ((object)x == null || (object)y == null) return false;
                // TODO: find out the correct way to determine equivalence between two items
                return x.Module.FullyQualifiedName == y.Module.FullyQualifiedName
                        && x.FullName == y.FullName;
            }

            public int GetHashCode(TypeDefinition obj)
            {
                if (obj == null) return 0;
                return obj.Module.FullyQualifiedName.GetHashCode()
                        ^ obj.FullName.GetHashCode();
            }
        }
        sealed class AssemblyDefinitionComparer : IEqualityComparer<AssemblyDefinition>
        {
            public bool Equals(AssemblyDefinition x, AssemblyDefinition y)
            {
                if ((object)x == (object)y) return true;
                if ((object)x == null || (object)y == null) return false;
                // TODO: find out the correct way to determine equivalence between two items
                return x.FullName == y.FullName;
            }

            public int GetHashCode(AssemblyDefinition obj)
            {
                if (obj == null) return 0;
                return obj.FullName.GetHashCode();
            }
        }


        /// <summary>
        /// Determines whether or not a type should be internalized.
        /// </summary>
        /// <param name="td">Definition to consider internalizing</param>
        /// <param name="defaultOption">What to return if no rule exists either way</param>
        /// <returns>True if the type should be internalized; false otherwise.</returns>
        public bool ShouldInternalize(TypeDefinition td, bool defaultOption, out InternalizeExceptionReason reason, bool reasonRequested)
        {
            reason = null;

            // do not internalize types exposed by non-internalized types
            if (publicTypes.Contains(td))
            {
                owner.VERBOSE("- Will not internalize {0} because it is marked as a public type", td.FullName);
                return false;
            }

            string nameWithModule = string.Concat("[", td.Module.Name, "]", td.FullName);
            foreach (InternalizeRule r in rules)
            {
                if (r.MemberNameRegex == null)
                {
                    if (r.TypeNameRegex.IsMatch(td.FullName) ||
                        r.TypeNameRegex.IsMatch(nameWithModule)
                        )
                    {
                        if (!r.ForceInternal && reasonRequested)
                            reason = InternalizeExceptionReason.InternalizeRule(r);
                        return r.ForceInternal;
                    }
                }
            }
            if (!defaultOption) reason = InternalizeExceptionReason.PrimaryAssembly;
            return defaultOption;
        }

        public MemberInternalizeManager GetMemberManagerForType(TypeDefinition td)
        {
            List<InternalizeRule> mrules = new List<InternalizeRule>();
            string nameWithModule = string.Concat("[", td.Module.Name, "]", td.FullName);
            foreach (InternalizeRule r in rules)
            {
                if (r.MemberNameRegex != null)
                {
                    if (r.TypeNameRegex.IsMatch(td.FullName) ||
                        r.TypeNameRegex.IsMatch(nameWithModule)
                        )
                    {
                        mrules.Add(r);
                    }
                }
            }
            if (mrules.Count == 0) return null;
            return new MemberInternalizeManager(mrules.ToArray());
        }

        public void AddAssemblies(IEnumerable<AssemblyDefinition> ads)
        {
            foreach (AssemblyDefinition ad in ads)
                assemblies.Add(ad);
        }

        public void AddAssembly(AssemblyDefinition ad)
        {
            assemblies.Add(ad);
        }

        public bool IsPublicType(TypeReference tr)
        {
            TypeDefinition td = tr.Resolve();
            return publicTypes.Contains(td);
        }

        public void AddPublicType(TypeReference tr, InternalizeExceptionReason reason)
        {
            // the T in class Foo<T> is some sort of mock type
            if (tr.IsGenericParameter && !tr.IsGenericInstance) return;

            TypeDefinition td = tr.Resolve();
            if (td == null)
            {
                owner.WARN(string.Format("Unable to resolve type: {0}", tr.FullName));
                return;
            }
            AddPublicType(td, reason);
            // e.g. if we have Nullable<Bar>, mark Bar as public
            if (tr is GenericInstanceType)
            {
                GenericInstanceType git = (GenericInstanceType)tr;
                foreach (TypeReference tr_arg in git.GenericArguments)
                {
                    AddPublicType(tr_arg, reason);

                }
            }
        }

        public void AddPublicType(TypeDefinition td, InternalizeExceptionReason reason)
        {
            if (!td.IsPublic) return; // oh, wait, nevermind.

            // HashSet.Add returns false if the item's already in there
            if (!publicTypes.Add(td)) return;
            if (td.BaseType != null)
                AddPublicType(td.BaseType, InternalizeExceptionReason.BaseClass(td));

            // Okay, if this type is publicly visible, then a whole bunch more things need to be public in order for things to work smoothly
            // First, if there are any generic type arguments, they must be publicly visible, too.
            //        Constraints, too.

            if (td.HasGenericParameters)
            {
                foreach (GenericParameter gp in td.GenericParameters)
                {
                    AddPublicType(gp, reason);
                    foreach (TypeReference constr in gp.Constraints)
                    {
                        AddPublicType(constr, reason);
                    }
                }
            }

            // if the type in question is not going to be merged in, it doesn't matter whether we consider it public or not.
            // note that this check is performed AFTER the generic-parameter handling
            // this ensures that e.g. a public method that returns Dictionary<MyType> will bring MyType in
            // even through the generic Dictionary<T> is 
            if (!assemblies.Contains(td.Module.Assembly)) return;
            owner.VERBOSE("- Preventing internalization of {0} {1}", td.FullName, ((object)reason) ?? "(no reason given)");

            MemberInternalizeManager mim = GetMemberManagerForType(td);
            // First, all public fields, events, and properties must have their types be public.
            if (td.HasFields)
            {
                foreach (FieldDefinition fd in td.Fields)
                {
                    if (!fd.IsPublic) continue;
                    if (mim != null && mim.ShouldInternalize(fd)) continue;
                    AddPublicType(fd.FieldType, InternalizeExceptionReason.FieldType(fd));
                }
            }
            if (td.HasEvents)
            {
                foreach (EventDefinition ed in td.Events)
                {
                    // Hello, Wilburrrrrr!
                    if (!(ed.AddMethod.IsPublic || ed.RemoveMethod.IsPublic)) continue;
                    if (mim != null && mim.ShouldInternalize(ed)) continue;
                    AddPublicType(ed.EventType, InternalizeExceptionReason.EventType(ed));
                }
            }
            if (td.HasProperties)
            {
                foreach (PropertyDefinition pd in td.Properties)
                {
                    if (!(
                            (pd.GetMethod != null && pd.GetMethod.IsPublic) || (pd.SetMethod != null && pd.SetMethod.IsPublic)
                        )) continue;
                    if (mim != null && mim.ShouldInternalize(pd)) continue;
                    AddPublicType(pd.PropertyType, InternalizeExceptionReason.PropertyType(pd));
                }
            }
            // For each method that's remaining public, the return type and the types of all parameters must be public.
            if (td.HasMethods)
            {
                foreach (MethodDefinition md in td.Methods)
                {
                    if (!md.IsPublic) continue;
                    if (mim != null && mim.ShouldInternalize(md)) continue;
                    AddPublicType(md.ReturnType, InternalizeExceptionReason.MethodReturnValue(md));
                    if (md.HasParameters)
                    {
                        foreach (ParameterDefinition parmd in md.Parameters)
                        {
                            AddPublicType(parmd.ParameterType, InternalizeExceptionReason.MethodParameter(md, parmd));
                        }
                    }
                }
            }

        } // method AddPublicType

    } // class InternalizeManager
}
